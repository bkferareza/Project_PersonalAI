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

internal interface IWindowsServiceInventorySource
{
    IReadOnlyList<NativeServiceStatus> Enumerate(
        CancellationToken cancellationToken);

    NativeServiceConfiguration QueryConfiguration(
        string serviceName,
        CancellationToken cancellationToken);
}

internal sealed record NativeServiceStatus(
    string Name,
    string DisplayName,
    uint ServiceType,
    uint State,
    uint ProcessId);

internal sealed record NativeServiceConfiguration(
    uint StartType,
    bool? DelayedAutomatic);

internal sealed class WindowsServiceInventorySource
    : IWindowsServiceInventorySource
{
    private const uint ScManagerEnumerateService = 0x0004;
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryConfig = 0x0001;
    // Includes modern per-user and packaged service flags in addition to
    // Win32 services and drivers.
    private const uint ServiceTypeAll = 0x000003FF;
    private const uint ServiceStateAll = 0x00000003;
    private const int ScEnumProcessInfo = 0;
    private const uint ServiceConfigDelayedAutoStartInfo = 3;
    private const int ErrorMoreData = 234;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorInvalidLevel = 124;
    private const int ErrorInvalidParameter = 87;

    public IReadOnlyList<NativeServiceStatus> Enumerate(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var manager = OpenSCManager(
            null,
            null,
            ScManagerEnumerateService);
        if (manager.IsInvalid)
        {
            throw LastError(nameof(OpenSCManager));
        }

        uint resume = 0;
        _ = EnumServicesStatusEx(
            manager,
            ScEnumProcessInfo,
            ServiceTypeAll,
            ServiceStateAll,
            IntPtr.Zero,
            0,
            out var bytesNeeded,
            out _,
            ref resume,
            null);
        var error = Marshal.GetLastWin32Error();
        if (bytesNeeded == 0 && error != ErrorMoreData)
        {
            if (error == 0)
            {
                return [];
            }
            throw new Win32Exception(error, "Service enumeration failed.");
        }

        var bufferSize = checked(bytesNeeded + 64 * 1024u);
        var buffer = Marshal.AllocHGlobal(checked((int)bufferSize));
        try
        {
            resume = 0;
            if (!EnumServicesStatusEx(
                    manager,
                    ScEnumProcessInfo,
                    ServiceTypeAll,
                    ServiceStateAll,
                    buffer,
                    bufferSize,
                    out _,
                    out var returned,
                    ref resume,
                    null))
            {
                throw LastError(nameof(EnumServicesStatusEx));
            }

            var result = new List<NativeServiceStatus>(
                checked((int)returned));
            var structureSize = Marshal.SizeOf<EnumServiceStatusProcess>();
            for (var index = 0; index < returned; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pointer = IntPtr.Add(
                    buffer,
                    checked((int)index * structureSize));
                var native = Marshal.PtrToStructure<
                    EnumServiceStatusProcess>(pointer);
                var name = Marshal.PtrToStringUni(native.ServiceName);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }
                result.Add(new(
                    name,
                    Marshal.PtrToStringUni(native.DisplayName) ?? name,
                    native.Status.ServiceType,
                    native.Status.CurrentState,
                    native.Status.ProcessId));
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public NativeServiceConfiguration QueryConfiguration(
        string serviceName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var manager = OpenSCManager(
            null,
            null,
            ScManagerConnect);
        if (manager.IsInvalid)
        {
            throw LastError(nameof(OpenSCManager));
        }
        using var service = OpenService(
            manager,
            serviceName,
            ServiceQueryConfig);
        if (service.IsInvalid)
        {
            throw LastError(nameof(OpenService));
        }

        _ = QueryServiceConfig(
            service,
            IntPtr.Zero,
            0,
            out var bytesNeeded);
        var error = Marshal.GetLastWin32Error();
        if (error != ErrorInsufficientBuffer || bytesNeeded == 0)
        {
            throw new Win32Exception(error, "Service configuration read failed.");
        }

        var buffer = Marshal.AllocHGlobal(checked((int)bytesNeeded));
        try
        {
            if (!QueryServiceConfig(
                    service,
                    buffer,
                    bytesNeeded,
                    out _))
            {
                throw LastError(nameof(QueryServiceConfig));
            }
            var configuration = Marshal.PtrToStructure<
                QueryServiceConfigData>(buffer);
            return new(
                configuration.StartType,
                configuration.StartType == 2
                    ? QueryDelayedAutomatic(service)
                    : null);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool? QueryDelayedAutomatic(SafeServiceHandle service)
    {
        var size = checked((uint)Marshal.SizeOf<int>());
        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            if (QueryServiceConfig2(
                    service,
                    ServiceConfigDelayedAutoStartInfo,
                    buffer,
                    size,
                    out _))
            {
                return Marshal.ReadInt32(buffer) != 0;
            }
            var error = Marshal.GetLastWin32Error();
            return error is ErrorInvalidLevel or ErrorInvalidParameter
                ? null
                : throw new Win32Exception(
                    error,
                    "Delayed-start configuration read failed.");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static Win32Exception LastError(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        return new Win32Exception(
            error,
            $"{operation} failed: {new Win32Exception(error).Message}");
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeServiceHandle OpenSCManager(
        string? machineName,
        string? databaseName,
        uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeServiceHandle OpenService(
        SafeServiceHandle serviceControlManager,
        string serviceName,
        uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumServicesStatusEx(
        SafeServiceHandle serviceControlManager,
        int infoLevel,
        uint serviceType,
        uint serviceState,
        IntPtr services,
        uint bufferSize,
        out uint bytesNeeded,
        out uint servicesReturned,
        ref uint resumeHandle,
        string? groupName);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfig(
        SafeServiceHandle service,
        IntPtr serviceConfig,
        uint bufferSize,
        out uint bytesNeeded);

    [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfig2W",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfig2(
        SafeServiceHandle service,
        uint infoLevel,
        IntPtr buffer,
        uint bufferSize,
        out uint bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumServiceStatusProcess
    {
        public IntPtr ServiceName;
        public IntPtr DisplayName;
        public ServiceStatusProcess Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryServiceConfigData
    {
        public uint ServiceType;
        public uint StartType;
        public uint ErrorControl;
        public IntPtr BinaryPathName;
        public IntPtr LoadOrderGroup;
        public uint TagId;
        public IntPtr Dependencies;
        public IntPtr ServiceStartName;
        public IntPtr DisplayName;
    }

    private sealed class SafeServiceHandle :
        SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeServiceHandle() : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() =>
            CloseServiceHandle(handle);
    }
}
