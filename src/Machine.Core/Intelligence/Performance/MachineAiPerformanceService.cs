namespace Machine.Core;

public sealed class MachineAiPerformanceService(
    IMachineAiMetricStore store,
    int capacity = 200,
    int recentWindowSize = 20,
    int meaningfulSampleSize = 5)
    : IMachineAiMetricRecorder
{
    public const int CurrentSchemaVersion = 1;
    public const int DefaultCapacity = 200;
    public const int DefaultRecentWindowSize = 20;

    private readonly object _sync = new();
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);
    private readonly List<MachineAiGenerationMetric> _generations = [];
    private bool _restoreAttempted;

    public async Task RestoreAsync(
        CancellationToken cancellationToken = default)
    {
        await _persistenceGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (_restoreAttempted)
            {
                return;
            }
            _restoreAttempted = true;
            try
            {
                var state = await store.LoadAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (state is not null && Validate(state, capacity))
                {
                    lock (_sync)
                    {
                        _generations.Clear();
                        _generations.AddRange(state.Generations
                            .OrderBy(metric => metric.Timestamp)
                            .TakeLast(capacity));
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    InvalidOperationException)
            {
                // AI quality history is diagnostic-only and must not block AI.
            }
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    public async Task RecordAsync(
        MachineAiGenerationMetric metric,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metric);
        await RestoreAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            _generations.Add(metric);
            if (_generations.Count > capacity)
            {
                _generations.RemoveRange(0, _generations.Count - capacity);
            }
        }
        try
        {
            await _persistenceGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                MachineAiPerformancePersistedState state;
                lock (_sync)
                {
                    state = new(CurrentSchemaVersion,
                        _generations.ToArray(), DateTimeOffset.UtcNow);
                }
                await store.SaveAsync(state, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _persistenceGate.Release();
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidOperationException)
        {
            // Persistence failure is visible only as missing diagnostic history.
        }
    }

    public MachineAiQualitySummary GetSummary()
    {
        MachineAiGenerationMetric[] recent;
        lock (_sync)
        {
            recent = _generations.TakeLast(recentWindowSize).ToArray();
        }
        if (recent.Length == 0)
        {
            return new(recentWindowSize, 0, false, null, null, null,
                null, null, null, null, null, null, null, null,
                new Dictionary<MachineBriefValidationFailure, int>());
        }

        return new(
            recentWindowSize,
            recent.Length,
            recent.Length >= meaningfulSampleSize,
            Percent(recent.Count(metric => metric.FirstPassGrounded), recent.Length),
            Percent(recent.Count(metric => metric.RepairAttempted), recent.Length),
            Percent(recent.Count(metric => metric.FallbackUsed), recent.Length),
            Median(recent.Select(metric => (double)metric.TotalLatency.Ticks)),
            Percentile(recent.Select(metric => (double)metric.TotalLatency.Ticks),
                0.95d),
            Median(recent.Where(metric => metric.ColdLoad)
                .Select(metric => metric.LoadLatency?.Ticks)),
            Median(recent.Select(metric =>
                metric.PromptEvaluationLatency?.Ticks)),
            MedianNumber(recent.Select(metric =>
                metric.GenerationTokensPerSecond)),
            MedianNumber(recent.Select(metric =>
                (double?)metric.EvidenceItemCount)),
            MedianNumber(recent.Select(metric => (double?)metric.InputTokens)),
            MedianNumber(recent.Select(metric => (double?)metric.OutputTokens)),
            recent.Where(metric => metric.RejectionCategory !=
                    MachineBriefValidationFailure.None)
                .GroupBy(metric => metric.RejectionCategory)
                .ToDictionary(group => group.Key, group => group.Count()));
    }

    public IReadOnlyList<MachineAiGenerationMetric> Snapshot()
    {
        lock (_sync)
        {
            return _generations.ToArray();
        }
    }

    public static bool Validate(
        MachineAiPerformancePersistedState state,
        int capacity = DefaultCapacity) =>
        state.SchemaVersion == CurrentSchemaVersion &&
        state.Generations is not null &&
        state.Generations.Count <= capacity &&
        state.Generations.All(metric => metric is not null &&
            !string.IsNullOrWhiteSpace(metric.ModelIdentity) &&
            !string.IsNullOrWhiteSpace(metric.Quantization) &&
            !string.IsNullOrWhiteSpace(metric.SituationFingerprint) &&
            metric.EvidenceItemCount is >= 0 and <= 24 &&
            metric.TotalLatency >= TimeSpan.Zero);

    private static double Percent(int count, int total) =>
        count * 100d / total;

    private static TimeSpan? Median(IEnumerable<double> values)
    {
        var value = MedianNumber(values.Select(item => (double?)item));
        return value is null ? null : TimeSpan.FromTicks((long)value.Value);
    }

    private static TimeSpan? Median(IEnumerable<long?> values)
    {
        var value = MedianNumber(values.Select(item => (double?)item));
        return value is null ? null : TimeSpan.FromTicks((long)value.Value);
    }

    private static TimeSpan? Percentile(
        IEnumerable<double> values,
        double percentile)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0)
        {
            return null;
        }
        var index = Math.Clamp((int)Math.Ceiling(percentile * ordered.Length) - 1,
            0, ordered.Length - 1);
        return TimeSpan.FromTicks((long)ordered[index]);
    }

    private static double? MedianNumber(IEnumerable<double?> values)
    {
        var ordered = values.Where(value => value is not null)
            .Select(value => value!.Value).Order().ToArray();
        if (ordered.Length == 0)
        {
            return null;
        }
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2d
            : ordered[middle];
    }
}
