namespace Machine.Core;

public interface IMachineStorageProvider
{
    Task<MachineStorageSnapshot> GetAsync(
        CancellationToken cancellationToken = default);
}
