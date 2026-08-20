using Machine.Core;

namespace Machine.Tests;

public sealed class MachineLearningActivityLogTests
{
    [Fact]
    public async Task RestoreRegressionRecordsDiagnosticWithoutRepairingCount()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-4);
        var source = new MachineLearningService(start);
        for (var index = 0; index < 164; index++)
        {
            Assert.True(source.Observe(CreateObservation(start.AddSeconds(index * 30))));
        }

        var learningStore = new MemoryLearningStore();
        await source.SaveIfDueAsync(learningStore, start.AddHours(2), force: true);
        learningStore.State = learningStore.State! with
        {
            Metadata = learningStore.State.Metadata! with
            {
                LifetimeAcceptedObservationCount = 57
            }
        };

        var activityStore = new MemoryActivityStore(new(
        [new MachineLearningActivityEvent(start,
            MachineLearningActivityKind.PersistenceSucceeded,
            ObservationCount: 164)]));
        var activity = new MachineLearningActivityLog();
        await activity.LoadAsync(activityStore);
        var restored = new MachineLearningService(start.AddHours(3), activity);

        await restored.LoadAsync(learningStore);

        Assert.Equal(57, restored.GetDashboardSnapshot(start.AddHours(3))
            .ObservationCount);
        Assert.Contains(activity.GetSnapshot(
                restored.GetDashboardSnapshot(start.AddHours(3)), start.AddHours(3))
                .RecentEvents,
            item => item.Kind ==
                MachineLearningActivityKind.LearningContinuityRegressionDetected);
    }

    [Fact]
    public void RetentionAndCoalescingStayBoundedAndAvoidObservationPayloads()
    {
        var log = new MachineLearningActivityLog();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 1_100; index++)
        {
            log.Record(MachineLearningActivityKind.ObservationAccepted,
                now.AddMinutes(-index));
        }
        log.Record(MachineLearningActivityKind.ObservationSkipped, now,
            detail: "Missing prerequisite");
        log.Record(MachineLearningActivityKind.ObservationSkipped,
            now.AddSeconds(1), detail: "Missing prerequisite");
        var learning = new MachineLearningService(now);
        var events = log.GetSnapshot(learning.GetDashboardSnapshot(now), now)
            .RecentEvents;

        Assert.True(events.Count <= 30);
        Assert.All(events, item =>
        {
            Assert.DoesNotContain("http", item.Detail ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("process", item.Detail ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains(events, item => item.Kind ==
            MachineLearningActivityKind.ObservationSkipped && item.Count == 2);
    }

    [Fact]
    public async Task NormalRestoreFrom164To168DoesNotReportRegression()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-4);
        var source = new MachineLearningService(start);
        for (var index = 0; index < 164; index++)
        {
            source.Observe(CreateObservation(start.AddSeconds(index * 30)));
        }
        var learningStore = new MemoryLearningStore();
        await source.SaveIfDueAsync(learningStore, start.AddHours(2), force: true);
        var activity = new MachineLearningActivityLog();
        await activity.LoadAsync(new MemoryActivityStore(new(
        [new MachineLearningActivityEvent(start, MachineLearningActivityKind.PersistenceSucceeded,
            ObservationCount: 164)])));
        var restoredAt = start.AddHours(3);
        var restored = new MachineLearningService(restoredAt, activity);
        await restored.LoadAsync(learningStore);
        for (var index = 0; index < 4; index++)
        {
            Assert.True(restored.Observe(CreateObservation(
                restoredAt.AddSeconds(index * 30))));
        }

        Assert.Equal(168, restored.GetDashboardSnapshot(restoredAt)
            .ObservationCount);
        Assert.DoesNotContain(activity.GetSnapshot(
                restored.GetDashboardSnapshot(restoredAt), restoredAt).RecentEvents,
            item => item.Kind ==
                MachineLearningActivityKind.LearningContinuityRegressionDetected);
    }

    private static MachineLearningObservation CreateObservation(DateTimeOffset at) =>
        new(at, 10, 40, MachineUserActivityState.Active,
            MachineOverallState.Stable, [], 40, "Active:Stable",
            MachineNetworkActivityClass.Quiet);

    private sealed class MemoryLearningStore : IMachineLearningStore
    {
        public MachineLearningPersistedState? State { get; set; }
        public Task<MachineLearningPersistedState?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(State);
        public Task SaveAsync(MachineLearningPersistedState state, CancellationToken cancellationToken = default)
        {
            State = state;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryActivityStore(MachineLearningActivityPersistedState? state = null) : IMachineLearningActivityStore
    {
        public MachineLearningActivityPersistedState? State { get; private set; } = state;
        public Task<MachineLearningActivityPersistedState?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(State);
        public Task SaveAsync(MachineLearningActivityPersistedState state, CancellationToken cancellationToken = default)
        {
            State = state;
            return Task.CompletedTask;
        }
    }
}
