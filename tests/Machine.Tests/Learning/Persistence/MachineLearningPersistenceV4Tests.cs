using Machine.Core;

namespace Machine.Tests;

public sealed class MachineLearningPersistenceV4Tests
{
    [Fact]
    public async Task VersionThreeMigrationPreservesAllExistingBehavioralEvidence()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var source = CreateRepresentativeVersionThreeState();
            var store = new FileMachineLearningStore(directory);
            await store.SaveAsync(source);
            var restoredAt = source.PersistedAt!.Value.AddDays(1);
            var service = new MachineLearningService(restoredAt);

            await service.LoadAsync(store);

            var snapshot = service.GetDashboardSnapshot(restoredAt);
            Assert.Equal(336, snapshot.Metadata.LifetimeAcceptedObservationCount);
            Assert.Equal(6, snapshot.Metadata.LifetimeMachineSessionCount);
            Assert.Equal(2, snapshot.Baselines.Count);
            Assert.Equal(2, snapshot.ContextProfiles.Count);
            Assert.Single(snapshot.BroaderPatterns);
            Assert.Single(snapshot.RecentEpisodes);
            Assert.All(snapshot.Baselines, baseline =>
            {
                Assert.Equal(168, baseline.SampleCount);
                Assert.Equal(MachineLearningConfidence.Established,
                    baseline.Confidence);
                Assert.Equal(0,
                    baseline.EstimatedWallPowerSampleCount);
                Assert.Equal(MachineLearningEvidenceMaturity.Insufficient,
                    baseline.EstimatedWallPowerMaturity);
                Assert.Null(baseline.EstimatedWallPowerMeanWatts);
            });
            Assert.All(snapshot.ContextProfiles, profile =>
            {
                Assert.Equal(MachineLearningConfidence.Established,
                    profile.Confidence);
                Assert.Equal(20d, profile.Cpu.AdaptiveMean);
                Assert.Equal(50d, profile.Memory.AdaptiveMean);
                Assert.Null(profile.EstimatedWallPower);
            });
            Assert.Equal(source.ContextProfiles![0].CreatedAt,
                snapshot.ContextProfiles[0].CreatedAt);
            Assert.Equal(source.BroaderPatterns![0].CreatedAt,
                snapshot.BroaderPatterns[0].CreatedAt);
            Assert.DoesNotContain(service.ActivityLog.GetSnapshot(
                    snapshot, restoredAt).RecentEvents,
                item => item.Kind ==
                    MachineLearningActivityKind.
                        LearningContinuityRegressionDetected);

            Assert.True(await service.SaveIfDueAsync(
                store, restoredAt, force: true));
            var migrated = await store.LoadAsync();
            Assert.Equal(MachineLearningService.PersistenceSchemaVersion,
                migrated!.SchemaVersion);
            Assert.All(migrated.Baselines, baseline =>
                Assert.Equal(0,
                    baseline.EstimatedWallPowerSampleCount));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task VersionFourFileRoundTripRestoresPowerWithoutDuplication()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var source = CreateRepresentativeVersionThreeState();
            var store = new FileMachineLearningStore(directory);
            await store.SaveAsync(source);
            var firstSessionAt = CreateLocalTime(2026, 8, 25, 20);
            var first = new MachineLearningService(firstSessionAt);
            await first.LoadAsync(store);
            for (var index = 0; index < 12; index++)
            {
                first.Observe(CreateObservation(
                    firstSessionAt.AddSeconds(index * 30),
                    150d + index));
            }

            var before = first.Baselines.Single(item =>
                item.LocalHour == 20 &&
                item.ActivityState == MachineUserActivityState.Active);
            Assert.Equal(12, before.EstimatedWallPowerSampleCount);
            Assert.Equal(155.5d,
                before.EstimatedWallPowerMeanWatts!.Value, 8);
            Assert.Equal(MachineLearningEvidenceMaturity.Provisional,
                before.EstimatedWallPowerMaturity);
            Assert.Equal(MachineLearningEvidenceMaturity.Provisional,
                first.ContextProfiles.Single(item => item.LocalHour == 20)
                    .EstimatedWallPower!.Maturity);

            Assert.True(await first.SaveFinalSnapshotAsync(
                store, firstSessionAt.AddMinutes(6)));
            var persisted = await store.LoadAsync();
            Assert.Equal(4, persisted!.SchemaVersion);
            var persistedPower = persisted.Baselines.Single(item =>
                item.LocalHour == 20);
            Assert.Equal(12,
                persistedPower.EstimatedWallPowerSampleCount);

            var secondSessionAt = firstSessionAt.AddDays(1);
            var second = new MachineLearningService(secondSessionAt);
            await second.LoadAsync(store);
            var after = second.Baselines.Single(item =>
                item.LocalHour == 20 &&
                item.ActivityState == MachineUserActivityState.Active);

            Assert.Equal(before.SampleCount, after.SampleCount);
            Assert.Equal(before.CpuMean, after.CpuMean);
            Assert.Equal(before.MemoryMean, after.MemoryMean);
            Assert.Equal(before.EstimatedWallPowerSampleCount,
                after.EstimatedWallPowerSampleCount);
            Assert.Equal(before.EstimatedWallPowerMeanWatts,
                after.EstimatedWallPowerMeanWatts);
            Assert.Equal(before.EstimatedWallPowerStandardDeviationWatts,
                after.EstimatedWallPowerStandardDeviationWatts);
            Assert.Equal(before.AdaptiveEstimatedWallPowerMeanWatts,
                after.AdaptiveEstimatedWallPowerMeanWatts);
            Assert.Equal(before.EstimatedWallPowerTypicalRange,
                after.EstimatedWallPowerTypicalRange);
            Assert.Equal(MachineLearningEvidenceMaturity.Provisional,
                second.ContextProfiles.Single(item => item.LocalHour == 20)
                    .EstimatedWallPower!.Maturity);
            Assert.Equal(348,
                second.GetDashboardSnapshot(secondSessionAt)
                    .Metadata.LifetimeAcceptedObservationCount);
            Assert.DoesNotContain(second.ActivityLog.GetSnapshot(
                    second.GetDashboardSnapshot(secondSessionAt),
                    secondSessionAt).RecentEvents,
                item => item.Kind ==
                    MachineLearningActivityKind.
                        LearningContinuityRegressionDetected);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MachineLearningPersistedState
        CreateRepresentativeVersionThreeState()
    {
        var hour20 = CreateLocalTime(2026, 8, 24, 20);
        var hour21 = hour20.AddHours(1);
        var firstObservedAt = hour20.AddDays(-6);
        var persistedAt = hour21.AddMinutes(10);
        var baselines = new[]
        {
            CreateBaseline(hour20, firstObservedAt),
            CreateBaseline(hour21, firstObservedAt.AddHours(1))
        };
        var profiles = baselines.Select(baseline =>
            CreateProfile(baseline)).ToArray();
        var pattern = new MachineLearningRecurringPattern(
            MachineUserActivityState.Active,
            20,
            22,
            false,
            profiles.Select(profile => profile.ContextKey).ToArray(),
            MachineLearningConfidence.Provisional,
            MachineLearningFreshness.Fresh,
            336,
            7,
            new(16d, 24d),
            new(46d, 54d),
            MachineNetworkActivityClass.Quiet,
            336,
            336,
            firstObservedAt,
            hour21);
        var episode = new MachineLearningEpisode(
            hour20,
            hour20.AddMinutes(5),
            MachineUserActivityState.Active,
            MachineOverallState.Stable,
            10,
            20d,
            24d,
            50d,
            [],
            "Session ended");
        var durationTicks = 336L *
            MachineLearningService.ObservationInterval.Ticks;
        return new MachineLearningPersistedState(
            MachineLearningService.PreviousPersistenceSchemaVersion,
            baselines,
            [episode],
            336,
            firstObservedAt,
            hour21,
            persistedAt,
            durationTicks,
            Metadata: new(
                336,
                durationTicks,
                5,
                firstObservedAt,
                hour21,
                hour20,
                hour20.AddDays(-1),
                persistedAt),
            ContextProfiles: profiles,
            BroaderPatterns: [pattern]);
    }

    private static MachineLearningBaselineState CreateBaseline(
        DateTimeOffset lastObservedAt,
        DateTimeOffset firstObservedAt)
    {
        const long sampleCount = 168;
        return new MachineLearningBaselineState(
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
            ObservedDurationTicks: sampleCount *
                MachineLearningService.ObservationInterval.Ticks,
            AdaptiveCpuMean: 20d,
            AdaptiveCpuVariance: 4d,
            AdaptiveMemoryMean: 50d,
            AdaptiveMemoryVariance: 4d,
            AdaptiveSampleCount: sampleCount,
            AdaptiveLastUpdatedAt: lastObservedAt);
    }

    private static MachineLearningContextProfile CreateProfile(
        MachineLearningBaselineState baseline) => new(
        baseline.LocalHour,
        baseline.ActivityState,
        MachineLearningConfidence.Established,
        MachineLearningFreshness.Fresh,
        baseline.SampleCount,
        baseline.ObservedDurationTicks,
        baseline.ObservedDayCount,
        baseline.FirstObservedAt,
        baseline.LastObservedAt,
        new(20d, 2d, new(16d, 24d)),
        new(50d, 2d, new(46d, 54d)),
        MachineNetworkActivityClass.Quiet,
        baseline.SampleCount,
        baseline.SampleCount,
        baseline.FirstObservedAt,
        baseline.LastObservedAt,
        baseline.LastObservedAt);

    private static MachineLearningObservation CreateObservation(
        DateTimeOffset timestamp,
        double watts) => new(
        timestamp,
        20d,
        50d,
        MachineUserActivityState.Active,
        MachineOverallState.Stable,
        [],
        40d,
        "stable",
        EstimatedWallPowerWatts: watts);

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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
}
