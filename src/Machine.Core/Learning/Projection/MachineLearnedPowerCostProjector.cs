namespace Machine.Core;

public sealed record MachineLearnedPowerCostProjection(
    int LocalHour,
    MachineUserActivityState ActivityState,
    double? TypicalEstimatedWallPowerWatts,
    MachineLearningRange? TypicalEstimatedWallPowerRange,
    MachineLearningEvidenceMaturity PowerMaturity,
    long PowerEvidenceCount,
    int ObservedPowerEvidenceDays,
    double? TypicalEnergyKilowattHoursPerObservedHour,
    decimal? ProjectedCostPerObservedHour,
    decimal? ProjectedLowerCostPerObservedHour,
    decimal? ProjectedUpperCostPerObservedHour,
    ElectricityRateSnapshot? Rate)
{
    public bool HasUsablePower =>
        TypicalEstimatedWallPowerWatts is not null &&
        TypicalEstimatedWallPowerRange is not null &&
        PowerMaturity != MachineLearningEvidenceMaturity.Insufficient;

    public bool CostAvailable =>
        HasUsablePower &&
        Rate is not null &&
        ProjectedCostPerObservedHour is not null;
}

public static class MachineLearnedPowerCostProjector
{
    public static MachineLearnedPowerCostProjection? Project(
        MachineLearningBaseline? baseline,
        ElectricityRateSnapshot? applicableRate)
    {
        if (baseline is null)
        {
            return null;
        }

        var maturity = baseline.EstimatedWallPowerMaturity;
        var range = baseline.EstimatedWallPowerTypicalRange;
        var hasUsablePower = maturity !=
                MachineLearningEvidenceMaturity.Insufficient &&
            IsValidPower(baseline.AdaptiveEstimatedWallPowerMeanWatts) &&
            IsValidRange(range);
        var typicalWatts = hasUsablePower
            ? baseline.AdaptiveEstimatedWallPowerMeanWatts
            : null;
        var usableRange = hasUsablePower ? range : null;
        var rate = IsValidRate(applicableRate) ? applicableRate : null;

        return new(
            baseline.LocalHour,
            baseline.ActivityState,
            typicalWatts,
            usableRange,
            maturity,
            baseline.EstimatedWallPowerSampleCount,
            baseline.EstimatedWallPowerObservedDayCount,
            typicalWatts / 1000d,
            CalculateCost(typicalWatts, rate),
            CalculateCost(usableRange?.Low, rate),
            CalculateCost(usableRange?.High, rate),
            rate);
    }

    internal static bool TryGetUsablePower(
        MachineLearningEstimatedWallPowerProfile? power,
        out double typicalWatts,
        out MachineLearningRange range)
    {
        if (power is not null &&
            power.Maturity != MachineLearningEvidenceMaturity.Insufficient &&
            IsValidPower(power.AdaptiveMeanWatts) &&
            IsValidRange(power.TypicalRange))
        {
            typicalWatts = power.AdaptiveMeanWatts;
            range = power.TypicalRange!;
            return true;
        }

        typicalWatts = 0d;
        range = new(0d, 0d);
        return false;
    }

    private static decimal? CalculateCost(
        double? watts,
        ElectricityRateSnapshot? rate) => watts is { } value
            ? MachineElectricityCostCalculator.Calculate(value, rate)
            : null;

    private static bool IsValidPower(double? watts) =>
        watts is { } value && double.IsFinite(value) && value >= 0d;

    private static bool IsValidRange(MachineLearningRange? range) =>
        range is not null &&
        double.IsFinite(range.Low) &&
        double.IsFinite(range.High) &&
        range.Low >= 0d &&
        range.High >= range.Low;

    private static bool IsValidRate(ElectricityRateSnapshot? rate) =>
        rate is not null &&
        rate.RatePerKWh > 0m &&
        !string.IsNullOrWhiteSpace(rate.ProviderName) &&
        !string.IsNullOrWhiteSpace(rate.CurrencyCode);
}
