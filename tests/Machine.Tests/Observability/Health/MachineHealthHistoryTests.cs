using Machine.Core;

namespace Machine.Tests;

public sealed class MachineHealthHistoryTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-14T12:00:00Z");

    [Fact]
    public async Task HealthHistoryPersistsSeparatelyAcrossRestart()
    {
        var service = new MachineHealthHistoryService();
        var store = new RecordingHealthStore();
        var reliability = Reliability(
        [
            Incident(Now.AddHours(-1), "repeat.exe"),
            Incident(Now.AddHours(-2), "repeat.exe")
        ]);
        var reboot = MachineRebootPendingAggregator.Aggregate(
        [
            new(MachineRebootPendingReason.WindowsUpdate, true)
        ], Now);
        service.Observe(Update(), reboot, reliability, Now);

        Assert.True(await service.SaveIfDueAsync(
            store,
            Now,
            force: true));
        var restored = new MachineHealthHistoryService();
        await restored.LoadAsync(store);
        var snapshot = restored.GetSnapshot();

        Assert.Equal(2, snapshot.LifetimeObservedIncidentCount);
        Assert.Equal(MachineWindowsUpdateState.UpdatesAvailable,
            snapshot.WindowsUpdate?.State);
        Assert.True(snapshot.RebootPending?.IsPending);
        Assert.Equal("repeat.exe",
            Assert.Single(snapshot.Reliability!.Summary
                .RecurringApplications).ApplicationName);
        Assert.Equal(MachineHealthHistoryDataStatus.Healthy,
            snapshot.DataStatus);
        Assert.False(snapshot.IsDirty);
    }

    [Fact]
    public async Task SameIncidentIsNotCountedAgainAfterRestart()
    {
        var store = new RecordingHealthStore();
        var first = new MachineHealthHistoryService();
        var reliability = Reliability(
        [
            Incident(Now.AddHours(-1), "one.exe")
        ]);
        first.Observe(null, null, reliability, Now);
        await first.SaveIfDueAsync(store, Now, force: true);
        var restored = new MachineHealthHistoryService();
        await restored.LoadAsync(store);

        restored.Observe(null, null, reliability, Now.AddMinutes(10));

        Assert.Equal(
            1,
            restored.GetSnapshot().LifetimeObservedIncidentCount);
    }

    [Fact]
    public void IncidentMemoryIsBounded()
    {
        var incidents = Enumerable.Range(0, 150)
            .Select(index => Incident(
                Now.AddMinutes(-(index * 3)),
                $"app-{index}.exe"));
        var service = new MachineHealthHistoryService();

        service.Observe(null, null, Reliability(incidents), Now);

        var memory = service.GetSnapshot().Reliability;
        Assert.NotNull(memory);
        Assert.Equal(
            MachineHealthHistoryService.MaximumIncidentCount,
            memory.RecentIncidents.Count);
        Assert.Equal(
            MachineHealthHistoryService.MaximumIncidentCount,
            service.GetSnapshot().LifetimeObservedIncidentCount);
    }

    [Fact]
    public void HealthMemoryRetainsLastUnexpectedShutdownBeyondIncidentBound()
    {
        var shutdownAt = Now.AddDays(-20);
        var candidates = Enumerable.Range(0, 110)
            .Select(index => Incident(
                Now.AddMinutes(-(index * 3)),
                $"app-{index}.exe"))
            .Append(new MachineReliabilityIncident(
                shutdownAt,
                MachineReliabilityIncidentCategory.UnexpectedShutdown,
                MachineReliabilityIncidentSeverity.Significant,
                "EventLog",
                null,
                null,
                null,
                6008,
                "windows.unexpected-shutdown"));
        var service = new MachineHealthHistoryService();

        service.Observe(null, null, Reliability(candidates), Now);

        Assert.Equal(
            shutdownAt,
            service.GetSnapshot().Reliability?.LastUnexpectedShutdown);
    }

    [Fact]
    public async Task FilePersistenceContainsNoRawXmlOrPrivatePath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "MachineHealthTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var service = new MachineHealthHistoryService();
            var store = new FileMachineHealthHistoryStore(directory);
            service.Observe(
                null,
                null,
                Reliability(
                [
                    Incident(
                        Now.AddHours(-1),
                        "C:\\Users\\Person\\Documents\\private-app.exe")
                ]),
                Now);

            await service.SaveIfDueAsync(store, Now, force: true);
            var json = await File.ReadAllTextAsync(Path.Combine(
                directory,
                "health-history-v1.json"));

            Assert.Contains("private-app.exe", json);
            Assert.DoesNotContain("C:\\\\Users", json,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<Event", json,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", json,
                StringComparison.OrdinalIgnoreCase);
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
    public void HealthMemoryDoesNotAlterBehavioralBaselineDimensions()
    {
        var learning = new MachineLearningService(Now);
        learning.Observe(new MachineLearningObservation(
            Now,
            10,
            20,
            MachineUserActivityState.Active,
            MachineOverallState.Stable,
            [],
            null,
            "stable"));
        var health = new MachineHealthHistoryService();
        health.Observe(null, null, Reliability(
        [
            Incident(Now.AddHours(-1), "one.exe")
        ]), Now);

        var baseline = Assert.Single(learning.Baselines);
        Assert.Equal(Now.ToLocalTime().Hour, baseline.LocalHour);
        Assert.Equal(MachineUserActivityState.Active, baseline.ActivityState);
        Assert.Equal(48, MachineLearningService.MaximumContextProfileCount);
        Assert.Equal(1, health.GetSnapshot().LifetimeObservedIncidentCount);
    }

    [Fact]
    public void LearnedItemsUseVerifiedHealthSummaryWithoutCausation()
    {
        var service = new MachineHealthHistoryService();
        service.Observe(null, null, Reliability(
        [
            Incident(Now.AddHours(-1), "repeat.exe"),
            Incident(Now.AddHours(-2), "repeat.exe"),
            new MachineReliabilityIncident(
                Now.AddHours(-3),
                MachineReliabilityIncidentCategory.UnexpectedShutdown,
                MachineReliabilityIncidentSeverity.Significant,
                "EventLog",
                null,
                null,
                null,
                6008,
                "windows.unexpected-shutdown")
        ]), Now);

        var items = MachineHealthLearnedItemProjector.Project(
            service.GetSnapshot());

        Assert.Contains(items, item =>
            item.Layer == MachineLearningMemoryLayer.HealthHistory &&
            item.Text.Contains(
                "2 crashes or hangs of repeat.exe",
                StringComparison.Ordinal));
        Assert.Contains(items, item => item.Text.Contains(
            "unexpected shutdown",
            StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, item => item.Text.Contains(
            "cause",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PartialReliabilityNeverProjectsPositiveAbsenceClaim()
    {
        var service = new MachineHealthHistoryService();
        var reliability = MachineReliabilityAggregator.Aggregate(
            [],
            Now,
            MachineHealthDataStatus.Partial,
            readFailureCount: 1);
        service.Observe(null, null, reliability, Now);

        var items = MachineHealthLearnedItemProjector.Project(
            service.GetSnapshot());

        Assert.Contains(items, item => item.Text.Contains(
            "partially available",
            StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, item => item.Text.StartsWith(
            "No update failures",
            StringComparison.OrdinalIgnoreCase));
    }

    private static MachineWindowsUpdateSnapshot Update() => new(
        Now,
        Now,
        true,
        Now.AddHours(-1),
        Now.AddDays(-1),
        2,
        1,
        MachineWindowsUpdateState.UpdatesAvailable,
        [],
        MachineHealthDataStatus.Complete,
        MachineWindowsUpdateRefreshStatus.Verified);

    private static MachineReliabilitySnapshot Reliability(
        IEnumerable<MachineReliabilityIncident> incidents) =>
        MachineReliabilityAggregator.Aggregate(incidents, Now);

    private static MachineReliabilityIncident Incident(
        DateTimeOffset occurredAt,
        string applicationName) => new(
        occurredAt,
        MachineReliabilityIncidentCategory.ApplicationCrash,
        MachineReliabilityIncidentSeverity.Significant,
        "Application Error",
        applicationName,
        null,
        null,
        1000,
        "application.crash");

    private sealed class RecordingHealthStore : IMachineHealthHistoryStore
    {
        public MachineHealthHistoryPersistedState? State { get; private set; }

        public Task<MachineHealthHistoryPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(State);

        public Task SaveAsync(
            MachineHealthHistoryPersistedState state,
            CancellationToken cancellationToken = default)
        {
            State = state;
            return Task.CompletedTask;
        }
    }
}
