namespace Machine.Core;

public interface IMachineGpuTelemetryProvider : IDisposable
{
    Task<MachineGpuTelemetrySnapshot> GetAsync(
        CancellationToken cancellationToken = default);
}
