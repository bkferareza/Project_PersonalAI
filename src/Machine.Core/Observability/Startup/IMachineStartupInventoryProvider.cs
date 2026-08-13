namespace Machine.Core;

public interface IMachineStartupInventoryProvider
{
    Task<MachineStartupInventorySnapshot> GetAsync(
        CancellationToken cancellationToken = default);
}
