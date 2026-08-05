using Machine.Core;

namespace Machine.Windows;

public interface IMachineIdentityProvider
{
    Task<MachineIdentity> GetAsync(
        CancellationToken cancellationToken = default);
}
