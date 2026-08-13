using Machine.App;
using Machine.Core;

namespace Machine.Tests;

public sealed class MachineShutdownCoordinatorTests
{
    [Fact]
    public async Task FinalSaveRunsAsynchronouslyAndOnlyOnce()
    {
        var service = CreateDirtyService();
        var store = new BlockingStore();
        var runtime = new RecordingRuntime();
        using var applicationCancellation = new CancellationTokenSource();
        var windowStops = 0;
        var httpDisposals = 0;
        var coordinator = new MachineShutdownCoordinator(
            service, store, runtime, applicationCancellation,
            () => windowStops++, () => httpDisposals++, TimeSpan.FromSeconds(1));

        var first = coordinator.BeginShutdown();
        var second = coordinator.BeginShutdown();

        Assert.Same(first, second);
        Assert.Equal(1, windowStops);
        Assert.True(applicationCancellation.IsCancellationRequested);
        Assert.Equal(1, store.SaveCount);
        Assert.False(first.IsCompleted);

        store.Complete();
        await first;

        Assert.Equal(1, runtime.ShutdownCount);
        Assert.Equal(1, httpDisposals);
        Assert.NotNull(store.State?.Metadata?.PreviousMachineSessionEndedAt);
        Assert.Null(store.State?.ActiveEpisode);
        Assert.Equal("Session ended", Assert.Single(store.State!.Episodes).Outcome);
    }

    [Fact]
    public async Task SaveTimeoutDoesNotBlockTeardown()
    {
        var service = CreateDirtyService();
        var store = new BlockingStore();
        var runtime = new RecordingRuntime();
        using var applicationCancellation = new CancellationTokenSource();
        var disposed = 0;
        var coordinator = new MachineShutdownCoordinator(
            service, store, runtime, applicationCancellation,
            () => { }, () => disposed++, TimeSpan.FromMilliseconds(25));

        await coordinator.BeginShutdown();

        Assert.Equal(1, store.SaveCount);
        Assert.Equal(1, disposed);
        Assert.Equal(1, runtime.ShutdownCount);
        store.Complete();
    }

    [Fact]
    public async Task SaveFailureStillCompletesShutdown()
    {
        var service = CreateDirtyService();
        var runtime = new RecordingRuntime();
        using var applicationCancellation = new CancellationTokenSource();
        var disposed = 0;
        var coordinator = new MachineShutdownCoordinator(
            service, new FailingStore(), runtime, applicationCancellation,
            () => { }, () => disposed++, TimeSpan.FromSeconds(1));

        await coordinator.BeginShutdown();

        Assert.Equal(1, runtime.ShutdownCount);
        Assert.Equal(1, disposed);
    }

    [Fact]
    public async Task FinalShutdownPersistsSeparateHealthHistory()
    {
        var learning = CreateDirtyService();
        var learningStore = new ImmediateStore();
        var health = new MachineHealthHistoryService();
        var healthStore = new RecordingHealthStore();
        var reliability = MachineReliabilityAggregator.Aggregate(
        [
            new MachineReliabilityIncident(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                MachineReliabilityIncidentCategory.ApplicationCrash,
                MachineReliabilityIncidentSeverity.Significant,
                "Application Error",
                "test.exe",
                null,
                null,
                1000,
                "application.crash")
        ], DateTimeOffset.UtcNow);
        health.Observe(null, null, reliability, DateTimeOffset.UtcNow);
        var history = new MachineHistoryService(
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1));
        var historyStore = new RecordingHistoryStore();
        history.BeginSession(DateTimeOffset.UtcNow.AddMinutes(-1));
        history.Observe(new MachineHistoryObservation(
            DateTimeOffset.UtcNow.AddSeconds(-30),
            10,
            20,
            null,
            null,
            MachineUserActivityState.Active,
            MachineOverallState.Stable));
        using var cancellation = new CancellationTokenSource();
        var coordinator = new MachineShutdownCoordinator(
            learning,
            learningStore,
            new RecordingRuntime(),
            cancellation,
            () => { },
            () => { },
            TimeSpan.FromSeconds(1),
            health,
            healthStore,
            history,
            historyStore);

        await coordinator.BeginShutdown();

        Assert.NotNull(healthStore.State);
        Assert.Equal(1, healthStore.State.LifetimeObservedIncidentCount);
        Assert.Single(healthStore.State.Reliability!.RecentIncidents);
        Assert.NotNull(historyStore.State);
        Assert.False(historyStore.State.SessionOpen);
        Assert.Contains(historyStore.State.Events, item =>
            item.Kind == MachineHistoryEventKind.MatasuriSessionEnded);
    }

    private static MachineLearningService CreateDirtyService()
    {
        var service = new MachineLearningService();
        service.Observe(new MachineLearningObservation(
            DateTimeOffset.UnixEpoch, 10, 20,
            MachineUserActivityState.Active, MachineOverallState.Stable,
            [], null, "stable"));
        return service;
    }

    private sealed class BlockingStore : IMachineLearningStore
    {
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public int SaveCount { get; private set; }
        public MachineLearningPersistedState? State { get; private set; }
        public Task<MachineLearningPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MachineLearningPersistedState?>(null);
        public Task SaveAsync(MachineLearningPersistedState state,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            State = state;
            return _completion.Task;
        }
        public void Complete() => _completion.TrySetResult();
    }

    private sealed class FailingStore : IMachineLearningStore
    {
        public Task<MachineLearningPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MachineLearningPersistedState?>(null);
        public Task SaveAsync(MachineLearningPersistedState state,
            CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("Simulated save failure."));
    }

    private sealed class ImmediateStore : IMachineLearningStore
    {
        public Task<MachineLearningPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MachineLearningPersistedState?>(null);

        public Task SaveAsync(
            MachineLearningPersistedState state,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingHealthStore : IMachineHealthHistoryStore
    {
        public MachineHealthHistoryPersistedState? State { get; private set; }

        public Task<MachineHealthHistoryPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(State);

        public Task SaveAsync(
            MachineHealthHistoryPersistedState state,
            CancellationToken cancellationToken = default)
        {
            State = state;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHistoryStore : IMachineHistoryStore
    {
        public MachineHistoryPersistedState? State { get; private set; }

        public Task<MachineHistoryPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(State);

        public Task SaveAsync(
            MachineHistoryPersistedState state,
            CancellationToken cancellationToken = default)
        {
            State = state;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRuntime : IOllamaRuntimeBootstrapper
    {
        public int ShutdownCount { get; private set; }
        public Task<OllamaRuntimeBootstrapResult> EnsureAvailableAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new OllamaRuntimeBootstrapResult(true, false, true));
        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            ShutdownCount++;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
