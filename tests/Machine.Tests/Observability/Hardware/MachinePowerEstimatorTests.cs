using Machine.Core;

namespace Machine.Tests;

public sealed class MachinePowerEstimatorTests
{
    [Theory]
    [InlineData(0d)]
    [InlineData(50d)]
    [InlineData(100d)]
    public void RyzenEstimateIsBoundedAndNeverUsesTdpAsCurrentWatts(double load)
    {
        var estimate = MachineCpuPowerEstimator.Estimate("AMD Ryzen 7 3800XT",
            load);

        Assert.NotNull(estimate.Center);
        Assert.True(estimate.Lower <= estimate.Center);
        Assert.True(estimate.Center <= estimate.Upper);
        Assert.NotEqual(105d, estimate.Center);
        Assert.Equal(MachinePowerEstimateConfidence.HighEstimate,
            estimate.Confidence);
    }

    [Fact]
    public void UnknownCpuRemainsUnavailableRatherThanInventingWatts()
    {
        var estimate = MachineCpuPowerEstimator.Estimate("Unknown CPU", 50d);
        Assert.Null(estimate.Center);
        Assert.Equal(MachinePowerEstimateConfidence.Unavailable,
            estimate.Confidence);
    }

    [Fact]
    public void WallEstimateUsesMeasuredGpuAndKeepsEfficiencyRangeVisible()
    {
        var cpu = new MachineCpuHardwareSnapshot(DateTimeOffset.UtcNow,
            "AMD Ryzen 7 3800XT", 8, 16, 40, null, null, null, 3900, 4700,
            null, null, null, 38, 28, 50,
            MachinePowerEstimateConfidence.HighEstimate,
            MachineHardwareTelemetryAvailability.Partial);
        var gpu = new MachineGpuAdapterTelemetry(0, "GPU", "NVIDIA", 20,
            null, null, null, null, 60, null, null, null);
        var estimate = MachinePowerEstimator.Estimate(DateTimeOffset.UtcNow,
            cpu, gpu, 32UL * 1024 * 1024 * 1024, 2);

        Assert.Equal(60d, estimate.MeasuredGpuBoardWatts);
        Assert.True(estimate.EstimatedWallLowerWatts <= estimate.EstimatedWallWatts);
        Assert.True(estimate.EstimatedWallWatts <= estimate.EstimatedWallUpperWatts);
    }

    [Fact]
    public void EnergyUsesMonotonicIntervalsAndDropsLargeGaps()
    {
        var energy = new MachineEnergyAccumulator(1_000,
            TimeSpan.FromHours(2));
        energy.Sample(100, 0, DateTimeOffset.UtcNow);
        Assert.Equal(100d, energy.Sample(100, 3_600_000,
            DateTimeOffset.UtcNow), 6);
        Assert.Equal(0d, energy.Sample(100, 14_400_000,
            DateTimeOffset.UtcNow), 6);
        Assert.Equal(100d, energy.GetSnapshot().SessionWattHours, 6);
    }

    [Fact]
    public void CostUsesDecimalAndRequiresRate()
    {
        var rate = new ElectricityRateSnapshot(1, "Reference", "PHP", 14.7833m,
            new DateOnly(2026, 8, 1), DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMonths(1), "official",
            MachinePowerEstimateConfidence.ModerateEstimate,
            MachinePowerEstimateConfidence.ModerateEstimate);
        Assert.Equal(14.78m, MachineElectricityCostCalculator.Calculate(1000,
            rate));
        Assert.Null(MachineElectricityCostCalculator.Calculate(1000, null));
    }
}
