namespace Machine.Core;

public interface IMachineIdentityProvider
{
    Task<MachineIdentity> GetAsync(
        CancellationToken cancellationToken = default);
}
