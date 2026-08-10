namespace Machine.Core;

public sealed class MachineLearningService
{
    public const int MaximumObservationCount = 2_880;
    public const int MaximumEpisodeCount = 200;
    public const int PersistenceSchemaVersion = 1;
    public static readonly TimeSpan ObservationInterval =
        TimeSpan.FromSeconds(30);
    public static readonly TimeSpan PersistenceInterval =
        TimeSpan.FromMinutes(15);

    private const int ProvisionalSampleCount = 12;
    private const int EstablishedSampleCount = 168;
    private static readonly TimeSpan EstablishedObservedRange =
        TimeSpan.FromDays(7);
    private readonly Queue<MachineLearningObservation> _journal = new();
    private readonly Queue<MachineLearningEpisode> _episodes = new();
    private readonly Dictionary<BaselineKey, OnlineBaseline> _baselines = new();
    private ActiveEpisode? _activeEpisode;
    private DateTimeOffset? _lastObservationAt;
    private DateTimeOffset? _firstObservedAt;
    private DateTimeOffset? _lastObservedAt;
    private DateTimeOffset? _lastPersistedAt;
    private long _observationCount;
    private bool _isDirty;

    public IReadOnlyList<MachineLearningObservation> Journal =>
        _journal.ToArray();

    public IReadOnlyList<MachineLearningEpisode> RecentEpisodes =>
        _episodes.ToArray();

    public bool CanObserveAt(DateTimeOffset timestamp) =>
        _lastObservationAt is null ||
        timestamp - _lastObservationAt.Value >= ObservationInterval;

    public bool Observe(MachineLearningObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!CanObserveAt(observation.Timestamp))
        {
            return false;
        }

        _lastObservationAt = observation.Timestamp;
        _firstObservedAt ??= observation.Timestamp;
        _lastObservedAt = observation.Timestamp;
        _observationCount++;
        _journal.Enqueue(observation);
        while (_journal.Count > MaximumObservationCount)
        {
            _journal.Dequeue();
        }

        var key = new BaselineKey(
            observation.Timestamp.ToLocalTime().Hour,
            observation.ActivityState);
        if (!_baselines.TryGetValue(key, out var baseline))
        {
            baseline = new OnlineBaseline(observation.Timestamp);
            _baselines.Add(key, baseline);
        }

        baseline.Add(observation);
        UpdateEpisodes(observation);
        _isDirty = true;
        return true;
    }

    public MachineLearningDashboardSnapshot GetDashboardSnapshot(
        DateTimeOffset now)
    {
        var current = _journal.Count == 0 ? null : _journal.Last();
        var baseline = current is null ? null : GetBaseline(current);
        var duration = _firstObservedAt is null
            ? TimeSpan.Zero
            : (now - _firstObservedAt.Value) < TimeSpan.Zero
                ? TimeSpan.Zero
                : now - _firstObservedAt.Value;

        return new MachineLearningDashboardSnapshot(
            _observationCount,
            duration,
            current,
            baseline,
            _episodes.Count);
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
                    episode.Outcome)).ToArray());
    }

    public async Task LoadAsync(
        IMachineLearningStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var state = await store.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (state is null ||
            state.SchemaVersion != PersistenceSchemaVersion ||
            state.Baselines is null ||
            state.Episodes is null)
        {
            return;
        }

        _baselines.Clear();
        foreach (var persisted in state.Baselines)
        {
            if (persisted.LocalHour is < 0 or > 23 ||
                persisted.SampleCount <= 0 ||
                !double.IsFinite(persisted.CpuMean) ||
                !double.IsFinite(persisted.CpuM2) ||
                !double.IsFinite(persisted.MemoryMean) ||
                !double.IsFinite(persisted.MemoryM2))
            {
                continue;
            }

            _baselines[new BaselineKey(
                persisted.LocalHour,
                persisted.ActivityState)] = new OnlineBaseline(persisted);
        }

        _episodes.Clear();
        foreach (var episode in state.Episodes.TakeLast(MaximumEpisodeCount))
        {
            _episodes.Enqueue(episode);
        }

        _observationCount = Math.Max(0, state.ObservationCount);
        _firstObservedAt = state.FirstObservedAt;
        _lastObservedAt = state.LastObservedAt;
        _isDirty = false;
    }

    public async Task<bool> SaveIfDueAsync(
        IMachineLearningStore store,
        DateTimeOffset now,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!_isDirty || (!force && _lastPersistedAt is not null &&
            now - _lastPersistedAt.Value < PersistenceInterval))
        {
            return false;
        }

        await store.SaveAsync(CreatePersistedState(), cancellationToken)
            .ConfigureAwait(false);
        _lastPersistedAt = now;
        _isDirty = false;
        return true;
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

    private void UpdateEpisodes(MachineLearningObservation observation)
    {
        var context = new EpisodeContext(
            observation.ActivityState,
            observation.OverallState,
            observation.FindingKeys.OrderBy(value => value,
                StringComparer.Ordinal).ToArray());

        if (_activeEpisode is null)
        {
            _activeEpisode = new ActiveEpisode(observation, context);
            return;
        }

        if (_activeEpisode.Context.ActivityState == context.ActivityState &&
            _activeEpisode.Context.OverallState == context.OverallState &&
            _activeEpisode.Context.FindingKeys.SequenceEqual(
                context.FindingKeys,
                StringComparer.Ordinal))
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

    private MachineLearningPersistedState CreatePersistedState() =>
        new(
            PersistenceSchemaVersion,
            _baselines.Select(pair => pair.Value.ToState(pair.Key)).ToArray(),
            _episodes.ToArray(),
            _observationCount,
            _firstObservedAt,
            _lastObservedAt);

    private readonly record struct BaselineKey(
        int Hour,
        MachineUserActivityState ActivityState);

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
            FirstObservedAt = state.FirstObservedAt;
            LastObservedAt = state.LastObservedAt;
        }

        public long Count { get; private set; }
        public double CpuMean { get; private set; }
        public double CpuM2 { get; private set; }
        public double MemoryMean { get; private set; }
        public double MemoryM2 { get; private set; }
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
            LastObservedAt = observation.Timestamp;
        }

        public MachineLearningBaseline ToSnapshot(BaselineKey key) =>
            new(
                key.Hour,
                key.ActivityState,
                Count,
                CpuMean,
                StandardDeviation(CpuM2, Count),
                MemoryMean,
                StandardDeviation(MemoryM2, Count),
                FirstObservedAt,
                LastObservedAt,
                GetConfidence(Count, FirstObservedAt, LastObservedAt));

        public MachineLearningBaselineState ToState(BaselineKey key) =>
            new(key.Hour, key.ActivityState, Count, CpuMean, CpuM2,
                MemoryMean, MemoryM2, FirstObservedAt, LastObservedAt);

        private static double StandardDeviation(double m2, long count) =>
            count < 2 ? 0d : Math.Sqrt(Math.Max(0d, m2 / (count - 1)));

        private static MachineLearningConfidence GetConfidence(
            long count,
            DateTimeOffset first,
            DateTimeOffset last) =>
            count >= EstablishedSampleCount &&
            last - first >= EstablishedObservedRange
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
        public ActiveEpisode(MachineLearningObservation observation,
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
        }
        public MachineLearningEpisode Complete(DateTimeOffset endedAt,
            string? outcome) => new(_startedAt, endedAt,
                Context.ActivityState, Context.OverallState, _sampleCount,
                _cpuTotal / _sampleCount, _peakCpu,
                _memoryTotal / _sampleCount, Context.FindingKeys, outcome);
    }

    private sealed record EpisodeContext(
        MachineUserActivityState ActivityState,
        MachineOverallState OverallState,
        IReadOnlyList<string> FindingKeys);
}
