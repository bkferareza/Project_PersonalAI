using System.Diagnostics;

namespace Machine.Core;

public sealed class MachineLearningService
{
    public const int MaximumObservationCount = 2_880;
    public const int MaximumEpisodeCount = 200;
    public const int PersistenceSchemaVersion = 2;
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
    private readonly Dictionary<BaselineKey, OnlineBaseline> _baselines = new();
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);
    private ActiveEpisode? _activeEpisode;
    private DateTimeOffset? _lastObservationAt;
    private DateTimeOffset? _lastObservationAttemptAt;
    private DateTimeOffset? _firstObservedAt;
    private DateTimeOffset? _lastObservedAt;
    private DateTimeOffset? _lastPersistedAt;
    private DateTimeOffset? _nextPersistenceAttemptAt;
    private long _observationCount;
    private long _sessionAcceptedObservationCount;
    private long _observedDurationTicks;
    private long _throttledObservationCount;
    private long _missingPrerequisiteCount;
    private long _changeVersion;
    private bool _isDirty;
    private bool _recoveredFromCorruptState;
    private MachineLearningDataHealth _dataHealth =
        MachineLearningDataHealth.NotYetPersisted;

    public IReadOnlyList<MachineLearningObservation> Journal =>
        _journal.ToArray();

    public IReadOnlyList<MachineLearningEpisode> RecentEpisodes =>
        _episodes.ToArray();

    public IReadOnlyList<MachineLearningBaseline> Baselines =>
        _baselines.Select(pair => pair.Value.ToSnapshot(pair.Key)).ToArray();

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
            _throttledObservationCount++;
            return false;
        }

        _lastObservationAttemptAt = timestamp;
        return true;
    }

    public void RecordMissingPrerequisite()
    {
        _missingPrerequisiteCount++;
    }

    public bool Observe(MachineLearningObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!CanObserveAt(observation.Timestamp))
        {
            _throttledObservationCount++;
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
        _observationCount++;
        _sessionAcceptedObservationCount++;
        _observedDurationTicks = AddDurationTicks(
            _observedDurationTicks,
            ObservationInterval.Ticks);
        _journal.Enqueue(observation);
        while (_journal.Count > MaximumObservationCount)
        {
            _journal.Dequeue();
        }

        var localTimestamp = observation.Timestamp.ToLocalTime();
        var key = new BaselineKey(
            localTimestamp.Hour,
            observation.ActivityState);
        if (!_baselines.TryGetValue(key, out var baseline))
        {
            baseline = new OnlineBaseline(observation.Timestamp);
            _baselines.Add(key, baseline);
        }

        baseline.Add(observation);
        UpdateEpisodes(observation, localTimestamp);
        _changeVersion++;
        _isDirty = true;
        return true;
    }

    public MachineLearningDashboardSnapshot GetDashboardSnapshot(
        DateTimeOffset now)
    {
        _ = now;
        var current = _journal.Count == 0 ? null : _journal.Last();
        var baseline = current is null ? null : GetBaseline(current);
        var baselines = Baselines;
        var episodes = RecentEpisodes;

        return new MachineLearningDashboardSnapshot(
            _observationCount,
            TimeSpan.FromTicks(Math.Clamp(
                _observedDurationTicks,
                0,
                TimeSpan.MaxValue.Ticks)),
            current,
            baseline,
            episodes.Count,
            _journal.Count,
            baselines,
            episodes,
            MachineLearnedItemProjector.Project(
                baselines,
                episodes,
                current),
            new MachineLearningDiagnostics(
                _sessionAcceptedObservationCount,
                _throttledObservationCount,
                _missingPrerequisiteCount,
                _lastObservedAt),
            _lastPersistedAt,
            _isDirty,
            _dataHealth);
    }

    public MachineLearnedContext? GetLearnedContext()
    {
        var current = _journal.Count == 0 ? null : _journal.Last();
        if (current is null)
        {
            return null;
        }

        var baseline = GetBaseline(current);
        if (baseline is null ||
            baseline.Confidence != MachineLearningConfidence.Established)
        {
            return null;
        }

        return new MachineLearnedContext(
            current.ActivityState,
            baseline.LocalHour,
            baseline.Confidence,
            baseline.SampleCount,
            baseline.CpuMean,
            baseline.CpuStandardDeviation,
            baseline.MemoryMean,
            baseline.MemoryStandardDeviation,
            _episodes.Reverse().Where(episode =>
                episode.ActivityState == current.ActivityState &&
                episode.OverallState == current.OverallState).Take(3).Select(episode =>
                new MachineLearningEpisodeSummary(
                    episode.ActivityState,
                    episode.OverallState,
                    episode.SampleCount,
                    episode.AverageCpuUsagePercent,
                    episode.PeakCpuUsagePercent,
                    episode.AverageMemoryUsagePercent,
                    episode.FindingKeys,
                    episode.Outcome)).ToArray(),
            baseline.DominantNetworkActivityClass,
            baseline.DominantNetworkActivityCount,
            baseline.NetworkObservationCount);
    }

    public async Task LoadAsync(
        IMachineLearningStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
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
            return;
        }

        if (state.SchemaVersion is not PersistenceSchemaVersion and
                not LegacyPersistenceSchemaVersion ||
            state.Baselines is null ||
            state.Episodes is null)
        {
            _recoveredFromCorruptState = true;
            _dataHealth = MachineLearningDataHealth.RecoveredFromCorruptState;
            return;
        }

        var ignoredInvalidState = false;
        _baselines.Clear();
        foreach (var persisted in state.Baselines)
        {
            if (persisted is null ||
                persisted.LocalHour is < 0 or > 23 ||
                !Enum.IsDefined(persisted.ActivityState) ||
                persisted.SampleCount <= 0 ||
                !double.IsFinite(persisted.CpuMean) ||
                !double.IsFinite(persisted.CpuM2) ||
                !double.IsFinite(persisted.MemoryMean) ||
                !double.IsFinite(persisted.MemoryM2) ||
                !HasValidNetworkCounts(persisted) ||
                persisted.LastObservedAt < persisted.FirstObservedAt)
            {
                ignoredInvalidState = true;
                continue;
            }

            _baselines[new BaselineKey(
                persisted.LocalHour,
                persisted.ActivityState)] = new OnlineBaseline(persisted);
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
                AddEpisode(state.ActiveEpisode);
            }
            else
            {
                ignoredInvalidState = true;
            }
        }

        _activeEpisode = null;
        _observationCount = Math.Max(0, state.ObservationCount);
        _sessionAcceptedObservationCount = 0;
        _firstObservedAt = state.FirstObservedAt;
        _lastObservedAt = state.LastObservedAt;
        _lastObservationAt = null;
        _lastObservationAttemptAt = null;
        _observedDurationTicks = state.ObservedDurationTicks > 0
            ? Math.Min(state.ObservedDurationTicks, TimeSpan.MaxValue.Ticks)
            : EstimateObservedDurationTicks(_observationCount);
        _lastPersistedAt = state.PersistedAt;
        _nextPersistenceAttemptAt = null;
        _changeVersion = 0;
        _isDirty = false;
        _recoveredFromCorruptState = ignoredInvalidState;
        _dataHealth = _recoveredFromCorruptState
            ? MachineLearningDataHealth.RecoveredFromCorruptState
            : MachineLearningDataHealth.Healthy;
    }

    public async Task<bool> SaveIfDueAsync(
        IMachineLearningStore store,
        DateTimeOffset now,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
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
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SaveFinalSnapshotAsync(
        IMachineLearningStore store,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!_isDirty)
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
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SaveSnapshotAsync(
        IMachineLearningStore store,
        MachineLearningPersistedState state,
        long snapshotVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
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
                _dataHealth =
                    MachineLearningDataHealth.PersistenceTemporarilyUnavailable;
                _nextPersistenceAttemptAt =
                    now + PersistenceFailureRetryInterval;
                return false;
            }

            _lastPersistedAt = now;
            _nextPersistenceAttemptAt = null;
            _dataHealth = _recoveredFromCorruptState
                ? MachineLearningDataHealth.RecoveredFromCorruptState
                : MachineLearningDataHealth.Healthy;
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

    private MachineLearningBaseline? GetBaseline(
        MachineLearningObservation observation)
    {
        var key = new BaselineKey(
            observation.Timestamp.ToLocalTime().Hour,
            observation.ActivityState);
        return _baselines.TryGetValue(key, out var baseline)
            ? baseline.ToSnapshot(key)
            : null;
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
        AddEpisode(_activeEpisode.Complete(observation.Timestamp, outcome));
        _activeEpisode = new ActiveEpisode(observation, context);
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
            _baselines.Select(pair => pair.Value.ToState(pair.Key)).ToArray(),
            _episodes.ToArray(),
            _observationCount,
            _firstObservedAt,
            _lastObservedAt,
            persistedAt,
            _observedDurationTicks,
            _activeEpisode?.Snapshot());

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

        var total = 0L;
        foreach (var count in new[]
        {
            state.NetworkQuietSampleCount,
            state.NetworkLightSampleCount,
            state.NetworkActiveSampleCount,
            state.NetworkUnavailableSampleCount
        })
        {
            total = total > long.MaxValue - count
                ? long.MaxValue
                : total + count;
        }

        return total <= state.SampleCount;
    }

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

    private readonly record struct BaselineKey(
        int Hour,
        MachineUserActivityState ActivityState);

    private sealed class OnlineBaseline
    {
        private readonly HashSet<DateOnly> _observedLocalDates = new();

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

            if (state.ObservedLocalDates is not null)
            {
                _observedLocalDates.UnionWith(state.ObservedLocalDates);
            }

            if (_observedLocalDates.Count == 0)
            {
                _observedLocalDates.Add(ToLocalDate(FirstObservedAt));
                _observedLocalDates.Add(ToLocalDate(LastObservedAt));
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

        public void Add(MachineLearningObservation observation)
        {
            Count++;
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
                    NetworkQuietSampleCount++;
                    break;
                case MachineNetworkActivityClass.Light:
                    NetworkLightSampleCount++;
                    break;
                case MachineNetworkActivityClass.Active:
                    NetworkActiveSampleCount++;
                    break;
                default:
                    NetworkUnavailableSampleCount++;
                    break;
            }
            LastObservedAt = observation.Timestamp;
            _observedLocalDates.Add(ToLocalDate(observation.Timestamp));
        }

        public MachineLearningBaseline ToSnapshot(BaselineKey key) => new(
            key.Hour,
            key.ActivityState,
            Count,
            CpuMean,
            StandardDeviation(CpuM2, Count),
            MemoryMean,
            StandardDeviation(MemoryM2, Count),
            FirstObservedAt,
            LastObservedAt,
            _observedLocalDates.Count,
            GetConfidence(Count, _observedLocalDates.Count),
            NetworkQuietSampleCount,
            NetworkLightSampleCount,
            NetworkActiveSampleCount,
            NetworkUnavailableSampleCount);

        public MachineLearningBaselineState ToState(BaselineKey key) => new(
            key.Hour,
            key.ActivityState,
            Count,
            CpuMean,
            CpuM2,
            MemoryMean,
            MemoryM2,
            FirstObservedAt,
            LastObservedAt,
            _observedLocalDates.Order().ToArray(),
            NetworkQuietSampleCount,
            NetworkLightSampleCount,
            NetworkActiveSampleCount,
            NetworkUnavailableSampleCount);

        private static DateOnly ToLocalDate(DateTimeOffset timestamp) =>
            DateOnly.FromDateTime(timestamp.ToLocalTime().DateTime);

        private static double StandardDeviation(double m2, long count) =>
            count < 2 ? 0d : Math.Sqrt(Math.Max(0d, m2 / (count - 1)));

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
        private DateTimeOffset _lastObservedAt;

        public ActiveEpisode(
            MachineLearningObservation observation,
            EpisodeContext context)
        {
            Context = context;
            _startedAt = observation.Timestamp;
            Add(observation);
        }

        public EpisodeContext Context { get; }

        public void Add(MachineLearningObservation observation)
        {
            _sampleCount++;
            _cpuTotal += observation.CpuUsagePercent;
            _memoryTotal += observation.MemoryUsagePercent;
            _peakCpu = Math.Max(_peakCpu, observation.CpuUsagePercent);
            _lastObservedAt = observation.Timestamp;
        }

        public MachineLearningEpisode Complete(
            DateTimeOffset endedAt,
            string? outcome) => CreateSnapshot(endedAt, outcome);

        public MachineLearningEpisode Snapshot() =>
            CreateSnapshot(_lastObservedAt, null);

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
