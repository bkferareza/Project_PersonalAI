using Machine.Core;

namespace Machine.Tests;

public sealed class MachineLearningServiceTests
{
    [Fact]
    public void ObserveThrottlesAndEvictsRawJournal()
    {
        var service = new MachineLearningService();
        var start = DateTimeOffset.UnixEpoch;

        Assert.True(service.Observe(CreateObservation(start)));
        Assert.False(service.Observe(CreateObservation(start.AddSeconds(29))));

        for (var index = 1;
             index <= MachineLearningService.MaximumObservationCount;
             index++)
        {
            Assert.True(service.Observe(CreateObservation(
                start.AddSeconds(index * 30))));
        }

        Assert.Equal(MachineLearningService.MaximumObservationCount,
            service.Journal.Count);
        Assert.Equal(start.AddSeconds(30), service.Journal[0].Timestamp);
    }

    [Fact]
    public void LearnsIndependentWelfordBaselinesByHourAndActivity()
    {
        var service = new MachineLearningService();
        var start = new DateTimeOffset(2026, 1, 1, 3, 0, 0,
            TimeSpan.Zero);
        service.Observe(CreateObservation(start, cpu: 10, memory: 40));
        service.Observe(CreateObservation(start.AddSeconds(30), cpu: 20,
            memory: 50));
        service.Observe(CreateObservation(start.AddSeconds(60), cpu: 30,
            memory: 60));
        var originalBucket = service.GetDashboardSnapshot(
            start.AddSeconds(60)).CurrentBaseline!;
        Assert.Equal(20d, originalBucket.CpuMean, 3);
        Assert.Equal(10d, originalBucket.CpuStandardDeviation, 3);
        Assert.Equal(50d, originalBucket.MemoryMean, 3);
        service.Observe(CreateObservation(start.AddHours(1), cpu: 90,
            memory: 90));
        service.Observe(CreateObservation(start.AddHours(1).AddSeconds(30),
            activity: MachineUserActivityState.Idle, cpu: 70, memory: 70));

        var snapshot = service.GetDashboardSnapshot(
            start.AddHours(1).AddSeconds(30));
        Assert.Equal(70d, snapshot.CurrentBaseline!.CpuMean, 3);
        Assert.Equal(1, snapshot.CurrentBaseline.SampleCount);

    }

    [Fact]
    public void ConfidenceRequiresSamplesAcrossDistinctDaysForEstablishedContext()
    {
        var service = new MachineLearningService();
        var start = new DateTimeOffset(2026, 1, 1, 3, 0, 0,
            TimeSpan.Zero);

        for (var index = 0; index < 12; index++)
        {
            service.Observe(CreateObservation(start.AddDays(index)));
        }
        Assert.Equal(MachineLearningConfidence.Provisional,
            service.GetDashboardSnapshot(start.AddDays(11))
                .CurrentBaseline!.Confidence);
        Assert.Null(service.GetLearnedContext());

        for (var index = 12; index < 168; index++)
        {
            service.Observe(CreateObservation(start.AddDays(index)));
        }
        Assert.Equal(MachineLearningConfidence.Established,
            service.GetDashboardSnapshot(start.AddDays(167))
                .CurrentBaseline!.Confidence);
        Assert.NotNull(service.GetLearnedContext());
    }

    [Fact]
    public void EpisodesAggregateContextChangesAndRemainBounded()
    {
        var service = new MachineLearningService();
        var start = DateTimeOffset.UnixEpoch;
        service.Observe(CreateObservation(start, cpu: 10, memory: 40));
        service.Observe(CreateObservation(start.AddSeconds(30), cpu: 20,
            memory: 60));
        service.Observe(CreateObservation(start.AddSeconds(60),
            activity: MachineUserActivityState.Idle, cpu: 30, memory: 80));

        var episode = Assert.Single(service.RecentEpisodes);
        Assert.Equal(2, episode.SampleCount);
        Assert.Equal(15d, episode.AverageCpuUsagePercent, 3);
        Assert.Equal(20d, episode.PeakCpuUsagePercent, 3);

        for (var index = 3; index < 205; index++)
        {
            service.Observe(CreateObservation(start.AddSeconds(index * 30),
                activity: index % 2 == 0
                    ? MachineUserActivityState.Active
                    : MachineUserActivityState.Idle,
                state: index % 2 == 0
                    ? MachineOverallState.Stable
                    : MachineOverallState.Attention));
        }
        Assert.Equal(MachineLearningService.MaximumEpisodeCount,
            service.RecentEpisodes.Count);
    }

    [Fact]
    public async Task PersistsAggregatesButNotRawJournalAndRecoversFromCorruption()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileMachineLearningStore(directory);
            var service = new MachineLearningService();
            var start = DateTimeOffset.UnixEpoch;
            service.Observe(CreateObservation(start));
            service.Observe(CreateObservation(start.AddSeconds(30),
                activity: MachineUserActivityState.Idle));
            Assert.True(await service.SaveIfDueAsync(store, start.AddMinutes(1),
                force: true));
            var persistedJson = await File.ReadAllTextAsync(Path.Combine(
                directory,
                "learning-state.json"));
            Assert.DoesNotContain("ContextFingerprint", persistedJson,
                StringComparison.Ordinal);
            Assert.DoesNotContain("SystemVolumeFreePercent", persistedJson,
                StringComparison.Ordinal);
            Assert.DoesNotContain("Interface", persistedJson,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("IPAddress", persistedJson,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("MacAddress", persistedJson,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("RemoteEndpoint", persistedJson,
                StringComparison.OrdinalIgnoreCase);

            var restored = new MachineLearningService();
            await restored.LoadAsync(store);
            Assert.Empty(restored.Journal);
            Assert.Equal(2, restored.RecentEpisodes.Count);
            Assert.Equal(2, restored.GetDashboardSnapshot(start).ObservationCount);
            Assert.Equal(start.AddMinutes(1), restored.LastPersistedAt);
            Assert.All(restored.Baselines,
                baseline => Assert.Equal(1, baseline.ObservedDayCount));
            Assert.Equal(MachineLearningDataHealth.Healthy,
                restored.DataHealth);

            await File.WriteAllTextAsync(Path.Combine(directory,
                "learning-state.json"), "not json");
            var corrupted = new MachineLearningService();
            await corrupted.LoadAsync(store);
            Assert.Equal(0, corrupted.GetDashboardSnapshot(start).ObservationCount);
            Assert.Equal(MachineLearningDataHealth.RecoveredFromCorruptState,
                corrupted.DataHealth);
            corrupted.Observe(CreateObservation(start));
            Assert.True(await corrupted.SaveIfDueAsync(
                store,
                start,
                force: true));
            Assert.Equal(MachineLearningDataHealth.RecoveredFromCorruptState,
                corrupted.DataHealth);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task IgnoresUnknownSchemaAndThrottlesSaves()
    {
        var store = new RecordingStore(new MachineLearningPersistedState(
            99, [], [], 99, null, null));
        var service = new MachineLearningService();
        await service.LoadAsync(store);
        Assert.Equal(0, service.GetDashboardSnapshot(DateTimeOffset.UtcNow)
            .ObservationCount);
        Assert.Equal(MachineLearningDataHealth.RecoveredFromCorruptState,
            service.DataHealth);

        var now = DateTimeOffset.UnixEpoch;
        service.Observe(CreateObservation(now));
        Assert.True(await service.SaveIfDueAsync(store, now, force: true));
        service.Observe(CreateObservation(now.AddSeconds(30)));
        Assert.False(await service.SaveIfDueAsync(store, now.AddMinutes(1)));
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void SchedulerAdmissionTracksAcceptedThrottledAndMissingPrerequisites()
    {
        var service = new MachineLearningService();
        var start = DateTimeOffset.UnixEpoch;

        Assert.True(service.TryBeginObservationAttempt(start));
        service.RecordMissingPrerequisite();
        Assert.False(service.TryBeginObservationAttempt(
            start.AddSeconds(29)));
        Assert.True(service.TryBeginObservationAttempt(
            start.AddSeconds(30)));
        Assert.True(service.Observe(CreateObservation(
            start.AddSeconds(30))));

        var diagnostics = service.GetDashboardSnapshot(start).Diagnostics;
        Assert.Equal(1, diagnostics.AcceptedObservationCount);
        Assert.Equal(1, diagnostics.ThrottledObservationCount);
        Assert.Equal(1, diagnostics.MissingPrerequisiteCount);
        Assert.Equal(start.AddSeconds(30),
            diagnostics.LastAcceptedObservationAt);
    }

    [Fact]
    public void LearningContinuesAcrossDashboardAndAmbientPresentation()
    {
        var service = new MachineLearningService();
        var interaction = new Machine.App.CompactPresenceInteraction();
        var start = DateTimeOffset.UnixEpoch;

        Assert.True(service.Observe(CreateObservation(start)));
        Assert.True(interaction.OpenDashboard());
        Assert.True(service.Observe(CreateObservation(start.AddSeconds(30))));
        Assert.True(interaction.CloseDashboard());
        Assert.True(service.Observe(CreateObservation(
            start.AddSeconds(60),
            activity: MachineUserActivityState.Idle)));

        Assert.Equal(3,
            service.GetDashboardSnapshot(start.AddMinutes(1))
                .ObservationCount);
    }

    [Fact]
    public void ActivityHourAndDateChangesCloseEpisodesDeterministically()
    {
        var service = new MachineLearningService();
        var start = CreateLocalTime(2026, 8, 11, 5, 59, 30);

        service.Observe(CreateObservation(start));
        service.Observe(CreateObservation(start.AddSeconds(30)));
        service.Observe(CreateObservation(
            start.AddSeconds(60),
            activity: MachineUserActivityState.Idle));
        service.Observe(CreateObservation(start.AddSeconds(90)));

        Assert.Equal(3, service.RecentEpisodes.Count);
        Assert.Equal(MachineUserActivityState.Active,
            service.RecentEpisodes[0].ActivityState);
        Assert.Equal(MachineUserActivityState.Idle,
            service.RecentEpisodes[2].ActivityState);
        Assert.Contains(service.Baselines,
            baseline => baseline.LocalHour == 5 &&
                baseline.ActivityState == MachineUserActivityState.Active);
        Assert.Contains(service.Baselines,
            baseline => baseline.LocalHour == 6 &&
                baseline.ActivityState == MachineUserActivityState.Idle);

        var midnightService = new MachineLearningService();
        var midnight = CreateLocalTime(2026, 8, 11, 23, 59, 30);
        midnightService.Observe(CreateObservation(midnight));
        midnightService.Observe(CreateObservation(midnight.AddSeconds(30)));
        Assert.Single(midnightService.RecentEpisodes);
        Assert.Equal(midnight.AddSeconds(30),
            midnightService.RecentEpisodes[0].EndedAt);
    }

    [Fact]
    public void ObservedDaysAreUniqueAndEstablishedNeedsSevenDistinctDays()
    {
        var provisional = new MachineLearningService();
        var start = CreateLocalTime(2026, 8, 1, 5, 0, 0);
        for (var index = 0; index < 12; index++)
        {
            provisional.Observe(CreateObservation(
                start.AddSeconds(index * 30)));
        }

        var provisionalBaseline = Assert.Single(provisional.Baselines);
        Assert.Equal(1, provisionalBaseline.ObservedDayCount);
        Assert.Equal(MachineLearningConfidence.Provisional,
            provisionalBaseline.Confidence);

        var established = new MachineLearningService();
        for (var day = 0; day < 7; day++)
        {
            for (var sample = 0; sample < 24; sample++)
            {
                established.Observe(CreateObservation(
                    start.AddDays(day).AddSeconds(sample * 30)));
            }
        }

        var establishedBaseline = Assert.Single(established.Baselines);
        Assert.Equal(168, establishedBaseline.SampleCount);
        Assert.Equal(7, establishedBaseline.ObservedDayCount);
        Assert.Equal(MachineLearningConfidence.Established,
            establishedBaseline.Confidence);
    }

    [Fact]
    public void ContinuousCadenceProducesExpectedCountsAndObservedDuration()
    {
        var service = new MachineLearningService();
        var start = DateTimeOffset.UnixEpoch;
        for (var index = 0; index <= 60; index++)
        {
            Assert.True(service.Observe(CreateObservation(
                start.AddSeconds(index * 30))));
        }

        var snapshot = service.GetDashboardSnapshot(start.AddMinutes(30));
        Assert.Equal(61, snapshot.ObservationCount);
        Assert.Equal(TimeSpan.FromMinutes(30.5), snapshot.ObservedDuration);
        Assert.Equal(61, snapshot.RawObservationCount);
    }

    [Fact]
    public void LearnedItemsAreEvidenceBoundedAndAvoidUnsupportedClaims()
    {
        var service = new MachineLearningService();
        var start = CreateLocalTime(2026, 8, 11, 5, 0, 0);
        for (var index = 0; index < 12; index++)
        {
            service.Observe(CreateObservation(
                start.AddSeconds(index * 30),
                cpu: 40 + index,
                memory: 52));
        }

        var items = service.GetDashboardSnapshot(start).LearnedItems;
        Assert.Equal(2, items.Count);
        Assert.All(items, item =>
        {
            Assert.Equal(12, item.EvidenceCount);
            Assert.True(item.IsEarlyObservation);
            Assert.Equal(MachineLearningConfidence.Provisional,
                item.Confidence);
            Assert.Contains("12 samples", item.Text);
            Assert.DoesNotContain("anomal", item.Text,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("productiv", item.Text,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("foreground", item.Text,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("recommend", item.Text,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void NetworkClassIsStoredAndDominanceRequiresEnoughEvidence()
    {
        var service = new MachineLearningService();
        var start = CreateLocalTime(2026, 8, 11, 6, 0, 0);
        for (var index = 0; index < 12; index++)
        {
            var networkClass = index < 8
                ? MachineNetworkActivityClass.Quiet
                : MachineNetworkActivityClass.Light;
            service.Observe(CreateObservation(
                start.AddSeconds(index * 30),
                activity: MachineUserActivityState.Idle,
                networkActivityClass: networkClass,
                receiveBytesPerSecond: 1_000,
                sendBytesPerSecond: 500));
        }

        var snapshot = service.GetDashboardSnapshot(start.AddMinutes(6));
        var baseline = Assert.Single(snapshot.Baselines);
        Assert.Equal(MachineNetworkActivityClass.Light,
            snapshot.CurrentObservation!.NetworkActivityClass);
        Assert.Equal(1_000,
            snapshot.CurrentObservation.ReceiveBytesPerSecond);
        Assert.Equal(8, baseline.NetworkQuietSampleCount);
        Assert.Equal(4, baseline.NetworkLightSampleCount);
        Assert.Equal(12, baseline.NetworkObservationCount);
        Assert.Equal(MachineNetworkActivityClass.Quiet,
            baseline.DominantNetworkActivityClass);
        Assert.Equal(8, baseline.DominantNetworkActivityCount);

        var learnedNetworkItem = Assert.Single(
            snapshot.LearnedItems,
            item => item.Text.Contains(
                "network activity",
                StringComparison.Ordinal));
        Assert.Equal(12, learnedNetworkItem.EvidenceCount);
        Assert.Contains(
            "8 of 12 Idle observations at 6 AM had Quiet network activity.",
            learnedNetworkItem.Text,
            StringComparison.Ordinal);

        var calibrating = new MachineLearningService();
        for (var index = 0; index < 12; index++)
        {
            calibrating.Observe(CreateObservation(
                start.AddSeconds(index * 30),
                networkActivityClass: index == 11
                    ? MachineNetworkActivityClass.Unavailable
                    : MachineNetworkActivityClass.Quiet));
        }
        var calibratingBaseline = Assert.Single(calibrating.Baselines);
        Assert.Equal(11, calibratingBaseline.NetworkObservationCount);
        Assert.Null(calibratingBaseline.DominantNetworkActivityClass);
        Assert.DoesNotContain(
            calibrating.GetDashboardSnapshot(start).LearnedItems,
            item => item.Text.Contains(
                "network activity",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task NetworkCountsRoundTripAndVersionOneStateMigratesSafely()
    {
        var start = CreateLocalTime(2026, 8, 11, 6, 0, 0);
        var store = new RecordingStore(null);
        var service = new MachineLearningService();
        for (var index = 0; index < 12; index++)
        {
            service.Observe(CreateObservation(
                start.AddSeconds(index * 30),
                networkActivityClass: MachineNetworkActivityClass.Quiet));
        }

        Assert.True(await service.SaveIfDueAsync(
            store,
            start.AddMinutes(6),
            force: true));
        Assert.Equal(MachineLearningService.PersistenceSchemaVersion,
            store.SavedState!.SchemaVersion);
        Assert.Equal(12,
            Assert.Single(store.SavedState.Baselines)
                .NetworkQuietSampleCount);

        var restored = new MachineLearningService();
        await restored.LoadAsync(new RecordingStore(store.SavedState));
        var restoredBaseline = Assert.Single(restored.Baselines);
        Assert.Equal(12, restoredBaseline.NetworkQuietSampleCount);
        Assert.Equal(MachineNetworkActivityClass.Quiet,
            restoredBaseline.DominantNetworkActivityClass);

        var legacyBaseline = new MachineLearningBaselineState(
            LocalHour: 6,
            ActivityState: MachineUserActivityState.Active,
            SampleCount: 12,
            CpuMean: 20,
            CpuM2: 0,
            MemoryMean: 50,
            MemoryM2: 0,
            FirstObservedAt: start,
            LastObservedAt: start.AddMinutes(6),
            ObservedLocalDates: [DateOnly.FromDateTime(start.DateTime)]);
        var legacy = new MachineLearningPersistedState(
            MachineLearningService.LegacyPersistenceSchemaVersion,
            [legacyBaseline],
            [],
            12,
            start,
            start.AddMinutes(6));
        var migrated = new MachineLearningService();

        await migrated.LoadAsync(new RecordingStore(legacy));

        Assert.Equal(MachineLearningDataHealth.Healthy, migrated.DataHealth);
        Assert.Equal(0, Assert.Single(migrated.Baselines)
            .NetworkObservationCount);
    }

    [Fact]
    public async Task VersionOneJsonWithoutNetworkFieldsLoadsSafely()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string legacyJson = """
                {
                  "SchemaVersion": 1,
                  "Baselines": [
                    {
                      "LocalHour": 0,
                      "ActivityState": 0,
                      "SampleCount": 12,
                      "CpuMean": 20,
                      "CpuM2": 0,
                      "MemoryMean": 50,
                      "MemoryM2": 0,
                      "FirstObservedAt": "1970-01-01T00:00:00+00:00",
                      "LastObservedAt": "1970-01-01T00:06:00+00:00"
                    }
                  ],
                  "Episodes": [],
                  "ObservationCount": 12,
                  "FirstObservedAt": "1970-01-01T00:00:00+00:00",
                  "LastObservedAt": "1970-01-01T00:06:00+00:00"
                }
                """;
            await File.WriteAllTextAsync(
                Path.Combine(directory, "learning-state.json"),
                legacyJson);
            var service = new MachineLearningService();

            await service.LoadAsync(new FileMachineLearningStore(directory));

            Assert.Equal(MachineLearningDataHealth.Healthy, service.DataHealth);
            var baseline = Assert.Single(service.Baselines);
            Assert.Equal(12, baseline.SampleCount);
            Assert.Equal(0, baseline.NetworkObservationCount);
            Assert.Null(baseline.DominantNetworkActivityClass);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EpisodeProjectionIsNewestFirstAndBoundedToFifty()
    {
        var start = DateTimeOffset.UnixEpoch;
        var episodes = Enumerable.Range(0, 60).Select(index =>
            new MachineLearningEpisode(
                start.AddMinutes(index),
                start.AddMinutes(index + 1),
                MachineUserActivityState.Idle,
                MachineOverallState.Stable,
                2,
                5,
                8,
                50,
                [],
                null)).ToArray();

        var projected = MachineLearningEpisodeProjector.Project(episodes);

        Assert.Equal(50, projected.Count);
        Assert.Equal(episodes[^1], projected[0]);
        Assert.Equal(episodes[10], projected[^1]);
    }

    [Fact]
    public async Task PersistenceTracksDirtyTimestampIntervalAndHealth()
    {
        var store = new RecordingStore(null);
        var service = new MachineLearningService();
        var now = DateTimeOffset.UnixEpoch;
        service.Observe(CreateObservation(now));

        Assert.True(service.IsDirty);
        Assert.True(await service.SaveIfDueAsync(store, now, force: true));
        Assert.False(service.IsDirty);
        Assert.Equal(now, service.LastPersistedAt);
        Assert.Equal(MachineLearningDataHealth.Healthy, service.DataHealth);
        Assert.False(await service.SaveIfDueAsync(
            store,
            now.AddMinutes(1)));
        Assert.Equal(1, store.SaveCount);

        service.Observe(CreateObservation(now.AddSeconds(30)));
        Assert.False(await service.SaveIfDueAsync(
            store,
            now.AddMinutes(9)));
        Assert.True(await service.SaveIfDueAsync(
            store,
            now.AddMinutes(10)));
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task PersistenceFailureKeepsLearningDirtyAndBacksOffRetries()
    {
        var store = new FailingRecordingStore();
        var service = new MachineLearningService();
        var now = DateTimeOffset.UnixEpoch;
        service.Observe(CreateObservation(now));

        Assert.False(await service.SaveIfDueAsync(store, now));
        Assert.True(service.IsDirty);
        Assert.Equal(
            MachineLearningDataHealth.PersistenceTemporarilyUnavailable,
            service.DataHealth);
        Assert.False(await service.SaveIfDueAsync(
            store,
            now.AddMinutes(4)));
        Assert.Equal(1, store.SaveCount);
        Assert.False(await service.SaveIfDueAsync(
            store,
            now.AddMinutes(5)));
        Assert.Equal(2, store.SaveCount);
        Assert.True(service.Observe(CreateObservation(
            now.AddSeconds(30))));
        Assert.Equal(2,
            service.GetDashboardSnapshot(now).ObservationCount);
    }

    [Fact]
    public async Task PeriodicSnapshotPreservesTheActiveAggregateAcrossRestart()
    {
        var store = new RecordingStore(null);
        var service = new MachineLearningService();
        var start = DateTimeOffset.UnixEpoch;
        service.Observe(CreateObservation(start, cpu: 10));
        service.Observe(CreateObservation(start.AddSeconds(30), cpu: 20));

        Assert.True(await service.SaveIfDueAsync(store, start, force: true));
        Assert.NotNull(store.SavedState?.ActiveEpisode);

        var restored = new MachineLearningService();
        await restored.LoadAsync(new RecordingStore(store.SavedState));

        var episode = Assert.Single(restored.RecentEpisodes);
        Assert.Equal(2, episode.SampleCount);
        Assert.Equal(15d, episode.AverageCpuUsagePercent, 3);
        Assert.Empty(restored.Journal);
        Assert.Equal(0,
            restored.GetDashboardSnapshot(start)
                .Diagnostics.AcceptedObservationCount);
    }

    private static MachineLearningObservation CreateObservation(
        DateTimeOffset timestamp,
        MachineUserActivityState activity = MachineUserActivityState.Active,
        MachineOverallState state = MachineOverallState.Stable,
        double cpu = 20,
        double memory = 50,
        MachineNetworkActivityClass networkActivityClass =
            MachineNetworkActivityClass.Unavailable,
        double? receiveBytesPerSecond = null,
        double? sendBytesPerSecond = null) => new(
            timestamp,
            cpu,
            memory,
            activity,
            state,
            [],
            40,
            $"{activity}:{state}",
            networkActivityClass,
            receiveBytesPerSecond,
            sendBytesPerSecond);

    private static DateTimeOffset CreateLocalTime(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second)
    {
        var local = new DateTime(
            year,
            month,
            day,
            hour,
            minute,
            second,
            DateTimeKind.Unspecified);
        return new DateTimeOffset(
            local,
            TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private sealed class RecordingStore(MachineLearningPersistedState? state)
        : IMachineLearningStore
    {
        public int SaveCount { get; private set; }
        public MachineLearningPersistedState? SavedState { get; private set; }
        public Task<MachineLearningPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(state);
        public Task SaveAsync(MachineLearningPersistedState persisted,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            SavedState = persisted;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingRecordingStore : IMachineLearningStore
    {
        public int SaveCount { get; private set; }

        public Task<MachineLearningPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MachineLearningPersistedState?>(null);

        public Task SaveAsync(
            MachineLearningPersistedState state,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromException(new IOException(
                "Simulated persistence failure."));
        }
    }
}
