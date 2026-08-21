namespace Machine.Core;

public sealed record MachineRunningBillInsight(
    string Title,
    decimal EstimatedPcElectricityCost,
    double TodayObservedEnergyKilowattHours,
    ElectricityRateSnapshot Rate);

public static class MachineRunningBillInsightProjector
{
    public const string Title = "Running bill today";

    public static MachineRunningBillInsight? Project(
        MachineTodayEnergyCostProjection? today) =>
        today is
        {
            HasObservedEnergy: true,
            EstimatedCost: { } cost,
            CostCoverage: MachineCostCoverage.Complete,
            Rate: { } rate
        }
            ? new(Title, cost,
                today.ObservedEnergyWattHours / 1000d, rate)
            : null;
}
