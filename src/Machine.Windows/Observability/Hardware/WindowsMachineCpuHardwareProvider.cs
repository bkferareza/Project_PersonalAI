using Microsoft.Win32;
using System.Runtime.InteropServices;
using Machine.Core;

namespace Machine.Windows;

public sealed class WindowsMachineCpuHardwareProvider : IMachineCpuHardwareProvider
{
    private const int ProcessorInformation = 11;

    public Task<MachineCpuHardwareSnapshot> GetAsync(
        MachineResourceSnapshot resources,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var name = ReadProcessorName();
        int? logicalProcessors = Environment.ProcessorCount > 0
            ? Environment.ProcessorCount : null;
        var isReference = name?.Contains("Ryzen 7 3800XT",
            StringComparison.OrdinalIgnoreCase) == true;
        var frequencies = ReadProcessorFrequencies(logicalProcessors ?? 0);
        double? effective = frequencies.Count == 0 ? null : frequencies
            .Where(item => item.CurrentMhz > 0)
            .Select(item => (double)item.CurrentMhz).DefaultIfEmpty().Average();
        double? maximum = frequencies.Count == 0 ? null : frequencies
            .Where(item => item.MaxMhz > 0)
            .Select(item => (double)item.MaxMhz).DefaultIfEmpty().Average();
        var (estimate, lower, upper, confidence) =
            MachineCpuPowerEstimator.Estimate(name, resources.CpuUsagePercent);
        var availability = name is null
            ? MachineHardwareTelemetryAvailability.Unavailable
            : effective is null
                ? MachineHardwareTelemetryAvailability.Partial
                : MachineHardwareTelemetryAvailability.Available;
        return Task.FromResult(new MachineCpuHardwareSnapshot(
            resources.CapturedAt,
            name,
            isReference ? 8 : null,
            logicalProcessors,
            resources.CpuUsagePercent,
            null,
            null,
            effective,
            isReference ? 3900d : null,
            isReference ? 4700d : maximum,
            null,
            null,
            null,
            estimate,
            lower,
            upper,
            confidence,
            availability,
            "CPU package temperature and measured package power are unavailable through the current safe Windows path."));
    }

    private static string? ReadProcessorName() => Registry.GetValue(
        @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
        "ProcessorNameString", null) as string;

    private static IReadOnlyList<ProcessorPowerInformation>
        ReadProcessorFrequencies(int logicalProcessorCount)
    {
        if (logicalProcessorCount <= 0)
        {
            return [];
        }

        var size = Marshal.SizeOf<ProcessorPowerInformation>() * logicalProcessorCount;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var status = CallNtPowerInformation(ProcessorInformation,
                IntPtr.Zero, 0, buffer, (uint)size);
            if (status != 0)
            {
                return [];
            }
            var result = new List<ProcessorPowerInformation>(logicalProcessorCount);
            for (var index = 0; index < logicalProcessorCount; index++)
            {
                result.Add(Marshal.PtrToStructure<ProcessorPowerInformation>(
                    buffer + index * Marshal.SizeOf<ProcessorPowerInformation>()));
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(
        int informationLevel,
        IntPtr inputBuffer,
        uint inputBufferLength,
        IntPtr outputBuffer,
        uint outputBufferLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPowerInformation
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }
}
