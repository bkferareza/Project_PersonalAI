namespace Machine.Core;

public static class MachineHistoryRangePolicy
{
    public static MachineHistoryResolution SelectResolution(
        MachineHistoryRange range) => range switch
        {
            MachineHistoryRange.Last24Hours =>
                MachineHistoryResolution.FiveMinutes,
            MachineHistoryRange.Last7Days or MachineHistoryRange.Last30Days =>
                MachineHistoryResolution.Hour,
            MachineHistoryRange.All => MachineHistoryResolution.Month,
            _ => throw new ArgumentOutOfRangeException(nameof(range))
        };

    public static DateTimeOffset? GetCutoff(
        MachineHistoryRange range,
        DateTimeOffset now) => range switch
        {
            MachineHistoryRange.Last24Hours => now.AddHours(-24),
            MachineHistoryRange.Last7Days => now.AddDays(-7),
            MachineHistoryRange.Last30Days => now.AddDays(-30),
            MachineHistoryRange.All => null,
            _ => throw new ArgumentOutOfRangeException(nameof(range))
        };
}

public static class MachineHistoryEventGrouper
{
    public static readonly TimeSpan DefaultApplicationFailureWindow =
        TimeSpan.FromHours(2);

    public static IReadOnlyList<MachineHistoryEvent> GroupForDisplay(
        IEnumerable<MachineHistoryEvent> events,
        TimeSpan? applicationFailureWindow = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        var window = applicationFailureWindow ??
            DefaultApplicationFailureWindow;
        if (window < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(applicationFailureWindow));
        }

        var result = new List<MachineHistoryEvent>();
        foreach (var candidate in events.OrderBy(item => item.OccurredAt))
        {
            if (candidate.Kind !=
                    MachineHistoryEventKind.ApplicationFailureRecorded ||
                string.IsNullOrWhiteSpace(candidate.Detail))
            {
                result.Add(candidate);
                continue;
            }

            var previousIndex = result.FindLastIndex(item =>
                item.Kind ==
                    MachineHistoryEventKind.ApplicationFailureRecorded &&
                string.Equals(
                    item.Detail,
                    candidate.Detail,
                    StringComparison.OrdinalIgnoreCase) &&
                candidate.OccurredAt -
                    (item.PeriodEnd ?? item.OccurredAt) <= window);
            if (previousIndex < 0)
            {
                result.Add(candidate with
                {
                    PeriodStart = candidate.PeriodStart ??
                        candidate.OccurredAt,
                    PeriodEnd = candidate.PeriodEnd ?? candidate.OccurredAt
                });
                continue;
            }

            var previous = result[previousIndex];
            result[previousIndex] = previous with
            {
                OccurredAt = candidate.OccurredAt,
                Count = SaturatingAdd(previous.Count, candidate.Count),
                PeriodStart = previous.PeriodStart ?? previous.OccurredAt,
                PeriodEnd = candidate.PeriodEnd ?? candidate.OccurredAt
            };
        }

        return result.OrderByDescending(item => item.OccurredAt).ToArray();
    }

    private static int SaturatingAdd(int left, int right) =>
        left > int.MaxValue - right ? int.MaxValue : left + right;
}

internal static class MachineHistoryAggregation
{
    public static MachineHistoryRollup Create(
        DateTimeOffset bucketStart,
        DateTimeOffset bucketEnd) => new(
        bucketStart,
        bucketEnd,
        0,
        null,
        null,
        null,
        null,
        null,
        new(0, 0, 0, 0, 0),
        new(0, 0));

    public static MachineHistoryRollup AddObservation(
        MachineHistoryRollup rollup,
        MachineHistoryObservation observation) => rollup with
        {
            CpuUtilizationPercent = Add(
                rollup.CpuUtilizationPercent,
                observation.CpuUtilizationPercent),
            MemoryUtilizationPercent = Add(
                rollup.MemoryUtilizationPercent,
                observation.MemoryUtilizationPercent),
            NetworkReceiveBytesPerSecond = Add(
                rollup.NetworkReceiveBytesPerSecond,
                observation.NetworkReceiveBytesPerSecond),
            NetworkSendBytesPerSecond = Add(
                rollup.NetworkSendBytesPerSecond,
                observation.NetworkSendBytesPerSecond),
            SystemVolumeFreePercent = Add(
                rollup.SystemVolumeFreePercent,
                observation.SystemVolumeFreePercent),
            GpuUtilizationPercent = Add(
                rollup.GpuUtilizationPercent,
                observation.GpuUtilizationPercent),
            GpuMemoryUtilizationPercent = Add(
                rollup.GpuMemoryUtilizationPercent,
                observation.GpuMemoryUtilizationPercent),
            GpuTemperatureCelsius = Add(
                rollup.GpuTemperatureCelsius,
                observation.GpuTemperatureCelsius),
            GpuBoardPowerWatts = Add(
                rollup.GpuBoardPowerWatts,
                observation.GpuBoardPowerWatts),
            CpuTemperatureCelsius = Add(
                rollup.CpuTemperatureCelsius,
                observation.CpuTemperatureCelsius),
            CpuPackagePowerWatts = Add(
                rollup.CpuPackagePowerWatts,
                observation.CpuPackagePowerWatts),
            StorageTemperatureCelsius = Add(
                rollup.StorageTemperatureCelsius,
                observation.StorageTemperatureCelsius),
            EstimatedSystemPowerWatts = Add(
                rollup.EstimatedSystemPowerWatts,
                observation.EstimatedSystemPowerWatts),
            EnergyWattHours = Add(
                rollup.EnergyWattHours,
                observation.EnergyWattHours)
        };

    public static MachineHistoryRollup AddDuration(
        MachineHistoryRollup rollup,
        long ticks,
        MachineOverallState? state,
        MachineUserActivityState? activity)
    {
        if (ticks <= 0)
        {
            return rollup;
        }

        var maximumTicks = Math.Max(
            0,
            (rollup.BucketEnd - rollup.BucketStart).Ticks);
        var remaining = Math.Max(
            0,
            maximumTicks - rollup.ObservedDurationTicks);
        var acceptedTicks = Math.Min(ticks, remaining);
        if (acceptedTicks <= 0)
        {
            return rollup;
        }

        var states = rollup.StateDurations;
        states = (state ?? MachineOverallState.Unknown) switch
        {
            MachineOverallState.Stable => states with
            {
                StableTicks = SaturatingAdd(
                    states.StableTicks,
                    acceptedTicks)
            },
            MachineOverallState.Attention => states with
            {
                AttentionTicks = SaturatingAdd(
                    states.AttentionTicks,
                    acceptedTicks)
            },
            MachineOverallState.Warning => states with
            {
                WarningTicks = SaturatingAdd(
                    states.WarningTicks,
                    acceptedTicks)
            },
            MachineOverallState.Critical => states with
            {
                CriticalTicks = SaturatingAdd(
                    states.CriticalTicks,
                    acceptedTicks)
            },
            _ => states with
            {
                UnknownTicks = SaturatingAdd(
                    states.UnknownTicks,
                    acceptedTicks)
            }
        };

        var activities = rollup.ActivityDurations;
        activities = activity switch
        {
            MachineUserActivityState.Active => activities with
            {
                ActiveTicks = SaturatingAdd(
                    activities.ActiveTicks,
                    acceptedTicks)
            },
            MachineUserActivityState.Idle => activities with
            {
                IdleTicks = SaturatingAdd(
                    activities.IdleTicks,
                    acceptedTicks)
            },
            _ => activities
        };

        return rollup with
        {
            ObservedDurationTicks = SaturatingAdd(
                rollup.ObservedDurationTicks,
                acceptedTicks),
            StateDurations = states,
            ActivityDurations = activities
        };
    }

    public static MachineHistoryRollup Merge(
        MachineHistoryRollup target,
        MachineHistoryRollup contribution)
    {
        var merged = target with
        {
            CpuUtilizationPercent = Merge(
                target.CpuUtilizationPercent,
                contribution.CpuUtilizationPercent),
            MemoryUtilizationPercent = Merge(
                target.MemoryUtilizationPercent,
                contribution.MemoryUtilizationPercent),
            NetworkReceiveBytesPerSecond = Merge(
                target.NetworkReceiveBytesPerSecond,
                contribution.NetworkReceiveBytesPerSecond),
            NetworkSendBytesPerSecond = Merge(
                target.NetworkSendBytesPerSecond,
                contribution.NetworkSendBytesPerSecond),
            SystemVolumeFreePercent = Merge(
                target.SystemVolumeFreePercent,
                contribution.SystemVolumeFreePercent),
            GpuUtilizationPercent = Merge(
                target.GpuUtilizationPercent,
                contribution.GpuUtilizationPercent),
            GpuMemoryUtilizationPercent = Merge(
                target.GpuMemoryUtilizationPercent,
                contribution.GpuMemoryUtilizationPercent),
            GpuTemperatureCelsius = Merge(
                target.GpuTemperatureCelsius,
                contribution.GpuTemperatureCelsius),
            GpuBoardPowerWatts = Merge(
                target.GpuBoardPowerWatts,
                contribution.GpuBoardPowerWatts),
            CpuTemperatureCelsius = Merge(
                target.CpuTemperatureCelsius,
                contribution.CpuTemperatureCelsius),
            CpuPackagePowerWatts = Merge(
                target.CpuPackagePowerWatts,
                contribution.CpuPackagePowerWatts),
            StorageTemperatureCelsius = Merge(
                target.StorageTemperatureCelsius,
                contribution.StorageTemperatureCelsius),
            EstimatedSystemPowerWatts = Merge(
                target.EstimatedSystemPowerWatts,
                contribution.EstimatedSystemPowerWatts),
            EnergyWattHours = Merge(
                target.EnergyWattHours,
                contribution.EnergyWattHours)
        };

        merged = AddStateDurations(merged, contribution.StateDurations);
        merged = AddActivityDurations(
            merged,
            contribution.ActivityDurations);
        var maximumTicks = Math.Max(
            0,
            (target.BucketEnd - target.BucketStart).Ticks);
        return merged with
        {
            ObservedDurationTicks = Math.Min(
                maximumTicks,
                SaturatingAdd(
                    target.ObservedDurationTicks,
                    contribution.ObservedDurationTicks))
        };
    }

    public static bool IsValid(MachineHistoryRollup rollup) =>
        rollup.BucketStart.Offset == TimeSpan.Zero &&
        rollup.BucketEnd.Offset == TimeSpan.Zero &&
        rollup.BucketEnd > rollup.BucketStart &&
        rollup.ObservedDurationTicks >= 0 &&
        rollup.ObservedDurationTicks <=
            (rollup.BucketEnd - rollup.BucketStart).Ticks &&
        IsValid(rollup.CpuUtilizationPercent) &&
        IsValid(rollup.MemoryUtilizationPercent) &&
        IsValid(rollup.NetworkReceiveBytesPerSecond) &&
        IsValid(rollup.NetworkSendBytesPerSecond) &&
        IsValid(rollup.SystemVolumeFreePercent) &&
        IsValid(rollup.GpuUtilizationPercent) &&
        IsValid(rollup.GpuMemoryUtilizationPercent) &&
        IsValid(rollup.GpuTemperatureCelsius) &&
        IsValid(rollup.GpuBoardPowerWatts) &&
        IsValid(rollup.CpuTemperatureCelsius) &&
        IsValid(rollup.CpuPackagePowerWatts) &&
        IsValid(rollup.StorageTemperatureCelsius) &&
        IsValid(rollup.EstimatedSystemPowerWatts) &&
        IsValid(rollup.EnergyWattHours) &&
        AreNonNegative(rollup.StateDurations.StableTicks,
            rollup.StateDurations.AttentionTicks,
            rollup.StateDurations.WarningTicks,
            rollup.StateDurations.CriticalTicks,
            rollup.StateDurations.UnknownTicks,
            rollup.ActivityDurations.ActiveTicks,
            rollup.ActivityDurations.IdleTicks);

    private static MachineHistoryNumericSummary? Add(
        MachineHistoryNumericSummary? summary,
        double? value)
    {
        if (value is null || !double.IsFinite(value.Value))
        {
            return summary;
        }

        if (summary is null)
        {
            return new(1, value.Value, value.Value, value.Value);
        }

        var count = summary.SampleCount == long.MaxValue
            ? long.MaxValue
            : summary.SampleCount + 1;
        var denominator = count == long.MaxValue
            ? (double)long.MaxValue
            : count;
        var mean = summary.Mean +
            (value.Value - summary.Mean) / denominator;
        return new(
            count,
            Math.Min(summary.Minimum, value.Value),
            Math.Max(summary.Maximum, value.Value),
            mean);
    }

    private static MachineHistoryNumericSummary? Merge(
        MachineHistoryNumericSummary? left,
        MachineHistoryNumericSummary? right)
    {
        if (left is null)
        {
            return right;
        }
        if (right is null)
        {
            return left;
        }

        var count = SaturatingAdd(left.SampleCount, right.SampleCount);
        var total = (double)left.SampleCount + right.SampleCount;
        var mean = total <= 0d || !double.IsFinite(total)
            ? left.Mean
            : (left.Mean * left.SampleCount +
               right.Mean * right.SampleCount) / total;
        return new(
            count,
            Math.Min(left.Minimum, right.Minimum),
            Math.Max(left.Maximum, right.Maximum),
            mean);
    }

    private static MachineHistoryRollup AddStateDurations(
        MachineHistoryRollup rollup,
        MachineHistoryStateDurations contribution) => rollup with
        {
            StateDurations = new(
                SaturatingAdd(
                    rollup.StateDurations.StableTicks,
                    contribution.StableTicks),
                SaturatingAdd(
                    rollup.StateDurations.AttentionTicks,
                    contribution.AttentionTicks),
                SaturatingAdd(
                    rollup.StateDurations.WarningTicks,
                    contribution.WarningTicks),
                SaturatingAdd(
                    rollup.StateDurations.CriticalTicks,
                    contribution.CriticalTicks),
                SaturatingAdd(
                    rollup.StateDurations.UnknownTicks,
                    contribution.UnknownTicks))
        };

    private static MachineHistoryRollup AddActivityDurations(
        MachineHistoryRollup rollup,
        MachineHistoryActivityDurations contribution) => rollup with
        {
            ActivityDurations = new(
                SaturatingAdd(
                    rollup.ActivityDurations.ActiveTicks,
                    contribution.ActiveTicks),
                SaturatingAdd(
                    rollup.ActivityDurations.IdleTicks,
                    contribution.IdleTicks))
        };

    private static bool IsValid(MachineHistoryNumericSummary? summary) =>
        summary is null ||
        summary.SampleCount > 0 &&
        double.IsFinite(summary.Minimum) &&
        double.IsFinite(summary.Maximum) &&
        double.IsFinite(summary.Mean) &&
        summary.Minimum <= summary.Mean &&
        summary.Mean <= summary.Maximum;

    private static bool AreNonNegative(params long[] values) =>
        values.All(value => value >= 0);

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right
            ? long.MaxValue
            : left + right;
}

