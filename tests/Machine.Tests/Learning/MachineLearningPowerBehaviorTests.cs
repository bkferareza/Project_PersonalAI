using System.Text.Json;
using Machine.Core;

namespace Machine.Tests;

public sealed class MachineLearningPowerBehaviorTests
{
    [Theory]
    [InlineData(MachinePowerEstimateConfidence.Measured)]
    [InlineData(MachinePowerEstimateConfidence.HighEstimate)]
    [InlineData(MachinePowerEstimateConfidence.ModerateEstimate)]
    public void EligibilityAcceptsCurrentTrustworthyWallPower(
        MachinePowerEstimateConfidence confidence)
    {
        var now = DateTimeOffset.Parse("2026-08-25T12:00:00+08:00");
        var estimate = CreateEstimate(now, 147.5d, confidence);

        Assert.Equal(147.5d,
            MachineLearningPowerEvidencePolicy.
                SelectEligibleEstimatedWallPowerWatts(estimate, now));
    }

    [Fact]
    public void EligibilityRejectsInvalidLowConfidenceUnavailableAndStalePower()
    {
        var now = DateTimeOffset.Parse("2026-08-25T12:00:00+08:00");
        var invalid = new double?[]
        {
            null,
            double.NaN,
            double.PositiveInfinity,
            -1d
        };
        foreach (var watts in invalid)
        {
            Assert.Null(MachineLearningPowerEvidencePolicy.
                SelectEligibleEstimatedWallPowerWatts(
                    CreateEstimate(now, watts,
                        MachinePowerEstimateConfidence.ModerateEstimate),
                    now));
        }

        Assert.Null(MachineLearningPowerEvidencePolicy.
            SelectEligibleEstimatedWallPowerWatts(
                CreateEstimate(now, 150d,
                    MachinePowerEstimateConfidence.LowEstimate), now));
        Assert.Null(MachineLearningPowerEvidencePolicy.
            SelectEligibleEstimatedWallPowerWatts(
                CreateEstimate(now, 150d,
                    MachinePowerEstimateConfidence.Unavailable), now));
        Assert.Null(MachineLearningPowerEvidencePolicy.
            SelectEligibleEstimatedWallPowerWatts(
                CreateEstimate(now.Subtract(
                        MachineLearningPowerEvidencePolicy.MaximumEstimateAge +
                        TimeSpan.FromTicks(1)),
                    150d,
                    MachinePowerEstimateConfidence.ModerateEstimate), now));
        Assert.Null(MachineLearningPowerEvidencePolicy.
            SelectEligibleEstimatedWallPowerWatts(
                CreateEstimate(now.AddTicks(1), 150d,
                    MachinePowerEstimateConfidence.ModerateEstimate), now));
    }

    [Fact]
    public void InvalidPowerDoesNotRejectOtherwiseValidLearningObservation()
    {
        var service = new MachineLearningService();
        var start = CreateLocalTime(2026, 8, 25, 12);
        var values = new double?[]
        {
            null,
            double.NaN,
            double.NegativeInfinity,
            -1d
        };

        for (var index = 0; index < values.Length; index++)
        {
            Assert.True(service.Observe(CreateObservation(
                start.AddSeconds(index * 30),
                powerWatts: values[index])));
        }

        var snapshot = service.GetDashboardSnapshot(start.AddMinutes(2));
        Assert.Equal(values.Length, snapshot.ObservationCount);
        var baseline = Assert.Single(snapshot.Baselines);
        Assert.Equal(0, baseline.EstimatedWallPowerSampleCount);
        Assert.Equal(MachineLearningEvidenceMaturity.Insufficient,
            baseline.EstimatedWallPowerMaturity);
        Assert.Null(baseline.EstimatedWallPowerMeanWatts);
        Assert.All(service.ActivityLog.GetSnapshot(snapshot,
                start.AddMinutes(2)).RecentEvents.Where(item =>
                item.Kind == MachineLearningActivityKind.ObservationAccepted),
            item => Assert.False(item.PowerEvidenceAccepted));
    }

    [Fact]
    public void ValidPowerUsesIndependentWelfordAndAdaptiveStatistics()
    {
        var service = new MachineLearningService();
        var start = CreateLocalTime(2026, 1, 1, 12);
        service.Observe(CreateObservation(start, powerWatts: 150d));
        service.Observe(CreateObservation(
            start.Add(MachineLearningPolicy.AdaptiveHalfLife),
            powerWatts: 170d));

        var baseline = Assert.Single(service.Baselines);
        Assert.Equal(2, baseline.EstimatedWallPowerSampleCount);
        Assert.Equal(160d,
            baseline.EstimatedWallPowerMeanWatts!.Value, 8);
        Assert.Equal(Math.Sqrt(200d),
            baseline.EstimatedWallPowerStandardDeviationWatts!.Value, 8);
        Assert.Equal(2,
            baseline.AdaptiveEstimatedWallPowerSampleCount);
        Assert.Equal(160d,
            baseline.AdaptiveEstimatedWallPowerMeanWatts!.Value, 8);
        Assert.Equal(10d,
            baseline.AdaptiveEstimatedWallPowerStandardDeviationWatts!.Value,
            8);
        Assert.Equal(2, baseline.EstimatedWallPowerObservedDayCount);
        Assert.Null(baseline.EstimatedWallPowerTypicalRange);
    }

    [Fact]
    public void PowerBaselinesRemainSeparateByActivityAndHour()
    {
        var service = new MachineLearningService();
        var start = CreateLocalTime(2026, 8, 25, 20);
        for (var index = 0; index < 12; index++)
        {
            service.Observe(CreateObservation(
                start.AddSeconds(index * 60),
                MachineUserActivityState.Active,
                powerWatts: 150d));
            service.Observe(CreateObservation(
                start.AddSeconds(index * 60 + 30),
                MachineUserActivityState.Idle,
                powerWatts: 85d));
        }
        for (var index = 0; index < 12; index++)
        {
            service.Observe(CreateObservation(
                start.AddHours(1).AddSeconds(index * 30),
                MachineUserActivityState.Active,
                powerWatts: 175d));
        }

        Assert.Equal(3, service.Baselines.Count);
        Assert.Equal(150d, FindBaseline(service, 20,
            MachineUserActivityState.Active)
            .EstimatedWallPowerMeanWatts);
        Assert.Equal(85d, FindBaseline(service, 20,
            MachineUserActivityState.Idle)
            .EstimatedWallPowerMeanWatts);
        Assert.Equal(175d, FindBaseline(service, 21,
            MachineUserActivityState.Active)
            .EstimatedWallPowerMeanWatts);
    }

    [Fact]
    public void EstimatedWallPowerRangeIsNeverClampedToOneHundredWatts()
    {
        var service = new MachineLearningService();
        var start = CreateLocalTime(2026, 8, 25, 20);
        for (var index = 0; index < 12; index++)
        {
            service.Observe(CreateObservation(
                start.AddSeconds(index * 30),
                powerWatts: 150d + index * (30d / 11d)));
        }

        var baseline = Assert.Single(service.Baselines);
        var range = Assert.IsType<MachineLearningRange>(
            baseline.EstimatedWallPowerTypicalRange);
        Assert.True(baseline.EstimatedWallPowerMeanWatts > 150d);
        Assert.True(range.Low > 100d);
        Assert.True(range.High > 100d);
        Assert.Equal(MachineLearningEvidenceMaturity.Provisional,
            baseline.EstimatedWallPowerMaturity);
        var power = Assert.Single(service.ContextProfiles)
            .EstimatedWallPower;
        Assert.NotNull(power);
        Assert.True(power.AdaptiveMeanWatts > 100d);
    }

    [Fact]
    public void LowPowerRangeRemainsNonnegative()
    {
        var service = new MachineLearningService();
        var start = CreateLocalTime(2026, 8, 25, 2);
        for (var index = 0; index < 12; index++)
        {
            service.Observe(CreateObservation(
                start.AddSeconds(index * 30),
                MachineUserActivityState.Idle,
                powerWatts: 50d + index * (40d / 11d)));
        }

        var range = Assert.IsType<MachineLearningRange>(
            Assert.Single(service.Baselines)
                .EstimatedWallPowerTypicalRange);
        Assert.True(range.Low >= 0d);
        Assert.True(range.High >= range.Low);
    }

    [Fact]
    public void PowerMaturityUsesOnlyPowerSamplesAndDistinctPowerDays()
    {
        var start = CreateLocalTime(2026, 8, 1, 12);
        var service = new MachineLearningService();
        for (var index = 0; index < 11; index++)
        {
            service.Observe(CreateObservation(
                start.AddSeconds(index * 30), powerWatts: 150d));
        }
        Assert.Equal(MachineLearningEvidenceMaturity.Insufficient,
            Assert.Single(service.Baselines).EstimatedWallPowerMaturity);
        Assert.Empty(service.ContextProfiles);

        service.Observe(CreateObservation(
            start.AddSeconds(11 * 30), powerWatts: 150d));
        Assert.Equal(MachineLearningEvidenceMaturity.Provisional,
            Assert.Single(service.Baselines).EstimatedWallPowerMaturity);
        Assert.Equal(MachineLearningEvidenceMaturity.Provisional,
            Assert.Single(service.ContextProfiles)
                .EstimatedWallPower!.Maturity);

        var established = new MachineLearningService();
        for (var day = 0; day < 7; day++)
        {
            for (var sample = 0; sample < 24; sample++)
            {
                established.Observe(CreateObservation(
                    start.AddDays(day).AddSeconds(sample * 30),
                    powerWatts: 150d));
            }
        }

        var baseline = Assert.Single(established.Baselines);
        Assert.Equal(168, baseline.EstimatedWallPowerSampleCount);
        Assert.Equal(7, baseline.EstimatedWallPowerObservedDayCount);
        Assert.Equal(MachineLearningEvidenceMaturity.Established,
            baseline.EstimatedWallPowerMaturity);
        Assert.Equal(MachineLearningEvidenceMaturity.Established,
            Assert.Single(established.ContextProfiles)
                .EstimatedWallPower!.Maturity);
    }

    [Fact]
    public async Task EstablishedLegacyProfileDoesNotInheritPowerMaturity()
    {
        var start = CreateLocalTime(2026, 8, 18, 12);
        var state = CreateEstablishedVersionThreeState(start, 1_000);
        var service = new MachineLearningService(start.AddDays(1));
        await service.LoadAsync(new MemoryStore(state));

        for (var index = 0; index < 5; index++)
        {
            service.Observe(CreateObservation(
                start.AddDays(1).AddSeconds(index * 30),
                powerWatts: 150d));
        }

        var baseline = Assert.Single(service.Baselines);
        Assert.Equal(MachineLearningConfidence.Established,
            baseline.Confidence);
        Assert.Equal(5, baseline.EstimatedWallPowerSampleCount);
        Assert.Equal(MachineLearningEvidenceMaturity.Insufficient,
            baseline.EstimatedWallPowerMaturity);
        var profile = Assert.Single(service.ContextProfiles);
        Assert.Equal(MachineLearningConfidence.Established,
            profile.Confidence);
        Assert.Null(profile.EstimatedWallPower);
    }

    [Fact]
    public async Task TariffAndSessionDurationDoNotChangeLearnedPowerBehavior()
    {
        var start = CreateLocalTime(2026, 8, 25, 12);
        var shortSession = LearnConstantPower(start, 12, 150d);
        var longSession = LearnConstantPower(start, 120, 150d);
        Assert.Equal(150d, Assert.Single(shortSession.Baselines)
            .EstimatedWallPowerMeanWatts);
        Assert.Equal(150d, Assert.Single(longSession.Baselines)
            .EstimatedWallPowerMeanWatts);

        var before = Assert.Single(shortSession.Baselines);
        var rate = new ElectricityRateSnapshot(
            1,
            "Reference",
            "PHP",
            20m,
            new DateOnly(2026, 8, 1),
            start,
            start.AddMonths(1),
            "official",
            MachinePowerEstimateConfidence.HighEstimate,
            MachinePowerEstimateConfidence.HighEstimate);
        _ = MachineElectricityCostCalculator.Calculate(1_000d, rate);
        var after = Assert.Single(shortSession.Baselines);
        Assert.Equal(before.EstimatedWallPowerSampleCount,
            after.EstimatedWallPowerSampleCount);
        Assert.Equal(before.EstimatedWallPowerMeanWatts,
            after.EstimatedWallPowerMeanWatts);

        var store = new MemoryStore();
        await shortSession.SaveIfDueAsync(store, start.AddHours(1), true);
        var json = JsonSerializer.Serialize(store.State);
        Assert.DoesNotContain("RatePerKWh", json,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CurrencyCode", json,
            StringComparison.Ordinal);
        Assert.DoesNotContain("EstimatedCost", json,
            StringComparison.Ordinal);
        Assert.DoesNotContain("EnergyWattHours", json,
            StringComparison.Ordinal);
    }

    private static MachineLearningService LearnConstantPower(
        DateTimeOffset start,
        int sampleCount,
        double watts)
    {
        var service = new MachineLearningService();
        for (var index = 0; index < sampleCount; index++)
        {
            service.Observe(CreateObservation(
                start.AddSeconds(index * 30),
                powerWatts: watts));
        }
        return service;
    }

    private static MachineLearningBaseline FindBaseline(
        MachineLearningService service,
        int hour,
        MachineUserActivityState activity) => service.Baselines.Single(item =>
        item.LocalHour == hour && item.ActivityState == activity);

    private static MachinePowerEstimate CreateEstimate(
        DateTimeOffset capturedAt,
        double? watts,
        MachinePowerEstimateConfidence confidence) => new(
        capturedAt,
        watts,
        watts,
        watts,
        null,
        null,
        null,
        confidence);

    private static MachineLearningObservation CreateObservation(
        DateTimeOffset timestamp,
        MachineUserActivityState activity = MachineUserActivityState.Active,
        double? powerWatts = null) => new(
        timestamp,
        20d,
        50d,
        activity,
        MachineOverallState.Stable,
        [],
        40d,
        "stable",
        EstimatedWallPowerWatts: powerWatts);

    private static MachineLearningPersistedState
        CreateEstablishedVersionThreeState(
            DateTimeOffset lastObservedAt,
            long sampleCount)
    {
        var firstObservedAt = lastObservedAt.AddDays(-6);
        var duration = sampleCount *
            MachineLearningService.ObservationInterval.Ticks;
        var baseline = new MachineLearningBaselineState(
            lastObservedAt.ToLocalTime().Hour,
            MachineUserActivityState.Active,
            sampleCount,
            20d,
            sampleCount * 4d,
            50d,
            sampleCount * 4d,
            firstObservedAt,
            lastObservedAt,
            NetworkQuietSampleCount: sampleCount,
            ObservedDayCount: 7,
            LastObservedLocalDate: DateOnly.FromDateTime(
                lastObservedAt.LocalDateTime),
            ObservedDurationTicks: duration,
            AdaptiveCpuMean: 20d,
            AdaptiveCpuVariance: 4d,
            AdaptiveMemoryMean: 50d,
            AdaptiveMemoryVariance: 4d,
            AdaptiveSampleCount: sampleCount,
            AdaptiveLastUpdatedAt: lastObservedAt);
        var profile = new MachineLearningContextProfile(
            baseline.LocalHour,
            baseline.ActivityState,
            MachineLearningConfidence.Established,
            MachineLearningFreshness.Fresh,
            sampleCount,
            duration,
            7,
            firstObservedAt,
            lastObservedAt,
            new(20d, 2d, new(16d, 24d)),
            new(50d, 2d, new(46d, 54d)),
            MachineNetworkActivityClass.Quiet,
            sampleCount,
            sampleCount,
            firstObservedAt,
            lastObservedAt,
            lastObservedAt);
        return new MachineLearningPersistedState(
            MachineLearningService.PreviousPersistenceSchemaVersion,
            [baseline],
            [],
            sampleCount,
            firstObservedAt,
            lastObservedAt,
            lastObservedAt,
            duration,
            Metadata: new(
                sampleCount,
                duration,
                3,
                firstObservedAt,
                lastObservedAt,
                firstObservedAt,
                firstObservedAt.AddDays(-1),
                lastObservedAt),
            ContextProfiles: [profile],
            BroaderPatterns: []);
    }

    private static DateTimeOffset CreateLocalTime(
        int year,
        int month,
        int day,
        int hour)
    {
        var local = new DateTime(year, month, day, hour, 0, 0,
            DateTimeKind.Unspecified);
        return new DateTimeOffset(local,
            TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private sealed class MemoryStore(
        MachineLearningPersistedState? state = null) : IMachineLearningStore
    {
        public MachineLearningPersistedState? State { get; private set; } =
            state;

        public Task<MachineLearningPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(State);

        public Task SaveAsync(
            MachineLearningPersistedState persisted,
            CancellationToken cancellationToken = default)
        {
            State = persisted;
            return Task.CompletedTask;
        }
    }
}
