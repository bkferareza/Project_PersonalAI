using System.Runtime.InteropServices;
using System.Text;
using Machine.Core;

namespace Machine.Windows;

internal interface INvmlTelemetrySource : IDisposable
{
    NvmlCapture Capture(CancellationToken cancellationToken);
}

internal sealed record NvmlCapture(
    bool LibraryAvailable,
    bool IsComplete,
    IReadOnlyList<NvmlDeviceCapture> Devices,
    string? FailureCode = null);

internal sealed record NvmlDeviceCapture(
    int AdapterIndex,
    string? AdapterName,
    double? GpuUtilizationPercent,
    ulong? MemoryUsedBytes,
    ulong? MemoryTotalBytes,
    double? TemperatureCelsius,
    double? BoardPowerWatts,
    uint? GraphicsClockMHz,
    uint? MemoryClockMHz,
    double? FanPercent);

internal sealed class DynamicNvmlTelemetrySource : INvmlTelemetrySource
{
    private const int NvmlSuccess = 0;
    private const int NvmlErrorNotSupported = 3;
    private const int NvmlErrorLibraryNotFound = 12;
    private const int NvmlErrorFunctionNotFound = 13;
    private const uint TemperatureGpu = 0;
    private const uint ClockGraphics = 0;
    private const uint ClockMemory = 2;
    private const int DeviceNameBufferLength = 96;
    private const string LibraryName = "nvml.dll";

    private readonly object _sync = new();
    private IntPtr _library;
    private NvmlInit? _initialize;
    private NvmlShutdown? _shutdown;
    private NvmlDeviceGetCount? _getCount;
    private NvmlDeviceGetHandleByIndex? _getHandle;
    private NvmlDeviceGetName? _getName;
    private NvmlDeviceGetUtilizationRates? _getUtilization;
    private NvmlDeviceGetMemoryInfo? _getMemory;
    private NvmlDeviceGetUnsignedValue? _getTemperature;
    private NvmlDeviceGetValue? _getPowerUsage;
    private NvmlDeviceGetClockInfo? _getClockInfo;
    private NvmlDeviceGetValue? _getFanSpeed;
    private bool _initialized;
    private bool _disposed;

    public NvmlCapture Capture(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (!EnsureInitialized(out var failureCode))
            {
                return new(false, false, [], failureCode);
            }
            if (_getCount!(out var count) != NvmlSuccess)
            {
                return new(true, false, [], "nvml.device-count-failed");
            }
            if (count == 0)
            {
                return new(true, true, [], "nvml.no-device");
            }

            var devices = new List<NvmlDeviceCapture>(
                checked((int)Math.Min(
                    count,
                    (uint)WindowsMachineGpuTelemetryProvider.MaximumGpuCount)));
            var complete = count <=
                WindowsMachineGpuTelemetryProvider.MaximumGpuCount;
            for (uint index = 0;
                 index < count && index <
                    WindowsMachineGpuTelemetryProvider.MaximumGpuCount;
                 index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_getHandle!(index, out var device) != NvmlSuccess ||
                    device == IntPtr.Zero)
                {
                    complete = false;
                    continue;
                }

                var name = ReadName(device, ref complete);
                var utilization = ReadUtilization(device, ref complete);
                var memory = ReadMemory(device, ref complete);
                var temperature = ReadUnsigned(
                    _getTemperature,
                    device,
                    TemperatureGpu,
                    ref complete,
                    value => value);
                var power = ReadUnsigned(
                    _getPowerUsage,
                    device,
                    ref complete,
                    value => value / 1000d);
                var graphicsClock = ReadClock(
                    device,
                    ClockGraphics,
                    ref complete);
                var memoryClock = ReadClock(
                    device,
                    ClockMemory,
                    ref complete);
                var fan = ReadUnsigned(
                    _getFanSpeed,
                    device,
                    ref complete,
                    value => value);
                devices.Add(new(
                    checked((int)index),
                    name,
                    utilization?.Gpu,
                    memory?.Used,
                    memory?.Total,
                    temperature,
                    power,
                    graphicsClock,
                    memoryClock,
                    fan));
            }
            return new(true, complete, devices);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_initialized)
            {
                try
                {
                    _ = _shutdown?.Invoke();
                }
                catch
                {
                }
                _initialized = false;
            }
            if (_library != IntPtr.Zero)
            {
                NativeLibrary.Free(_library);
                _library = IntPtr.Zero;
            }
        }
    }

    private bool EnsureInitialized(out string? failureCode)
    {
        failureCode = null;
        if (_initialized)
        {
            return true;
        }
        if (_library == IntPtr.Zero &&
            !NativeLibrary.TryLoad(LibraryName, out _library))
        {
            failureCode = "nvml.library-not-found";
            return false;
        }

        if (!TryBind("nvmlInit_v2", out _initialize) ||
            !TryBind("nvmlShutdown", out _shutdown) ||
            !TryBind("nvmlDeviceGetCount_v2", out _getCount) ||
            !TryBind("nvmlDeviceGetHandleByIndex_v2", out _getHandle) ||
            !TryBind("nvmlDeviceGetName", out _getName))
        {
            failureCode = "nvml.required-function-unavailable";
            return false;
        }
        TryBind("nvmlDeviceGetUtilizationRates", out _getUtilization);
        TryBind("nvmlDeviceGetMemoryInfo", out _getMemory);
        TryBind("nvmlDeviceGetTemperature", out _getTemperature);
        TryBind("nvmlDeviceGetPowerUsage", out _getPowerUsage);
        TryBind("nvmlDeviceGetClockInfo", out _getClockInfo);
        TryBind("nvmlDeviceGetFanSpeed", out _getFanSpeed);
        var result = _initialize!();
        if (result != NvmlSuccess)
        {
            failureCode = NormalizeError(result);
            return false;
        }
        _initialized = true;
        return true;
    }

    private string? ReadName(IntPtr device, ref bool complete)
    {
        var builder = new StringBuilder(DeviceNameBufferLength);
        var result = _getName!(
            device,
            builder,
            checked((uint)builder.Capacity));
        if (result == NvmlSuccess)
        {
            return builder.ToString();
        }
        complete = false;
        return null;
    }

    private NvmlUtilization? ReadUtilization(
        IntPtr device,
        ref bool complete)
    {
        if (_getUtilization is null)
        {
            complete = false;
            return null;
        }
        var result = _getUtilization(device, out var value);
        if (result == NvmlSuccess)
        {
            return value;
        }
        complete = false;
        return null;
    }

    private NvmlMemory? ReadMemory(
        IntPtr device,
        ref bool complete)
    {
        if (_getMemory is null)
        {
            complete = false;
            return null;
        }
        var result = _getMemory(device, out var value);
        if (result == NvmlSuccess)
        {
            return value;
        }
        complete = false;
        return null;
    }

    private uint? ReadClock(
        IntPtr device,
        uint clockType,
        ref bool complete)
    {
        if (_getClockInfo is null)
        {
            complete = false;
            return null;
        }
        var result = _getClockInfo(device, clockType, out var value);
        if (result == NvmlSuccess)
        {
            return value;
        }
        complete = false;
        return null;
    }

    private static double? ReadUnsigned(
        NvmlDeviceGetUnsignedValue? query,
        IntPtr device,
        uint selector,
        ref bool complete,
        Func<uint, double> convert)
    {
        if (query is null)
        {
            complete = false;
            return null;
        }
        var result = query(device, selector, out var value);
        if (result == NvmlSuccess)
        {
            return convert(value);
        }
        if (result != NvmlErrorNotSupported)
        {
            complete = false;
        }
        else
        {
            complete = false;
        }
        return null;
    }

    private static double? ReadUnsigned(
        NvmlDeviceGetValue? query,
        IntPtr device,
        ref bool complete,
        Func<uint, double> convert)
    {
        if (query is null)
        {
            complete = false;
            return null;
        }
        var result = query(device, out var value);
        if (result == NvmlSuccess)
        {
            return convert(value);
        }
        complete = false;
        return null;
    }

    private bool TryBind<T>(string name, out T? function)
        where T : Delegate
    {
        if (NativeLibrary.TryGetExport(_library, name, out var address))
        {
            function = Marshal.GetDelegateForFunctionPointer<T>(address);
            return true;
        }
        function = null;
        return false;
    }

    private static string NormalizeError(int result) => result switch
    {
        NvmlErrorLibraryNotFound => "nvml.library-not-found",
        NvmlErrorFunctionNotFound => "nvml.function-not-found",
        _ => $"nvml.error-{result}"
    };

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlInit();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlShutdown();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetCount(out uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetHandleByIndex(
        uint index,
        out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetName(
        IntPtr device,
        [MarshalAs(UnmanagedType.LPStr)] StringBuilder name,
        uint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetUtilizationRates(
        IntPtr device,
        out NvmlUtilization utilization);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetMemoryInfo(
        IntPtr device,
        out NvmlMemory memory);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetUnsignedValue(
        IntPtr device,
        uint selector,
        out uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetValue(
        IntPtr device,
        out uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetClockInfo(
        IntPtr device,
        uint clockType,
        out uint clockMHz);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }
}
