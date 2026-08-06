namespace Machine.Core;

public interface IMachinePackagedSoftwareInventoryProvider
{
    Task<MachinePackagedSoftwareInventorySnapshot> GetAsync(
        CancellationToken cancellationToken = default);
}
