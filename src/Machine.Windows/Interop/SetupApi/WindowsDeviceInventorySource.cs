using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Machine.Core;
using Microsoft.Win32.SafeHandles;

namespace Machine.Windows;

internal interface IWindowsDeviceInventorySource
{
    NativeDeviceCapture Capture(CancellationToken cancellationToken);
}

internal sealed record NativeDeviceCapture(
    IReadOnlyList<NativeDeviceRecord> Devices,
    int ReadFailureCount,
    bool IsComplete,
    DateTimeOffset CapturedAt);

internal sealed record NativeDeviceRecord(
    string? DisplayName,
    string? DeviceClass,
    string? Manufacturer,
    bool IsPresent,
    bool? IsEnabled,
    uint? ProblemCode,
    string? DriverProvider,
    string? DriverVersion,
    DateOnly? DriverDate);

internal sealed class WindowsDeviceInventorySource
    : IWindowsDeviceInventorySource
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfAllClasses = 0x00000004;
    private const uint SpdrpDeviceDescription = 0x00000000;
    private const uint SpdrpClass = 0x00000007;
    private const uint SpdrpManufacturer = 0x0000000B;
    private const uint SpdrpFriendlyName = 0x0000000C;
    private const uint DicsFlagGlobal = 0x00000001;
    private const uint DiregDriver = 0x00000002;
    private const int KeyRead = 0x00020019;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorInvalidData = 13;
    private const uint CmProblemDisabled = 22;
    private const uint CrSuccess = 0;
    private const uint RrfRtRegSz = 0x00000002;
    private const uint RrfRtRegBinary = 0x00000008;

    public NativeDeviceCapture Capture(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deviceSet = SetupDiGetClassDevs(
            IntPtr.Zero,
            null,
            IntPtr.Zero,
            DigcfPresent | DigcfAllClasses);
        if (deviceSet.IsInvalid)
        {
            throw LastError(nameof(SetupDiGetClassDevs));
        }

        var devices = new List<NativeDeviceRecord>();
        var readFailures = 0;
        for (uint index = 0; ; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = new SpDevInfoData
            {
                Size = checked((uint)Marshal.SizeOf<SpDevInfoData>())
            };
            if (!SetupDiEnumDeviceInfo(deviceSet, index, ref data))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorNoMoreItems)
                {
                    break;
                }
                readFailures++;
                continue;
            }

            try
            {
                var friendlyName = ReadDeviceString(
                    deviceSet,
                    ref data,
                    SpdrpFriendlyName,
                    ref readFailures) ?? ReadDeviceString(
                        deviceSet,
                        ref data,
                        SpdrpDeviceDescription,
                        ref readFailures);
                var deviceClass = ReadDeviceString(
                    deviceSet,
                    ref data,
                    SpdrpClass,
                    ref readFailures);
                if (string.IsNullOrWhiteSpace(friendlyName) ||
                    string.IsNullOrWhiteSpace(deviceClass))
                {
                    readFailures++;
                    continue;
                }

                var manufacturer = ReadDeviceString(
                    deviceSet,
                    ref data,
                    SpdrpManufacturer,
                    ref readFailures);
                bool? isEnabled = null;
                uint? problemCode = null;
                var configResult = CM_Get_DevNode_Status(
                    out _,
                    out var nativeProblemCode,
                    data.DevInst,
                    0);
                if (configResult == CrSuccess)
                {
                    isEnabled = nativeProblemCode != CmProblemDisabled;
                    problemCode = nativeProblemCode == 0
                        ? null
                        : nativeProblemCode;
                }
                else
                {
                    readFailures++;
                }

                ReadDriver(
                    deviceSet,
                    ref data,
                    out var driverProvider,
                    out var driverVersion,
                    out var driverDate,
                    ref readFailures);
                devices.Add(new(
                    friendlyName,
                    deviceClass,
                    manufacturer,
                    true,
                    isEnabled,
                    problemCode,
                    driverProvider,
                    driverVersion,
                    driverDate));
            }
            catch (Exception exception) when (
                exception is Win32Exception or
                    InvalidOperationException or
                    UnauthorizedAccessException)
            {
                // A device may disappear while SetupAPI is projecting it.
                readFailures++;
            }
        }

        return new(
            devices,
            readFailures,
            readFailures == 0,
            DateTimeOffset.UtcNow);
    }

    private static string? ReadDeviceString(
        SafeDeviceInfoSetHandle deviceSet,
        ref SpDevInfoData data,
        uint property,
        ref int readFailures)
    {
        _ = SetupDiGetDeviceRegistryProperty(
            deviceSet,
            ref data,
            property,
            out _,
            IntPtr.Zero,
            0,
            out var requiredSize);
        var error = Marshal.GetLastWin32Error();
        if (error == ErrorInvalidData)
        {
            return null;
        }
        if (error != ErrorInsufficientBuffer || requiredSize == 0)
        {
            readFailures++;
            return null;
        }

        var buffer = Marshal.AllocHGlobal(checked((int)requiredSize));
        try
        {
            if (!SetupDiGetDeviceRegistryProperty(
                    deviceSet,
                    ref data,
                    property,
                    out _,
                    buffer,
                    requiredSize,
                    out _))
            {
                readFailures++;
                return null;
            }
            return Marshal.PtrToStringUni(buffer)?.TrimEnd('\0');
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void ReadDriver(
        SafeDeviceInfoSetHandle deviceSet,
        ref SpDevInfoData data,
        out string? provider,
        out string? version,
        out DateOnly? date,
        ref int readFailures)
    {
        provider = null;
        version = null;
        date = null;
        var rawHandle = SetupDiOpenDevRegKey(
            deviceSet,
            ref data,
            DicsFlagGlobal,
            0,
            DiregDriver,
            KeyRead);
        if (rawHandle == new IntPtr(-1))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorInvalidData)
            {
                readFailures++;
            }
            return;
        }

        using var handle = new SafeRegistryHandle(
            rawHandle,
            ownsHandle: true);
        provider = ReadRegistryString(handle, "ProviderName");
        version = ReadRegistryString(handle, "DriverVersion");
        var fileTime = ReadRegistryFileTime(handle, "DriverDateData");
        if (fileTime is { } timestamp)
        {
            try
            {
                date = DateOnly.FromDateTime(
                    DateTime.FromFileTimeUtc(timestamp));
            }
            catch (ArgumentOutOfRangeException)
            {
                readFailures++;
            }
        }
        else
        {
            var dateText = ReadRegistryString(handle, "DriverDate");
            if (DateOnly.TryParse(
                    dateText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                date = parsed;
            }
        }
    }

    private static string? ReadRegistryString(
        SafeRegistryHandle key,
        string valueName)
    {
        uint size = 0;
        var result = RegGetValue(
            key,
            null,
            valueName,
            RrfRtRegSz,
            out _,
            IntPtr.Zero,
            ref size);
        if (result != 0 || size == 0)
        {
            return null;
        }
        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            result = RegGetValue(
                key,
                null,
                valueName,
                RrfRtRegSz,
                out _,
                buffer,
                ref size);
            return result == 0
                ? Marshal.PtrToStringUni(buffer)?.TrimEnd('\0')
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static long? ReadRegistryFileTime(
        SafeRegistryHandle key,
        string valueName)
    {
        uint size = sizeof(long);
        var buffer = Marshal.AllocHGlobal(sizeof(long));
        try
        {
            var result = RegGetValue(
                key,
                null,
                valueName,
                RrfRtRegBinary,
                out _,
                buffer,
                ref size);
            return result == 0 && size == sizeof(long)
                ? Marshal.ReadInt64(buffer)
                : null;
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

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeDeviceInfoSetHandle SetupDiGetClassDevs(
        IntPtr classGuid,
        string? enumerator,
        IntPtr parentWindow,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(
        SafeDeviceInfoSetHandle deviceInfoSet,
        uint memberIndex,
        ref SpDevInfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        uint property,
        out uint propertyRegistryDataType,
        IntPtr propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiOpenDevRegKey(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        uint scope,
        uint hardwareProfile,
        uint keyType,
        int desiredAccess);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(
        IntPtr deviceInfoSet);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Status(
        out uint status,
        out uint problemNumber,
        uint deviceInstance,
        uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegGetValue(
        SafeRegistryHandle key,
        string? subKey,
        string value,
        uint flags,
        out uint type,
        IntPtr data,
        ref uint dataSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    private sealed class SafeDeviceInfoSetHandle :
        SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeDeviceInfoSetHandle() : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() =>
            SetupDiDestroyDeviceInfoList(handle);
    }
}
