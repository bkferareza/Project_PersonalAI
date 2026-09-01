using Machine.Core;

namespace Machine.Tests;

public sealed class MachineLearningLabProjectorTests
{
    [Fact]
    public void FirstAcceptedSampleIsImmediatelyVisibleWithoutProfileGate()
    {
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0,
            TimeSpan.Zero);
        var activityLog = new MachineLearningActivityLog();
        var service = new MachineLearningService(start, activityLog);

        Assert.True(service.Observe(CreateObservation(start, 121d)));
        var learning = service.GetDashboardSnapshot(start.AddSeconds(1));
        var activity = activityLog.GetSnapshot(
            learning,
            start.AddSeconds(1));

        var lab = MachineLearningLabProjector.Project(
            learning,
            activity,
            start.AddSeconds(1));

        var context = Assert.Single(lab.LearnedContexts);
        Assert.Equal(1, context.SampleCount);
        Assert.Equal(1, context.ObservedDayCount);
        Assert.Equal(MachineLearningConfidence.Calibrating,
            context.Confidence);
        Assert.Empty(learning.ContextProfiles);
        Assert.Equal(MachineLearningIntakeOutcome.Accepted,
            lab.Live.LastIntakeOutcome);
        Assert.True(lab.Live.PowerEvidenceAccepted);
        Assert.Equal(1, lab.Live.LifetimeObservationCount);
        Assert.Equal(1, lab.Live.SessionObservationCount);
        Assert.Equal(1, lab.Memory.RawObservationCount);
        Assert.Equal(1, lab.Memory.BaselineCount);
        Assert.Equal(0, lab.Memory.ProfileCount);
        Assert.Equal(TimeSpan.FromHours(24),
            lab.Memory.RawObservationRetention);

        var change = Assert.Single(lab.RecentChanges, item =>
            item.Kind == MachineLearningActivityKind.ObservationAccepted);
        Assert.NotNull(change.ContextChange);
        Assert.Equal(0, change.ContextChange!.PreviousSampleCount);
        Assert.Equal(1, change.ContextChange.SampleCount);
        Assert.Equal(0, change.ContextChange.PreviousObservedDayCount);
        Assert.Equal(1, change.ContextChange.ObservedDayCount);
        Assert.Equal(0, change.ContextChange.PreviousPowerEvidenceCount);
        Assert.Equal(1, change.ContextChange.PowerEvidenceCount);
    }

    [Fact]
    public void LatestRejectedIntakeExposesExactDeterministicReason()
    {
        var activityLog = new MachineLearningActivityLog();
        var service = new MachineLearningService(
            DateTimeOffset.UtcNow,
            activityLog);
        Assert.True(service.TryBeginObservationAttempt(
            DateTimeOffset.UtcNow));
        service.RecordMissingPrerequisite("Activity signal unavailable");
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        var learning = service.GetDashboardSnapshot(now);

        var lab = MachineLearningLabProjector.Project(
            learning,
            activityLog.GetSnapshot(learning, now),
            now);

        Assert.Equal(MachineLearningIntakeOutcome.Rejected,
            lab.Live.LastIntakeOutcome);
        Assert.Equal("Activity signal unavailable",
            lab.Live.LastIntakeReason);
        Assert.Empty(lab.LearnedContexts);
    }

    [Fact]
    public void RecentChangeCapturesStatisticalAndMaturityMovement()
    {
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0,
            TimeSpan.Zero);
        var activityLog = new MachineLearningActivityLog();
        var service = new MachineLearningService(start, activityLog);
        for (var index = 0; index <
            MachineLearningService.ProvisionalSampleCount; index++)
        {
            Assert.True(service.Observe(CreateObservation(
                start.AddSeconds(index * 30),
                100d + index)));
        }
        var now = start.AddMinutes(6);
        var learning = service.GetDashboardSnapshot(now);

        var lab = MachineLearningLabProjector.Project(
            learning,
            activityLog.GetSnapshot(learning, now),
            now);

        var latest = lab.RecentChanges.First(item =>
            item.Kind == MachineLearningActivityKind.ObservationAccepted);
        var change = Assert.IsType<MachineLearningContextChange>(
            latest.ContextChange);
        Assert.Equal(11, change.PreviousSampleCount);
        Assert.Equal(12, change.SampleCount);
        Assert.Equal(MachineLearningConfidence.Calibrating,
            change.PreviousMaturity);
        Assert.Equal(MachineLearningConfidence.Provisional,
            change.Maturity);
        Assert.Equal(105d, change.PreviousPowerMeanWatts!.Value, 6);
        Assert.Equal(105.5d, change.PowerMeanWatts!.Value, 6);
        Assert.Equal(10d, change.PreviousAdaptiveCpuMean!.Value, 6);
        Assert.Equal(10d, change.AdaptiveCpuMean, 6);
    }

    [Fact]
    public void DifferentRejectionReasonsAreNotCoalesced()
    {
        var now = DateTimeOffset.UtcNow;
        var activityLog = new MachineLearningActivityLog();
        activityLog.Record(MachineLearningActivityKind.ObservationSkipped,
            now, detail: "CPU or memory signal unavailable");
        activityLog.Record(MachineLearningActivityKind.ObservationSkipped,
            now.AddSeconds(1), detail: "Activity signal unavailable");
        var service = new MachineLearningService(now, activityLog);

        var events = activityLog.GetSnapshot(
                service.GetDashboardSnapshot(now.AddSeconds(2)),
                now.AddSeconds(2))
            .RecentEvents
            .Where(item => item.Kind ==
                MachineLearningActivityKind.ObservationSkipped)
            .ToArray();

        Assert.Equal(2, events.Length);
        Assert.Contains(events, item => item.Detail ==
            "CPU or memory signal unavailable");
        Assert.Contains(events, item => item.Detail ==
            "Activity signal unavailable");
    }

    private static MachineLearningObservation CreateObservation(
        DateTimeOffset at,
        double powerWatts) => new(
        at,
        CpuUsagePercent: 10d,
        MemoryUsagePercent: 40d,
        MachineUserActivityState.Active,
        MachineOverallState.Stable,
        FindingKeys: [],
        SystemVolumeFreePercent: 40d,
        ContextFingerprint: "Active:Stable",
        NetworkActivityClass: MachineNetworkActivityClass.Quiet,
        EstimatedWallPowerWatts: powerWatts);
}
