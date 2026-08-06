using System.ComponentModel;
using System.Diagnostics;
using Machine.Core;

namespace Machine.Windows;

public sealed class WindowsMachineProcessProvider : IMachineProcessProvider
{
    private const int SamplingDelayMilliseconds = 300;

    public async Task<IReadOnlyList<MachineProcessSnapshot>> GetTopAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        if (count is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var firstSamples = await Task.Run(
            () => CaptureProcesses(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var elapsedTime = Stopwatch.StartNew();

        await Task.Delay(
            SamplingDelayMilliseconds,
            cancellationToken).ConfigureAwait(false);

        var secondSamples = CaptureProcesses(cancellationToken);
        elapsedTime.Stop();

        cancellationToken.ThrowIfCancellationRequested();

        var snapshots = new List<MachineProcessSnapshot>();

        foreach (var secondSample in secondSamples.Values)
        {
            if (!firstSamples.TryGetValue(
                    secondSample.ProcessId,
                    out var firstSample) ||
                !string.Equals(
                    firstSample.Name,
                    secondSample.Name,
                    StringComparison.Ordinal) ||
                secondSample.TotalProcessorTime <
                    firstSample.TotalProcessorTime)
            {
                continue;
            }

            snapshots.Add(new MachineProcessSnapshot(
                secondSample.ProcessId,
                secondSample.Name,
                CalculateCpuUsage(
                    firstSample,
                    secondSample,
                    elapsedTime.Elapsed),
                secondSample.WorkingSetBytes));
        }

        return snapshots
            .OrderByDescending(snapshot => snapshot.CpuUsagePercent)
            .ThenByDescending(snapshot => snapshot.WorkingSetBytes)
            .ThenBy(
                snapshot => snapshot.Name,
                StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .ToArray();
    }

    private static Dictionary<int, ProcessSample> CaptureProcesses(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var samples = new Dictionary<int, ProcessSample>();
        var processes = Process.GetProcesses();

        try
        {
            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var processId = process.Id;
                    var name = process.ProcessName;
                    var workingSetBytes = process.WorkingSet64;
                    var totalProcessorTime = process.TotalProcessorTime;

                    if (processId <= 0 ||
                        string.IsNullOrWhiteSpace(name) ||
                        workingSetBytes < 0 ||
                        totalProcessorTime < TimeSpan.Zero)
                    {
                        continue;
                    }

                    samples[processId] = new ProcessSample(
                        processId,
                        name,
                        workingSetBytes,
                        totalProcessorTime);
                }
                catch (Exception exception)
                    when (IsProcessAccessException(exception))
                {
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return samples;
    }

    private static double CalculateCpuUsage(
        ProcessSample firstSample,
        ProcessSample secondSample,
        TimeSpan elapsedTime)
    {
        if (elapsedTime <= TimeSpan.Zero)
        {
            return 0d;
        }

        var processorTime =
            secondSample.TotalProcessorTime -
            firstSample.TotalProcessorTime;

        var cpuUsage =
            processorTime.TotalSeconds /
            elapsedTime.TotalSeconds /
            Environment.ProcessorCount *
            100d;

        return Math.Clamp(cpuUsage, 0d, 100d);
    }

    private static bool IsProcessAccessException(Exception exception) =>
        exception is Win32Exception or
            InvalidOperationException or
            NotSupportedException;

    private readonly record struct ProcessSample(
        int ProcessId,
        string Name,
        long WorkingSetBytes,
        TimeSpan TotalProcessorTime);
}
