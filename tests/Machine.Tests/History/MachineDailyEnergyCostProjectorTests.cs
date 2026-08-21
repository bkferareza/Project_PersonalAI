using Machine.Core;

namespace Machine.Tests;

public sealed class MachineDailyEnergyCostProjectorTests
{
    [Fact]
    public void GroupsAdditiveEnergyAndMarksPartialRateCoverage()
    {
        var august = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);
        var september = august.AddDays(1);
        var result = MachineDailyEnergyCostProjector.Project(
        [
            Rollup(august, 100d), Rollup(august.AddHours(1), 50d),
            Rollup(september, 200d)
        ],
        [Rate(new DateOnly(2026, 8, 1), 10m)]);

        Assert.Equal(0.35d, result.ObservedEnergyKilowattHours, 6);
        Assert.Equal(1.50m, result.EstimatedCost);
        Assert.Equal(MachineCostCoverage.Partial, result.CostCoverage);
        Assert.Equal(0.15d, result.Days.Single(day => day.Date.Month == 8).ObservedEnergyKilowattHours, 6);
        Assert.Null(result.Days.Single(day => day.Date.Month == 9).EstimatedCost);
    }

    private static MachineHistoryRollup Rollup(DateTimeOffset start, double wh) => new(
        start, start.AddHours(1), 0, null, null, null, null, null,
        new(0, 0, 0, 0, 0), new(0, 0), ObservedEnergyWattHours: new(1, wh));

    private static ElectricityRateSnapshot Rate(DateOnly month, decimal value) => new(1,
        "Meralco", "PHP", value, month, DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddMonths(1), "official",
        MachinePowerEstimateConfidence.HighEstimate,
        MachinePowerEstimateConfidence.HighEstimate);
}
