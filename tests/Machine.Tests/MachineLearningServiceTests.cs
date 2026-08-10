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
    public void ConfidenceRequiresLongRangeForEstablishedContext()
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

            var restored = new MachineLearningService();
            await restored.LoadAsync(store);
            Assert.Empty(restored.Journal);
            Assert.Single(restored.RecentEpisodes);
            Assert.Equal(2, restored.GetDashboardSnapshot(start).ObservationCount);

            await File.WriteAllTextAsync(Path.Combine(directory,
                "learning-state.json"), "not json");
            var corrupted = new MachineLearningService();
            await corrupted.LoadAsync(store);
            Assert.Equal(0, corrupted.GetDashboardSnapshot(start).ObservationCount);
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

        var now = DateTimeOffset.UnixEpoch;
        service.Observe(CreateObservation(now));
        Assert.True(await service.SaveIfDueAsync(store, now, force: true));
        service.Observe(CreateObservation(now.AddSeconds(30)));
        Assert.False(await service.SaveIfDueAsync(store, now.AddMinutes(1)));
        Assert.Equal(1, store.SaveCount);
    }

    private static MachineLearningObservation CreateObservation(
        DateTimeOffset timestamp,
        MachineUserActivityState activity = MachineUserActivityState.Active,
        MachineOverallState state = MachineOverallState.Stable,
        double cpu = 20,
        double memory = 50) => new(timestamp, cpu, memory, activity, state,
            [], 40, $"{activity}:{state}");

    private sealed class RecordingStore(MachineLearningPersistedState? state)
        : IMachineLearningStore
    {
        public int SaveCount { get; private set; }
        public Task<MachineLearningPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(state);
        public Task SaveAsync(MachineLearningPersistedState persisted,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
