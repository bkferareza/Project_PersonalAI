namespace Machine.Core;

public interface IOllamaStatusProvider
{
    Task<OllamaStatusSnapshot> GetStatusAsync(
        CancellationToken cancellationToken = default);
}
