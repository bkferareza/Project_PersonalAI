using System.Text.Json;
using Machine.Core;

namespace Machine.Tests;

public sealed class MachineLearningEnergyCostProjectionTests
{
    private static readonly DateTimeOffset Today =
        DateTimeOffset.Parse("2026-08-25T00:00:00Z");

    [Fact]
    public void CostPerObservedHourUsesAdaptiveTypicalPowerAndDecimalRate()
    {
        var projection = MachineLearnedPowerCostProjector.Project(
            Baseline(10, MachineUserActivityState.Active, 150d, 5d),
            Rate(14.7833m));

        Assert.NotNull(projection);
        Assert.Equal(150d,
            projection.TypicalEstimatedWallPowerWatts);
        Assert.Equal(0.150d,
            projection.TypicalEnergyKilowattHoursPerObservedHour);
        Assert.Equal(140d,
            projection.TypicalEstimatedWallPowerRange!.Low);
        Assert.Equal(160d,
            projection.TypicalEstimatedWallPowerRange.High);
        Assert.Equal(2.22m, projection.ProjectedCostPerObservedHour);
        Assert.Equal(2.07m,
            projection.ProjectedLowerCostPerObservedHour);
        Assert.Equal(2.37m,
            projection.ProjectedUpperCostPerObservedHour);
        Assert.Equal(MachineLearningEvidenceMaturity.Provisional,
            projection.PowerMaturity);
        Assert.True(projection.CostAvailable);
    }

    [Fact]
    public void RateChangeChangesOnlyDerivedCost()
    {
        var baseline = Baseline(
            10,
            MachineUserActivityState.Active,
            150d,
            5d);

        var rateA = MachineLearnedPowerCostProjector.Project(
            baseline,
            Rate(10m));
        var rateB = MachineLearnedPowerCostProjector.Project(
            baseline,
            Rate(20m));

        Assert.Equal(1.50m, rateA!.ProjectedCostPerObservedHour);
        Assert.Equal(3.00m, rateB!.ProjectedCostPerObservedHour);
        Assert.Equal(rateA.TypicalEstimatedWallPowerWatts,
            rateB.TypicalEstimatedWallPowerWatts);
        Assert.Equal(rateA.TypicalEstimatedWallPowerRange,
            rateB.TypicalEstimatedWallPowerRange);
        Assert.Equal(rateA.PowerEvidenceCount,
            rateB.PowerEvidenceCount);
        Assert.Equal(rateA.PowerMaturity, rateB.PowerMaturity);
        Assert.Equal(baseline,
            Baseline(10, MachineUserActivityState.Active, 150d, 5d));
    }

    [Fact]
    public void MissingRateKeepsPowerVisibleAndCostUnavailable()
    {
        var projection = MachineLearnedPowerCostProjector.Project(
            Baseline(10, MachineUserActivityState.Active, 150d, 5d),
            null);

        Assert.NotNull(projection);
        Assert.Equal(150d,
            projection.TypicalEstimatedWallPowerWatts);
        Assert.Null(projection.ProjectedCostPerObservedHour);
        Assert.Null(projection.ProjectedLowerCostPerObservedHour);
        Assert.Null(projection.ProjectedUpperCostPerObservedHour);
        Assert.False(projection.CostAvailable);
    }

    [Fact]
    public void InsufficientPowerDoesNotProjectTypicalCost()
    {
        var projection = MachineLearnedPowerCostProjector.Project(
            Baseline(
                10,
                MachineUserActivityState.Active,
                150d,
                5d,
                evidenceCount: 11),
            Rate(14.7833m));

        Assert.NotNull(projection);
        Assert.Equal(MachineLearningEvidenceMaturity.Insufficient,
            projection.PowerMaturity);
        Assert.Null(projection.TypicalEstimatedWallPowerWatts);
        Assert.Null(projection.TypicalEstimatedWallPowerRange);
        Assert.Null(projection.ProjectedCostPerObservedHour);
        Assert.False(projection.CostAvailable);
    }

    [Fact]
    public void TodayExpectationKeepsActiveAndIdleContextsSeparate()
    {
        var rollups = new[]
        {
            Rollup(
                Today.AddHours(10),
                TimeSpan.FromHours(1),
                TimeSpan.FromMinutes(30),
                190d)
        };
        var comparison = ProjectToday(
            rollups,
            [
                Profile(10, MachineUserActivityState.Active,
                    150d, 150d, 150d),
                Profile(10, MachineUserActivityState.Idle,
                    80d, 80d, 80d)
            ],
            now: Today.AddHours(12));

        Assert.Equal(TimeSpan.FromHours(1.5),
            comparison.ObservedDuration);
        Assert.Equal(comparison.ObservedDuration,
            comparison.LearnedCoveredDuration);
        Assert.Equal(1d, comparison.LearnedCoverage, 8);
        Assert.Equal(0.190d,
            comparison.ExpectedObservedEnergyKilowattHours!.Value, 8);
        Assert.Equal(MachineTodayLearnedEnergyComparisonState.
            WithinLearnedRange, comparison.ComparisonState);
    }

    [Fact]
    public void TodayExpectationUsesEachMatchingHourAndActivityProfile()
    {
        var rollups = new[]
        {
            Rollup(Today.AddHours(10), TimeSpan.FromMinutes(30),
                TimeSpan.Zero, 50d),
            Rollup(Today.AddHours(11), TimeSpan.FromMinutes(30),
                TimeSpan.FromMinutes(30), 115d)
        };
        var comparison = ProjectToday(
            rollups,
            [
                Profile(10, MachineUserActivityState.Active,
                    100d, 100d, 100d),
                Profile(11, MachineUserActivityState.Active,
                    150d, 150d, 150d),
                Profile(11, MachineUserActivityState.Idle,
                    80d, 80d, 80d)
            ],
            now: Today.AddHours(12));

        Assert.Equal(0.165d,
            comparison.ExpectedObservedEnergyKilowattHours!.Value, 8);
        Assert.Equal(1d, comparison.LearnedCoverage, 8);
    }

    [Fact]
    public void IncompleteProfileCoverageNeverProducesAboveOrBelowVerdict()
    {
        var rollup = Rollup(
            Today.AddHours(10),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(30),
            200d);
        var comparison = ProjectToday(
            [rollup],
            [Profile(10, MachineUserActivityState.Active,
                100d, 90d, 110d)],
            now: Today.AddHours(11));

        Assert.Equal(0.5d, comparison.LearnedCoverage, 8);
        Assert.Equal(TimeSpan.FromMinutes(30),
            comparison.LearnedCoveredDuration);
        Assert.Equal(MachineTodayLearnedEnergyComparisonState.StillLearning,
            comparison.ComparisonState);
        Assert.Null(comparison.ExpectedObservedEnergyKilowattHours);
        Assert.Null(comparison.DifferenceKilowattHours);
        Assert.Null(comparison.DifferencePercent);
    }

    [Fact]
    public void CompleteProvisionalCoverageProducesEarlyLearnedProjection()
    {
        var comparison = ProjectToday(
            [Rollup(Today.AddHours(10), TimeSpan.FromMinutes(30),
                TimeSpan.FromMinutes(30), 100d)],
            [
                Profile(10, MachineUserActivityState.Active,
                    120d, 100d, 140d),
                Profile(10, MachineUserActivityState.Idle,
                    80d, 60d, 100d)
            ],
            now: Today.AddHours(11));

        Assert.Equal(1d, comparison.LearnedCoverage, 8);
        Assert.Equal(0.100d,
            comparison.ExpectedObservedEnergyKilowattHours!.Value, 8);
        Assert.Equal(0.080d,
            comparison.ExpectedLowerEnergyKilowattHours!.Value, 8);
        Assert.Equal(0.120d,
            comparison.ExpectedUpperEnergyKilowattHours!.Value, 8);
        Assert.Equal(MachineLearningEvidenceMaturity.Provisional,
            comparison.ComparisonMaturity);
        Assert.Equal(MachineTodayLearnedEnergyComparisonState.
            WithinLearnedRange, comparison.ComparisonState);
    }

    [Theory]
    [InlineData(100d,
        MachineTodayLearnedEnergyComparisonState.WithinLearnedRange)]
    [InlineData(120d,
        MachineTodayLearnedEnergyComparisonState.AboveLearnedRange)]
    [InlineData(80d,
        MachineTodayLearnedEnergyComparisonState.BelowLearnedRange)]
    public void CompleteCoverageClassifiesAgainstLearnedRange(
        double actualWattHours,
        MachineTodayLearnedEnergyComparisonState expectedState)
    {
        var comparison = ProjectToday(
            [Rollup(Today.AddHours(10), TimeSpan.FromHours(1),
                TimeSpan.Zero, actualWattHours)],
            [Profile(10, MachineUserActivityState.Active,
                100d, 90d, 110d)],
            now: Today.AddHours(11));

        Assert.Equal(expectedState, comparison.ComparisonState);
        Assert.NotNull(comparison.DifferencePercent);
    }

    [Fact]
    public void MeaninglessExpectedZeroNeverCalculatesPercentageDifference()
    {
        var comparison = ProjectToday(
            [Rollup(Today.AddHours(10), TimeSpan.FromHours(1),
                TimeSpan.Zero, 1d)],
            [Profile(10, MachineUserActivityState.Active,
                0d, 0d, 0d)],
            now: Today.AddHours(11));

        Assert.Equal(MachineTodayLearnedEnergyComparisonState.StillLearning,
            comparison.ComparisonState);
        Assert.Null(comparison.DifferencePercent);
        Assert.Null(comparison.ExpectedObservedEnergyKilowattHours);
    }

    [Fact]
    public void ExpectedAndActualCostsFollowPrecomputedEnergy()
    {
        var comparison = ProjectToday(
            [Rollup(Today.AddHours(10), TimeSpan.FromHours(1),
                TimeSpan.Zero, 105d)],
            [Profile(10, MachineUserActivityState.Active,
                100d, 90d, 110d)],
            Rate(10m),
            Today.AddHours(11));

        Assert.Equal(1.05m, comparison.ActualEstimatedCost);
        Assert.Equal(1.00m, comparison.ExpectedEstimatedCost);
        Assert.Equal(0.90m, comparison.ExpectedLowerCost);
        Assert.Equal(1.10m, comparison.ExpectedUpperCost);
        Assert.True(comparison.CostAvailable);
    }

    [Fact]
    public void MissingRateKeepsTodayEnergyComparisonAndOmitsCosts()
    {
        var rollups = new[]
        {
            Rollup(Today.AddHours(10), TimeSpan.FromHours(1),
                TimeSpan.Zero, 100d)
        };
        var accepted = MachineTodayEnergyCostProjector.Project(
            rollups,
            [],
            Today.AddHours(11),
            timeZone: TimeZoneInfo.Utc);

        var comparison = MachineTodayLearnedEnergyProjector.Project(
            rollups,
            [Profile(10, MachineUserActivityState.Active,
                100d, 90d, 110d)],
            accepted,
            Today.AddHours(11),
            TimeZoneInfo.Utc);

        Assert.Equal(MachineTodayLearnedEnergyComparisonState.
            WithinLearnedRange, comparison.ComparisonState);
        Assert.Equal(0.100d,
            comparison.ExpectedObservedEnergyKilowattHours!.Value, 8);
        Assert.Null(comparison.Rate);
        Assert.Null(comparison.ActualEstimatedCost);
        Assert.Null(comparison.ExpectedEstimatedCost);
        Assert.Null(comparison.ExpectedLowerCost);
        Assert.Null(comparison.ExpectedUpperCost);
        Assert.False(comparison.CostAvailable);
    }

    [Fact]
    public async Task PersistedHistoryDurationAndLearningProfilesSurviveRestart()
    {
        var historyStore = new MemoryHistoryStore();
        var learningStore = new MemoryLearningStore();
        var history = new MachineHistoryService(
            TimeSpan.Zero,
            TimeSpan.FromHours(2));
        var learning = LearnPowerContext(
            Today.AddHours(10),
            150d);
        history.BeginSession(Today.AddHours(10));
        history.Observe(HistoryObservation(
            Today.AddHours(10),
            0d));
        history.Observe(HistoryObservation(
            Today.AddHours(11),
            150d));
        await history.SaveFinalSnapshotAsync(
            historyStore,
            Today.AddHours(11).AddMinutes(1));
        await learning.SaveFinalSnapshotAsync(
            learningStore,
            Today.AddHours(11).AddMinutes(1));

        var restoredHistory = new MachineHistoryService(
            TimeSpan.Zero,
            TimeSpan.FromHours(2));
        var restoredLearning = new MachineLearningService(
            Today.AddHours(11).AddMinutes(2));
        await restoredHistory.LoadAsync(historyStore);
        await restoredLearning.LoadAsync(learningStore);
        restoredHistory.BeginSession(Today.AddHours(11).AddMinutes(2));
        var rollups = restoredHistory.GetSnapshot(
            MachineHistoryRange.Last7Days,
            Today.AddHours(11).AddMinutes(2)).Rollups;
        var accepted = MachineTodayEnergyCostProjector.Project(
            rollups,
            [Rate(10m)],
            Today.AddHours(11).AddMinutes(2),
            timeZone: TimeZoneInfo.Local);

        var comparison = MachineTodayLearnedEnergyProjector.Project(
            rollups,
            restoredLearning.ContextProfiles,
            accepted,
            Today.AddHours(11).AddMinutes(2),
            TimeZoneInfo.Local);

        Assert.Equal(TimeSpan.FromHours(1),
            comparison.ObservedDuration);
        Assert.Equal(comparison.ObservedDuration,
            comparison.LearnedCoveredDuration);
        Assert.Equal(0.150d,
            comparison.ExpectedObservedEnergyKilowattHours!.Value, 8);
        Assert.Equal(MachineTodayLearnedEnergyComparisonState.
            WithinLearnedRange, comparison.ComparisonState);
        Assert.Equal(4,
            restoredLearning.GetDashboardSnapshot(
                Today.AddHours(11).AddMinutes(2))
                .Metadata.PersistedSchemaVersion);
    }

    [Fact]
    public async Task DerivedViewsDoNotChangeSchemaOrPersistTariffAndCost()
    {
        var store = new MemoryLearningStore();
        var service = LearnPowerContext(
            Today.AddHours(10),
            150d);
        await service.SaveFinalSnapshotAsync(
            store,
            Today.AddHours(11));

        Assert.NotNull(store.State);
        Assert.Equal(MachineLearningService.PersistenceSchemaVersion,
            store.State.SchemaVersion);
        var json = JsonSerializer.Serialize(store.State);
        Assert.DoesNotContain("RatePerKWh", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Currency", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Provider", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProjectedCost", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TodayExpected", json,
            StringComparison.OrdinalIgnoreCase);

        var restored = new MachineLearningService(Today.AddHours(12));
        await restored.LoadAsync(store);
        Assert.Equal(4,
            restored.GetDashboardSnapshot(Today.AddHours(12))
                .Metadata.PersistedSchemaVersion);
        Assert.DoesNotContain(
            restored.ActivityLog.GetSnapshot(
                restored.GetDashboardSnapshot(Today.AddHours(12)),
                Today.AddHours(12)).RecentEvents,
            item => item.Kind ==
                MachineLearningActivityKind.RestoreMigrated);
    }

    private static MachineTodayLearnedEnergyComparison ProjectToday(
        IReadOnlyList<MachineHistoryRollup> rollups,
        IReadOnlyList<MachineLearningContextProfile> profiles,
        DateTimeOffset now) => ProjectToday(
            rollups,
            profiles,
            Rate(14.7833m),
            now);

    private static MachineTodayLearnedEnergyComparison ProjectToday(
        IReadOnlyList<MachineHistoryRollup> rollups,
        IReadOnlyList<MachineLearningContextProfile> profiles,
        ElectricityRateSnapshot rate,
        DateTimeOffset now)
    {
        var accepted = MachineTodayEnergyCostProjector.Project(
            rollups,
            [rate],
            now,
            timeZone: TimeZoneInfo.Utc);
        return MachineTodayLearnedEnergyProjector.Project(
            rollups,
            profiles,
            accepted,
            now,
            TimeZoneInfo.Utc);
    }

    private static MachineLearningBaseline Baseline(
        int hour,
        MachineUserActivityState activity,
        double adaptiveWatts,
        double adaptiveStandardDeviation,
        long evidenceCount = 12,
        int observedDays = 1) => new(
        hour,
        activity,
        evidenceCount,
        20d,
        2d,
        50d,
        2d,
        Today.AddDays(-1),
        Today,
        observedDays,
        MachineLearningConfidence.Provisional,
        EstimatedWallPowerSampleCount: evidenceCount,
        EstimatedWallPowerMeanWatts: adaptiveWatts,
        EstimatedWallPowerStandardDeviationWatts:
            adaptiveStandardDeviation,
        EstimatedWallPowerObservedDayCount: observedDays,
        EstimatedWallPowerFirstObservedAt: Today.AddDays(-1),
        EstimatedWallPowerLastObservedAt: Today,
        AdaptiveEstimatedWallPowerMeanWatts: adaptiveWatts,
        AdaptiveEstimatedWallPowerStandardDeviationWatts:
            adaptiveStandardDeviation,
        AdaptiveEstimatedWallPowerSampleCount: evidenceCount,
        AdaptiveEstimatedWallPowerLastUpdatedAt: Today,
        EstimatedWallPowerFreshness: MachineLearningFreshness.Fresh);

    private static MachineLearningContextProfile Profile(
        int hour,
        MachineUserActivityState activity,
        double adaptiveWatts,
        double lowWatts,
        double highWatts,
        MachineLearningEvidenceMaturity maturity =
            MachineLearningEvidenceMaturity.Provisional)
    {
        var evidenceCount = maturity ==
                MachineLearningEvidenceMaturity.Established
            ? MachineLearningService.EstablishedSampleCount
            : MachineLearningService.ProvisionalSampleCount;
        var observedDays = maturity ==
                MachineLearningEvidenceMaturity.Established
            ? MachineLearningService.EstablishedObservedDayCount
            : 1;
        var power = new MachineLearningEstimatedWallPowerProfile(
            evidenceCount,
            observedDays,
            adaptiveWatts,
            Math.Max(0d, (highWatts - lowWatts) / 4d),
            adaptiveWatts,
            Math.Max(0d, (highWatts - lowWatts) / 4d),
            new(lowWatts, highWatts),
            maturity,
            MachineLearningFreshness.Fresh,
            Today.AddDays(-1),
            Today,
            Today);
        return new(
            hour,
            activity,
            MachineLearningConfidence.Provisional,
            MachineLearningFreshness.Fresh,
            evidenceCount,
            evidenceCount *
                MachineLearningService.ObservationInterval.Ticks,
            observedDays,
            Today.AddDays(-1),
            Today,
            new(20d, 2d, new(16d, 24d)),
            new(50d, 2d, new(46d, 54d)),
            MachineNetworkActivityClass.Quiet,
            evidenceCount,
            evidenceCount,
            Today.AddDays(-1),
            Today,
            Today,
            power);
    }

    private static MachineHistoryRollup Rollup(
        DateTimeOffset start,
        TimeSpan activeDuration,
        TimeSpan idleDuration,
        double actualWattHours)
    {
        var duration = activeDuration + idleDuration;
        return new(
            start,
            start + TimeSpan.FromHours(Math.Max(1d,
                duration.TotalHours)),
            duration.Ticks,
            null,
            null,
            null,
            null,
            null,
            new(0, 0, 0, 0, 0),
            new(activeDuration.Ticks, idleDuration.Ticks),
            ObservedEnergyWattHours: new(1, actualWattHours));
    }

    private static MachineLearningService LearnPowerContext(
        DateTimeOffset start,
        double watts)
    {
        var service = new MachineLearningService(start);
        for (var index = 0; index <
            MachineLearningService.ProvisionalSampleCount; index++)
        {
            service.Observe(new(
                start.AddSeconds(index * 30),
                20d,
                50d,
                MachineUserActivityState.Active,
                MachineOverallState.Stable,
                [],
                40d,
                "stable",
                EstimatedWallPowerWatts: watts));
        }
        return service;
    }

    private static MachineHistoryObservation HistoryObservation(
        DateTimeOffset capturedAt,
        double wattHours) => new(
        capturedAt,
        20d,
        50d,
        null,
        null,
        MachineUserActivityState.Active,
        MachineOverallState.Stable,
        ObservedEnergyWattHours: wattHours);

    private static ElectricityRateSnapshot Rate(decimal value) => new(
        1,
        "Meralco",
        "PHP",
        value,
        new DateOnly(2026, 8, 1),
        Today,
        Today.AddMonths(1),
        "official",
        MachinePowerEstimateConfidence.HighEstimate,
        MachinePowerEstimateConfidence.HighEstimate);

    private sealed class MemoryLearningStore : IMachineLearningStore
    {
        public MachineLearningPersistedState? State { get; private set; }

        public Task<MachineLearningPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(State);

        public Task SaveAsync(
            MachineLearningPersistedState state,
            CancellationToken cancellationToken = default)
        {
            State = state;
            return Task.CompletedTask;
        }
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
