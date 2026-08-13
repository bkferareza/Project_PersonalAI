using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Machine.Core;

public sealed class MachineHistoryService
{
    public const int PersistenceSchemaVersion = 1;
    public const int MaximumFiveMinuteRollupCount = 576;
    public const int MaximumHourlyRollupCount = 2_160;
    public const int MaximumDailyRollupCount = 730;
    public const int MaximumMonthlyRollupCount = 120;
    public const int MaximumEventCount = 2_000;
    public static readonly TimeSpan FiveMinuteRetention =
        TimeSpan.FromHours(48);
    public static readonly TimeSpan HourlyRetention =
        TimeSpan.FromDays(90);
    public static readonly TimeSpan DailyRetention =
        TimeSpan.FromDays(730);
    public static readonly TimeSpan EventRetention =
        TimeSpan.FromDays(730);
    public static readonly TimeSpan MinimumObservationInterval =
        TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaximumContinuousObservationGap =
        TimeSpan.FromSeconds(90);
    public static readonly TimeSpan PersistenceInterval =
        TimeSpan.FromMinutes(10);
    public static readonly TimeSpan PersistenceFailureRetryInterval =
        TimeSpan.FromMinutes(5);

    private readonly TimeSpan _minimumObservationInterval;
    private readonly TimeSpan _maximumContinuousObservationGap;
    private readonly List<MachineHistoryRollup> _fiveMinuteRollups = [];
    private readonly List<MachineHistoryRollup> _hourlyRollups = [];
    private readonly List<MachineHistoryRollup> _dailyRollups = [];
    private readonly List<MachineHistoryRollup> _monthlyRollups = [];
    private readonly List<MachineHistoryEvent> _events = [];
    private readonly HashSet<string> _eventFingerprints =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);
    private MachineHistoryRollup? _activeFiveMinuteRollup;
    private MachineHistoryRollup? _activeHourlyRollup;
    private MachineHistoryRollup? _activeDailyRollup;
    private MachineHistoryRollup? _activeMonthlyRollup;
    private MachineHistoryObservation? _lastObservation;
    private DateTimeOffset? _lastAcceptedAt;
    private MachineWindowsUpdateState? _lastWindowsUpdateState;
    private bool? _lastRestartPending;
    private DateTimeOffset? _firstObservedAt;
    private DateTimeOffset? _lastObservedAt;
    private DateTimeOffset? _lastPersistedAt;
    private DateTimeOffset? _nextPersistenceAttemptAt;
    private bool _sessionOpen;
    private bool _isDirty;
    private bool _recoveredFromInvalidState;
    private long _changeVersion;
    private MachineHistoryDataStatus _dataStatus =
        MachineHistoryDataStatus.NotYetPersisted;

    public MachineHistoryService(
        TimeSpan? minimumObservationInterval = null,
        TimeSpan? maximumContinuousObservationGap = null)
    {
        _minimumObservationInterval = minimumObservationInterval ??
            MinimumObservationInterval;
        _maximumContinuousObservationGap =
            maximumContinuousObservationGap ??
            MaximumContinuousObservationGap;
        if (_minimumObservationInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumObservationInterval));
        }
        if (_maximumContinuousObservationGap <
            _minimumObservationInterval)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumContinuousObservationGap));
        }
    }

    public DateTimeOffset? LastPersistedAt => _lastPersistedAt;

    public MachineHistoryDataStatus DataStatus => _dataStatus;

    public bool IsDirty => _isDirty;

    public MachineHistorySnapshot GetSnapshot(
        MachineHistoryRange range,
        DateTimeOffset now)
    {
        var utcNow = now.ToUniversalTime();
        var resolution = MachineHistoryRangePolicy.SelectResolution(range);
        var cutoff = MachineHistoryRangePolicy.GetCutoff(range, utcNow);
        var rollups = GetRollups(resolution)
            .Where(rollup =>
                (cutoff is null || rollup.BucketEnd > cutoff.Value) &&
                rollup.BucketStart <= utcNow)
            .OrderBy(rollup => rollup.BucketStart)
            .ToArray();
        var events = _events
            .Where(item =>
                (cutoff is null || item.OccurredAt >= cutoff.Value) &&
                item.OccurredAt <= utcNow)
            .OrderByDescending(item => item.OccurredAt)
            .ToArray();
        var observedTicks = rollups.Aggregate(
            0L,
            (total, rollup) => SaturatingAdd(
                total,
                rollup.ObservedDurationTicks));
        return new(
            range,
            resolution,
            rollups,
            events,
            observedTicks,
            _firstObservedAt,
            _lastObservedAt,
            _lastPersistedAt,
            _isDirty,
            _dataStatus);
    }

    public bool Observe(MachineHistoryObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var normalized = NormalizeObservation(observation);
        if (_lastAcceptedAt is { } lastAcceptedAt &&
            normalized.CapturedAt > lastAcceptedAt &&
            normalized.CapturedAt - lastAcceptedAt <
                _minimumObservationInterval)
        {
            return false;
        }

        var previous = _lastObservation;
        if (previous is not null &&
            normalized.CapturedAt > previous.CapturedAt)
        {
            var elapsed = normalized.CapturedAt - previous.CapturedAt;
            if (elapsed <= _maximumContinuousObservationGap)
            {
                AddDuration(
                    previous.CapturedAt,
                    normalized.CapturedAt,
                    previous.MachineState,
                    previous.ActivityState);
            }
            else
            {
                _lastObservation = null;
            }
        }
        else if (previous is not null)
        {
            // A backwards or repeated wall-clock value starts a new observed
            // segment. It never creates negative or duplicated duration.
            _lastObservation = null;
        }

        if (previous?.ActivityState is { } previousActivity &&
            normalized.ActivityState is { } currentActivity &&
            previousActivity != currentActivity)
        {
            var kind = currentActivity == MachineUserActivityState.Active
                ? MachineHistoryEventKind.ActivityBecameActive
                : MachineHistoryEventKind.ActivityBecameIdle;
            RecordEventInternal(new(
                normalized.CapturedAt,
                kind,
                currentActivity == MachineUserActivityState.Active
                    ? "Activity became Active"
                    : "Activity became Idle",
                null,
                "LocalInput",
                CreateFingerprint(
                    kind,
                    normalized.CapturedAt,
                    currentActivity.ToString())));
        }

        if (previous?.MachineState is { } previousState &&
            normalized.MachineState is { } currentState &&
            previousState != currentState)
        {
            var kind = MachineHistoryEventKind.MachineStateChanged;
            RecordEventInternal(new(
                normalized.CapturedAt,
                kind,
                "Machine state changed",
                $"{previousState} to {currentState}",
                "DeterministicState",
                CreateFingerprint(
                    kind,
                    normalized.CapturedAt,
                    $"{previousState}|{currentState}")));
        }

        EnsureFiveMinuteBucket(normalized.CapturedAt);
        _activeFiveMinuteRollup =
            MachineHistoryAggregation.AddObservation(
                _activeFiveMinuteRollup!,
                normalized);
        _lastObservation = normalized;
        _lastAcceptedAt = normalized.CapturedAt;
        _firstObservedAt ??= normalized.CapturedAt;
        _lastObservedAt = _lastObservedAt is null ||
            normalized.CapturedAt > _lastObservedAt
                ? normalized.CapturedAt
                : _lastObservedAt;
        MarkDirty();
        return true;
    }

    public void BeginSession(DateTimeOffset occurredAt)
    {
        var timestamp = occurredAt.ToUniversalTime();
        _lastObservation = null;
        _lastAcceptedAt = null;
        if (_sessionOpen)
        {
            var interruptedKind =
                MachineHistoryEventKind.PreviousSessionInterrupted;
            RecordEventInternal(new(
                timestamp,
                interruptedKind,
                "Previous Matasuri session was interrupted",
                null,
                "MatasuriSession",
                CreateFingerprint(
                    interruptedKind,
                    timestamp,
                    "previous")));
        }

        _sessionOpen = true;
        var kind = MachineHistoryEventKind.MatasuriSessionStarted;
        RecordEventInternal(new(
            timestamp,
            kind,
            "Matasuri session started",
            null,
            "MatasuriSession",
            CreateFingerprint(kind, timestamp, "start")));
        MarkDirty();
    }

    public void EndSession(DateTimeOffset occurredAt)
    {
        if (!_sessionOpen)
        {
            return;
        }

        var timestamp = occurredAt.ToUniversalTime();
        _lastObservation = null;
        _lastAcceptedAt = null;
        _sessionOpen = false;
        var kind = MachineHistoryEventKind.MatasuriSessionEnded;
        RecordEventInternal(new(
            timestamp,
            kind,
            "Matasuri session ended",
            null,
            "MatasuriSession",
            CreateFingerprint(kind, timestamp, "end")));
        MarkDirty();
    }

    public void RecordPowerTransition(
        MachineHistoryEventKind kind,
        DateTimeOffset occurredAt)
    {
        var title = kind switch
        {
            MachineHistoryEventKind.SystemSuspend => "System suspended",
            MachineHistoryEventKind.SystemResumeAutomatic =>
                "System resumed automatically",
            MachineHistoryEventKind.SystemResumeSuspend =>
                "System resumed",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var timestamp = occurredAt.ToUniversalTime();
        _lastObservation = null;
        _lastAcceptedAt = null;
        RecordEventInternal(new(
            timestamp,
            kind,
            title,
            null,
            "WindowsPowerBroadcast",
            CreateFingerprint(kind, timestamp, title)));
    }

    public void ObserveHealth(
        MachineWindowsUpdateSnapshot? update,
        MachineRebootPendingSnapshot? restart,
        MachineReliabilitySnapshot? reliability,
        DateTimeOffset observedAt)
    {
        var timestamp = observedAt.ToUniversalTime();
        var metadataChanged = false;
        if (update?.VerifiedAt is not null)
        {
            if (_lastWindowsUpdateState is { } previous &&
                previous != update.UpdateState)
            {
                var kind =
                    MachineHistoryEventKind.WindowsUpdateStateChanged;
                RecordEventInternal(new(
                    timestamp,
                    kind,
                    "Windows Update state changed",
                    $"{FormatUpdateState(previous)} to " +
                        FormatUpdateState(update.UpdateState),
                    "WindowsUpdate",
                    CreateFingerprint(
                        kind,
                        timestamp,
                        $"{previous}|{update.UpdateState}")));
            }
            if (_lastWindowsUpdateState != update.UpdateState)
            {
                _lastWindowsUpdateState = update.UpdateState;
                metadataChanged = true;
            }
        }

        if (restart?.IsPending is { } isPending)
        {
            if (_lastRestartPending is { } previous &&
                previous != isPending)
            {
                var kind = MachineHistoryEventKind.RestartPendingChanged;
                RecordEventInternal(new(
                    timestamp,
                    kind,
                    "Restart-pending state changed",
                    isPending
                        ? "Restart pending"
                        : "Restart no longer pending",
                    "WindowsRestartIndicators",
                    CreateFingerprint(
                        kind,
                        timestamp,
                        isPending.ToString())));
            }
            if (_lastRestartPending != isPending)
            {
                _lastRestartPending = isPending;
                metadataChanged = true;
            }
        }

        if (reliability is not null)
        {
            foreach (var incident in reliability.Incidents)
            {
                RecordReliabilityIncident(incident);
            }
        }

        if (metadataChanged)
        {
            MarkDirty();
        }
        Trim(timestamp);
    }

    public void ObserveLearningMilestones(
        MachineLearningDashboardSnapshot learning)
    {
        ArgumentNullException.ThrowIfNull(learning);
        foreach (var profile in learning.ContextProfiles.Where(profile =>
            profile.Confidence == MachineLearningConfidence.Established))
        {
            var kind =
                MachineHistoryEventKind.LearningProfileEstablished;
            RecordEventInternal(new(
                profile.CreatedAt.ToUniversalTime(),
                kind,
                "Learning profile established",
                $"{profile.LocalHour:00}:00 · {profile.ActivityState}",
                "DeterministicLearning",
                CreateFingerprint(
                    kind,
                    profile.CreatedAt,
                    $"{profile.LocalHour}|{profile.ActivityState}")));
        }

        foreach (var pattern in learning.BroaderPatterns.Where(pattern =>
            pattern.Confidence == MachineLearningConfidence.Established))
        {
            var kind =
                MachineHistoryEventKind.BroaderPatternEstablished;
            RecordEventInternal(new(
                pattern.CreatedAt.ToUniversalTime(),
                kind,
                "Broader pattern established",
                $"{pattern.StartHour:00}:00–" +
                    $"{pattern.EndHourExclusive:00}:00 · " +
                    pattern.ActivityState,
                "DeterministicLearning",
                CreateFingerprint(
                    kind,
                    pattern.CreatedAt,
                    $"{pattern.StartHour}|{pattern.EndHourExclusive}|" +
                    $"{pattern.ActivityState}")));
        }
    }

    public async Task LoadAsync(
        IMachineHistoryStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var state = await store.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var loadStatus = (store as IMachineHistoryStoreDiagnostics)?
            .LastLoadStatus;
        if (state is null)
        {
            _recoveredFromInvalidState = loadStatus ==
                MachineHistoryStoreLoadStatus.Corrupt;
            _dataStatus = loadStatus switch
            {
                MachineHistoryStoreLoadStatus.Corrupt =>
                    MachineHistoryDataStatus.RecoveredFromInvalidState,
                MachineHistoryStoreLoadStatus.Unavailable =>
                    MachineHistoryDataStatus
                        .PersistenceTemporarilyUnavailable,
                _ => MachineHistoryDataStatus.NotYetPersisted
            };
            return;
        }

        if (!IsValidState(state))
        {
            _recoveredFromInvalidState = true;
            _dataStatus =
                MachineHistoryDataStatus.RecoveredFromInvalidState;
            return;
        }

        ReplaceRollups(_fiveMinuteRollups, state.FiveMinuteRollups);
        ReplaceRollups(_hourlyRollups, state.HourlyRollups);
        ReplaceRollups(_dailyRollups, state.DailyRollups);
        ReplaceRollups(_monthlyRollups, state.MonthlyRollups);
        _activeFiveMinuteRollup = state.ActiveFiveMinuteRollup;
        _activeHourlyRollup = state.ActiveHourlyRollup;
        _activeDailyRollup = state.ActiveDailyRollup;
        _activeMonthlyRollup = state.ActiveMonthlyRollup;
        _events.Clear();
        _events.AddRange(state.Events.OrderBy(item => item.OccurredAt));
        _eventFingerprints.Clear();
        foreach (var item in _events)
        {
            _eventFingerprints.Add(item.Fingerprint);
        }
        _lastObservation = state.LastObservation is null
            ? null
            : NormalizeObservation(state.LastObservation);
        _lastAcceptedAt = _lastObservation?.CapturedAt;
        _sessionOpen = state.SessionOpen;
        _lastWindowsUpdateState = state.LastWindowsUpdateState;
        _lastRestartPending = state.LastRestartPending;
        _firstObservedAt = state.FirstObservedAt;
        _lastObservedAt = state.LastObservedAt;
        _lastPersistedAt = state.PersistedAt;
        _nextPersistenceAttemptAt = null;
        _isDirty = false;
        _recoveredFromInvalidState = false;
        _dataStatus = MachineHistoryDataStatus.Healthy;
        Trim(state.PersistedAt);
    }

    public Task<bool> SaveIfDueAsync(
        IMachineHistoryStore store,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        SaveIfDueAsync(store, now, force: false, cancellationToken);

    public async Task<bool> SaveIfDueAsync(
        IMachineHistoryStore store,
        DateTimeOffset now,
        bool force,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!_isDirty ||
            !force &&
            ((_nextPersistenceAttemptAt is not null &&
              now < _nextPersistenceAttemptAt.Value) ||
             (_lastPersistedAt is not null &&
              now - _lastPersistedAt.Value < PersistenceInterval)))
        {
            return false;
        }

        var snapshotVersion = _changeVersion;
        var state = CreatePersistedState(now.ToUniversalTime());
        await _persistenceGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            try
            {
                await store.SaveAsync(state, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                _dataStatus = MachineHistoryDataStatus
                    .PersistenceTemporarilyUnavailable;
                _nextPersistenceAttemptAt =
                    now + PersistenceFailureRetryInterval;
                return false;
            }

            _lastPersistedAt = now.ToUniversalTime();
            _nextPersistenceAttemptAt = null;
            _dataStatus = _recoveredFromInvalidState
                ? MachineHistoryDataStatus.RecoveredFromInvalidState
                : MachineHistoryDataStatus.Healthy;
            if (_changeVersion == snapshotVersion)
            {
                _isDirty = false;
            }
            return true;
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    public async Task<bool> SaveFinalSnapshotAsync(
        IMachineHistoryStore store,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        EndSession(now);
        return await SaveIfDueAsync(
            store,
            now,
            force: true,
            cancellationToken).ConfigureAwait(false);
    }

    private void AddDuration(
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        MachineOverallState? state,
        MachineUserActivityState? activity)
    {
        var cursor = startedAt;
        while (cursor < endedAt)
        {
            EnsureFiveMinuteBucket(cursor);
            var segmentEnd = endedAt < _activeFiveMinuteRollup!.BucketEnd
                ? endedAt
                : _activeFiveMinuteRollup.BucketEnd;
            _activeFiveMinuteRollup =
                MachineHistoryAggregation.AddDuration(
                    _activeFiveMinuteRollup,
                    (segmentEnd - cursor).Ticks,
                    state,
                    activity);
            cursor = segmentEnd;
        }
    }

    private void EnsureFiveMinuteBucket(DateTimeOffset timestamp)
    {
        var bucketStart = FloorToFiveMinutes(timestamp);
        if (_activeFiveMinuteRollup is null)
        {
            _activeFiveMinuteRollup =
                MachineHistoryAggregation.Create(
                    bucketStart,
                    bucketStart.AddMinutes(5));
            return;
        }
        if (_activeFiveMinuteRollup.BucketStart == bucketStart)
        {
            return;
        }

        FinalizeFiveMinute(_activeFiveMinuteRollup);
        _activeFiveMinuteRollup = MachineHistoryAggregation.Create(
            bucketStart,
            bucketStart.AddMinutes(5));
    }

    private void FinalizeFiveMinute(MachineHistoryRollup rollup)
    {
        Upsert(_fiveMinuteRollups, rollup);
        TrimRollups(
            _fiveMinuteRollups,
            rollup.BucketEnd - FiveMinuteRetention,
            MaximumFiveMinuteRollupCount);
        PromoteToHour(rollup);
    }

    private void PromoteToHour(MachineHistoryRollup contribution)
    {
        var start = FloorToHour(contribution.BucketStart);
        if (_activeHourlyRollup is null)
        {
            _activeHourlyRollup = MergeIntoNew(
                start,
                start.AddHours(1),
                contribution);
            return;
        }
        if (_activeHourlyRollup.BucketStart == start)
        {
            _activeHourlyRollup = MachineHistoryAggregation.Merge(
                _activeHourlyRollup,
                contribution);
            return;
        }
        if (start > _activeHourlyRollup.BucketStart)
        {
            var completed = _activeHourlyRollup;
            Upsert(_hourlyRollups, completed);
            TrimRollups(
                _hourlyRollups,
                completed.BucketEnd - HourlyRetention,
                MaximumHourlyRollupCount);
            PromoteToDay(completed);
            _activeHourlyRollup = MergeIntoNew(
                start,
                start.AddHours(1),
                contribution);
            return;
        }

        Upsert(_hourlyRollups, MergeIntoNew(
            start,
            start.AddHours(1),
            contribution));
        PromoteToDay(contribution);
    }

    private void PromoteToDay(MachineHistoryRollup contribution)
    {
        var start = StartOfDay(contribution.BucketStart);
        if (_activeDailyRollup is null)
        {
            _activeDailyRollup = MergeIntoNew(
                start,
                start.AddDays(1),
                contribution);
            return;
        }
        if (_activeDailyRollup.BucketStart == start)
        {
            _activeDailyRollup = MachineHistoryAggregation.Merge(
                _activeDailyRollup,
                contribution);
            return;
        }
        if (start > _activeDailyRollup.BucketStart)
        {
            var completed = _activeDailyRollup;
            Upsert(_dailyRollups, completed);
            TrimRollups(
                _dailyRollups,
                completed.BucketEnd - DailyRetention,
                MaximumDailyRollupCount);
            PromoteToMonth(completed);
            _activeDailyRollup = MergeIntoNew(
                start,
                start.AddDays(1),
                contribution);
            return;
        }

        Upsert(_dailyRollups, MergeIntoNew(
            start,
            start.AddDays(1),
            contribution));
        PromoteToMonth(contribution);
    }

    private void PromoteToMonth(MachineHistoryRollup contribution)
    {
        var start = StartOfMonth(contribution.BucketStart);
        if (_activeMonthlyRollup is null)
        {
            _activeMonthlyRollup = MergeIntoNew(
                start,
                start.AddMonths(1),
                contribution);
            return;
        }
        if (_activeMonthlyRollup.BucketStart == start)
        {
            _activeMonthlyRollup = MachineHistoryAggregation.Merge(
                _activeMonthlyRollup,
                contribution);
            return;
        }
        if (start > _activeMonthlyRollup.BucketStart)
        {
            Upsert(_monthlyRollups, _activeMonthlyRollup);
            TrimRollups(
                _monthlyRollups,
                _activeMonthlyRollup.BucketEnd.AddMonths(
                    -MaximumMonthlyRollupCount),
                MaximumMonthlyRollupCount);
            _activeMonthlyRollup = MergeIntoNew(
                start,
                start.AddMonths(1),
                contribution);
            return;
        }

        Upsert(_monthlyRollups, MergeIntoNew(
            start,
            start.AddMonths(1),
            contribution));
    }

    private IReadOnlyList<MachineHistoryRollup> GetRollups(
        MachineHistoryResolution resolution)
    {
        var result = resolution switch
        {
            MachineHistoryResolution.FiveMinutes =>
                _fiveMinuteRollups.ToList(),
            MachineHistoryResolution.Hour => _hourlyRollups.ToList(),
            MachineHistoryResolution.Day => _dailyRollups.ToList(),
            MachineHistoryResolution.Month => _monthlyRollups.ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(resolution))
        };
        var active = resolution switch
        {
            MachineHistoryResolution.FiveMinutes =>
                _activeFiveMinuteRollup,
            MachineHistoryResolution.Hour => _activeHourlyRollup,
            MachineHistoryResolution.Day => _activeDailyRollup,
            MachineHistoryResolution.Month => _activeMonthlyRollup,
            _ => null
        };
        if (active is not null)
        {
            Upsert(result, active);
        }

        AddUnpromotedPreview(result, resolution);
        return result;
    }

    private void AddUnpromotedPreview(
        List<MachineHistoryRollup> result,
        MachineHistoryResolution resolution)
    {
        if (resolution == MachineHistoryResolution.FiveMinutes ||
            _activeFiveMinuteRollup is null)
        {
            return;
        }

        if (resolution == MachineHistoryResolution.Hour)
        {
            UpsertContributionPreview(
                result,
                _activeFiveMinuteRollup,
                FloorToHour(_activeFiveMinuteRollup.BucketStart),
                static start => start.AddHours(1));
            return;
        }

        if (_activeHourlyRollup is not null)
        {
            var start = resolution == MachineHistoryResolution.Day
                ? StartOfDay(_activeHourlyRollup.BucketStart)
                : StartOfMonth(_activeHourlyRollup.BucketStart);
            UpsertContributionPreview(
                result,
                _activeHourlyRollup,
                start,
                resolution == MachineHistoryResolution.Day
                    ? static value => value.AddDays(1)
                    : static value => value.AddMonths(1));
        }

        var fiveStart = resolution == MachineHistoryResolution.Day
            ? StartOfDay(_activeFiveMinuteRollup.BucketStart)
            : StartOfMonth(_activeFiveMinuteRollup.BucketStart);
        UpsertContributionPreview(
            result,
            _activeFiveMinuteRollup,
            fiveStart,
            resolution == MachineHistoryResolution.Day
                ? static value => value.AddDays(1)
                : static value => value.AddMonths(1));

        if (resolution == MachineHistoryResolution.Month &&
            _activeDailyRollup is not null)
        {
            var monthStart = StartOfMonth(
                _activeDailyRollup.BucketStart);
            UpsertContributionPreview(
                result,
                _activeDailyRollup,
                monthStart,
                static value => value.AddMonths(1));
        }
    }

    private static void UpsertContributionPreview(
        List<MachineHistoryRollup> result,
        MachineHistoryRollup contribution,
        DateTimeOffset bucketStart,
        Func<DateTimeOffset, DateTimeOffset> getBucketEnd)
    {
        var preview = MergeIntoNew(
            bucketStart,
            getBucketEnd(bucketStart),
            contribution);
        Upsert(result, preview);
    }

    private void RecordReliabilityIncident(
        MachineReliabilityIncident incident)
    {
        var normalized =
            MachineReliabilityAggregator.NormalizeIncident(incident);
        if (normalized is null)
        {
            return;
        }

        var kind = normalized.Category switch
        {
            MachineReliabilityIncidentCategory.UnexpectedShutdown =>
                MachineHistoryEventKind.UnexpectedShutdownRecorded,
            MachineReliabilityIncidentCategory.ApplicationCrash or
                MachineReliabilityIncidentCategory.ApplicationHang =>
                MachineHistoryEventKind.ApplicationFailureRecorded,
            _ => MachineHistoryEventKind.ReliabilityIncidentRecorded
        };
        var title = kind switch
        {
            MachineHistoryEventKind.UnexpectedShutdownRecorded =>
                "Unexpected shutdown recorded",
            MachineHistoryEventKind.ApplicationFailureRecorded =>
                normalized.Category ==
                    MachineReliabilityIncidentCategory.ApplicationHang
                    ? "Application hang recorded"
                    : "Application failure recorded",
            _ => "Windows reliability event recorded"
        };
        var detail = kind ==
                MachineHistoryEventKind.ApplicationFailureRecorded
            ? normalized.ApplicationName
            : normalized.Category.ToString();
        var identity = string.Join(
            '|',
            normalized.Category,
            normalized.ApplicationName?.ToUpperInvariant(),
            normalized.UpdateIdentifier?.ToUpperInvariant(),
            normalized.EventId,
            normalized.SummaryCode,
            normalized.CorrelationId?.ToUpperInvariant());
        RecordEventInternal(new(
            normalized.OccurredAt.ToUniversalTime(),
            kind,
            title,
            detail,
            "WindowsReliability",
            CreateFingerprint(kind, normalized.OccurredAt, identity)));
    }

    private bool RecordEventInternal(MachineHistoryEvent item)
    {
        if (!Enum.IsDefined(item.Kind) ||
            item.OccurredAt == default ||
            string.IsNullOrWhiteSpace(item.Title) ||
            string.IsNullOrWhiteSpace(item.Source) ||
            !IsValidFingerprint(item.Fingerprint) ||
            item.Count <= 0)
        {
            return false;
        }
        if (!_eventFingerprints.Add(item.Fingerprint))
        {
            return false;
        }

        _events.Add(item with
        {
            OccurredAt = item.OccurredAt.ToUniversalTime(),
            Title = Truncate(item.Title.Trim(), 120),
            Detail = string.IsNullOrWhiteSpace(item.Detail)
                ? null
                : Truncate(item.Detail.Trim(), 240),
            Source = Truncate(item.Source.Trim(), 64)
        });
        _events.Sort(static (left, right) =>
            left.OccurredAt.CompareTo(right.OccurredAt));
        TrimEvents(item.OccurredAt.ToUniversalTime());
        MarkDirty();
        return true;
    }

    private void Trim(DateTimeOffset now)
    {
        TrimRollups(
            _fiveMinuteRollups,
            now - FiveMinuteRetention,
            MaximumFiveMinuteRollupCount);
        TrimRollups(
            _hourlyRollups,
            now - HourlyRetention,
            MaximumHourlyRollupCount);
        TrimRollups(
            _dailyRollups,
            now - DailyRetention,
            MaximumDailyRollupCount);
        TrimRollups(
            _monthlyRollups,
            now.AddMonths(-MaximumMonthlyRollupCount),
            MaximumMonthlyRollupCount);

        if (TrimEvents(now))
        {
            MarkDirty();
        }
    }

    private bool TrimEvents(DateTimeOffset now)
    {
        var eventCutoff = now - EventRetention;
        var removed = _events.RemoveAll(item =>
            item.OccurredAt < eventCutoff);
        if (_events.Count > MaximumEventCount)
        {
            removed += _events.Count - MaximumEventCount;
            _events.RemoveRange(0, _events.Count - MaximumEventCount);
        }
        if (removed <= 0)
        {
            return false;
        }

        _eventFingerprints.Clear();
        foreach (var item in _events)
        {
            _eventFingerprints.Add(item.Fingerprint);
        }
        return true;
    }

    private MachineHistoryPersistedState CreatePersistedState(
        DateTimeOffset persistedAt) => new(
        PersistenceSchemaVersion,
        _fiveMinuteRollups.ToArray(),
        _hourlyRollups.ToArray(),
        _dailyRollups.ToArray(),
        _monthlyRollups.ToArray(),
        _activeFiveMinuteRollup,
        _activeHourlyRollup,
        _activeDailyRollup,
        _activeMonthlyRollup,
        _events.ToArray(),
        _lastObservation,
        _sessionOpen,
        _lastWindowsUpdateState,
        _lastRestartPending,
        _firstObservedAt,
        _lastObservedAt,
        persistedAt);

    private static bool IsValidState(
        MachineHistoryPersistedState state) =>
        state.SchemaVersion == PersistenceSchemaVersion &&
        IsValidList(
            state.FiveMinuteRollups,
            MaximumFiveMinuteRollupCount) &&
        IsValidList(state.HourlyRollups, MaximumHourlyRollupCount) &&
        IsValidList(state.DailyRollups, MaximumDailyRollupCount) &&
        IsValidList(state.MonthlyRollups, MaximumMonthlyRollupCount) &&
        IsValidActive(state.ActiveFiveMinuteRollup) &&
        IsValidActive(state.ActiveHourlyRollup) &&
        IsValidActive(state.ActiveDailyRollup) &&
        IsValidActive(state.ActiveMonthlyRollup) &&
        state.Events is not null &&
        state.Events.Count <= MaximumEventCount &&
        state.Events.All(IsValidEvent) &&
        state.Events.Select(item => item.Fingerprint)
            .Distinct(StringComparer.Ordinal).Count() == state.Events.Count &&
        (state.LastWindowsUpdateState is null ||
         Enum.IsDefined(state.LastWindowsUpdateState.Value)) &&
        (state.FirstObservedAt is null || state.LastObservedAt is null ||
         state.FirstObservedAt <= state.LastObservedAt) &&
        state.PersistedAt != default;

    private static bool IsValidList(
        IReadOnlyList<MachineHistoryRollup>? items,
        int maximumCount) =>
        items is not null &&
        items.Count <= maximumCount &&
        items.All(MachineHistoryAggregation.IsValid) &&
        items.Select(item => item.BucketStart).Distinct().Count() ==
            items.Count;

    private static bool IsValidActive(MachineHistoryRollup? item) =>
        item is null || MachineHistoryAggregation.IsValid(item);

    private static bool IsValidEvent(MachineHistoryEvent item) =>
        Enum.IsDefined(item.Kind) &&
        item.OccurredAt != default &&
        !string.IsNullOrWhiteSpace(item.Title) &&
        item.Title.Length <= 120 &&
        (item.Detail is null || item.Detail.Length <= 240) &&
        !string.IsNullOrWhiteSpace(item.Source) &&
        item.Source.Length <= 64 &&
        IsValidFingerprint(item.Fingerprint) &&
        item.Count > 0;

    private static MachineHistoryObservation NormalizeObservation(
        MachineHistoryObservation observation) => observation with
        {
            CapturedAt = observation.CapturedAt.ToUniversalTime(),
            CpuUtilizationPercent = NormalizePercent(
                observation.CpuUtilizationPercent),
            MemoryUtilizationPercent = NormalizePercent(
                observation.MemoryUtilizationPercent),
            NetworkReceiveBytesPerSecond = NormalizeNonNegative(
                observation.NetworkReceiveBytesPerSecond),
            NetworkSendBytesPerSecond = NormalizeNonNegative(
                observation.NetworkSendBytesPerSecond),
            ActivityState = observation.ActivityState is { } activity &&
                Enum.IsDefined(activity)
                    ? activity
                    : null,
            MachineState = observation.MachineState is { } state &&
                Enum.IsDefined(state)
                    ? state
                    : null,
            SystemVolumeFreePercent = NormalizePercent(
                observation.SystemVolumeFreePercent),
            GpuUtilizationPercent = NormalizePercent(
                observation.GpuUtilizationPercent),
            GpuMemoryUtilizationPercent = NormalizePercent(
                observation.GpuMemoryUtilizationPercent),
            GpuTemperatureCelsius = NormalizeTemperature(
                observation.GpuTemperatureCelsius),
            GpuBoardPowerWatts = NormalizeNonNegative(
                observation.GpuBoardPowerWatts),
            CpuTemperatureCelsius = NormalizeTemperature(
                observation.CpuTemperatureCelsius),
            CpuPackagePowerWatts = NormalizeNonNegative(
                observation.CpuPackagePowerWatts),
            StorageTemperatureCelsius = NormalizeTemperature(
                observation.StorageTemperatureCelsius),
            EstimatedSystemPowerWatts = NormalizeNonNegative(
                observation.EstimatedSystemPowerWatts),
            EnergyWattHours = NormalizeNonNegative(
                observation.EnergyWattHours)
        };

    private static double? NormalizePercent(double? value) =>
        value is { } candidate &&
        double.IsFinite(candidate) &&
        candidate is >= 0d and <= 100d
            ? candidate
            : null;

    private static double? NormalizeNonNegative(double? value) =>
        value is { } candidate &&
        double.IsFinite(candidate) &&
        candidate >= 0d
            ? candidate
            : null;

    private static double? NormalizeTemperature(double? value) =>
        value is { } candidate &&
        double.IsFinite(candidate) &&
        candidate is >= -100d and <= 500d
            ? candidate
            : null;

    private static MachineHistoryRollup MergeIntoNew(
        DateTimeOffset bucketStart,
        DateTimeOffset bucketEnd,
        MachineHistoryRollup contribution) =>
        MachineHistoryAggregation.Merge(
            MachineHistoryAggregation.Create(bucketStart, bucketEnd),
            contribution);

    private static void Upsert(
        List<MachineHistoryRollup> items,
        MachineHistoryRollup candidate)
    {
        if (items.Count == 0 ||
            items[^1].BucketStart < candidate.BucketStart)
        {
            items.Add(candidate);
            return;
        }
        if (items[^1].BucketStart == candidate.BucketStart)
        {
            items[^1] = MachineHistoryAggregation.Merge(
                items[^1],
                candidate);
            return;
        }

        var index = items.BinarySearch(
            candidate,
            RollupStartComparer.Instance);
        if (index >= 0)
        {
            items[index] = MachineHistoryAggregation.Merge(
                items[index],
                candidate);
        }
        else
        {
            items.Insert(~index, candidate);
        }
    }

    private static void ReplaceRollups(
        List<MachineHistoryRollup> target,
        IReadOnlyList<MachineHistoryRollup> source)
    {
        target.Clear();
        target.AddRange(source.OrderBy(item => item.BucketStart));
    }

    private static void TrimRollups(
        List<MachineHistoryRollup> items,
        DateTimeOffset cutoff,
        int maximumCount)
    {
        var expiredCount = 0;
        while (expiredCount < items.Count &&
            items[expiredCount].BucketEnd <= cutoff)
        {
            expiredCount++;
        }
        if (expiredCount > 0)
        {
            items.RemoveRange(0, expiredCount);
        }
        if (items.Count > maximumCount)
        {
            items.RemoveRange(0, items.Count - maximumCount);
        }
    }

    private static DateTimeOffset FloorToFiveMinutes(
        DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var fiveMinuteTicks = TimeSpan.FromMinutes(5).Ticks;
        return new DateTimeOffset(
            utc.Ticks - utc.Ticks % fiveMinuteTicks,
            TimeSpan.Zero);
    }

    private static DateTimeOffset FloorToHour(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(
            utc.Year,
            utc.Month,
            utc.Day,
            utc.Hour,
            0,
            0,
            TimeSpan.Zero);
    }

    private static DateTimeOffset StartOfDay(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(
            utc.Year,
            utc.Month,
            utc.Day,
            0,
            0,
            0,
            TimeSpan.Zero);
    }

    private static DateTimeOffset StartOfMonth(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(
            utc.Year,
            utc.Month,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
    }

    private static string CreateFingerprint(
        MachineHistoryEventKind kind,
        DateTimeOffset occurredAt,
        string identity)
    {
        var value = string.Join(
            '|',
            kind,
            occurredAt.ToUniversalTime().UtcTicks,
            identity.ToUpperInvariant());
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(value)));
    }

    private static bool IsValidFingerprint(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static string FormatUpdateState(
        MachineWindowsUpdateState state) => state switch
        {
            MachineWindowsUpdateState.UpToDate => "Up to date",
            MachineWindowsUpdateState.UpdatesAvailable =>
                "Updates available",
            MachineWindowsUpdateState.InstallPending =>
                "Installation pending",
            MachineWindowsUpdateState.RestartRequired =>
                "Restart required",
            _ => "Unknown"
        };

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength
            ? value
            : value[..maximumLength];

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right
            ? long.MaxValue
            : left + right;

    private void MarkDirty()
    {
        _changeVersion = SaturatingAdd(_changeVersion, 1);
        _isDirty = true;
    }

    private sealed class RollupStartComparer :
        IComparer<MachineHistoryRollup>
    {
        public static RollupStartComparer Instance { get; } = new();

        public int Compare(
            MachineHistoryRollup? left,
            MachineHistoryRollup? right) =>
            Comparer<DateTimeOffset>.Default.Compare(
                left?.BucketStart ?? default,
                right?.BucketStart ?? default);
    }
}
