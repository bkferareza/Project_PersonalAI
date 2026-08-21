namespace Machine.Core;

public sealed record MachineHistoryEnergyCostSummary(
    double ObservedWattHours,
    decimal? EstimatedCost,
    int MonthsWithRate,
    int MonthsWithoutRate);

public static class MachineHistoryEnergyCostProjector
{
    public static MachineHistoryEnergyCostSummary Project(
        IEnumerable<MachineHistoryRollup> rollups,
        IEnumerable<ElectricityRateSnapshot> rates)
    {
        ArgumentNullException.ThrowIfNull(rollups);
        ArgumentNullException.ThrowIfNull(rates);
        var rateByMonth = rates.Where(rate => rate.RatePerKWh > 0m)
            .GroupBy(rate => rate.EffectiveMonth)
            .ToDictionary(group => group.Key,
                group => group.OrderByDescending(rate => rate.RetrievedAt).First());
        var energy = 0d;
        decimal totalCost = 0m;
        var ratedMonths = new HashSet<DateOnly>();
        var unratedMonths = new HashSet<DateOnly>();
        foreach (var rollup in rollups)
        {
            var wattHours = rollup.ObservedEnergyWattHours?.Total;
            if (wattHours is not { } value || value <= 0d) continue;
            energy += value;
            var local = rollup.BucketStart.ToLocalTime();
            var month = new DateOnly(local.Year, local.Month, 1);
            if (!rateByMonth.TryGetValue(month, out var rate))
            {
                unratedMonths.Add(month);
                continue;
            }
            var cost = MachineElectricityCostCalculator.Calculate(value, rate);
            if (cost is { } amount)
            {
                totalCost += amount;
                ratedMonths.Add(month);
            }
        }
        return new(energy, ratedMonths.Count == 0 ? null : totalCost,
            ratedMonths.Count, unratedMonths.Count);
    }
}
