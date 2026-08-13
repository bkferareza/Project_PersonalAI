using System.ComponentModel;
using System.Runtime.InteropServices;
using Machine.Core;
using Microsoft.Win32.SafeHandles;

namespace Machine.Windows;

public sealed class WindowsMachineServiceInventoryProvider
    : IMachineServiceInventoryProvider
{
    public const int MaximumServiceCount = 4_096;

    private readonly IWindowsServiceInventorySource _source;

    public WindowsMachineServiceInventoryProvider()
        : this(new WindowsServiceInventorySource())
    {
    }

    internal WindowsMachineServiceInventoryProvider(
        IWindowsServiceInventorySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public Task<MachineServiceInventorySnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(
            () => Capture(cancellationToken),
            cancellationToken);
    }

    private MachineServiceInventorySnapshot Capture(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NativeServiceStatus> statuses;
        try
        {
            statuses = _source.Enumerate(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return new(
                [],
                false,
                1,
                0,
                DateTimeOffset.UtcNow);
        }

        var items = new List<MachineServiceSnapshot>();
        var readFailureCount = 0;
        foreach (var status in statuses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(status.Name))
            {
                readFailureCount++;
                continue;
            }

            NativeServiceConfiguration? configuration = null;
            try
            {
                configuration = _source.QueryConfiguration(
                    status.Name,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsReadFailure(exception))
            {
                readFailureCount++;
            }

            items.Add(Map(status, configuration));
        }

        var ordered = items
            .OrderBy(item => item.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        var truncated = Math.Max(0, ordered.Length - MaximumServiceCount);
        var bounded = ordered.Take(MaximumServiceCount).ToArray();
        return new(
            bounded,
            readFailureCount == 0 && truncated == 0,
            readFailureCount,
            truncated,
            DateTimeOffset.UtcNow);
    }

    internal static MachineServiceSnapshot Map(
        NativeServiceStatus status,
        NativeServiceConfiguration? configuration)
    {
        var name = status.Name.Trim();
        var displayName = string.IsNullOrWhiteSpace(status.DisplayName)
            ? name
            : status.DisplayName.Trim();
        var startType = configuration is null
            ? MachineServiceStartType.Unknown
            : MapStartType(
                configuration.StartType,
                configuration.DelayedAutomatic);
        return new(
            name,
            displayName,
            MapState(status.State),
            startType,
            MapCategory(status.ServiceType),
            status.ProcessId is > 0 and <= int.MaxValue
                ? (int)status.ProcessId
                : null);
    }

    internal static MachineServiceState MapState(uint value) => value switch
    {
        1 => MachineServiceState.Stopped,
        2 => MachineServiceState.StartPending,
        3 => MachineServiceState.StopPending,
        4 => MachineServiceState.Running,
        5 => MachineServiceState.ContinuePending,
        6 => MachineServiceState.PausePending,
        7 => MachineServiceState.Paused,
        _ => MachineServiceState.Unknown
    };

    internal static MachineServiceStartType MapStartType(
        uint value,
        bool? delayedAutomatic) => value switch
        {
            0 => MachineServiceStartType.Boot,
            1 => MachineServiceStartType.System,
            2 when delayedAutomatic == true =>
                MachineServiceStartType.AutomaticDelayed,
            2 => MachineServiceStartType.Automatic,
            3 => MachineServiceStartType.Manual,
            4 => MachineServiceStartType.Disabled,
            _ => MachineServiceStartType.Unknown
        };

    internal static MachineServiceCategory MapCategory(uint serviceType)
    {
        if ((serviceType & 0x00000004) != 0)
        {
            return MachineServiceCategory.Adapter;
        }
        if ((serviceType & 0x00000008) != 0)
        {
            return MachineServiceCategory.FileSystemRecognizer;
        }
        if ((serviceType & 0x00000003) != 0)
        {
            return MachineServiceCategory.Driver;
        }
        if ((serviceType & 0x00000030) != 0)
        {
            return MachineServiceCategory.Service;
        }
        return MachineServiceCategory.Unknown;
    }

    private static bool IsReadFailure(Exception exception) =>
        exception is Win32Exception or
            InvalidOperationException or
            UnauthorizedAccessException;
}
