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
