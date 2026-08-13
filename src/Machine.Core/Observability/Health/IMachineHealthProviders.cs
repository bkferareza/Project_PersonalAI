namespace Machine.Core;

public interface IMachineWindowsUpdateProvider
{
    Task<MachineWindowsUpdateSnapshot> GetAsync(
        CancellationToken cancellationToken = default);
}

public interface IMachineRebootPendingProvider
{
    Task<MachineRebootPendingSnapshot> GetAsync(
        CancellationToken cancellationToken = default);
}

public interface IMachineReliabilityProvider
{
    Task<MachineReliabilitySnapshot> GetAsync(
        CancellationToken cancellationToken = default);
}
