namespace Machine.Core;

public interface IMachineServiceInventoryProvider
{
    Task<MachineServiceInventorySnapshot> GetAsync(
        CancellationToken cancellationToken = default);
}
