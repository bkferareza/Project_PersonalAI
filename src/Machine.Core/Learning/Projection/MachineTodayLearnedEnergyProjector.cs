namespace Machine.Core;

public enum MachineTodayLearnedEnergyComparisonState
{
    Unavailable,
    StillLearning,
    WithinLearnedRange,
    AboveLearnedRange,
    BelowLearnedRange
}

public sealed record MachineTodayLearnedEnergyComparison(
    DateOnly LocalDate,
    double ActualObservedEnergyKilowattHours,
    TimeSpan ObservedDuration,
    TimeSpan LearnedCoveredDuration,
    double LearnedCoverage,
    double? ExpectedObservedEnergyKilowattHours,
    double? ExpectedLowerEnergyKilowattHours,
    double? ExpectedUpperEnergyKilowattHours,
    MachineTodayLearnedEnergyComparisonState ComparisonState,
    MachineLearningEvidenceMaturity ComparisonMaturity,
    double? DifferenceKilowattHours,
    double? DifferencePercent,
    decimal? ActualEstimatedCost,
    decimal? ExpectedEstimatedCost,
    decimal? ExpectedLowerCost,
    decimal? ExpectedUpperCost,
    ElectricityRateSnapshot? Rate)
{
    public bool HasCompleteLearnedCoverage =>
        ObservedDuration > TimeSpan.Zero &&
        ObservedDuration.Ticks - LearnedCoveredDuration.Ticks <=
            MachineTodayLearnedEnergyProjector.CoverageCompletionToleranceTicks;

    public bool CostAvailable =>
        Rate is not null && ActualEstimatedCost is not null;
}

public static class MachineTodayLearnedEnergyProjector
{
    public const long CoverageCompletionToleranceTicks = 1;
    public const double MinimumMeaningfulExpectedEnergyKilowattHours = 1e-9d;

    public static MachineTodayLearnedEnergyComparison Project(
        IEnumerable<MachineHistoryRollup> hourlyRollups,
        IEnumerable<MachineLearningContextProfile> profiles,
        MachineTodayEnergyCostProjection acceptedToday,
        DateTimeOffset now,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(hourlyRollups);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(acceptedToday);

        var zone = timeZone ?? TimeZoneInfo.Local;
        var utcNow = now.ToUniversalTime();
        var powerByContext = profiles
            .Where(profile => MachineLearnedPowerCostProjector.
                TryGetUsablePower(
                    profile.EstimatedWallPower,
                    out _,
                    out _))
            .GroupBy(profile => profile.ContextKey)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(profile =>
                    profile.LastReinforcedAt).First());

        long observedTicks = 0;
        long coveredTicks = 0;
        double expectedWattHours = 0d;
        double expectedLowerWattHours = 0d;
        double expectedUpperWattHours = 0d;
        var hasEligibleContext = false;
        var allEligibleContextsEstablished = true;

        foreach (var rollup in hourlyRollups.Where(rollup =>
            rollup.BucketStart <= utcNow &&
            DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(rollup.BucketStart, zone).Date) ==
                    acceptedToday.LocalDate))
        {
            var boundedObservedTicks = Math.Max(
                0,
                rollup.ObservedDurationTicks);
            observedTicks = SaturatingAdd(observedTicks,
                boundedObservedTicks);
            var remainingTicks = boundedObservedTicks;
            var activeTicks = Math.Min(
                Math.Max(0, rollup.ActivityDurations.ActiveTicks),
                remainingTicks);
            remainingTicks -= activeTicks;
            var idleTicks = Math.Min(
                Math.Max(0, rollup.ActivityDurations.IdleTicks),
                remainingTicks);
            var localHour = TimeZoneInfo.ConvertTime(
                rollup.BucketStart,
                zone).Hour;

            AddContextExpectation(
                localHour,
                MachineUserActivityState.Active,
                activeTicks,
                powerByContext,
                ref coveredTicks,
                ref expectedWattHours,
                ref expectedLowerWattHours,
                ref expectedUpperWattHours,
                ref hasEligibleContext,
                ref allEligibleContextsEstablished);
            AddContextExpectation(
                localHour,
                MachineUserActivityState.Idle,
                idleTicks,
                powerByContext,
                ref coveredTicks,
                ref expectedWattHours,
                ref expectedLowerWattHours,
                ref expectedUpperWattHours,
                ref hasEligibleContext,
                ref allEligibleContextsEstablished);
        }

        coveredTicks = Math.Min(coveredTicks, observedTicks);
        var coverage = observedTicks <= 0
            ? 0d
            : Math.Clamp(coveredTicks / (double)observedTicks, 0d, 1d);
        var completeCoverage = observedTicks > 0 &&
            observedTicks - coveredTicks <=
                CoverageCompletionToleranceTicks;
        var comparisonMaturity = !hasEligibleContext
            ? MachineLearningEvidenceMaturity.Insufficient
            : allEligibleContextsEstablished
                ? MachineLearningEvidenceMaturity.Established
                : MachineLearningEvidenceMaturity.Provisional;
        var hasActualEnergy = acceptedToday.HasObservedEnergy;
        var canCompare = completeCoverage && hasEligibleContext &&
            hasActualEnergy &&
            expectedWattHours / 1000d >
                MinimumMeaningfulExpectedEnergyKilowattHours;
        double? expectedKwh = canCompare
            ? expectedWattHours / 1000d
            : null;
        double? expectedLowerKwh = canCompare
            ? expectedLowerWattHours / 1000d
            : null;
        double? expectedUpperKwh = canCompare
            ? expectedUpperWattHours / 1000d
            : null;
        var actualKwh = Math.Max(
            0d,
            acceptedToday.ObservedEnergyWattHours / 1000d);
        var state = SelectState(
            observedTicks,
            hasActualEnergy,
            canCompare,
            actualKwh,
            expectedLowerKwh,
            expectedUpperKwh);
        double? difference = canCompare && expectedKwh is { } expected
            ? actualKwh - expected
            : null;
        double? differencePercent = difference is { } delta &&
                expectedKwh is { } denominator &&
                denominator > MinimumMeaningfulExpectedEnergyKilowattHours
            ? delta / denominator * 100d
            : null;
        var rate = acceptedToday.Rate;

        return new(
            acceptedToday.LocalDate,
            actualKwh,
            TimeSpan.FromTicks(observedTicks),
            TimeSpan.FromTicks(coveredTicks),
            coverage,
            expectedKwh,
            expectedLowerKwh,
            expectedUpperKwh,
            state,
            comparisonMaturity,
            difference,
            differencePercent,
            acceptedToday.EstimatedCost,
            CalculateCost(expectedKwh, rate),
            CalculateCost(expectedLowerKwh, rate),
            CalculateCost(expectedUpperKwh, rate),
            rate);
    }

    private static void AddContextExpectation(
        int localHour,
        MachineUserActivityState activityState,
        long durationTicks,
        IReadOnlyDictionary<MachineLearningContextKey,
            MachineLearningContextProfile> powerByContext,
        ref long coveredTicks,
        ref double expectedWattHours,
        ref double expectedLowerWattHours,
        ref double expectedUpperWattHours,
        ref bool hasEligibleContext,
        ref bool allEligibleContextsEstablished)
    {
        if (durationTicks <= 0 ||
            !powerByContext.TryGetValue(
                new(localHour, activityState),
                out var profile) ||
            !MachineLearnedPowerCostProjector.TryGetUsablePower(
                profile.EstimatedWallPower,
                out var typicalWatts,
                out var range))
        {
            return;
        }

        coveredTicks = SaturatingAdd(coveredTicks, durationTicks);
        var durationHours = durationTicks /
            (double)TimeSpan.TicksPerHour;
        expectedWattHours += typicalWatts * durationHours;
        expectedLowerWattHours += range.Low * durationHours;
        expectedUpperWattHours += range.High * durationHours;
        hasEligibleContext = true;
        if (profile.EstimatedWallPower!.Maturity !=
            MachineLearningEvidenceMaturity.Established)
        {
            allEligibleContextsEstablished = false;
        }
    }

    private static MachineTodayLearnedEnergyComparisonState SelectState(
        long observedTicks,
        bool hasActualEnergy,
        bool canCompare,
        double actualKwh,
        double? expectedLowerKwh,
        double? expectedUpperKwh)
    {
        if (observedTicks <= 0 || !hasActualEnergy)
        {
            return MachineTodayLearnedEnergyComparisonState.Unavailable;
        }
        if (!canCompare || expectedLowerKwh is null ||
            expectedUpperKwh is null)
        {
            return MachineTodayLearnedEnergyComparisonState.StillLearning;
        }
        if (actualKwh > expectedUpperKwh.Value)
        {
            return MachineTodayLearnedEnergyComparisonState.AboveLearnedRange;
        }
        if (actualKwh < expectedLowerKwh.Value)
        {
            return MachineTodayLearnedEnergyComparisonState.BelowLearnedRange;
        }
        return MachineTodayLearnedEnergyComparisonState.WithinLearnedRange;
    }

    private static decimal? CalculateCost(
        double? kilowattHours,
        ElectricityRateSnapshot? rate) => kilowattHours is { } value
            ? MachineElectricityCostCalculator.Calculate(
                value * 1000d,
                rate)
            : null;

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right
            ? long.MaxValue
            : left + right;
}
