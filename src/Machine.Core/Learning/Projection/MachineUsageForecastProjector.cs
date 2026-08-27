namespace Machine.Core;

public enum MachineUsageForecastAvailabilityReason
{
    Available,
    PartialFutureCoverage,
    NoHistoricalActivityEvidence,
    MissingFuturePowerEvidence
}

public sealed record MachineUsageForecast(
    DateTimeOffset CapturedAt,
    MachineLearningContextKey? CurrentContext,
    MachineLearningConfidence CurrentContextMaturity,
    MachineLearningEvidenceMaturity CurrentPowerMaturity,
    MachineLearnedHourlyUsageProfile? CurrentHourUsage,
    double? TypicalPowerWatts,
    double? TypicalPowerLowerWatts,
    double? TypicalPowerUpperWatts,
    double? NextObservedHourEnergyKilowattHours,
    double? NextObservedHourEnergyLowerKilowattHours,
    double? NextObservedHourEnergyUpperKilowattHours,
    decimal? NextObservedHourEstimatedCost,
    decimal? NextObservedHourEstimatedCostLower,
    decimal? NextObservedHourEstimatedCostUpper,
    MachineTodayLearnedEnergyComparison Today,
    TimeSpan RemainingDayExpectedObservedDuration,
    double? RemainingDayExpectedEnergyKilowattHours,
    double? RemainingDayLowerKilowattHours,
    double? RemainingDayUpperKilowattHours,
    double? ProjectedEndOfDayObservedEnergyKilowattHours,
    double? ProjectedEndOfDayLowerKilowattHours,
    double? ProjectedEndOfDayUpperKilowattHours,
    decimal? ProjectedEndOfDayEstimatedCost,
    decimal? ProjectedEndOfDayCostLower,
    decimal? ProjectedEndOfDayCostUpper,
    MachineLearningEvidenceMaturity ForecastMaturity,
    double ForecastCoverage,
    MachineUsageForecastAvailabilityReason AvailabilityReason,
    ElectricityRateSnapshot? RateReference)
{
    public bool HasNextObservedHourForecast =>
        NextObservedHourEnergyKilowattHours is not null;

    public bool HasEndOfDayForecast =>
        ProjectedEndOfDayObservedEnergyKilowattHours is not null;
}

public static class MachineUsageForecastProjector
{
    public static MachineUsageForecast Project(
        DateTimeOffset capturedAt,
        MachineLearningBaseline? currentBaseline,
        IEnumerable<MachineLearningContextProfile> contextProfiles,
        MachineLearnedUsageSnapshot learnedUsage,
        MachineLearnedPowerCostProjection? currentPower,
        MachineTodayLearnedEnergyComparison today,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(contextProfiles);
        ArgumentNullException.ThrowIfNull(learnedUsage);
        ArgumentNullException.ThrowIfNull(today);

        var zone = timeZone ?? TimeZoneInfo.Local;
        var localNow = TimeZoneInfo.ConvertTime(capturedAt, zone);
        MachineLearningContextKey? currentContext = currentBaseline is null
            ? null
            : new MachineLearningContextKey(
                currentBaseline.LocalHour,
                currentBaseline.ActivityState);
        var currentContextMaturity = currentBaseline?.Confidence ??
            MachineLearningConfidence.Calibrating;
        var currentPowerMaturity = currentPower?.PowerMaturity ??
            MachineLearningEvidenceMaturity.Insufficient;
        var currentHourUsage = learnedUsage.HourlyProfiles.FirstOrDefault(
            profile => profile.LocalHour == localNow.Hour);
        var rate = SelectRate(currentPower?.Rate, today.Rate);
        var typicalPower = currentPower?.HasUsablePower == true
            ? currentPower.TypicalEstimatedWallPowerWatts
            : null;
        var typicalRange = currentPower?.HasUsablePower == true
            ? currentPower.TypicalEstimatedWallPowerRange
            : null;
        var nextEnergy = typicalPower / 1000d;
        var nextLowerEnergy = typicalRange?.Low / 1000d;
        var nextUpperEnergy = typicalRange?.High / 1000d;

        var future = ProjectRemainingDay(
            capturedAt,
            zone,
            contextProfiles,
            learnedUsage);
        double? remainingEnergy = future.HasProjection
            ? future.ExpectedWattHours / 1000d
            : null;
        double? remainingLower = future.HasProjection
            ? future.LowerWattHours / 1000d
            : null;
        double? remainingUpper = future.HasProjection
            ? future.UpperWattHours / 1000d
            : null;
        double? endOfDay = remainingEnergy is { } expected
            ? Math.Max(0d, today.ActualObservedEnergyKilowattHours) +
                expected
            : null;
        double? endOfDayLower = remainingLower is { } lower
            ? Math.Max(0d, today.ActualObservedEnergyKilowattHours) + lower
            : null;
        double? endOfDayUpper = remainingUpper is { } upper
            ? Math.Max(0d, today.ActualObservedEnergyKilowattHours) + upper
            : null;

        return new(
            capturedAt,
            currentContext,
            currentContextMaturity,
            currentPowerMaturity,
            currentHourUsage,
            typicalPower,
            typicalRange?.Low,
            typicalRange?.High,
            nextEnergy,
            nextLowerEnergy,
            nextUpperEnergy,
            CalculateCost(nextEnergy, rate),
            CalculateCost(nextLowerEnergy, rate),
            CalculateCost(nextUpperEnergy, rate),
            today,
            TimeSpan.FromTicks(future.ExpectedObservedTicks),
            remainingEnergy,
            remainingLower,
            remainingUpper,
            endOfDay,
            endOfDayLower,
            endOfDayUpper,
            CalculateCost(endOfDay, rate),
            CalculateCost(endOfDayLower, rate),
            CalculateCost(endOfDayUpper, rate),
            future.Maturity,
            future.Coverage,
            future.AvailabilityReason,
            rate);
    }

    private static RemainingDayProjection ProjectRemainingDay(
        DateTimeOffset capturedAt,
        TimeZoneInfo zone,
        IEnumerable<MachineLearningContextProfile> contextProfiles,
        MachineLearnedUsageSnapshot learnedUsage)
    {
        var localNow = TimeZoneInfo.ConvertTime(capturedAt, zone);
        var localMidnight = new DateTimeOffset(
            localNow.Year,
            localNow.Month,
            localNow.Day,
            0,
            0,
            0,
            localNow.Offset).AddDays(1);
        var totalRemainingTicks = Math.Max(
            0,
            (localMidnight - localNow).Ticks);
        var usageByHour = learnedUsage.HourlyProfiles
            .GroupBy(profile => profile.LocalHour)
            .ToDictionary(group => group.Key, group => group.First());
        var powerByContext = contextProfiles
            .Where(profile => profile.LocalHour is >= 0 and <= 23)
            .GroupBy(profile => profile.ContextKey)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(profile =>
                    profile.LastReinforcedAt).First());

        long coveredClockTicks = 0;
        long expectedObservedTicks = 0;
        double expectedWattHours = 0d;
        double lowerWattHours = 0d;
        double upperWattHours = 0d;
        var hasUsableActivity = false;
        var missingPower = false;
        var allEstablished = true;
        var cursor = localNow;
        while (cursor < localMidnight)
        {
            var nextHour = new DateTimeOffset(
                cursor.Year,
                cursor.Month,
                cursor.Day,
                cursor.Hour,
                0,
                0,
                cursor.Offset).AddHours(1);
            var segmentEnd = nextHour < localMidnight
                ? nextHour
                : localMidnight;
            var segmentTicks = Math.Max(0, (segmentEnd - cursor).Ticks);
            if (segmentTicks <= 0)
            {
                break;
            }

            if (!usageByHour.TryGetValue(cursor.Hour, out var usage) ||
                !usage.HasUsableEvidence)
            {
                cursor = segmentEnd;
                continue;
            }

            hasUsableActivity = true;
            var segmentFraction = Math.Clamp(
                segmentTicks / (double)TimeSpan.TicksPerHour,
                0d,
                1d);
            var activeTicks = ScaleTicks(
                usage.TypicalActiveDuration.Ticks,
                segmentFraction);
            var idleTicks = ScaleTicks(
                usage.TypicalIdleDuration.Ticks,
                segmentFraction);
            var activePower = TryGetPower(
                powerByContext,
                cursor.Hour,
                MachineUserActivityState.Active,
                activeTicks);
            var idlePower = TryGetPower(
                powerByContext,
                cursor.Hour,
                MachineUserActivityState.Idle,
                idleTicks);
            if (!activePower.Available || !idlePower.Available)
            {
                missingPower = true;
                cursor = segmentEnd;
                continue;
            }

            coveredClockTicks = SaturatingAdd(
                coveredClockTicks,
                segmentTicks);
            expectedObservedTicks = SaturatingAdd(
                expectedObservedTicks,
                SaturatingAdd(activeTicks, idleTicks));
            AddPowerExpectation(
                activeTicks,
                activePower,
                ref expectedWattHours,
                ref lowerWattHours,
                ref upperWattHours);
            AddPowerExpectation(
                idleTicks,
                idlePower,
                ref expectedWattHours,
                ref lowerWattHours,
                ref upperWattHours);
            if (usage.Maturity !=
                    MachineLearningEvidenceMaturity.Established ||
                activePower.Maturity !=
                    MachineLearningEvidenceMaturity.Established ||
                idlePower.Maturity !=
                    MachineLearningEvidenceMaturity.Established)
            {
                allEstablished = false;
            }
            cursor = segmentEnd;
        }

        var coverage = totalRemainingTicks > 0
            ? Math.Clamp(
                coveredClockTicks / (double)totalRemainingTicks,
                0d,
                1d)
            : 0d;
        var hasProjection = coveredClockTicks > 0;
        var maturity = !hasProjection
            ? MachineLearningEvidenceMaturity.Insufficient
            : allEstablished
                ? MachineLearningEvidenceMaturity.Established
                : MachineLearningEvidenceMaturity.Provisional;
        var reason = !hasProjection
            ? hasUsableActivity || missingPower
                ? MachineUsageForecastAvailabilityReason
                    .MissingFuturePowerEvidence
                : MachineUsageForecastAvailabilityReason
                    .NoHistoricalActivityEvidence
            : coverage >= 1d
                ? MachineUsageForecastAvailabilityReason.Available
                : MachineUsageForecastAvailabilityReason
                    .PartialFutureCoverage;

        return new(
            hasProjection,
            expectedObservedTicks,
            expectedWattHours,
            lowerWattHours,
            upperWattHours,
            maturity,
            coverage,
            reason);
    }

    private static ContextPower TryGetPower(
        IReadOnlyDictionary<MachineLearningContextKey,
            MachineLearningContextProfile> powerByContext,
        int localHour,
        MachineUserActivityState activityState,
        long expectedDurationTicks)
    {
        if (expectedDurationTicks <= 0)
        {
            return ContextPower.NotRequired;
        }

        if (powerByContext.TryGetValue(
                new(localHour, activityState),
                out var profile) &&
            profile.EstimatedWallPower is { } power &&
            power.Freshness != MachineLearningFreshness.Stale &&
            MachineLearnedPowerCostProjector.TryGetUsablePower(
                power,
                out var typical,
                out var range))
        {
            return new(true, typical, range.Low, range.High,
                power.Maturity);
        }

        return ContextPower.Missing;
    }

    private static void AddPowerExpectation(
        long durationTicks,
        ContextPower power,
        ref double expectedWattHours,
        ref double lowerWattHours,
        ref double upperWattHours)
    {
        if (durationTicks <= 0)
        {
            return;
        }

        var hours = durationTicks / (double)TimeSpan.TicksPerHour;
        expectedWattHours += power.TypicalWatts * hours;
        lowerWattHours += power.LowerWatts * hours;
        upperWattHours += power.UpperWatts * hours;
    }

    private static ElectricityRateSnapshot? SelectRate(
        ElectricityRateSnapshot? primary,
        ElectricityRateSnapshot? fallback)
    {
        var candidate = primary ?? fallback;
        return candidate is not null &&
            candidate.RatePerKWh > 0m &&
            !string.IsNullOrWhiteSpace(candidate.ProviderName) &&
            !string.IsNullOrWhiteSpace(candidate.CurrencyCode)
                ? candidate
                : null;
    }

    private static decimal? CalculateCost(
        double? kilowattHours,
        ElectricityRateSnapshot? rate) => kilowattHours is { } value
            ? MachineElectricityCostCalculator.Calculate(
                value * 1000d,
                rate)
            : null;

    private static long ScaleTicks(long ticks, double fraction) =>
        ticks <= 0 || fraction <= 0d
            ? 0
            : (long)Math.Clamp(
                Math.Round(ticks * fraction,
                    MidpointRounding.ToEven),
                0d,
                long.MaxValue);

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right
            ? long.MaxValue
            : left + right;

    private sealed record RemainingDayProjection(
        bool HasProjection,
        long ExpectedObservedTicks,
        double ExpectedWattHours,
        double LowerWattHours,
        double UpperWattHours,
        MachineLearningEvidenceMaturity Maturity,
        double Coverage,
        MachineUsageForecastAvailabilityReason AvailabilityReason);

    private sealed record ContextPower(
        bool Available,
        double TypicalWatts,
        double LowerWatts,
        double UpperWatts,
        MachineLearningEvidenceMaturity Maturity)
    {
        public static ContextPower Missing { get; } = new(
            false,
            0d,
            0d,
            0d,
            MachineLearningEvidenceMaturity.Insufficient);

        public static ContextPower NotRequired { get; } = new(
            true,
            0d,
            0d,
            0d,
            MachineLearningEvidenceMaturity.Established);
    }
}
