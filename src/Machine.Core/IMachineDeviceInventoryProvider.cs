namespace Machine.Core;

public interface IMachineDeviceInventoryProvider
{
    Task<MachineDeviceInventorySnapshot> GetAsync(
        CancellationToken cancellationToken = default);
}
