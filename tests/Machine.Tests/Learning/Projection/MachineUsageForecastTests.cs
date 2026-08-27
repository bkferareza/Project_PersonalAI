using Machine.Core;

namespace Machine.Tests;

public sealed class MachineUsageForecastTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 8, 28, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UsageProjectionAggregatesActiveAndIdleDurationByLocalHour()
    {
        var result = MachineLearnedUsageProjector.Project(
            [
                Rollup(AtDayOffset(-2, 10),
                    TimeSpan.FromMinutes(45),
                    TimeSpan.FromMinutes(15)),
                Rollup(AtDayOffset(-1, 10),
                    TimeSpan.FromMinutes(30),
                    TimeSpan.FromMinutes(30))
            ],
            CapturedAt,
            TimeZoneInfo.Utc);

        var profile = Assert.Single(result.HourlyProfiles);
        Assert.Equal(10, profile.LocalHour);
        Assert.Equal(0.625d, profile.ActiveFraction, 8);
        Assert.Equal(0.375d, profile.IdleFraction, 8);
        Assert.Equal(TimeSpan.FromMinutes(37.5),
            profile.TypicalActiveDuration);
        Assert.Equal(TimeSpan.FromMinutes(22.5),
            profile.TypicalIdleDuration);
        Assert.Equal(TimeSpan.FromHours(1),
            profile.TypicalObservedDuration);
        Assert.Equal(2, profile.ObservedDayCount);
        Assert.Equal(MachineLearningEvidenceMaturity.Provisional,
            profile.Maturity);
    }

    [Fact]
    public void MissingDayReducesObservedDurationButNeverBecomesIdle()
    {
        var result = MachineLearnedUsageProjector.Project(
            [Rollup(AtDayOffset(-2, 10),
                TimeSpan.FromMinutes(30),
                TimeSpan.Zero)],
            CapturedAt,
            TimeZoneInfo.Utc);

        var profile = Assert.Single(result.HourlyProfiles);
        Assert.Equal(2, result.HistoricalDayCount);
        Assert.Equal(1d, profile.ActiveFraction, 8);
        Assert.Equal(0d, profile.IdleFraction, 8);
        Assert.Equal(TimeSpan.FromMinutes(15),
            profile.TypicalObservedDuration);
        Assert.Equal(TimeSpan.Zero, profile.TypicalIdleDuration);
        Assert.Equal(MachineLearningEvidenceMaturity.Insufficient,
            profile.Maturity);
    }

    [Fact]
    public void MultipleSessionsOnOneDayRemainOneObservedDay()
    {
        var day = AtDayOffset(-1, 0);
        var result = MachineLearnedUsageProjector.Project(
            [
                Rollup(day.AddHours(10),
                    TimeSpan.FromMinutes(15),
                    TimeSpan.Zero),
                Rollup(day.AddHours(10).AddMinutes(30),
                    TimeSpan.Zero,
                    TimeSpan.FromMinutes(15))
            ],
            CapturedAt,
            TimeZoneInfo.Utc);

        var profile = Assert.Single(result.HourlyProfiles);
        Assert.Equal(1, profile.ObservedDayCount);
        Assert.Equal(0.5d, profile.ActiveFraction, 8);
        Assert.Equal(0.5d, profile.IdleFraction, 8);
        Assert.Equal(TimeSpan.FromMinutes(30),
            profile.TypicalObservedDuration);
    }

    [Fact]
    public void UsageProjectionKeepsMidnightHoursDistinct()
    {
        var result = MachineLearnedUsageProjector.Project(
            [
                Rollup(AtDayOffset(-2, 23),
                    TimeSpan.FromMinutes(30),
                    TimeSpan.Zero),
                Rollup(AtDayOffset(-1, 0),
                    TimeSpan.Zero,
                    TimeSpan.FromMinutes(30))
            ],
            CapturedAt,
            TimeZoneInfo.Utc);

        Assert.Equal([0, 23],
            result.HourlyProfiles.Select(profile => profile.LocalHour));
        Assert.Equal(1d,
            result.HourlyProfiles.Single(profile =>
                profile.LocalHour == 23).ActiveFraction);
        Assert.Equal(1d,
            result.HourlyProfiles.Single(profile =>
                profile.LocalHour == 0).IdleFraction);
    }

    [Fact]
    public void UsageMaturityUsesItsOwnRepeatedDayEvidence()
    {
        var rollups = Enumerable.Range(1, 7)
            .Select(daysAgo => Rollup(
                AtDayOffset(-daysAgo, 10),
                TimeSpan.FromMinutes(30),
                TimeSpan.FromMinutes(15)))
            .ToArray();

        var profile = Assert.Single(
            MachineLearnedUsageProjector.Project(
                rollups,
                CapturedAt,
                TimeZoneInfo.Utc).HourlyProfiles);

        Assert.Equal(7, profile.ObservedDayCount);
        Assert.Equal(MachineLearningEvidenceMaturity.Established,
            profile.Maturity);
    }

    [Fact]
    public async Task UsageProjectionIsStableAcrossHistoryRestart()
    {
        var service = new MachineHistoryService();
        ObserveHistoryPair(service,
            AtDayOffset(-2, 10),
            MachineUserActivityState.Active);
        ObserveHistoryPair(service,
            AtDayOffset(-1, 10),
            MachineUserActivityState.Idle);
        var before = MachineLearnedUsageProjector.Project(
            service.GetSnapshot(
                MachineHistoryRange.Last30Days,
                CapturedAt).Rollups,
            CapturedAt,
            TimeZoneInfo.Utc);
        var store = new MemoryHistoryStore();
        await service.SaveFinalSnapshotAsync(store, CapturedAt);

        var restored = new MachineHistoryService();
        await restored.LoadAsync(store);
        var after = MachineLearnedUsageProjector.Project(
            restored.GetSnapshot(
                MachineHistoryRange.Last30Days,
                CapturedAt).Rollups,
            CapturedAt,
            TimeZoneInfo.Utc);

        Assert.Equal(before.CapturedAt, after.CapturedAt);
        Assert.Equal(before.HistoricalStartDate,
            after.HistoricalStartDate);
        Assert.Equal(before.HistoricalEndDate,
            after.HistoricalEndDate);
        Assert.Equal(before.HistoricalDayCount,
            after.HistoricalDayCount);
        Assert.Equal(before.HourlyProfiles, after.HourlyProfiles);
    }

    [Fact]
    public void NextObservedHourUsesOnlyCurrentDeterministicPower()
    {
        var rate = Rate(10m);
        var baseline = Baseline(22, 150d, 10d);
        var forecast = ProjectForecast(
            baseline,
            [],
            Usage(),
            rate);

        Assert.Equal(0.150d,
            forecast.NextObservedHourEnergyKilowattHours);
        Assert.Equal(0.130d,
            forecast.NextObservedHourEnergyLowerKilowattHours);
        Assert.Equal(0.170d,
            forecast.NextObservedHourEnergyUpperKilowattHours);
        Assert.Equal(1.50m,
            forecast.NextObservedHourEstimatedCost);
        Assert.Equal(1.30m,
            forecast.NextObservedHourEstimatedCostLower);
        Assert.Equal(1.70m,
            forecast.NextObservedHourEstimatedCostUpper);
        Assert.Equal(MachineLearningEvidenceMaturity.Provisional,
            forecast.CurrentPowerMaturity);
    }

    [Fact]
    public void RateChangeChangesForecastMoneyButNotEnergy()
    {
        var baseline = Baseline(22, 150d, 10d);
        var atTen = ProjectForecast(
            baseline,
            [],
            Usage(),
            Rate(10m));
        var atTwenty = ProjectForecast(
            baseline,
            [],
            Usage(),
            Rate(20m));

        Assert.Equal(atTen.NextObservedHourEnergyKilowattHours,
            atTwenty.NextObservedHourEnergyKilowattHours);
        Assert.Equal(1.50m, atTen.NextObservedHourEstimatedCost);
        Assert.Equal(3.00m, atTwenty.NextObservedHourEstimatedCost);
        Assert.Equal(atTen.CurrentPowerMaturity,
            atTwenty.CurrentPowerMaturity);
    }

    [Fact]
    public void MissingRateKeepsForecastEnergyAndRemovesOnlyMoney()
    {
        var baseline = Baseline(22, 150d, 10d);
        var forecast = ProjectForecast(
            baseline,
            [],
            Usage(),
            null);

        Assert.Equal(0.150d,
            forecast.NextObservedHourEnergyKilowattHours);
        Assert.Null(forecast.NextObservedHourEstimatedCost);
        Assert.Null(forecast.RateReference);
    }

    [Fact]
    public void FutureForecastWeightsLearnedActiveAndIdleDurations()
    {
        var usage = Usage(
            UsageProfile(22, 30, 30,
                MachineLearningEvidenceMaturity.Established),
            UsageProfile(23, 30, 30,
                MachineLearningEvidenceMaturity.Established));
        var profiles = new[]
        {
            PowerProfile(22, MachineUserActivityState.Active, 200),
            PowerProfile(22, MachineUserActivityState.Idle, 100),
            PowerProfile(23, MachineUserActivityState.Active, 200),
            PowerProfile(23, MachineUserActivityState.Idle, 100)
        };

        var forecast = ProjectForecast(
            Baseline(22, 200d, 0d, established: true),
            profiles,
            usage,
            Rate(10m),
            actualTodayKwh: 0.1d);

        Assert.Equal(TimeSpan.FromHours(2),
            forecast.RemainingDayExpectedObservedDuration);
        Assert.Equal(0.3d,
            forecast.RemainingDayExpectedEnergyKilowattHours!.Value, 8);
        Assert.Equal(0.4d,
            forecast.ProjectedEndOfDayObservedEnergyKilowattHours!.Value, 8);
        Assert.Equal(4.00m,
            forecast.ProjectedEndOfDayEstimatedCost);
        Assert.Equal(1d, forecast.ForecastCoverage, 8);
        Assert.Equal(MachineLearningEvidenceMaturity.Established,
            forecast.ForecastMaturity);
        Assert.Equal(MachineUsageForecastAvailabilityReason.Available,
            forecast.AvailabilityReason);
    }

    [Fact]
    public void MissingFutureHourProducesExplicitPartialCoverage()
    {
        var usage = Usage(UsageProfile(22, 60, 0));
        var forecast = ProjectForecast(
            Baseline(22, 100d, 0d),
            [PowerProfile(22, MachineUserActivityState.Active, 100,
                established: false)],
            usage,
            Rate(10m));

        Assert.Equal(0.5d, forecast.ForecastCoverage, 8);
        Assert.Equal(
            MachineUsageForecastAvailabilityReason.PartialFutureCoverage,
            forecast.AvailabilityReason);
        Assert.NotNull(forecast.ProjectedEndOfDayObservedEnergyKilowattHours);
    }

    [Fact]
    public void MissingMatchingPowerMakesEndOfDayUnavailable()
    {
        var usage = Usage(
            UsageProfile(22, 30, 30),
            UsageProfile(23, 30, 30));
        var forecast = ProjectForecast(
            Baseline(22, 100d, 0d),
            [
                PowerProfile(22, MachineUserActivityState.Active, 100,
                    established: false),
                PowerProfile(23, MachineUserActivityState.Active, 100,
                    established: false)
            ],
            usage,
            Rate(10m));

        Assert.False(forecast.HasEndOfDayForecast);
        Assert.Equal(0d, forecast.ForecastCoverage);
        Assert.Equal(
            MachineUsageForecastAvailabilityReason.MissingFuturePowerEvidence,
            forecast.AvailabilityReason);
    }

    [Fact]
    public void MissingAndPoweredOffTimeIsNotProjectedAsIdleOrFullHours()
    {
        var usage = Usage(
            UsageProfile(22, 15, 0),
            UsageProfile(23, 15, 0));
        var profiles = new[]
        {
            PowerProfile(22, MachineUserActivityState.Active, 100,
                established: false),
            PowerProfile(23, MachineUserActivityState.Active, 100,
                established: false)
        };

        var forecast = ProjectForecast(
            Baseline(22, 100d, 0d),
            profiles,
            usage,
            Rate(10m));

        Assert.Equal(TimeSpan.FromMinutes(30),
            forecast.RemainingDayExpectedObservedDuration);
        Assert.Equal(0.05d,
            forecast.RemainingDayExpectedEnergyKilowattHours!.Value, 8);
    }

    [Fact]
    public void ForecastStopsAtLocalMidnight()
    {
        var atEleven = CapturedAt.AddHours(1);
        var forecast = ProjectForecast(
            Baseline(23, 100d, 0d),
            [PowerProfile(23, MachineUserActivityState.Active, 100,
                established: false)],
            Usage(UsageProfile(23, 60, 0)),
            Rate(10m),
            capturedAt: atEleven);

        Assert.Equal(TimeSpan.FromHours(1),
            forecast.RemainingDayExpectedObservedDuration);
        Assert.Equal(0.1d,
            forecast.RemainingDayExpectedEnergyKilowattHours!.Value, 8);
        Assert.Equal(1d, forecast.ForecastCoverage, 8);
    }

    [Fact]
    public void ZeroPowerIsFiniteAndProjectionIsRepeatable()
    {
        var baseline = Baseline(22, 0d, 0d);
        var profiles = new[]
        {
            PowerProfile(22, MachineUserActivityState.Active, 0,
                established: false),
            PowerProfile(23, MachineUserActivityState.Active, 0,
                established: false)
        };
        var usage = Usage(
            UsageProfile(22, 60, 0),
            UsageProfile(23, 60, 0));

        var first = ProjectForecast(
            baseline,
            profiles,
            usage,
            Rate(10m));
        var second = ProjectForecast(
            baseline,
            profiles,
            usage,
            Rate(10m));

        Assert.Equal(first, second);
        Assert.Equal(0d,
            first.ProjectedEndOfDayObservedEnergyKilowattHours);
        Assert.Equal(0m, first.ProjectedEndOfDayEstimatedCost);
        Assert.True(double.IsFinite(first.ForecastCoverage));
    }

    private static MachineUsageForecast ProjectForecast(
        MachineLearningBaseline baseline,
        IReadOnlyList<MachineLearningContextProfile> profiles,
        MachineLearnedUsageSnapshot usage,
        ElectricityRateSnapshot? rate,
        double actualTodayKwh = 0d,
        DateTimeOffset? capturedAt = null)
    {
        var now = capturedAt ?? CapturedAt;
        var currentPower = MachineLearnedPowerCostProjector.Project(
            baseline,
            rate);
        return MachineUsageForecastProjector.Project(
            now,
            baseline,
            profiles,
            usage,
            currentPower,
            Today(now, actualTodayKwh, rate),
            TimeZoneInfo.Utc);
    }

    private static MachineLearnedUsageSnapshot Usage(
        params MachineLearnedHourlyUsageProfile[] profiles) => new(
            CapturedAt,
            new DateOnly(2026, 8, 21),
            new DateOnly(2026, 8, 27),
            7,
            profiles);

    private static MachineLearnedHourlyUsageProfile UsageProfile(
        int hour,
        int activeMinutes,
        int idleMinutes,
        MachineLearningEvidenceMaturity maturity =
            MachineLearningEvidenceMaturity.Provisional)
    {
        var total = activeMinutes + idleMinutes;
        return new(
            hour,
            total > 0 ? activeMinutes / (double)total : 0d,
            total > 0 ? idleMinutes / (double)total : 0d,
            TimeSpan.FromMinutes(activeMinutes),
            TimeSpan.FromMinutes(idleMinutes),
            TimeSpan.FromMinutes(total),
            7,
            maturity == MachineLearningEvidenceMaturity.Established ? 7 : 2,
            1d,
            maturity);
    }

    private static MachineLearningBaseline Baseline(
        int hour,
        double watts,
        double standardDeviation,
        bool established = false)
    {
        var samples = established ? 168 : 12;
        var days = established ? 7 : 2;
        return new(
            hour,
            MachineUserActivityState.Active,
            samples,
            20,
            2,
            50,
            2,
            CapturedAt.AddDays(-days),
            CapturedAt,
            days,
            established
                ? MachineLearningConfidence.Established
                : MachineLearningConfidence.Provisional,
            EstimatedWallPowerSampleCount: samples,
            EstimatedWallPowerMeanWatts: watts,
            EstimatedWallPowerStandardDeviationWatts: standardDeviation,
            EstimatedWallPowerObservedDayCount: days,
            EstimatedWallPowerFirstObservedAt: CapturedAt.AddDays(-days),
            EstimatedWallPowerLastObservedAt: CapturedAt,
            AdaptiveEstimatedWallPowerMeanWatts: watts,
            AdaptiveEstimatedWallPowerStandardDeviationWatts:
                standardDeviation,
            AdaptiveEstimatedWallPowerSampleCount: samples,
            AdaptiveEstimatedWallPowerLastUpdatedAt: CapturedAt,
            EstimatedWallPowerFreshness: MachineLearningFreshness.Fresh);
    }

    private static MachineLearningContextProfile PowerProfile(
        int hour,
        MachineUserActivityState activity,
        double watts,
        bool established = true)
    {
        var maturity = established
            ? MachineLearningEvidenceMaturity.Established
            : MachineLearningEvidenceMaturity.Provisional;
        var samples = established ? 168 : 12;
        var days = established ? 7 : 2;
        var power = new MachineLearningEstimatedWallPowerProfile(
            samples,
            days,
            watts,
            0d,
            watts,
            0d,
            new(watts, watts),
            maturity,
            MachineLearningFreshness.Fresh,
            CapturedAt.AddDays(-days),
            CapturedAt,
            CapturedAt);
        return new(
            hour,
            activity,
            established
                ? MachineLearningConfidence.Established
                : MachineLearningConfidence.Provisional,
            MachineLearningFreshness.Fresh,
            samples,
            TimeSpan.FromMinutes(samples * 0.5d).Ticks,
            days,
            CapturedAt.AddDays(-days),
            CapturedAt,
            new(20, 2, new(16, 24)),
            new(50, 2, new(46, 54)),
            MachineNetworkActivityClass.Quiet,
            samples,
            samples,
            CapturedAt.AddDays(-days),
            CapturedAt,
            CapturedAt,
            power);
    }

    private static MachineTodayLearnedEnergyComparison Today(
        DateTimeOffset now,
        double actualKwh,
        ElectricityRateSnapshot? rate) => new(
            DateOnly.FromDateTime(now.Date),
            actualKwh,
            TimeSpan.FromHours(1),
            TimeSpan.Zero,
            0d,
            null,
            null,
            null,
            MachineTodayLearnedEnergyComparisonState.StillLearning,
            MachineLearningEvidenceMaturity.Insufficient,
            null,
            null,
            MachineElectricityCostCalculator.Calculate(
                actualKwh * 1000d,
                rate),
            null,
            null,
            null,
            rate);

    private static ElectricityRateSnapshot Rate(decimal value) => new(
        1,
        "Meralco",
        "PHP",
        value,
        new DateOnly(2026, 8, 1),
        CapturedAt,
        CapturedAt.AddDays(30),
        "official-test",
        MachinePowerEstimateConfidence.HighEstimate,
        MachinePowerEstimateConfidence.HighEstimate);

    private static DateTimeOffset AtDayOffset(
        int dayOffset,
        int hour) => new(
            2026,
            8,
            28 + dayOffset,
            hour,
            0,
            0,
            TimeSpan.Zero);

    private static MachineHistoryRollup Rollup(
        DateTimeOffset start,
        TimeSpan active,
        TimeSpan idle,
        TimeSpan? unknown = null)
    {
        var observed = active + idle + (unknown ?? TimeSpan.Zero);
        return new(
            start,
            start.AddHours(1),
            observed.Ticks,
            null,
            null,
            null,
            null,
            null,
            new(0, 0, 0, 0, observed.Ticks),
            new(active.Ticks, idle.Ticks));
    }

    private static void ObserveHistoryPair(
        MachineHistoryService service,
        DateTimeOffset start,
        MachineUserActivityState activity)
    {
        service.Observe(new(
            start,
            null,
            null,
            null,
            null,
            activity,
            MachineOverallState.Stable));
        service.Observe(new(
            start.AddSeconds(30),
            null,
            null,
            null,
            null,
            activity,
            MachineOverallState.Stable));
    }

    private sealed class MemoryHistoryStore : IMachineHistoryStore
    {
        private MachineHistoryPersistedState? _state;

        public Task<MachineHistoryPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_state);

        public Task SaveAsync(
            MachineHistoryPersistedState state,
            CancellationToken cancellationToken = default)
        {
            _state = state;
            return Task.CompletedTask;
        }
    }
}
