namespace Machine.Core;

public sealed record MachineHistoryInsightPeriod(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    long ObservedDurationSeconds,
    double? CpuMeanPercent,
    double? MemoryMeanPercent,
    double? NetworkReceiveMeanBytesPerSecond,
    double? NetworkSendMeanBytesPerSecond,
    double? GpuMeanPercent,
    double? GpuMemoryMeanPercent,
    double? GpuTemperatureMeanCelsius,
    double? GpuBoardPowerMeanWatts);

public sealed record MachineHistoryInsightEvent(
    DateTimeOffset OccurredAt,
    MachineHistoryEventKind Kind,
    string Title,
    string? Detail,
    int Count);

public sealed record MachineHistoryInsightContext(
    MachineHistoryInsightPeriod CurrentPeriod,
    MachineHistoryInsightPeriod? RecentComparable,
    MachineHistoryInsightEvent? SignificantEvent);

public static class MachineHistoryInsightProjector
{
    public static MachineHistoryInsightContext? Project(
        MachineHistorySnapshot history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var observed = history.Rollups
            .Where(HasEvidence)
            .OrderBy(item => item.BucketStart)
            .ToArray();
        if (observed.Length == 0)
        {
            return null;
        }

        var current = observed[^1];
        MachineHistoryRollup? comparable = null;
        if (observed.Length > 1)
        {
            var prior = observed[..^1];
            comparable = MachineHistoryAggregation.Create(
                prior[0].BucketStart,
                prior[^1].BucketEnd);
            foreach (var rollup in prior)
            {
                comparable = MachineHistoryAggregation.Merge(
                    comparable,
                    rollup);
            }
        }

        var significant = history.Events.FirstOrDefault(item =>
            item.Kind is
                MachineHistoryEventKind.UnexpectedShutdownRecorded or
                MachineHistoryEventKind.ApplicationFailureRecorded or
                MachineHistoryEventKind.ReliabilityIncidentRecorded or
                MachineHistoryEventKind.WindowsUpdateStateChanged or
                MachineHistoryEventKind.RestartPendingChanged or
                MachineHistoryEventKind.MachineStateChanged);
        return new(
            CreatePeriod(current),
            comparable is null ? null : CreatePeriod(comparable),
            significant is null
                ? null
                : new(
                    significant.OccurredAt,
                    significant.Kind,
                    significant.Title,
                    significant.Detail,
                    significant.Count));
    }

    private static bool HasEvidence(MachineHistoryRollup rollup) =>
        rollup.ObservedDurationTicks > 0 ||
        rollup.CpuUtilizationPercent is not null ||
        rollup.MemoryUtilizationPercent is not null ||
        rollup.NetworkReceiveBytesPerSecond is not null ||
        rollup.NetworkSendBytesPerSecond is not null ||
        rollup.GpuUtilizationPercent is not null;

    private static MachineHistoryInsightPeriod CreatePeriod(
        MachineHistoryRollup rollup) => new(
        rollup.BucketStart,
        rollup.BucketEnd,
        Math.Max(0, rollup.ObservedDurationTicks / TimeSpan.TicksPerSecond),
        rollup.CpuUtilizationPercent?.Mean,
        rollup.MemoryUtilizationPercent?.Mean,
        rollup.NetworkReceiveBytesPerSecond?.Mean,
        rollup.NetworkSendBytesPerSecond?.Mean,
        rollup.GpuUtilizationPercent?.Mean,
        rollup.GpuMemoryUtilizationPercent?.Mean,
        rollup.GpuTemperatureCelsius?.Mean,
        rollup.GpuBoardPowerWatts?.Mean);
}

