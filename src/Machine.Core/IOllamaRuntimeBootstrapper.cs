namespace Machine.Core;

public interface IOllamaRuntimeBootstrapper : IAsyncDisposable
{
    Task<OllamaRuntimeBootstrapResult> EnsureAvailableAsync(
        CancellationToken cancellationToken = default);
}

public sealed record OllamaRuntimeBootstrapResult(
    bool IsAvailable,
    bool StartedByMachine,
    bool ExecutableWasFound);
