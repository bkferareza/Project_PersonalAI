using System.ComponentModel;
using System.Runtime.InteropServices;
using Machine.Core;

namespace Machine.Windows;

public sealed class WindowsMachineResourceProvider : IMachineResourceProvider
{
    private const int CpuSamplingDelayMilliseconds = 250;

    public async Task<MachineResourceSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var firstSample = ReadSystemTimes();

        await Task.Delay(
            CpuSamplingDelayMilliseconds,
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var secondSample = ReadSystemTimes();
        var memoryStatus = ReadMemoryStatus();

        cancellationToken.ThrowIfCancellationRequested();

        var totalMemory = memoryStatus.TotalPhysical;
        var availableMemory = Math.Min(
            memoryStatus.AvailablePhysical,
            totalMemory);
        var usedMemory = totalMemory - availableMemory;

        return new MachineResourceSnapshot(
            CpuUsagePercent: CalculateCpuUsage(
                firstSample,
                secondSample),
            TotalMemoryBytes: totalMemory,
            UsedMemoryBytes: usedMemory,
            CapturedAt: DateTimeOffset.UtcNow);
    }

    private static SystemTimeSample ReadSystemTimes()
    {
        if (!GetSystemTimes(
                out var idleTime,
                out var kernelTime,
                out var userTime))
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw CreateWindowsException(
                errorCode,
                nameof(GetSystemTimes));
        }

        return new SystemTimeSample(
            ToUInt64(idleTime),
            ToUInt64(kernelTime),
            ToUInt64(userTime));
    }

    private static MemoryStatusEx ReadMemoryStatus()
    {
        var status = new MemoryStatusEx
        {
            Length = checked(
                (uint)Marshal.SizeOf<MemoryStatusEx>())
        };

        if (!GlobalMemoryStatusEx(ref status))
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw CreateWindowsException(
                errorCode,
                nameof(GlobalMemoryStatusEx));
        }

        return status;
    }

    private static double CalculateCpuUsage(
        SystemTimeSample first,
        SystemTimeSample second)
    {
        if (second.Idle < first.Idle ||
            second.Kernel < first.Kernel ||
            second.User < first.User)
        {
            return 0d;
        }

        var idleDelta = second.Idle - first.Idle;
        var kernelDelta = second.Kernel - first.Kernel;
        var userDelta = second.User - first.User;

        // Windows kernel time includes idle time.
        var totalDelta = (double)kernelDelta + userDelta;
        if (totalDelta <= 0d)
        {
            return 0d;
        }

        var busyDelta = Math.Max(
            0d,
            totalDelta - idleDelta);

        return Math.Clamp(
            busyDelta / totalDelta * 100d,
            0d,
            100d);
    }

    private static ulong ToUInt64(NativeFileTime value) =>
        ((ulong)value.HighDateTime << 32) |
        value.LowDateTime;

    private static Win32Exception CreateWindowsException(
        int errorCode,
        string operation)
    {
        var systemMessage = new Win32Exception(errorCode).Message;

        return new Win32Exception(
            errorCode,
            $"{operation} failed: {systemMessage}");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out NativeFileTime idleTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(
        ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private readonly record struct SystemTimeSample(
        ulong Idle,
        ulong Kernel,
        ulong User);
}
