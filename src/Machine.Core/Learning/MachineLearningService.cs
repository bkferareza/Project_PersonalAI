using System.Diagnostics;

namespace Machine.Core;

public sealed class MachineLearningService
{
    public const int MaximumObservationCount = 2_880;
    public const int MaximumContextProfileCount = 48;
    public const int MaximumEpisodeCount = 200;
    public const int PersistenceSchemaVersion = 3;
    public const int PreviousPersistenceSchemaVersion = 2;
    public const int LegacyPersistenceSchemaVersion = 1;
    public const int ProvisionalSampleCount = 12;
    public const int EstablishedSampleCount = 168;
    public const int EstablishedObservedDayCount = 7;
    public static readonly TimeSpan ObservationInterval =
        TimeSpan.FromSeconds(30);
    public static readonly TimeSpan PersistenceInterval =
        TimeSpan.FromMinutes(10);
    public static readonly TimeSpan PersistenceFailureRetryInterval =
        TimeSpan.FromMinutes(5);

    private readonly Queue<MachineLearningObservation> _journal = new();
    private readonly Queue<MachineLearningEpisode> _episodes = new();
    private readonly Dictionary<MachineLearningContextKey, OnlineBaseline>
        _baselines = new();
    private readonly Dictionary<MachineLearningContextKey,
        MachineLearningContextProfile> _profiles = new();
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);
    private readonly MachineLearningActivityLog _activityLog;
    private IReadOnlyList<MachineLearningRecurringPattern> _patterns = [];
    private ActiveEpisode? _activeEpisode;
    private DateTimeOffset? _lastObservationAt;
    private DateTimeOffset? _lastObservationAttemptAt;
    private DateTimeOffset? _firstObservedAt;
    private DateTimeOffset? _lastObservedAt;
    private DateTimeOffset? _lastPersistedAt;
    private DateTimeOffset? _nextPersistenceAttemptAt;
    private DateTimeOffset? _previousMachineSessionEndedAt;
    private readonly DateTimeOffset _currentSessionStartedAt;
    private int _persistedSchemaVersion = PersistenceSchemaVersion;
    private long _observationCount;
    private long _lifetimeMachineSessionCount = 1;
    private long _sessionAcceptedObservationCount;
    private long _observedDurationTicks;
    private long _throttledObservationCount;
    private long _missingPrerequisiteCount;
    private long _changeVersion = 1;
    private bool _isDirty = true;
    private bool _currentSessionFinalized;
    private bool _recoveredFromCorruptState;
    private MachineLearningDataHealth _dataHealth =
        MachineLearningDataHealth.NotYetPersisted;

    public MachineLearningService(DateTimeOffset? sessionStartedAt = null,
        MachineLearningActivityLog? activityLog = null)
    {
        _currentSessionStartedAt = sessionStartedAt ?? DateTimeOffset.UtcNow;
        _activityLog = activityLog ?? new MachineLearningActivityLog();
        _activityLog.Record(MachineLearningActivityKind.RuntimeStarted,
            _currentSessionStartedAt);
    }

    public MachineLearningActivityLog ActivityLog => _activityLog;

    public IReadOnlyList<MachineLearningObservation> Journal =>
        _journal.ToArray();

    public IReadOnlyList<MachineLearningEpisode> RecentEpisodes =>
        _episodes.ToArray();

    public IReadOnlyList<MachineLearningBaseline> Baselines =>
        GetBaselines(_lastObservedAt ?? _currentSessionStartedAt);

    public IReadOnlyList<MachineLearningContextProfile> ContextProfiles =>
        GetProfiles(_lastObservedAt ?? _currentSessionStartedAt);

    public IReadOnlyList<MachineLearningRecurringPattern> BroaderPatterns =>
        GetPatterns(_lastObservedAt ?? _currentSessionStartedAt);

    public bool IsDirty => _isDirty;

    public DateTimeOffset? LastPersistedAt => _lastPersistedAt;

    public MachineLearningDataHealth DataHealth => _dataHealth;

    public bool CanObserveAt(DateTimeOffset timestamp) =>
        _lastObservationAt is null ||
        timestamp - _lastObservationAt.Value >= ObservationInterval;

    public bool TryBeginObservationAttempt(DateTimeOffset timestamp)
    {
        if (_lastObservationAttemptAt is not null &&
            timestamp - _lastObservationAttemptAt.Value < ObservationInterval)
        {
            _throttledObservationCount = SaturatingIncrement(
                _throttledObservationCount);
            _activityLog.Record(MachineLearningActivityKind.ObservationSkipped,
                timestamp, detail: "Throttled");
            return false;
        }

        _lastObservationAttemptAt = timestamp;
        return true;
    }

    public void RecordMissingPrerequisite()
    {
        _missingPrerequisiteCount = SaturatingIncrement(
            _missingPrerequisiteCount);
        _activityLog.Record(MachineLearningActivityKind.ObservationSkipped,
            DateTimeOffset.UtcNow, detail: "Missing prerequisite");
    }

    public bool Observe(MachineLearningObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!IsValidObservation(observation))
        {
            _missingPrerequisiteCount = SaturatingIncrement(
                _missingPrerequisiteCount);
            _activityLog.Record(MachineLearningActivityKind.ObservationSkipped,
                observation.Timestamp, detail: "Invalid observation");
            return false;
        }

        if (_lastObservedAt is not null &&
            observation.Timestamp < _lastObservedAt.Value)
        {
            _missingPrerequisiteCount = SaturatingIncrement(
                _missingPrerequisiteCount);
            _activityLog.Record(MachineLearningActivityKind.ObservationSkipped,
                observation.Timestamp, detail: "Out-of-order observation");
            return false;
        }

        if (!CanObserveAt(observation.Timestamp))
        {
            _throttledObservationCount = SaturatingIncrement(
                _throttledObservationCount);
            _activityLog.Record(MachineLearningActivityKind.ObservationSkipped,
                observation.Timestamp, detail: "Throttled");
            return false;
        }

        _lastObservationAt = observation.Timestamp;
        if (_lastObservationAttemptAt is null ||
            observation.Timestamp > _lastObservationAttemptAt.Value)
        {
            _lastObservationAttemptAt = observation.Timestamp;
        }
        _firstObservedAt ??= observation.Timestamp;
        _lastObservedAt = observation.Timestamp;
        _observationCount = SaturatingIncrement(_observationCount);
        _sessionAcceptedObservationCount = SaturatingIncrement(
            _sessionAcceptedObservationCount);
        _observedDurationTicks = AddDurationTicks(
            _observedDurationTicks,
            ObservationInterval.Ticks);
        _journal.Enqueue(observation);
        while (_journal.Count > MaximumObservationCount)
        {
            _journal.Dequeue();
        }

        var localTimestamp = observation.Timestamp.ToLocalTime();
        var key = new MachineLearningContextKey(
            localTimestamp.Hour,
            observation.ActivityState);
        if (!_baselines.TryGetValue(key, out var baseline))
        {
            baseline = new OnlineBaseline(observation.Timestamp);
            _baselines.Add(key, baseline);
        }

        baseline.Add(observation);
        var episodeCount = _episodes.Count;
        UpdateEpisodes(observation, localTimestamp);
        if (UpdateProfile(key, baseline, observation.Timestamp))
        {
            RecomputePatterns(observation.Timestamp);
            _activityLog.Record(MachineLearningActivityKind.ProfileUpdated,
                observation.Timestamp, _observationCount, _profiles.Count,
                _episodes.Count);
        }

        if (_episodes.Count != episodeCount)
        {
            _activityLog.Record(MachineLearningActivityKind.EpisodeUpdated,
                observation.Timestamp, _observationCount, _profiles.Count,
                _episodes.Count);
        }

        MarkDirty();
        _activityLog.Record(MachineLearningActivityKind.ObservationAccepted,
            observation.Timestamp, _observationCount, _profiles.Count,
            _episodes.Count);
        return true;
    }

    public MachineLearningDashboardSnapshot GetDashboardSnapshot(
        DateTimeOffset now)
    {
        RefreshFreshness(now);
        var current = _journal.Count == 0 ? null : _journal.Last();
        var baseline = current is null ? null : GetBaseline(current, now);
        var baselines = GetBaselines(now);
        var profiles = _profiles.Values
            .OrderBy(profile => profile.LocalHour)
            .ThenBy(profile => profile.ActivityState)
            .ToArray();
        var episodes = RecentEpisodes;

        return new MachineLearningDashboardSnapshot(
            _observationCount,
            ToDuration(_observedDurationTicks),
            current,
            baseline,
            episodes.Count,
            _journal.Count,
            baselines,
            episodes,
            MachineLearnedItemProjector.Project(
                baselines,
                profiles,
                _patterns,
                episodes,
                current),
            new MachineLearningDiagnostics(
                _sessionAcceptedObservationCount,
                _throttledObservationCount,
                _missingPrerequisiteCount,
                _lastObservedAt),
            _lastPersistedAt,
            _isDirty,
            _dataHealth,
            CreateMetadata(),
            profiles,
            _patterns);
    }

    public MachineLearnedContext? GetLearnedContext(
        DateTimeOffset? now = null)
    {
        var current = _journal.Count == 0 ? null : _journal.Last();
        if (current is null)
        {
            return null;
        }

        var effectiveNow = now ?? DateTimeOffset.UtcNow;
        RefreshFreshness(effectiveNow);
        var baseline = GetBaseline(current, effectiveNow);
        if (baseline is null)
        {
            return null;
        }

        var key = new MachineLearningContextKey(
            baseline.LocalHour,
            baseline.ActivityState);
        _profiles.TryGetValue(key, out var profile);
        var pattern = _patterns
            .Where(candidate =>
                candidate.Confidence == MachineLearningConfidence.Established &&
                candidate.MemberContexts.Contains(key))
            .OrderByDescending(candidate => candidate.MemberContexts.Count)
            .FirstOrDefault();

        return new MachineLearnedContext(
            baseline,
            profile,
            pattern,
            _episodes.Reverse().Where(episode =>
                episode.ActivityState == current.ActivityState &&
                episode.OverallState == current.OverallState).Take(2).Select(episode =>
                new MachineLearningEpisodeSummary(
                    episode.ActivityState,
                    episode.OverallState,
                    episode.SampleCount,
                    episode.AverageCpuUsagePercent,
                    episode.PeakCpuUsagePercent,
                    episode.AverageMemoryUsagePercent,
                    episode.FindingKeys,
                    episode.Outcome)).ToArray());
    }

    public async Task LoadAsync(
        IMachineLearningStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        _activityLog.Record(MachineLearningActivityKind.RestoreStarted,
            _currentSessionStartedAt);
        var state = await store.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var loadStatus = (store as IMachineLearningStoreDiagnostics)?
            .LastLoadStatus;

        if (state is null)
        {
            _recoveredFromCorruptState =
                loadStatus == MachineLearningStoreLoadStatus.Corrupt;
            _dataHealth = loadStatus switch
            {
                MachineLearningStoreLoadStatus.Corrupt =>
                    MachineLearningDataHealth.RecoveredFromCorruptState,
                MachineLearningStoreLoadStatus.Unavailable =>
                    MachineLearningDataHealth.PersistenceTemporarilyUnavailable,
                _ => MachineLearningDataHealth.NotYetPersisted
            };
            _activityLog.Record(loadStatus switch
            {
                MachineLearningStoreLoadStatus.Corrupt =>
                    MachineLearningActivityKind.RestoreCorrupt,
                MachineLearningStoreLoadStatus.Unavailable =>
                    MachineLearningActivityKind.RestoreUnavailable,
                _ => MachineLearningActivityKind.RestoreMissing
            }, _currentSessionStartedAt);
            return;
        }

        if (state.SchemaVersion is not PersistenceSchemaVersion and
                not PreviousPersistenceSchemaVersion and
                not LegacyPersistenceSchemaVersion ||
            state.Baselines is null ||
            state.Episodes is null)
        {
            _recoveredFromCorruptState = true;
            _dataHealth = MachineLearningDataHealth.RecoveredFromCorruptState;
            _activityLog.Record(MachineLearningActivityKind.RestoreCorrupt,
                _currentSessionStartedAt, schemaVersion: state.SchemaVersion);
            return;
        }

        var ignoredInvalidState = state.ObservationCount < 0 ||
            state.ObservedDurationTicks < 0;
        _baselines.Clear();
        foreach (var persisted in state.Baselines)
        {
            if (!IsValidBaselineState(persisted))
            {
                ignoredInvalidState = true;
                continue;
            }

            var key = new MachineLearningContextKey(
                persisted.LocalHour,
                persisted.ActivityState);
            if (_baselines.ContainsKey(key))
            {
                ignoredInvalidState = true;
            }
            _baselines[key] = new OnlineBaseline(persisted);
        }

        _episodes.Clear();
        foreach (var episode in state.Episodes.TakeLast(MaximumEpisodeCount))
        {
            if (!IsValidEpisode(episode))
            {
                ignoredInvalidState = true;
                continue;
            }

            AddEpisode(episode);
        }

        if (state.ActiveEpisode is not null)
        {
            if (IsValidEpisode(state.ActiveEpisode))
            {
                AddEpisode(state.ActiveEpisode with
                {
                    Outcome = state.SchemaVersion == PersistenceSchemaVersion
                        ? "Session interrupted"
                        : "Prior session ended"
                });
            }
            else
            {
                ignoredInvalidState = true;
            }
        }

        _activeEpisode = null;
        var metadata = IsValidMetadata(state.Metadata)
            ? state.Metadata
            : null;
        if (state.Metadata is not null && metadata is null ||
            state.SchemaVersion == PersistenceSchemaVersion &&
            state.Metadata is null)
        {
            ignoredInvalidState = true;
        }
        if (state.SchemaVersion == PersistenceSchemaVersion &&
            metadata is not null &&
            !IsMetadataConsistentWithState(metadata, state))
        {
            ignoredInvalidState = true;
        }
        _observationCount = Math.Max(
            0,
            metadata?.LifetimeAcceptedObservationCount ??
                state.ObservationCount);
        _sessionAcceptedObservationCount = 0;
        _firstObservedAt = metadata?.FirstLearningAt ?? state.FirstObservedAt;
        _lastObservedAt = metadata?.LastLearningAt ?? state.LastObservedAt;
        _lastObservationAt = null;
        _lastObservationAttemptAt = null;
        var persistedDurationTicks =
            metadata?.LifetimeObservedDurationTicks ??
            state.ObservedDurationTicks;
        _observedDurationTicks = persistedDurationTicks > 0
            ? Math.Min(persistedDurationTicks, TimeSpan.MaxValue.Ticks)
            : EstimateObservedDurationTicks(_observationCount);
        var priorSessionCount = metadata?.LifetimeMachineSessionCount ??
            (_observationCount > 0 ? 1 : 0);
        _lifetimeMachineSessionCount = SaturatingIncrement(
            Math.Max(0, priorSessionCount));
        _previousMachineSessionEndedAt =
            metadata?.PreviousMachineSessionEndedAt;
        _persistedSchemaVersion = state.SchemaVersion;
        _lastPersistedAt = state.PersistedAt ?? metadata?.LastPersistedAt;
        _nextPersistenceAttemptAt = null;
        _currentSessionFinalized = false;

        _profiles.Clear();
        if (state.SchemaVersion == PersistenceSchemaVersion &&
            state.ContextProfiles is not null)
        {
            if (state.ContextProfiles.Count > MaximumContextProfileCount)
            {
                ignoredInvalidState = true;
            }

            foreach (var profile in state.ContextProfiles)
            {
                if (!IsValidProfile(profile))
                {
                    ignoredInvalidState = true;
                    continue;
                }

                if (_profiles.ContainsKey(profile.ContextKey))
                {
                    ignoredInvalidState = true;
                }
                _profiles[profile.ContextKey] = profile;
            }
        }
        else if (state.SchemaVersion == PersistenceSchemaVersion)
        {
            ignoredInvalidState = true;
        }

        var previousPatterns = state.SchemaVersion ==
                PersistenceSchemaVersion &&
            state.BroaderPatterns is not null
                ? state.BroaderPatterns.Where(IsValidPattern).ToArray()
                : [];
        if (state.SchemaVersion == PersistenceSchemaVersion &&
            state.BroaderPatterns is not null &&
            (previousPatterns.Length != state.BroaderPatterns.Count ||
             state.BroaderPatterns.Count >
                MachineLearningPolicy.MaximumPatternCount))
        {
            ignoredInvalidState = true;
        }
        else if (state.SchemaVersion == PersistenceSchemaVersion &&
            state.BroaderPatterns is null)
        {
            ignoredInvalidState = true;
        }

        ReconcileProfiles(_currentSessionStartedAt);
        _patterns = MachineRecurringPatternSynthesizer.Synthesize(
            _profiles.Values.ToArray(),
            _currentSessionStartedAt,
            previousPatterns);
        _changeVersion = 1;
        _isDirty = true;
        _recoveredFromCorruptState = ignoredInvalidState;
        _dataHealth = _recoveredFromCorruptState
            ? MachineLearningDataHealth.RecoveredFromCorruptState
            : MachineLearningDataHealth.Healthy;
        var lastAuditedCount = _activityLog.LastSuccessfulPersistenceObservationCount;
        if (lastAuditedCount is not null && _observationCount < lastAuditedCount)
        {
            _activityLog.Record(
                MachineLearningActivityKind.LearningContinuityRegressionDetected,
                _currentSessionStartedAt, _observationCount,
                detail: "Persisted count is lower than the last audit summary");
        }
        if (state.SchemaVersion != PersistenceSchemaVersion)
        {
            _activityLog.Record(MachineLearningActivityKind.RestoreMigrated,
                _currentSessionStartedAt, _observationCount, _profiles.Count,
                _episodes.Count, state.SchemaVersion);
        }
        _activityLog.Record(MachineLearningActivityKind.RestoreSucceeded,
            _currentSessionStartedAt, _observationCount, _profiles.Count,
            _episodes.Count, state.SchemaVersion);
        _activityLog.Record(MachineLearningActivityKind.SessionStarted,
            _currentSessionStartedAt, _observationCount);
    }

    public async Task<bool> SaveIfDueAsync(
        IMachineLearningStore store,
        DateTimeOffset now,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        RefreshFreshness(now);
        if (!_isDirty)
        {
            return false;
        }

        if (!force &&
            ((_nextPersistenceAttemptAt is not null &&
              now < _nextPersistenceAttemptAt.Value) ||
             (_lastPersistedAt is not null &&
              now - _lastPersistedAt.Value < PersistenceInterval)))
        {
            return false;
        }

        var snapshotVersion = _changeVersion;
        var state = CreatePersistedState(now);
        return await SaveSnapshotAsync(
            store,
            state,
            snapshotVersion,
            now,
            "Periodic",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SaveFinalSnapshotAsync(
        IMachineLearningStore store,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        RefreshFreshness(now);
        _activityLog.Record(MachineLearningActivityKind.ShutdownStarted, now,
            _observationCount, _profiles.Count, _episodes.Count);
        FinalizeCurrentSession(now);
        if (!_isDirty)
        {
            return false;
        }

        var snapshotVersion = _changeVersion;
        var state = CreatePersistedState(now);
        var saved = await SaveSnapshotAsync(
            store,
            state,
            snapshotVersion,
            now,
            "Shutdown",
            cancellationToken).ConfigureAwait(false);
        _activityLog.Record(saved
                ? MachineLearningActivityKind.ShutdownSucceeded
                : MachineLearningActivityKind.ShutdownFailed,
            now, _observationCount, _profiles.Count, _episodes.Count);
        _activityLog.Record(MachineLearningActivityKind.RuntimeStopped, now,
            _observationCount);
        return saved;
    }

    private async Task<bool> SaveSnapshotAsync(
        IMachineLearningStore store,
        MachineLearningPersistedState state,
        long snapshotVersion,
        DateTimeOffset now,
        string persistenceReason,
        CancellationToken cancellationToken)
    {
        await _persistenceGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var stopwatch = new Stopwatch();
            try
            {
                _activityLog.Record(MachineLearningActivityKind.PersistenceStarted,
                    now, state.Metadata?.LifetimeAcceptedObservationCount ??
                        state.ObservationCount,
                    state.ContextProfiles?.Count, state.Episodes.Count,
                    state.SchemaVersion, detail: persistenceReason);
                stopwatch.Start();
                await store.SaveAsync(state, cancellationToken)
                    .ConfigureAwait(false);
                stopwatch.Stop();
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                _dataHealth =
                    MachineLearningDataHealth.PersistenceTemporarilyUnavailable;
                _nextPersistenceAttemptAt =
                    now + PersistenceFailureRetryInterval;
                _activityLog.Record(MachineLearningActivityKind.PersistenceFailed,
                    now, state.Metadata?.LifetimeAcceptedObservationCount ??
                        state.ObservationCount,
                    detail: persistenceReason + " store unavailable");
                return false;
            }

            _lastPersistedAt = now;
            _persistedSchemaVersion = state.SchemaVersion;
            _nextPersistenceAttemptAt = null;
            _dataHealth = _recoveredFromCorruptState
                ? MachineLearningDataHealth.RecoveredFromCorruptState
                : MachineLearningDataHealth.Healthy;
            if (_changeVersion == snapshotVersion)
            {
                _isDirty = false;
            }
            _activityLog.Record(MachineLearningActivityKind.PersistenceSucceeded,
                now, state.Metadata?.LifetimeAcceptedObservationCount ??
                    state.ObservationCount, state.ContextProfiles?.Count,
                state.Episodes.Count, state.SchemaVersion,
                detail: persistenceReason,
                byteCount: (store as IMachineLearningStoreSaveDiagnostics)?
                    .LastSavedByteCount,
                durationMilliseconds: stopwatch.ElapsedMilliseconds);
            return true;
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    private MachineLearningBaseline? GetBaseline(
        MachineLearningObservation observation,
        DateTimeOffset now)
    {
        var key = new MachineLearningContextKey(
            observation.Timestamp.ToLocalTime().Hour,
            observation.ActivityState);
        return _baselines.TryGetValue(key, out var baseline)
            ? baseline.ToSnapshot(key, now)
            : null;
    }

    private IReadOnlyList<MachineLearningBaseline> GetBaselines(
        DateTimeOffset now) => _baselines
        .Select(pair => pair.Value.ToSnapshot(pair.Key, now))
        .ToArray();

    private IReadOnlyList<MachineLearningContextProfile> GetProfiles(
        DateTimeOffset now)
    {
        RefreshFreshness(now);
        return _profiles.Values.ToArray();
    }

    private IReadOnlyList<MachineLearningRecurringPattern> GetPatterns(
        DateTimeOffset now)
    {
        RefreshFreshness(now);
        return _patterns;
    }

    private bool UpdateProfile(
        MachineLearningContextKey key,
        OnlineBaseline baseline,
        DateTimeOffset now)
    {
        var snapshot = baseline.ToSnapshot(key, now);
        if (snapshot.Confidence == MachineLearningConfidence.Calibrating)
        {
            return false;
        }

        _profiles.TryGetValue(key, out var existing);
        var candidate = CreateProfile(snapshot, existing, now);
        if (existing is null)
        {
            _profiles[key] = candidate;
            return true;
        }

        var materiallyChanged = IsMateriallyDifferent(existing, candidate);
        var reinforcementDue = IsProfileReinforcementDue(
            existing,
            candidate);
        if (!materiallyChanged && !reinforcementDue)
        {
            return false;
        }

        _profiles[key] = candidate with
        {
            CreatedAt = existing.CreatedAt,
            LastReinforcedAt = candidate.LastObservedAt,
            LastMateriallyChangedAt = materiallyChanged
                ? candidate.LastObservedAt
                : existing.LastMateriallyChangedAt
        };
        return true;
    }

    private void ReconcileProfiles(DateTimeOffset now)
    {
        var validKeys = new HashSet<MachineLearningContextKey>();
        foreach (var pair in _baselines)
        {
            var snapshot = pair.Value.ToSnapshot(pair.Key, now);
            if (snapshot.Confidence == MachineLearningConfidence.Calibrating)
            {
                continue;
            }

            validKeys.Add(pair.Key);
            _profiles.TryGetValue(pair.Key, out var existing);
            var reconciled = CreateProfile(snapshot, existing, now);
            if (existing is not null)
            {
                var materiallyChanged = IsMateriallyDifferent(
                    existing,
                    reconciled);
                if (!materiallyChanged &&
                    !IsProfileReinforcementDue(existing, reconciled))
                {
                    continue;
                }

                reconciled = reconciled with
                {
                    CreatedAt = existing.CreatedAt,
                    LastReinforcedAt = snapshot.LastObservedAt,
                    LastMateriallyChangedAt = materiallyChanged
                        ? snapshot.LastObservedAt
                        : existing.LastMateriallyChangedAt
                };
            }
            _profiles[pair.Key] = reconciled;
        }

        foreach (var key in _profiles.Keys.Where(key =>
            !validKeys.Contains(key)).ToArray())
        {
            _profiles.Remove(key);
        }
    }

    private static MachineLearningContextProfile CreateProfile(
        MachineLearningBaseline baseline,
        MachineLearningContextProfile? existing,
        DateTimeOffset now)
    {
        var createdAt = existing?.CreatedAt ?? now;
        var reinforcedAt = existing?.LastReinforcedAt ??
            baseline.LastObservedAt;
        var materiallyChangedAt = existing?.LastMateriallyChangedAt ?? now;
        return new MachineLearningContextProfile(
            baseline.LocalHour,
            baseline.ActivityState,
            baseline.Confidence,
            baseline.Freshness,
            baseline.SampleCount,
            baseline.ObservedDurationTicks,
            baseline.ObservedDayCount,
            baseline.FirstObservedAt,
            baseline.LastObservedAt,
            new MachineLearningMetricProfile(
                baseline.AdaptiveCpuMean,
                baseline.AdaptiveCpuStandardDeviation,
                baseline.CpuTypicalRange),
            new MachineLearningMetricProfile(
                baseline.AdaptiveMemoryMean,
                baseline.AdaptiveMemoryStandardDeviation,
                baseline.MemoryTypicalRange),
            baseline.DominantNetworkActivityClass,
            baseline.DominantNetworkActivityCount,
            baseline.NetworkObservationCount,
            createdAt,
            reinforcedAt,
            materiallyChangedAt);
    }

    private static bool IsMateriallyDifferent(
        MachineLearningContextProfile existing,
        MachineLearningContextProfile candidate) =>
        existing.Confidence != candidate.Confidence ||
        existing.Freshness != candidate.Freshness ||
        existing.DominantNetworkActivityClass !=
            candidate.DominantNetworkActivityClass ||
        Math.Abs(existing.Cpu.AdaptiveMean -
            candidate.Cpu.AdaptiveMean) >=
            MachineLearningPolicy.MaterialMeanShiftPercentagePoints ||
        Math.Abs(existing.Memory.AdaptiveMean -
            candidate.Memory.AdaptiveMean) >=
            MachineLearningPolicy.MaterialMeanShiftPercentagePoints ||
        RangeMateriallyChanged(
            existing.Cpu.TypicalRange,
            candidate.Cpu.TypicalRange) ||
        RangeMateriallyChanged(
            existing.Memory.TypicalRange,
            candidate.Memory.TypicalRange);

    private static bool IsProfileReinforcementDue(
        MachineLearningContextProfile existing,
        MachineLearningContextProfile candidate)
    {
        var newEvidence = candidate.LifetimeSampleCount >=
                existing.LifetimeSampleCount
            ? candidate.LifetimeSampleCount - existing.LifetimeSampleCount
            : long.MaxValue;
        return newEvidence >=
                MachineLearningPolicy.ProfileReinforcementSampleInterval ||
            candidate.DistinctObservedDayCount !=
                existing.DistinctObservedDayCount ||
            candidate.Confidence != existing.Confidence ||
            candidate.Freshness != existing.Freshness ||
            candidate.DominantNetworkActivityClass !=
                existing.DominantNetworkActivityClass;
    }

    private static bool RangeMateriallyChanged(
        MachineLearningRange? existing,
        MachineLearningRange? candidate)
    {
        if (existing is null || candidate is null)
        {
            return existing != candidate;
        }

        return Math.Abs(existing.Low - candidate.Low) >=
                MachineLearningPolicy.MaterialRangeBoundShiftPercentagePoints ||
            Math.Abs(existing.High - candidate.High) >=
                MachineLearningPolicy.MaterialRangeBoundShiftPercentagePoints;
    }

    private void RefreshFreshness(DateTimeOffset now)
    {
        var changed = false;
        foreach (var pair in _profiles.ToArray())
        {
            var freshness = MachineLearningPolicy.GetFreshness(
                pair.Value.LastObservedAt,
                now);
            if (freshness == pair.Value.Freshness)
            {
                continue;
            }

            _profiles[pair.Key] = pair.Value with
            {
                Freshness = freshness,
                LastMateriallyChangedAt = now
            };
            changed = true;
        }

        if (changed)
        {
            RecomputePatterns(now);
            MarkDirty();
        }
    }

    private void RecomputePatterns(DateTimeOffset now)
    {
        _patterns = MachineRecurringPatternSynthesizer.Synthesize(
            _profiles.Values.ToArray(),
            now,
            _patterns);
    }

    private void UpdateEpisodes(
        MachineLearningObservation observation,
        DateTimeOffset localTimestamp)
    {
        var context = new EpisodeContext(
            observation.ActivityState,
            observation.OverallState,
            observation.FindingKeys.OrderBy(value => value,
                StringComparer.Ordinal).ToArray(),
            localTimestamp.Hour,
            DateOnly.FromDateTime(localTimestamp.DateTime));

        if (_activeEpisode is null)
        {
            _activeEpisode = new ActiveEpisode(observation, context);
            return;
        }

        if (_activeEpisode.Context.Matches(context))
        {
            _activeEpisode.Add(observation);
            return;
        }

        var outcome = _activeEpisode.Context.OverallState !=
                MachineOverallState.Stable &&
            context.OverallState == MachineOverallState.Stable
                ? "Recovered to Stable"
                : null;
        AddEpisode(_activeEpisode.Complete(
            observation.Timestamp,
            outcome));
        _activeEpisode = new ActiveEpisode(observation, context);
    }

    private void FinalizeCurrentSession(DateTimeOffset endedAt)
    {
        if (_currentSessionFinalized)
        {
            return;
        }

        if (_activeEpisode is not null)
        {
            AddEpisode(_activeEpisode.Complete(
                _activeEpisode.LastObservedAt,
                "Session ended"));
            _activeEpisode = null;
        }
        _previousMachineSessionEndedAt = endedAt;
        _currentSessionFinalized = true;
        MarkDirty();
    }

    private void AddEpisode(MachineLearningEpisode episode)
    {
        _episodes.Enqueue(episode);
        while (_episodes.Count > MaximumEpisodeCount)
        {
            _episodes.Dequeue();
        }
    }

    private MachineLearningPersistedState CreatePersistedState(
        DateTimeOffset persistedAt) => new(
            PersistenceSchemaVersion,
            _baselines.Select(pair =>
                pair.Value.ToState(pair.Key)).ToArray(),
            _episodes.ToArray(),
            _observationCount,
            _firstObservedAt,
            _lastObservedAt,
            persistedAt,
            _observedDurationTicks,
            _activeEpisode?.Snapshot(),
            new MachineLearningMetadataState(
                _observationCount,
                _observedDurationTicks,
                _lifetimeMachineSessionCount,
                _firstObservedAt,
                _lastObservedAt,
                _currentSessionStartedAt,
                _previousMachineSessionEndedAt,
                persistedAt),
            _profiles.Values
                .OrderBy(profile => profile.LocalHour)
                .ThenBy(profile => profile.ActivityState)
                .ToArray(),
            _patterns);

    private MachineLearningMetadata CreateMetadata() => new(
        _observationCount,
        ToDuration(_observedDurationTicks),
        _lifetimeMachineSessionCount,
        _firstObservedAt,
        _lastObservedAt,
        _currentSessionStartedAt,
        _previousMachineSessionEndedAt,
        _lastPersistedAt,
        _persistedSchemaVersion);

    private void MarkDirty()
    {
        _changeVersion = SaturatingIncrement(_changeVersion);
        _isDirty = true;
        _activityLog.Record(MachineLearningActivityKind.MarkedDirty,
            DateTimeOffset.UtcNow, _observationCount);
    }

    private static bool IsValidObservation(
        MachineLearningObservation observation) =>
        double.IsFinite(observation.CpuUsagePercent) &&
        observation.CpuUsagePercent is >= 0d and <= 100d &&
        double.IsFinite(observation.MemoryUsagePercent) &&
        observation.MemoryUsagePercent is >= 0d and <= 100d &&
        Enum.IsDefined(observation.ActivityState) &&
        Enum.IsDefined(observation.OverallState) &&
        Enum.IsDefined(observation.NetworkActivityClass) &&
        observation.FindingKeys is not null &&
        IsValidOptionalPercentage(observation.SystemVolumeFreePercent) &&
        IsValidOptionalRate(observation.ReceiveBytesPerSecond) &&
        IsValidOptionalRate(observation.SendBytesPerSecond);

    private static bool IsValidOptionalPercentage(double? value) =>
        value is null ||
        double.IsFinite(value.Value) && value.Value is >= 0d and <= 100d;

    private static bool IsValidOptionalRate(double? value) =>
        value is null || double.IsFinite(value.Value) && value.Value >= 0d;

    private static bool IsValidBaselineState(
        MachineLearningBaselineState? state) =>
        state is not null &&
        state.LocalHour is >= 0 and <= 23 &&
        Enum.IsDefined(state.ActivityState) &&
        state.SampleCount > 0 &&
        IsValidPercentage(state.CpuMean) &&
        double.IsFinite(state.CpuM2) && state.CpuM2 >= 0d &&
        IsValidPercentage(state.MemoryMean) &&
        double.IsFinite(state.MemoryM2) && state.MemoryM2 >= 0d &&
        HasValidNetworkCounts(state) &&
        state.ObservedDayCount >= 0 &&
        (state.ObservedDayCount == 0 ||
            state.ObservedDayCount <= state.SampleCount) &&
        (state.ObservedLocalDates is null ||
            state.ObservedLocalDates.Count <= state.SampleCount) &&
        state.ObservedDurationTicks >= 0 &&
        state.AdaptiveSampleCount >= 0 &&
        state.AdaptiveSampleCount <= state.SampleCount &&
        state.LastObservedAt >= state.FirstObservedAt &&
        IsValidAdaptiveState(state);

    private static bool IsValidAdaptiveState(
        MachineLearningBaselineState state)
    {
        if (state.AdaptiveSampleCount <= 0)
        {
            return true;
        }

        return state.AdaptiveLastUpdatedAt is not null &&
            state.AdaptiveLastUpdatedAt >= state.FirstObservedAt &&
            state.AdaptiveLastUpdatedAt <= state.LastObservedAt &&
            state.AdaptiveCpuMean is { } cpuMean &&
            IsValidPercentage(cpuMean) &&
            state.AdaptiveCpuVariance is { } cpuVariance &&
            double.IsFinite(cpuVariance) && cpuVariance >= 0d &&
            state.AdaptiveMemoryMean is { } memoryMean &&
            IsValidPercentage(memoryMean) &&
            state.AdaptiveMemoryVariance is { } memoryVariance &&
            double.IsFinite(memoryVariance) && memoryVariance >= 0d;
    }

    private static bool IsValidProfile(
        MachineLearningContextProfile? profile) =>
        profile is not null &&
        profile.LocalHour is >= 0 and <= 23 &&
        Enum.IsDefined(profile.ActivityState) &&
        Enum.IsDefined(profile.Confidence) &&
        profile.Confidence != MachineLearningConfidence.Calibrating &&
        Enum.IsDefined(profile.Freshness) &&
        profile.LifetimeSampleCount >= ProvisionalSampleCount &&
        profile.LifetimeObservedDurationTicks >= 0 &&
        profile.DistinctObservedDayCount > 0 &&
        profile.LastObservedAt >= profile.FirstObservedAt &&
        profile.Cpu is not null &&
        profile.Memory is not null &&
        IsValidPercentage(profile.Cpu.AdaptiveMean) &&
        IsValidStandardDeviation(profile.Cpu.AdaptiveStandardDeviation) &&
        IsValidRange(profile.Cpu.TypicalRange) &&
        IsValidPercentage(profile.Memory.AdaptiveMean) &&
        IsValidStandardDeviation(profile.Memory.AdaptiveStandardDeviation) &&
        IsValidRange(profile.Memory.TypicalRange) &&
        IsValidLearnedNetworkClass(
            profile.DominantNetworkActivityClass) &&
        profile.DominantNetworkActivityCount >= 0 &&
        profile.NetworkObservationCount >=
            profile.DominantNetworkActivityCount &&
        profile.NetworkObservationCount <= profile.LifetimeSampleCount &&
        profile.LastReinforcedAt >= profile.FirstObservedAt &&
            profile.LastReinforcedAt <= profile.LastObservedAt;

    private static bool IsValidMetadata(
        MachineLearningMetadataState? metadata)
    {
        if (metadata is null)
        {
            return false;
        }

        if (metadata.LifetimeAcceptedObservationCount < 0 ||
            metadata.LifetimeObservedDurationTicks < 0 ||
            metadata.LifetimeMachineSessionCount <= 0)
        {
            return false;
        }

        var hasLearning = metadata.LifetimeAcceptedObservationCount > 0;
        if (hasLearning != (metadata.FirstLearningAt is not null) ||
            hasLearning != (metadata.LastLearningAt is not null))
        {
            return false;
        }

        return (metadata.FirstLearningAt is null ||
            metadata.LastLearningAt is null ||
            metadata.LastLearningAt >= metadata.FirstLearningAt) &&
            (metadata.LastPersistedAt is null ||
             metadata.LastLearningAt is null ||
             metadata.LastPersistedAt >= metadata.LastLearningAt);
    }

    private static bool IsMetadataConsistentWithState(
        MachineLearningMetadataState metadata,
        MachineLearningPersistedState state) =>
        metadata.LifetimeAcceptedObservationCount ==
            state.ObservationCount &&
        metadata.LifetimeObservedDurationTicks ==
            state.ObservedDurationTicks &&
        metadata.FirstLearningAt == state.FirstObservedAt &&
        metadata.LastLearningAt == state.LastObservedAt &&
        metadata.LastPersistedAt == state.PersistedAt;

    private static bool IsValidPattern(
        MachineLearningRecurringPattern? pattern) =>
        pattern is not null &&
        Enum.IsDefined(pattern.ActivityState) &&
        pattern.StartHour is >= 0 and <= 23 &&
        pattern.EndHourExclusive is >= 0 and <= 23 &&
        pattern.MemberContexts is not null &&
        pattern.MemberContexts.Count is >=
            MachineLearningPolicy.MinimumPatternProfileCount and < 24 &&
        pattern.MemberContexts.Distinct().Count() ==
            pattern.MemberContexts.Count &&
        IsValidPatternContexts(pattern) &&
        Enum.IsDefined(pattern.Confidence) &&
        Enum.IsDefined(pattern.Freshness) &&
        pattern.CombinedSampleCount > 0 &&
        pattern.MinimumDistinctObservedDayCount > 0 &&
        pattern.CpuTypicalRange is not null &&
        IsValidRange(pattern.CpuTypicalRange) &&
        pattern.MemoryTypicalRange is not null &&
        IsValidRange(pattern.MemoryTypicalRange) &&
        IsValidLearnedNetworkClass(
            pattern.DominantNetworkActivityClass) &&
        pattern.DominantNetworkActivityCount >= 0 &&
        pattern.NetworkObservationCount >=
            pattern.DominantNetworkActivityCount;

    private static bool IsValidEpisode(MachineLearningEpisode? episode) =>
        episode is not null &&
        episode.SampleCount > 0 &&
        episode.EndedAt >= episode.StartedAt &&
        Enum.IsDefined(episode.ActivityState) &&
        Enum.IsDefined(episode.OverallState) &&
        double.IsFinite(episode.AverageCpuUsagePercent) &&
        double.IsFinite(episode.PeakCpuUsagePercent) &&
        double.IsFinite(episode.AverageMemoryUsagePercent) &&
        episode.FindingKeys is not null;

    private static bool HasValidNetworkCounts(
        MachineLearningBaselineState state)
    {
        if (state.NetworkQuietSampleCount < 0 ||
            state.NetworkLightSampleCount < 0 ||
            state.NetworkActiveSampleCount < 0 ||
            state.NetworkUnavailableSampleCount < 0)
        {
            return false;
        }

        var total = SaturatingSum(
        [
            state.NetworkQuietSampleCount,
            state.NetworkLightSampleCount,
            state.NetworkActiveSampleCount,
            state.NetworkUnavailableSampleCount
        ]);
        return total <= state.SampleCount;
    }

    private static bool IsValidPercentage(double value) =>
        double.IsFinite(value) && value is >= 0d and <= 100d;

    private static bool IsValidStandardDeviation(double value) =>
        double.IsFinite(value) && value >= 0d;

    private static bool IsValidLearnedNetworkClass(
        MachineNetworkActivityClass? activityClass) =>
        activityClass is null or
            MachineNetworkActivityClass.Quiet or
            MachineNetworkActivityClass.Light or
            MachineNetworkActivityClass.Active;

    private static bool IsValidPatternContexts(
        MachineLearningRecurringPattern pattern)
    {
        if (pattern.CrossesMidnight !=
            (pattern.EndHourExclusive <= pattern.StartHour))
        {
            return false;
        }

        for (var index = 0; index < pattern.MemberContexts.Count; index++)
        {
            var context = pattern.MemberContexts[index];
            if (context.LocalHour is < 0 or > 23 ||
                context.ActivityState != pattern.ActivityState ||
                index > 0 && context.LocalHour !=
                    (pattern.MemberContexts[index - 1].LocalHour + 1) % 24)
            {
                return false;
            }
        }

        return pattern.StartHour ==
                pattern.MemberContexts[0].LocalHour &&
            pattern.EndHourExclusive ==
                (pattern.MemberContexts[^1].LocalHour + 1) % 24;
    }

    private static bool IsValidRange(MachineLearningRange? range) =>
        range is null ||
        IsValidPercentage(range.Low) &&
        IsValidPercentage(range.High) &&
        range.High >= range.Low;

    private static long AddDurationTicks(long current, long additional) =>
        current >= TimeSpan.MaxValue.Ticks - additional
            ? TimeSpan.MaxValue.Ticks
            : current + additional;

    private static long EstimateObservedDurationTicks(long observationCount) =>
        observationCount <= 0
            ? 0
            : observationCount >=
                TimeSpan.MaxValue.Ticks / ObservationInterval.Ticks
                ? TimeSpan.MaxValue.Ticks
                : observationCount * ObservationInterval.Ticks;

    private static long SaturatingIncrement(long value) =>
        value == long.MaxValue ? long.MaxValue : value + 1;

    private static long SaturatingSum(IEnumerable<long> values)
    {
        var total = 0L;
        foreach (var value in values)
        {
            total = total >= long.MaxValue - value
                ? long.MaxValue
                : total + value;
        }
        return total;
    }

    private static TimeSpan ToDuration(long ticks) => TimeSpan.FromTicks(
        Math.Clamp(ticks, 0, TimeSpan.MaxValue.Ticks));

    private sealed class OnlineBaseline
    {
        public OnlineBaseline(DateTimeOffset observedAt)
        {
            FirstObservedAt = observedAt;
            LastObservedAt = observedAt;
        }

        public OnlineBaseline(MachineLearningBaselineState state)
        {
            Count = state.SampleCount;
            CpuMean = state.CpuMean;
            CpuM2 = state.CpuM2;
            MemoryMean = state.MemoryMean;
            MemoryM2 = state.MemoryM2;
            NetworkQuietSampleCount = state.NetworkQuietSampleCount;
            NetworkLightSampleCount = state.NetworkLightSampleCount;
            NetworkActiveSampleCount = state.NetworkActiveSampleCount;
            NetworkUnavailableSampleCount =
                state.NetworkUnavailableSampleCount;
            FirstObservedAt = state.FirstObservedAt;
            LastObservedAt = state.LastObservedAt;
            ObservedDayCount = GetObservedDayCount(state);
            LastObservedLocalDate = state.LastObservedLocalDate ??
                GetLastObservedLocalDate(state);
            ObservedDurationTicks = state.ObservedDurationTicks > 0
                ? Math.Min(state.ObservedDurationTicks,
                    TimeSpan.MaxValue.Ticks)
                : EstimateObservedDurationTicks(state.SampleCount);

            if (state.AdaptiveSampleCount > 0 &&
                state.AdaptiveCpuMean is { } adaptiveCpuMean &&
                state.AdaptiveCpuVariance is { } adaptiveCpuVariance &&
                state.AdaptiveMemoryMean is { } adaptiveMemoryMean &&
                state.AdaptiveMemoryVariance is { } adaptiveMemoryVariance &&
                state.AdaptiveLastUpdatedAt is { } adaptiveLastUpdatedAt)
            {
                AdaptiveSampleCount = state.AdaptiveSampleCount;
                AdaptiveCpuMean = adaptiveCpuMean;
                AdaptiveCpuVariance = adaptiveCpuVariance;
                AdaptiveMemoryMean = adaptiveMemoryMean;
                AdaptiveMemoryVariance = adaptiveMemoryVariance;
                AdaptiveLastUpdatedAt = adaptiveLastUpdatedAt;
            }
            else
            {
                AdaptiveSampleCount = state.SampleCount;
                AdaptiveCpuMean = state.CpuMean;
                AdaptiveCpuVariance = Variance(state.CpuM2, state.SampleCount);
                AdaptiveMemoryMean = state.MemoryMean;
                AdaptiveMemoryVariance = Variance(
                    state.MemoryM2,
                    state.SampleCount);
                AdaptiveLastUpdatedAt = state.LastObservedAt;
            }
        }

        public long Count { get; private set; }
        public double CpuMean { get; private set; }
        public double CpuM2 { get; private set; }
        public double MemoryMean { get; private set; }
        public double MemoryM2 { get; private set; }
        public long NetworkQuietSampleCount { get; private set; }
        public long NetworkLightSampleCount { get; private set; }
        public long NetworkActiveSampleCount { get; private set; }
        public long NetworkUnavailableSampleCount { get; private set; }
        public DateTimeOffset FirstObservedAt { get; }
        public DateTimeOffset LastObservedAt { get; private set; }
        public int ObservedDayCount { get; private set; }
        public DateOnly? LastObservedLocalDate { get; private set; }
        public long ObservedDurationTicks { get; private set; }
        public double AdaptiveCpuMean { get; private set; }
        public double AdaptiveCpuVariance { get; private set; }
        public double AdaptiveMemoryMean { get; private set; }
        public double AdaptiveMemoryVariance { get; private set; }
        public long AdaptiveSampleCount { get; private set; }
        public DateTimeOffset? AdaptiveLastUpdatedAt { get; private set; }

        public void Add(MachineLearningObservation observation)
        {
            Count = SaturatingIncrement(Count);
            var cpuDelta = observation.CpuUsagePercent - CpuMean;
            CpuMean += cpuDelta / Count;
            CpuM2 += cpuDelta * (observation.CpuUsagePercent - CpuMean);
            var memoryDelta = observation.MemoryUsagePercent - MemoryMean;
            MemoryMean += memoryDelta / Count;
            MemoryM2 += memoryDelta *
                (observation.MemoryUsagePercent - MemoryMean);
            switch (observation.NetworkActivityClass)
            {
                case MachineNetworkActivityClass.Quiet:
                    NetworkQuietSampleCount = SaturatingIncrement(
                        NetworkQuietSampleCount);
                    break;
                case MachineNetworkActivityClass.Light:
                    NetworkLightSampleCount = SaturatingIncrement(
                        NetworkLightSampleCount);
                    break;
                case MachineNetworkActivityClass.Active:
                    NetworkActiveSampleCount = SaturatingIncrement(
                        NetworkActiveSampleCount);
                    break;
                default:
                    NetworkUnavailableSampleCount = SaturatingIncrement(
                        NetworkUnavailableSampleCount);
                    break;
            }

            AddAdaptiveObservation(observation);
            LastObservedAt = observation.Timestamp;
            var localDate = ToLocalDate(observation.Timestamp);
            if (LastObservedLocalDate is null ||
                localDate > LastObservedLocalDate.Value)
            {
                ObservedDayCount = ObservedDayCount == int.MaxValue
                    ? int.MaxValue
                    : ObservedDayCount + 1;
                LastObservedLocalDate = localDate;
            }
            ObservedDurationTicks = AddDurationTicks(
                ObservedDurationTicks,
                ObservationInterval.Ticks);
        }

        public MachineLearningBaseline ToSnapshot(
            MachineLearningContextKey key,
            DateTimeOffset now) => new(
            key.LocalHour,
            key.ActivityState,
            Count,
            CpuMean,
            StandardDeviation(CpuM2, Count),
            MemoryMean,
            StandardDeviation(MemoryM2, Count),
            FirstObservedAt,
            LastObservedAt,
            ObservedDayCount,
            GetConfidence(Count, ObservedDayCount),
            NetworkQuietSampleCount,
            NetworkLightSampleCount,
            NetworkActiveSampleCount,
            NetworkUnavailableSampleCount,
            ObservedDurationTicks,
            AdaptiveCpuMean,
            Math.Sqrt(Math.Max(0d, AdaptiveCpuVariance)),
            AdaptiveMemoryMean,
            Math.Sqrt(Math.Max(0d, AdaptiveMemoryVariance)),
            AdaptiveSampleCount,
            AdaptiveLastUpdatedAt,
            MachineLearningPolicy.GetFreshness(LastObservedAt, now));

        public MachineLearningBaselineState ToState(
            MachineLearningContextKey key) => new(
            key.LocalHour,
            key.ActivityState,
            Count,
            CpuMean,
            CpuM2,
            MemoryMean,
            MemoryM2,
            FirstObservedAt,
            LastObservedAt,
            null,
            NetworkQuietSampleCount,
            NetworkLightSampleCount,
            NetworkActiveSampleCount,
            NetworkUnavailableSampleCount,
            ObservedDayCount,
            LastObservedLocalDate,
            ObservedDurationTicks,
            AdaptiveCpuMean,
            AdaptiveCpuVariance,
            AdaptiveMemoryMean,
            AdaptiveMemoryVariance,
            AdaptiveSampleCount,
            AdaptiveLastUpdatedAt);

        private void AddAdaptiveObservation(
            MachineLearningObservation observation)
        {
            if (AdaptiveSampleCount == 0 || AdaptiveLastUpdatedAt is null)
            {
                AdaptiveCpuMean = observation.CpuUsagePercent;
                AdaptiveCpuVariance = 0d;
                AdaptiveMemoryMean = observation.MemoryUsagePercent;
                AdaptiveMemoryVariance = 0d;
                AdaptiveSampleCount = 1;
                AdaptiveLastUpdatedAt = observation.Timestamp;
                return;
            }

            var elapsed = observation.Timestamp <=
                    AdaptiveLastUpdatedAt.Value
                ? TimeSpan.Zero
                : observation.Timestamp - AdaptiveLastUpdatedAt.Value;
            var decay = elapsed <= TimeSpan.Zero
                ? 1d
                : Math.Exp(-Math.Log(2d) *
                    elapsed.TotalSeconds /
                    MachineLearningPolicy.AdaptiveHalfLife.TotalSeconds);
            var observationWeight = 1d - decay;
            var cpuMean = AdaptiveCpuMean;
            var cpuVariance = AdaptiveCpuVariance;
            UpdateAdaptive(
                observation.CpuUsagePercent,
                decay,
                observationWeight,
                ref cpuMean,
                ref cpuVariance);
            AdaptiveCpuMean = cpuMean;
            AdaptiveCpuVariance = cpuVariance;
            var memoryMean = AdaptiveMemoryMean;
            var memoryVariance = AdaptiveMemoryVariance;
            UpdateAdaptive(
                observation.MemoryUsagePercent,
                decay,
                observationWeight,
                ref memoryMean,
                ref memoryVariance);
            AdaptiveMemoryMean = memoryMean;
            AdaptiveMemoryVariance = memoryVariance;
            AdaptiveSampleCount = SaturatingIncrement(AdaptiveSampleCount);
            AdaptiveLastUpdatedAt = observation.Timestamp;
        }

        private static void UpdateAdaptive(
            double value,
            double previousWeight,
            double observationWeight,
            ref double mean,
            ref double variance)
        {
            var previousMean = mean;
            var updatedMean = previousWeight * previousMean +
                observationWeight * value;
            var updatedVariance = previousWeight *
                    (variance + Math.Pow(previousMean - updatedMean, 2d)) +
                observationWeight * Math.Pow(value - updatedMean, 2d);
            mean = Math.Clamp(updatedMean, 0d, 100d);
            variance = Math.Max(0d, updatedVariance);
        }

        private static int GetObservedDayCount(
            MachineLearningBaselineState state)
        {
            if (state.ObservedDayCount > 0)
            {
                return state.ObservedDayCount;
            }

            if (state.ObservedLocalDates is { Count: > 0 })
            {
                return state.ObservedLocalDates.Distinct().Count();
            }

            return ToLocalDate(state.FirstObservedAt) ==
                ToLocalDate(state.LastObservedAt) ? 1 : 2;
        }

        private static DateOnly GetLastObservedLocalDate(
            MachineLearningBaselineState state) =>
            state.ObservedLocalDates is { Count: > 0 }
                ? state.ObservedLocalDates.Max()
                : ToLocalDate(state.LastObservedAt);

        private static DateOnly ToLocalDate(DateTimeOffset timestamp) =>
            DateOnly.FromDateTime(timestamp.ToLocalTime().DateTime);

        private static double StandardDeviation(double m2, long count) =>
            count < 2 ? 0d : Math.Sqrt(Variance(m2, count));

        private static double Variance(double m2, long count) =>
            count < 2 ? 0d : Math.Max(0d, m2 / (count - 1));

        private static MachineLearningConfidence GetConfidence(
            long count,
            int observedDayCount) =>
            count >= EstablishedSampleCount &&
            observedDayCount >= EstablishedObservedDayCount
                ? MachineLearningConfidence.Established
                : count >= ProvisionalSampleCount
                    ? MachineLearningConfidence.Provisional
                    : MachineLearningConfidence.Calibrating;
    }

    private sealed class ActiveEpisode
    {
        private double _cpuTotal;
        private double _memoryTotal;
        private double _peakCpu;
        private int _sampleCount;
        private readonly DateTimeOffset _startedAt;

        public ActiveEpisode(
            MachineLearningObservation observation,
            EpisodeContext context)
        {
            Context = context;
            _startedAt = observation.Timestamp;
            Add(observation);
        }

        public EpisodeContext Context { get; }
        public DateTimeOffset LastObservedAt { get; private set; }

        public void Add(MachineLearningObservation observation)
        {
            _sampleCount++;
            _cpuTotal += observation.CpuUsagePercent;
            _memoryTotal += observation.MemoryUsagePercent;
            _peakCpu = Math.Max(_peakCpu, observation.CpuUsagePercent);
            LastObservedAt = observation.Timestamp;
        }

        public MachineLearningEpisode Complete(
            DateTimeOffset endedAt,
            string? outcome) => CreateSnapshot(endedAt, outcome);

        public MachineLearningEpisode Snapshot() =>
            CreateSnapshot(LastObservedAt, null);

        private MachineLearningEpisode CreateSnapshot(
            DateTimeOffset endedAt,
            string? outcome) => new(
                _startedAt,
                endedAt,
                Context.ActivityState,
                Context.OverallState,
                _sampleCount,
                _cpuTotal / _sampleCount,
                _peakCpu,
                _memoryTotal / _sampleCount,
                Context.FindingKeys,
                outcome);
    }

    private sealed class EpisodeContext
    {
        public EpisodeContext(
            MachineUserActivityState activityState,
            MachineOverallState overallState,
            IReadOnlyList<string> findingKeys,
            int localHour,
            DateOnly localDate)
        {
            ActivityState = activityState;
            OverallState = overallState;
            FindingKeys = findingKeys;
            LocalHour = localHour;
            LocalDate = localDate;
        }

        public MachineUserActivityState ActivityState { get; }
        public MachineOverallState OverallState { get; }
        public IReadOnlyList<string> FindingKeys { get; }
        public int LocalHour { get; }
        public DateOnly LocalDate { get; }

        public bool Matches(EpisodeContext? other) =>
            other is not null &&
            ActivityState == other.ActivityState &&
            OverallState == other.OverallState &&
            LocalHour == other.LocalHour &&
            LocalDate == other.LocalDate &&
            FindingKeys.SequenceEqual(
                other.FindingKeys,
                StringComparer.Ordinal);
    }
}
