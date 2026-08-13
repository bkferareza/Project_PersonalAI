namespace Machine.Core;

public interface IMachineScheduledTaskInventoryProvider
{
    Task<MachineScheduledTaskInventorySnapshot> GetAsync(
        CancellationToken cancellationToken = default);
}
