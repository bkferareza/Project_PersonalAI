using System.Runtime.InteropServices;
using Machine.Core;

namespace Machine.Windows;

public sealed class WindowsMachineIdentityProvider : IMachineIdentityProvider
{
    public Task<MachineIdentity> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var identity = new MachineIdentity(
            DeviceName: Environment.MachineName,
            OperatingSystem: RuntimeInformation.OSDescription.Trim(),
            Architecture: RuntimeInformation.OSArchitecture.ToString());

        return Task.FromResult(identity);
    }
}
