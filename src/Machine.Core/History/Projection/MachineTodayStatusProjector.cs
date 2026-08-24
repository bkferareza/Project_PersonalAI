namespace Machine.Core;

public sealed record MachineTodayStatusProjection(
    string Title,
    bool HasObservedEnergy,
    double ObservedEnergyKilowattHours,
    decimal? EstimatedPcElectricityCost,
    ElectricityRateSnapshot? Rate);

public static class MachineTodayStatusProjector
{
    public const string Title = "Running bill today";

    public static MachineTodayStatusProjection Project(
        MachineTodayEnergyCostProjection? today)
    {
        var hasObservedEnergy = today?.HasObservedEnergy == true;
        var estimatedCost = hasObservedEnergy &&
            today?.CostCoverage == MachineCostCoverage.Complete &&
            today.Rate is not null
                ? today.EstimatedCost
                : null;

        return new(
            Title,
            hasObservedEnergy,
            hasObservedEnergy
                ? today!.ObservedEnergyWattHours / 1000d
                : 0d,
            estimatedCost,
            today?.Rate);
    }
}
