using System.Text;
using System.Text.Json;
using Machine.Core;

namespace Machine.Tests;

public sealed class MachineLearningPersistenceV3Tests
{
    [Fact]
    public async Task RestartContinuesCumulativeEvidenceAndAdaptiveState()
    {
        var start = CreateLocalTime(2026, 1, 1, 3);
        var store = new MutableMemoryStore();
        var first = new MachineLearningService(start);
        var dailyCounts = new[] { 28, 28, 27, 27, 27 };
        for (var day = 0; day < dailyCounts.Length; day++)
        {
            for (var sample = 0; sample < dailyCounts[day]; sample++)
            {
                first.Observe(CreateObservation(
                    start.AddDays(day).AddSeconds(sample * 30),
                    cpu: 10,
                    network: MachineNetworkActivityClass.Quiet));
            }
        }

        var before = Assert.Single(first.Baselines);
        Assert.Equal(137, before.SampleCount);
        Assert.Equal(5, before.ObservedDayCount);
        await first.SaveFinalSnapshotAsync(store, start.AddDays(4).AddHours(1));

        var secondStart = start.AddDays(5);
        var second = new MachineLearningService(secondStart);
        await second.LoadAsync(store);
        for (var sample = 0; sample < 20; sample++)
        {
            second.Observe(CreateObservation(
                secondStart.AddSeconds(sample * 30),
                cpu: 90,
                network: MachineNetworkActivityClass.Light));
        }

        var snapshot = second.GetDashboardSnapshot(
            secondStart.AddMinutes(10));
        var accumulated = Assert.Single(snapshot.Baselines);
        Assert.Equal(157, accumulated.SampleCount);
        Assert.Equal(6, accumulated.ObservedDayCount);
        Assert.Equal(137, accumulated.NetworkQuietSampleCount);
        Assert.Equal(20, accumulated.NetworkLightSampleCount);
        Assert.Equal(157, accumulated.AdaptiveSampleCount);
        Assert.InRange(accumulated.AdaptiveCpuMean, 10.01, 89.99);
        Assert.InRange(accumulated.CpuMean, 20.18, 20.20);
        Assert.Equal(MachineLearningConfidence.Provisional,
            accumulated.Confidence);
        Assert.Equal(157, snapshot.Metadata.LifetimeAcceptedObservationCount);
        Assert.Equal(TimeSpan.FromMinutes(78.5),
            snapshot.Metadata.LifetimeObservedDuration);
        Assert.Equal(2, snapshot.Metadata.LifetimeMachineSessionCount);
    }

    [Fact]
    public async Task ThreeSessionsExcludeOfflineGapsAndNeverInventIdle()
    {
        var start = CreateLocalTime(2026, 1, 1, 3);
        var store = new MutableMemoryStore();

        var first = new MachineLearningService(start);
        first.Observe(CreateObservation(start));
        first.Observe(CreateObservation(start.AddSeconds(30)));
        await first.SaveFinalSnapshotAsync(store, start.AddMinutes(1));

        var secondStart = start.AddDays(10);
        var second = new MachineLearningService(secondStart);
        await second.LoadAsync(store);
        second.Observe(CreateObservation(secondStart));
        await second.SaveFinalSnapshotAsync(store, secondStart.AddMinutes(1));

        var thirdStart = start.AddDays(40);
        var third = new MachineLearningService(thirdStart);
        await third.LoadAsync(store);
        var snapshot = third.GetDashboardSnapshot(thirdStart);

        Assert.Equal(3, snapshot.Metadata.LifetimeMachineSessionCount);
        Assert.Equal(3, snapshot.Metadata.LifetimeAcceptedObservationCount);
        Assert.Equal(TimeSpan.FromSeconds(90),
            snapshot.Metadata.LifetimeObservedDuration);
        Assert.Equal(3, Assert.Single(snapshot.Baselines).SampleCount);
        Assert.Empty(third.Journal);
        Assert.Equal(2, third.RecentEpisodes.Count);
        Assert.All(third.RecentEpisodes, episode =>
            Assert.Equal(MachineUserActivityState.Active,
                episode.ActivityState));
        Assert.Equal(secondStart.AddMinutes(1),
            snapshot.Metadata.PreviousMachineSessionEndedAt);
        Assert.Equal(thirdStart,
            snapshot.Metadata.CurrentSessionStartedAt);
    }

    [Fact]
    public async Task UnexpectedTerminationRestoresOnlyLastAtomicSnapshot()
    {
        var start = CreateLocalTime(2026, 1, 1, 3);
        var store = new MutableMemoryStore();
        var interrupted = new MachineLearningService(start);
        interrupted.Observe(CreateObservation(start));
        interrupted.Observe(CreateObservation(start.AddSeconds(30)));
        await interrupted.SaveIfDueAsync(
            store,
            start.AddMinutes(1),
            force: true);

        interrupted.Observe(CreateObservation(start.AddSeconds(60)));
        interrupted.Observe(CreateObservation(start.AddSeconds(90)));

        var restartedAt = start.AddDays(1);
        var restarted = new MachineLearningService(restartedAt);
        await restarted.LoadAsync(store);

        var persistedEpisode = Assert.Single(restarted.RecentEpisodes);
        Assert.Equal(2, persistedEpisode.SampleCount);
        Assert.Equal("Session interrupted", persistedEpisode.Outcome);
        Assert.Equal(start.AddSeconds(30), persistedEpisode.EndedAt);
        Assert.Equal(2,
            restarted.GetDashboardSnapshot(restartedAt).ObservationCount);

        restarted.Observe(CreateObservation(restartedAt));
        var continued = restarted.GetDashboardSnapshot(restartedAt);
        Assert.Equal(3, continued.ObservationCount);
        Assert.Equal(3, Assert.Single(continued.Baselines).SampleCount);
        Assert.Equal(TimeSpan.FromSeconds(90), continued.ObservedDuration);
    }

    [Fact]
    public async Task VersionOneAndTwoMigrateAdaptiveEvidenceIntoSchemaThree()
    {
        var start = CreateLocalTime(2026, 1, 1, 3);
        var legacyBaseline = new MachineLearningBaselineState(
            start.ToLocalTime().Hour,
            MachineUserActivityState.Active,
            137,
            20,
            1_360,
            50,
            544,
            start,
            start.AddDays(4),
            [
                DateOnly.FromDateTime(start.ToLocalTime().DateTime),
                DateOnly.FromDateTime(start.AddDays(1).ToLocalTime().DateTime),
                DateOnly.FromDateTime(start.AddDays(2).ToLocalTime().DateTime),
                DateOnly.FromDateTime(start.AddDays(3).ToLocalTime().DateTime),
                DateOnly.FromDateTime(start.AddDays(4).ToLocalTime().DateTime)
            ]);

        foreach (var version in new[]
        {
            MachineLearningService.LegacyPersistenceSchemaVersion,
            MachineLearningService.VersionTwoPersistenceSchemaVersion
        })
        {
            var versionedBaseline = version ==
                    MachineLearningService.VersionTwoPersistenceSchemaVersion
                ? legacyBaseline with
                {
                    NetworkQuietSampleCount = 100,
                    NetworkUnavailableSampleCount = 37
                }
                : legacyBaseline;
            var legacy = new MachineLearningPersistedState(
                version,
                [versionedBaseline],
                [],
                137,
                start,
                start.AddDays(4));
            var store = new MutableMemoryStore(legacy);
            var service = new MachineLearningService(start.AddDays(5));

            await service.LoadAsync(store);

            var migrated = Assert.Single(service.Baselines);
            Assert.Equal(137, migrated.AdaptiveSampleCount);
            Assert.Equal(20, migrated.AdaptiveCpuMean);
            Assert.Equal(migrated.CpuStandardDeviation,
                migrated.AdaptiveCpuStandardDeviation, 8);
            Assert.Equal(5, migrated.ObservedDayCount);
            Assert.Equal(version ==
                    MachineLearningService.VersionTwoPersistenceSchemaVersion
                    ? 100
                    : 0,
                migrated.NetworkQuietSampleCount);
            Assert.Single(service.ContextProfiles);
            Assert.Equal(2, service.GetDashboardSnapshot(
                start.AddDays(5)).Metadata.LifetimeMachineSessionCount);

            await service.SaveIfDueAsync(
                store,
                start.AddDays(5),
                force: true);
            Assert.Equal(MachineLearningService.PersistenceSchemaVersion,
                store.SavedState!.SchemaVersion);
            var state = Assert.Single(store.SavedState.Baselines);
            Assert.Null(state.ObservedLocalDates);
            Assert.Equal(5, state.ObservedDayCount);
            Assert.NotNull(state.AdaptiveLastUpdatedAt);
        }
    }

    [Fact]
    public async Task VersionFourRoundTripPreservesProfilesPatternsAndMetadata()
    {
        var start = CreateLocalTime(2026, 1, 1, 2);
        var baselines = Enumerable.Range(0, 3).Select(offset =>
            CreateBaselineState(
                start.AddHours(offset),
                sampleCount: 168,
                observedDayCount: 7,
                network: MachineNetworkActivityClass.Light)).ToArray();
        var sourceState = new MachineLearningPersistedState(
            MachineLearningService.PersistenceSchemaVersion,
            baselines,
            [],
            504,
            start,
            start.AddDays(6).AddHours(2),
            start.AddDays(6).AddHours(3),
            504 * MachineLearningService.ObservationInterval.Ticks,
            Metadata: new MachineLearningMetadataState(
                504,
                504 * MachineLearningService.ObservationInterval.Ticks,
                2,
                start,
                start.AddDays(6).AddHours(2),
                start,
                start.AddDays(-1),
                start.AddDays(6).AddHours(3)),
            ContextProfiles: [],
            BroaderPatterns: []);
        var store = new MutableMemoryStore(sourceState);
        var loaded = new MachineLearningService(start.AddDays(7));
        await loaded.LoadAsync(store);

        Assert.Equal(3, loaded.ContextProfiles.Count);
        Assert.Single(loaded.BroaderPatterns);
        Assert.Equal(MachineLearningConfidence.Established,
            loaded.BroaderPatterns[0].Confidence);

        await loaded.SaveIfDueAsync(
            store,
            start.AddDays(7),
            force: true);
        var serialized = JsonSerializer.Serialize(store.SavedState);
        var roundTrippedState = JsonSerializer.Deserialize<
            MachineLearningPersistedState>(serialized)!;
        var restored = new MachineLearningService(start.AddDays(8));
        await restored.LoadAsync(new MutableMemoryStore(roundTrippedState));

        var snapshot = restored.GetDashboardSnapshot(start.AddDays(8));
        Assert.Equal(3, snapshot.Baselines.Count);
        Assert.Equal(3, snapshot.ContextProfiles.Count);
        Assert.Single(snapshot.BroaderPatterns);
        Assert.Equal(4, snapshot.Metadata.LifetimeMachineSessionCount);
        Assert.Equal(504,
            snapshot.Metadata.LifetimeAcceptedObservationCount);
        Assert.Equal(MachineLearningService.PersistenceSchemaVersion,
            snapshot.Metadata.PersistedSchemaVersion);
    }

    [Fact]
    public async Task CorruptVersionFourCollectionsRecoverWithoutCrashing()
    {
        var start = CreateLocalTime(2026, 1, 1, 3);
        var baseline = CreateBaselineState(
            start,
            sampleCount: 12,
            observedDayCount: 1,
            network: MachineNetworkActivityClass.Unavailable);
        var durationTicks = 12 *
            MachineLearningService.ObservationInterval.Ticks;
        var state = new MachineLearningPersistedState(
            MachineLearningService.PersistenceSchemaVersion,
            [baseline],
            [],
            12,
            start,
            start,
            start,
            durationTicks,
            Metadata: new MachineLearningMetadataState(
                12,
                durationTicks,
                1,
                start,
                start,
                start,
                null,
                start),
            ContextProfiles: null,
            BroaderPatterns: null);
        var store = new MutableMemoryStore(state);
        var service = new MachineLearningService(start.AddDays(1));

        await service.LoadAsync(store);

        Assert.Equal(MachineLearningDataHealth.RecoveredFromCorruptState,
            service.DataHealth);
        Assert.Equal(12,
            service.GetDashboardSnapshot(start.AddDays(1)).ObservationCount);
        Assert.Single(service.Baselines);
        Assert.Single(service.ContextProfiles);

        Assert.True(await service.SaveIfDueAsync(
            store,
            start.AddDays(1),
            force: true));
        Assert.NotNull(store.SavedState!.ContextProfiles);
        Assert.NotNull(store.SavedState.BroaderPatterns);
    }

    [Fact]
    public async Task MillionObservationAggregateKeepsSerializedStateBounded()
    {
        var start = CreateLocalTime(2025, 1, 1, 3);
        var initialCount = 999_999L;
        var baseline = CreateBaselineState(
            start,
            initialCount,
            observedDayCount: 365,
            network: MachineNetworkActivityClass.Quiet);
        var source = new MachineLearningPersistedState(
            MachineLearningService.PersistenceSchemaVersion,
            [baseline],
            [],
            initialCount,
            start.AddYears(-1),
            start,
            start,
            initialCount * MachineLearningService.ObservationInterval.Ticks,
            Metadata: new MachineLearningMetadataState(
                initialCount,
                initialCount *
                    MachineLearningService.ObservationInterval.Ticks,
                10,
                start.AddYears(-1),
                start,
                start,
                start.AddDays(-1),
                start),
            ContextProfiles: [],
            BroaderPatterns: []);
        var store = new MutableMemoryStore(source);
        var observationAt = start.AddDays(21);
        var service = new MachineLearningService(observationAt);
        await service.LoadAsync(store);

        service.Observe(CreateObservation(
            observationAt,
            cpu: 80,
            network: MachineNetworkActivityClass.Quiet));
        await service.SaveFinalSnapshotAsync(store, observationAt.AddMinutes(1));

        var persisted = store.SavedState!;
        var json = JsonSerializer.Serialize(persisted);
        Assert.Equal(1_000_000,
            persisted.Metadata!.LifetimeAcceptedObservationCount);
        Assert.Single(persisted.Baselines);
        Assert.Single(persisted.ContextProfiles!);
        Assert.Empty(persisted.BroaderPatterns!);
        Assert.Single(persisted.Episodes);
        Assert.Null(Assert.Single(persisted.Baselines).ObservedLocalDates);
        Assert.DoesNotContain("ContextFingerprint", json,
            StringComparison.Ordinal);
        Assert.DoesNotContain("RawObservation", json,
            StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(json) < 25_000);
        Assert.True(service.Journal.Count <=
            MachineLearningService.MaximumObservationCount);
        Assert.True(persisted.ContextProfiles!.Count <=
            MachineLearningService.MaximumContextProfileCount);
        Assert.True(persisted.BroaderPatterns!.Count <=
            MachineLearningPolicy.MaximumPatternCount);
        Assert.True(persisted.Episodes.Count <=
            MachineLearningService.MaximumEpisodeCount);
    }

    [Fact]
    public async Task FileStoreUsesAtomicTemporaryReplacementForSchemaFour()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileMachineLearningStore(directory);
            var start = DateTimeOffset.UnixEpoch;
            var state = new MachineLearningPersistedState(
                MachineLearningService.PersistenceSchemaVersion,
                [],
                [],
                0,
                null,
                null,
                start,
                Metadata: new MachineLearningMetadataState(
                    0,
                    0,
                    1,
                    null,
                    null,
                    start,
                    null,
                    start),
                ContextProfiles: [],
                BroaderPatterns: []);

            await store.SaveAsync(state);
            await store.SaveAsync(state with
            {
                PersistedAt = start.AddMinutes(1)
            });

            Assert.True(File.Exists(Path.Combine(
                directory,
                "learning-state.json")));
            Assert.False(File.Exists(Path.Combine(
                directory,
                "learning-state.json.tmp")));
            var restored = await store.LoadAsync();
            Assert.Equal(start.AddMinutes(1), restored!.PersistedAt);
            Assert.Equal(MachineLearningStoreLoadStatus.Loaded,
                store.LastLoadStatus);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static MachineLearningObservation CreateObservation(
        DateTimeOffset timestamp,
        double cpu = 20,
        double memory = 50,
        MachineNetworkActivityClass network =
            MachineNetworkActivityClass.Unavailable) => new(
            timestamp,
            cpu,
            memory,
            MachineUserActivityState.Active,
            MachineOverallState.Stable,
            [],
            40,
            "stable",
            network);

    private static MachineLearningBaselineState CreateBaselineState(
        DateTimeOffset timestamp,
        long sampleCount,
        int observedDayCount,
        MachineNetworkActivityClass network)
    {
        var quiet = network == MachineNetworkActivityClass.Quiet
            ? sampleCount
            : 0;
        var light = network == MachineNetworkActivityClass.Light
            ? sampleCount
            : 0;
        var active = network == MachineNetworkActivityClass.Active
            ? sampleCount
            : 0;
        var unavailable = network == MachineNetworkActivityClass.Unavailable
            ? sampleCount
            : 0;
        return new MachineLearningBaselineState(
            timestamp.ToLocalTime().Hour,
            MachineUserActivityState.Active,
            sampleCount,
            20,
            sampleCount * 4,
            50,
            sampleCount * 4,
            timestamp.AddDays(-(observedDayCount - 1)),
            timestamp,
            NetworkQuietSampleCount: quiet,
            NetworkLightSampleCount: light,
            NetworkActiveSampleCount: active,
            NetworkUnavailableSampleCount: unavailable,
            ObservedDayCount: observedDayCount,
            LastObservedLocalDate: DateOnly.FromDateTime(
                timestamp.ToLocalTime().DateTime),
            ObservedDurationTicks: sampleCount *
                MachineLearningService.ObservationInterval.Ticks,
            AdaptiveCpuMean: 20,
            AdaptiveCpuVariance: 4,
            AdaptiveMemoryMean: 50,
            AdaptiveMemoryVariance: 4,
            AdaptiveSampleCount: sampleCount,
            AdaptiveLastUpdatedAt: timestamp);
    }

    private static DateTimeOffset CreateLocalTime(
        int year,
        int month,
        int day,
        int hour)
    {
        var local = new DateTime(
            year,
            month,
            day,
            hour,
            0,
            0,
            DateTimeKind.Unspecified);
        return new DateTimeOffset(
            local,
            TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private sealed class MutableMemoryStore : IMachineLearningStore
    {
        public MutableMemoryStore(MachineLearningPersistedState? state = null)
        {
            SavedState = state;
        }

        public MachineLearningPersistedState? SavedState { get; private set; }

        public Task<MachineLearningPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedState);

        public Task SaveAsync(
            MachineLearningPersistedState state,
            CancellationToken cancellationToken = default)
        {
            SavedState = state;
            return Task.CompletedTask;
        }
    }
}
