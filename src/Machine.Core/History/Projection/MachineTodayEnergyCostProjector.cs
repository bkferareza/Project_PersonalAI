namespace Machine.Core;

public sealed record MachineTodayEnergyCostProjection(
    DateOnly LocalDate,
    double ObservedEnergyWattHours,
    decimal? EstimatedCost,
    MachineCostCoverage CostCoverage,
    TimeSpan ObservedDuration,
    double? AverageEstimatedWallPowerWatts,
    double? PeakEstimatedWallPowerWatts,
    long EnergyContributionCount,
    ElectricityRateSnapshot? Rate)
{
    public bool HasObservedEnergy =>
        EnergyContributionCount > 0 && ObservedEnergyWattHours > 0d;
}

public static class MachineTodayEnergyCostProjector
{
    public static MachineTodayEnergyCostProjection Project(
        IEnumerable<MachineHistoryRollup> rollups,
        IEnumerable<ElectricityRateSnapshot> rates,
        DateTimeOffset now,
        double pendingObservedEnergyWattHours = 0d,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(rollups);
        ArgumentNullException.ThrowIfNull(rates);
        var zone = timeZone ?? TimeZoneInfo.Local;
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var localDate = DateOnly.FromDateTime(localNow.Date);
        var utcNow = now.ToUniversalTime();
        var today = rollups.Where(rollup =>
            rollup.BucketStart <= utcNow &&
            DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(rollup.BucketStart, zone).Date) ==
                localDate).ToArray();
        var acceptedEnergy = today.Sum(rollup =>
            rollup.ObservedEnergyWattHours?.Total ?? 0d);
        var acceptedContributions = today.Aggregate(
            0L,
            (total, rollup) => SaturatingAdd(total,
                rollup.ObservedEnergyWattHours?.ContributionCount ?? 0));
        var pendingEnergy = double.IsFinite(pendingObservedEnergyWattHours) &&
            pendingObservedEnergyWattHours > 0d
                ? pendingObservedEnergyWattHours
                : 0d;
        var energy = acceptedEnergy + pendingEnergy;
        var contributionCount = pendingEnergy > 0d
            ? SaturatingAdd(acceptedContributions, 1)
            : acceptedContributions;
        var month = new DateOnly(localDate.Year, localDate.Month, 1);
        var rate = rates.Where(candidate =>
                candidate.EffectiveMonth == month &&
                candidate.RatePerKWh > 0m)
            .OrderByDescending(candidate => candidate.RetrievedAt)
            .FirstOrDefault();
        var power = today.Select(rollup =>
                rollup.EstimatedSystemPowerWatts)
            .Where(summary => summary is not null)
            .Select(summary => summary!)
            .ToArray();
        var sampleCount = power.Sum(summary =>
            (double)summary.SampleCount);
        var observedTicks = today.Aggregate(
            0L,
            (total, rollup) => SaturatingAdd(total,
                rollup.ObservedDurationTicks));
        var cost = energy > 0d
            ? MachineElectricityCostCalculator.Calculate(energy, rate)
            : null;
        return new(localDate, energy, cost,
            cost is null ? MachineCostCoverage.Unavailable
                : MachineCostCoverage.Complete,
            TimeSpan.FromTicks(observedTicks),
            sampleCount > 0d
                ? power.Sum(summary =>
                    summary.Mean * summary.SampleCount) / sampleCount
                : null,
            power.Length > 0
                ? power.Max(summary => summary.Maximum)
                : null,
            contributionCount,
            rate);
    }

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right
            ? long.MaxValue
            : left + right;
}
