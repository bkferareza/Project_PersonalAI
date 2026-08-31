namespace Machine.Core;

public interface ILocalInferenceRuntime : IAsyncDisposable
{
    Task<LocalInferenceStartResult> EnsureAvailableAsync(
        CancellationToken cancellationToken = default);

    Task<LocalInferenceStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task<LocalInferenceResult> GenerateAsync(
        LocalInferenceRequest request,
        CancellationToken cancellationToken = default);

    Task RequestUnloadAsync(
        CancellationToken cancellationToken = default);

    Task ShutdownAsync(
        CancellationToken cancellationToken = default);
}
