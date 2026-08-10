using System.Net;
using Machine.Ollama;

namespace Machine.Tests;

public sealed class OllamaRuntimeBootstrapperTests
{
    [Fact]
    public async Task ReusesAlreadyHealthyRuntimeWithoutStartingProcess()
    {
        var launcher = new RecordingLauncher();
        await using var bootstrapper = Create(new SequenceProbe(true), launcher);

        var result = await bootstrapper.EnsureAvailableAsync();

        Assert.True(result.IsAvailable);
        Assert.False(result.StartedByMachine);
        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public async Task StartsAndPollsUntilRuntimeIsHealthy()
    {
        var launcher = new RecordingLauncher();
        await using var bootstrapper = Create(new SequenceProbe(false, true), launcher,
            TimeSpan.FromSeconds(1));

        var result = await bootstrapper.EnsureAvailableAsync();

        Assert.True(result.IsAvailable);
        Assert.True(result.StartedByMachine);
        Assert.Equal(1, launcher.StartCount);
    }

    [Fact]
    public async Task ReturnsUnavailableWhenExecutableCannotBeResolved()
    {
        var launcher = new RecordingLauncher();
        await using var bootstrapper = new OllamaRuntimeBootstrapper(
            new SequenceProbe(false), launcher, () => null, Task.Delay,
            TimeSpan.Zero);

        var result = await bootstrapper.EnsureAvailableAsync();

        Assert.False(result.IsAvailable);
        Assert.False(result.ExecutableWasFound);
        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public async Task ReturnsUnavailableAfterReadinessTimeout()
    {
        var launcher = new RecordingLauncher();
        await using var bootstrapper = Create(new SequenceProbe(false), launcher,
            TimeSpan.Zero);

        var result = await bootstrapper.EnsureAvailableAsync();

        Assert.False(result.IsAvailable);
        Assert.True(result.StartedByMachine);
        Assert.Equal(1, launcher.StartCount);
    }

    [Fact]
    public async Task HonorsCancellationAndDoesNotStartAgainWhileOwnedProcessRuns()
    {
        var launcher = new RecordingLauncher();
        await using var bootstrapper = Create(new SequenceProbe(false), launcher,
            TimeSpan.Zero);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            bootstrapper.EnsureAvailableAsync(cancellation.Token));

        var first = await bootstrapper.EnsureAvailableAsync();
        var second = await bootstrapper.EnsureAvailableAsync();
        Assert.False(first.IsAvailable);
        Assert.False(second.IsAvailable);
        Assert.Equal(1, launcher.StartCount);
    }

    [Fact]
    public async Task HealthProbeCallsOnlyVersionEndpoint()
    {
        using var handler = new EndpointHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/")
        };
        var probe = new OllamaRuntimeHealthProbe(client);

        Assert.True(await probe.IsHealthyAsync(CancellationToken.None));
        Assert.Equal("/api/version", handler.Path);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task PreExistingRuntimeIsNeverStoppedDuringShutdown()
    {
        var launcher = new RecordingLauncher();
        await using var bootstrapper = Create(new SequenceProbe(true), launcher);

        await bootstrapper.EnsureAvailableAsync();
        await bootstrapper.ShutdownAsync();

        Assert.Equal(0, launcher.StartCount);
        Assert.Equal(0, launcher.StopCount);
    }

    [Fact]
    public async Task OwnedRuntimeIsStoppedOnlyOnceDuringShutdown()
    {
        var launcher = new RecordingLauncher();
        await using var bootstrapper = Create(new SequenceProbe(false), launcher,
            TimeSpan.Zero);

        await bootstrapper.EnsureAvailableAsync();
        await bootstrapper.ShutdownAsync();
        await bootstrapper.ShutdownAsync();

        Assert.Equal(1, launcher.StartCount);
        Assert.Equal(1, launcher.StopCount);
    }

    [Fact]
    public async Task DisposingDuringCancelledBootstrapIsRaceSafe()
    {
        var probe = new BlockingProbe(firstResult: false);
        var launcher = new RecordingLauncher();
        await using var bootstrapper = new OllamaRuntimeBootstrapper(
            probe, launcher, () => "C:\\Ollama\\ollama.exe", Task.Delay,
            TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();

        var bootstrap = bootstrapper.EnsureAvailableAsync(cancellation.Token);
        await probe.WaitUntilSecondCallAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bootstrap);

        await bootstrapper.DisposeAsync();

        Assert.Equal(1, launcher.StartCount);
        Assert.Equal(1, launcher.StopCount);
    }

    [Fact]
    public async Task OwnedProcessShutdownHonorsTimeout()
    {
        var launcher = new BlockingStopLauncher();
        await using var bootstrapper = new OllamaRuntimeBootstrapper(
            new SequenceProbe(false), launcher, () => "C:\\Ollama\\ollama.exe",
            Task.Delay, TimeSpan.Zero);
        await bootstrapper.EnsureAvailableAsync();
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            bootstrapper.ShutdownAsync(timeout.Token));

        launcher.ReleaseStop();
    }

    private static OllamaRuntimeBootstrapper Create(SequenceProbe probe,
        RecordingLauncher launcher, TimeSpan? timeout = null) => new(probe,
            launcher, () => "C:\\Ollama\\ollama.exe", Task.Delay,
            timeout ?? TimeSpan.Zero);

    private sealed class SequenceProbe(params bool[] values)
        : IOllamaRuntimeHealthProbe
    {
        private int _index;
        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = values[Math.Min(_index, values.Length - 1)];
            _index++;
            return Task.FromResult(value);
        }
    }

    private sealed class RecordingLauncher : IOllamaRuntimeProcessLauncher
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public IOllamaRuntimeProcess Start(string executablePath)
        {
            StartCount++;
            return new RecordingProcess(() => StopCount++);
        }
    }

    private sealed class RecordingProcess(Action stop) : IOllamaRuntimeProcess
    {
        public bool HasExited { get; private set; }
        public void Stop()
        {
            HasExited = true;
            stop();
        }
        public void Dispose() { }
    }

    private sealed class BlockingProbe(bool firstResult)
        : IOllamaRuntimeHealthProbe
    {
        private readonly TaskCompletionSource _secondCall = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;
        public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                return firstResult;
            }

            _secondCall.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return false;
        }
        public Task WaitUntilSecondCallAsync() => _secondCall.Task;
    }

    private sealed class BlockingStopLauncher : IOllamaRuntimeProcessLauncher
    {
        private readonly TaskCompletionSource _releaseStop = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public IOllamaRuntimeProcess Start(string executablePath) =>
            new BlockingStopProcess(_releaseStop.Task);
        public void ReleaseStop() => _releaseStop.TrySetResult();
    }

    private sealed class BlockingStopProcess(Task releaseStop)
        : IOllamaRuntimeProcess
    {
        public bool HasExited { get; private set; }
        public void Stop()
        {
            releaseStop.GetAwaiter().GetResult();
            HasExited = true;
        }
        public void Dispose() { }
    }

    private sealed class EndpointHandler : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
