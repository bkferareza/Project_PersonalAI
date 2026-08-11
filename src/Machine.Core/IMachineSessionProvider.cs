namespace Machine.Core;

public interface IMachineSessionProvider
{
    Task<MachineSessionSnapshot> GetAsync(
        CancellationToken cancellationToken = default);
}
