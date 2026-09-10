using Machine.Core;

namespace Machine.Tests;

public sealed class MachineAiPerformanceServiceTests
{
    [Fact]
    public async Task RecentSummaryReportsMeasuredRatesAndPercentiles()
    {
        var store = new MemoryStore();
        var service = new MachineAiPerformanceService(store);
        for (var index = 0; index < 5; index++)
        {
            await service.RecordAsync(Metric(index,
                firstPass: index < 4,
                repair: index == 4,
                fallback: false));
        }

        var summary = service.GetSummary();

        Assert.True(summary.HasMeaningfulSample);
        Assert.Equal(5, summary.RecentGenerationCount);
        Assert.Equal(80d, summary.FirstPassGroundedPercent);
        Assert.Equal(20d, summary.RepairPercent);
        Assert.Equal(0d, summary.FallbackPercent);
        Assert.Equal(TimeSpan.FromSeconds(3), summary.MedianResponseTime);
        Assert.Equal(TimeSpan.FromSeconds(5), summary.P95ResponseTime);
        Assert.Equal(5, store.State!.Generations.Count);
    }

    [Fact]
    public async Task HistoryRestoresAndRemainsBounded()
    {
        var persisted = Enumerable.Range(0, 200)
            .Select(index => Metric(index, true, false, false)).ToArray();
        var store = new MemoryStore(new(1, persisted, DateTimeOffset.UtcNow));
        var service = new MachineAiPerformanceService(store);

        await service.RestoreAsync();
        await service.RecordAsync(Metric(201, false, true, true));

        Assert.Equal(200, service.Snapshot().Count);
        Assert.Equal(5d, service.GetSummary().FallbackPercent);
    }

    [Fact]
    public async Task PersistenceFailureNeverBlocksGenerationMetric()
    {
        var service = new MachineAiPerformanceService(new ThrowingStore());

        await service.RecordAsync(Metric(1, true, false, false));

        Assert.Single(service.Snapshot());
    }

    private static MachineAiGenerationMetric Metric(
        int index,
        bool firstPass,
        bool repair,
        bool fallback) => new(
        new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero)
            .AddMinutes(index),
        "Qwen3.5-4B",
        "Q4_K_M",
        MachineAiRequestType.Brief,
        $"fingerprint-{index}",
        8,
        900,
        40,
        index == 0,
        index == 0 ? TimeSpan.FromSeconds(3) : null,
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromSeconds(index + 1),
        TimeSpan.FromSeconds(index + 1),
        40d,
        firstPass,
        repair,
        repair && !fallback,
        fallback,
        firstPass ? MachineBriefValidationFailure.None :
            MachineBriefValidationFailure.ClaimGrounding);

    private sealed class MemoryStore(
        MachineAiPerformancePersistedState? initial = null)
        : IMachineAiMetricStore
    {
        public MachineAiPerformancePersistedState? State { get; private set; } =
            initial;

        public Task<MachineAiPerformancePersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(State);

        public Task SaveAsync(
            MachineAiPerformancePersistedState state,
            CancellationToken cancellationToken = default)
        {
            State = state;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingStore : IMachineAiMetricStore
    {
        public Task<MachineAiPerformancePersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            throw new IOException("Unavailable");

        public Task SaveAsync(
            MachineAiPerformancePersistedState state,
            CancellationToken cancellationToken = default) =>
            throw new IOException("Unavailable");
    }
}
