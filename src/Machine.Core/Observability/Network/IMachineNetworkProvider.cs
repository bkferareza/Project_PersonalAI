namespace Machine.Core;

public interface IMachineNetworkProvider
{
    Task<MachineNetworkSnapshot> GetAsync(
        CancellationToken cancellationToken = default);
}
