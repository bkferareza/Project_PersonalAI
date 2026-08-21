using Machine.Core;

namespace Machine.Tests;

public sealed class MachineTodayEnergyCostProjectorTests
{
    private static readonly DateTimeOffset Today =
        DateTimeOffset.Parse("2026-08-22T00:00:00Z");

    [Fact]
    public void SumsAcceptedTodayEnergyAndProjectsPowerEvidence()
    {
        var result = MachineTodayEnergyCostProjector.Project(
        [
            Rollup(Today.AddHours(1), 100d, 90d, 150d),
            Rollup(Today.AddHours(2), 250d, 110d, 180d)
        ],
        [Rate()], Today.AddHours(3), timeZone: TimeZoneInfo.Utc);

        Assert.Equal(350d, result.ObservedEnergyWattHours, 6);
        Assert.Equal(5.17m, result.EstimatedCost);
        Assert.Equal(MachineCostCoverage.Complete, result.CostCoverage);
        Assert.Equal(TimeSpan.FromMinutes(10), result.ObservedDuration);
        Assert.Equal(100d, result.AverageEstimatedWallPowerWatts);
        Assert.Equal(180d, result.PeakEstimatedWallPowerWatts);
        Assert.Equal(2, result.EnergyContributionCount);
    }

    [Fact]
    public async Task PersistedAndNewSessionEnergyRemainOneTodayTotal()
    {
        var store = new RecordingHistoryStore();
        var firstSession = new MachineHistoryService(TimeSpan.Zero,
            TimeSpan.FromMinutes(5));
        firstSession.BeginSession(Today.AddHours(1));
        firstSession.Observe(Observation(Today.AddHours(1), 100d));
        firstSession.Observe(Observation(
            Today.AddHours(1).AddSeconds(30), 50d));
        await firstSession.SaveFinalSnapshotAsync(
            store, Today.AddHours(2));

        var secondSession = new MachineHistoryService(TimeSpan.Zero,
            TimeSpan.FromMinutes(5));
        await secondSession.LoadAsync(store);
        secondSession.BeginSession(Today.AddHours(3));
        secondSession.Observe(Observation(Today.AddHours(3), 75d));
        var snapshot = secondSession.GetSnapshot(
            MachineHistoryRange.Last24Hours, Today.AddHours(3));

        var result = MachineTodayEnergyCostProjector.Project(
            snapshot.Rollups, [Rate()], Today.AddHours(3),
            timeZone: TimeZoneInfo.Utc);

        Assert.Equal(225d, result.ObservedEnergyWattHours, 6);
        Assert.Equal(3.33m, result.EstimatedCost);
    }

    [Fact]
    public void PendingEnergyIsIncludedExactlyOnceBeforeAndAfterAcceptance()
    {
        var beforeAcceptance = MachineTodayEnergyCostProjector.Project(
            [Rollup(Today.AddHours(1), 100d)], [Rate()],
            Today.AddHours(2), pendingObservedEnergyWattHours: 50d,
            timeZone: TimeZoneInfo.Utc);
        var afterAcceptance = MachineTodayEnergyCostProjector.Project(
            [Rollup(Today.AddHours(1), 150d)], [Rate()],
            Today.AddHours(2), pendingObservedEnergyWattHours: 0d,
            timeZone: TimeZoneInfo.Utc);

        Assert.Equal(150d, beforeAcceptance.ObservedEnergyWattHours, 6);
        Assert.Equal(beforeAcceptance.ObservedEnergyWattHours,
            afterAcceptance.ObservedEnergyWattHours, 6);
        Assert.Equal(beforeAcceptance.EstimatedCost,
            afterAcceptance.EstimatedCost);
    }

    [Fact]
    public void MissingCurrentMonthRateKeepsCostUnknownAndOmitsInsight()
    {
        var today = MachineTodayEnergyCostProjector.Project(
            [Rollup(Today.AddHours(1), 100d)],
            [Rate(new DateOnly(2026, 7, 1))], Today.AddHours(2),
            timeZone: TimeZoneInfo.Utc);

        Assert.True(today.HasObservedEnergy);
        Assert.Null(today.EstimatedCost);
        Assert.Equal(MachineCostCoverage.Unavailable,
            today.CostCoverage);
        Assert.Null(MachineRunningBillInsightProjector.Project(today));
    }

    [Fact]
    public void LocalMidnightStartsFreshWithoutRemovingYesterday()
    {
        var rollups = new[]
        {
            Rollup(Today.AddHours(-1), 200d),
            Rollup(Today.AddMinutes(5), 50d)
        };

        var result = MachineTodayEnergyCostProjector.Project(
            rollups, [Rate()], Today.AddMinutes(10),
            timeZone: TimeZoneInfo.Utc);

        Assert.Equal(new DateOnly(2026, 8, 22), result.LocalDate);
        Assert.Equal(50d, result.ObservedEnergyWattHours, 6);
        Assert.Equal(2, rollups.Length);
    }

    [Fact]
    public void RunningBillInsightUsesPrecomputedTodayValues()
    {
        var today = MachineTodayEnergyCostProjector.Project(
            [Rollup(Today.AddHours(1), 840d)], [Rate()],
            Today.AddHours(2), timeZone: TimeZoneInfo.Utc);

        var insight = MachineRunningBillInsightProjector.Project(today);

        Assert.NotNull(insight);
        Assert.Equal("Running bill today", insight.Title);
        Assert.Equal(today.EstimatedCost,
            insight.EstimatedPcElectricityCost);
        Assert.Equal(0.84d,
            insight.TodayObservedEnergyKilowattHours, 6);
        Assert.Equal("Meralco", insight.Rate.ProviderName);
    }

    private static MachineHistoryRollup Rollup(
        DateTimeOffset start,
        double wattHours,
        double averageWatts = 100d,
        double peakWatts = 100d) => new(
        start,
        start.AddMinutes(5),
        TimeSpan.FromMinutes(5).Ticks,
        null,
        null,
        null,
        null,
        null,
        new(0, 0, 0, 0, 0),
        new(0, 0),
        EstimatedSystemPowerWatts: new(1, averageWatts, peakWatts,
            averageWatts),
        ObservedEnergyWattHours: new(1, wattHours));

    private static MachineHistoryObservation Observation(
        DateTimeOffset capturedAt,
        double wattHours) => new(capturedAt, 10d, 20d, null, null,
        MachineUserActivityState.Active, MachineOverallState.Stable,
        ObservedEnergyWattHours: wattHours);

    private static ElectricityRateSnapshot Rate(
        DateOnly? month = null) => new(1, "Meralco", "PHP", 14.7833m,
        month ?? new DateOnly(2026, 8, 1), Today, Today.AddMonths(1),
        "official", MachinePowerEstimateConfidence.HighEstimate,
        MachinePowerEstimateConfidence.HighEstimate);

    private sealed class RecordingHistoryStore : IMachineHistoryStore
    {
        private MachineHistoryPersistedState? _state;

        public Task<MachineHistoryPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_state);

        public Task SaveAsync(MachineHistoryPersistedState state,
            CancellationToken cancellationToken = default)
        {
            _state = state;
            return Task.CompletedTask;
        }
    }
}
