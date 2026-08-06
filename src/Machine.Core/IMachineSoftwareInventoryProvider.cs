namespace Machine.Core;

public interface IMachineSoftwareInventoryProvider
{
    Task<MachineSoftwareInventorySnapshot> GetAsync(
        CancellationToken cancellationToken = default);
}
