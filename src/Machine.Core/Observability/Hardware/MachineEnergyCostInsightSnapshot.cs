namespace Machine.Core;

public sealed record MachineEnergyCostInsightSnapshot(
    DateTimeOffset CapturedAt,
    double? EstimatedWallPowerWatts,
    double? EstimatedWallPowerLowerWatts,
    double? EstimatedWallPowerUpperWatts,
    MachinePowerEstimateConfidence PowerEstimateConfidence,
    double? SessionObservedEnergyKilowattHours,
    double? TodayObservedEnergyKilowattHours,
    double? ThirtyDayObservedEnergyKilowattHours,
    decimal? SessionEstimatedCost,
    decimal? TodayEstimatedCost,
    decimal? ThirtyDayEstimatedCost,
    MachineCostCoverage ThirtyDayCostCoverage,
    string? ElectricityProvider,
    string? CurrencyCode,
    decimal? RatePerKilowattHour,
    DateOnly? RateEffectiveMonth,
    MachinePowerEstimateConfidence RateConfidence);
