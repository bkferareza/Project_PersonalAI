namespace Machine.Core;

public enum MachineCostCoverage
{
    Unavailable,
    Complete,
    Partial
}

public sealed record MachineDailyEnergyCostPresentation(
    DateOnly Date,
    double ObservedEnergyKilowattHours,
    decimal? EstimatedCost,
    string? CurrencyCode,
    bool HasObservedEnergy);

public sealed record MachineDailyEnergyCostRangePresentation(
    IReadOnlyList<MachineDailyEnergyCostPresentation> Days,
    double ObservedEnergyKilowattHours,
    decimal? EstimatedCost,
    string? CurrencyCode,
    MachineCostCoverage CostCoverage);

public static class MachineDailyEnergyCostProjector
{
    public static MachineDailyEnergyCostRangePresentation Project(
        IEnumerable<MachineHistoryRollup> rollups,
        IEnumerable<ElectricityRateSnapshot> rates)
    {
        var byMonth = rates.GroupBy(rate => rate.EffectiveMonth)
            .ToDictionary(group => group.Key,
                group => group.OrderByDescending(rate => rate.RetrievedAt).First());
        var days = rollups.Where(rollup => rollup.ObservedEnergyWattHours is not null)
            .GroupBy(rollup => DateOnly.FromDateTime(rollup.BucketStart.ToLocalTime().Date))
            .Select(group =>
            {
                var wattHours = group.Sum(item => item.ObservedEnergyWattHours!.Total);
                var month = new DateOnly(group.Key.Year, group.Key.Month, 1);
                var rate = byMonth.GetValueOrDefault(month);
                return new MachineDailyEnergyCostPresentation(group.Key,
                    wattHours / 1000d,
                    MachineElectricityCostCalculator.Calculate(wattHours, rate),
                    rate?.CurrencyCode, wattHours > 0d);
            }).Where(day => day.HasObservedEnergy).OrderByDescending(day => day.Date).ToArray();
        var covered = days.Where(day => day.EstimatedCost is not null).ToArray();
        var coverage = days.Length == 0 || covered.Length == 0
            ? MachineCostCoverage.Unavailable
            : covered.Length == days.Length ? MachineCostCoverage.Complete
            : MachineCostCoverage.Partial;
        return new(days, days.Sum(day => day.ObservedEnergyKilowattHours),
            covered.Sum(day => day.EstimatedCost!.Value),
            covered.FirstOrDefault()?.CurrencyCode, coverage);
    }
}
