using System.Runtime.InteropServices;
using System.Security;
using Machine.Core;
using Microsoft.Win32;

namespace Machine.Windows;

public sealed class WindowsMachineRebootPendingProvider
    : IMachineRebootPendingProvider
{
    private readonly IWindowsRebootIndicatorSource _source;

    public WindowsMachineRebootPendingProvider()
        : this(new WindowsRebootIndicatorSource())
    {
    }

    internal WindowsMachineRebootPendingProvider(
        IWindowsRebootIndicatorSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public Task<MachineRebootPendingSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(
            () => MachineRebootPendingAggregator.Aggregate(
                _source.ReadIndicators(cancellationToken),
                DateTimeOffset.UtcNow),
            cancellationToken);
    }
}
