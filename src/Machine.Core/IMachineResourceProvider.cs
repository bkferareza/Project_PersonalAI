namespace Machine.Core;

public interface IMachineResourceProvider
{
    Task<MachineResourceSnapshot> GetAsync(
        CancellationToken cancellationToken = default);
}
