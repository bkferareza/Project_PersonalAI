using Machine.Core;

namespace Machine.Tests;

public sealed class MachineInsightTriggerPolicyTests
{
    private static readonly DateTimeOffset Start =
        DateTimeOffset.UnixEpoch;

    [Fact]
    public void StateTransitionTriggersAfterTwoEquivalentEvaluations()
    {
        var policy = CreatePolicyWithStableBaseline();
        var attention = CreateSnapshot(
            MachineOverallState.Attention,
            CreateFinding(
                "cpu.usage.high",
                MachineFindingSeverity.Attention));

        var first = policy.ObserveTelemetry(
            attention,
            Start.AddSeconds(10),
            isLocalInferenceAvailable: true);
        var second = policy.ObserveTelemetry(
            attention,
            Start.AddSeconds(12),
            isLocalInferenceAvailable: true);

        Assert.False(first.ShouldGenerate);
        Assert.True(second.ShouldGenerate);
        Assert.Equal(
            MachineInsightTriggerReason.StateChanged,
            second.Reason);
    }

    [Fact]
    public void SingleSampleSpikeDoesNotTrigger()
    {
        var policy = CreatePolicyWithStableBaseline();
        var attention = CreateSnapshot(
            MachineOverallState.Attention,
            CreateFinding(
                "cpu.usage.high",
                MachineFindingSeverity.Attention));
        var stable = CreateSnapshot(MachineOverallState.Stable);

        var spike = policy.ObserveTelemetry(
            attention,
            Start.AddSeconds(10),
            isLocalInferenceAvailable: true);
        var recoverySample = policy.ObserveTelemetry(
            stable,
            Start.AddSeconds(12),
            isLocalInferenceAvailable: true);
        var stableAgain = policy.ObserveTelemetry(
            stable,
            Start.AddSeconds(14),
            isLocalInferenceAvailable: true);

        Assert.False(spike.ShouldGenerate);
        Assert.False(recoverySample.ShouldGenerate);
        Assert.False(stableAgain.ShouldGenerate);
        Assert.False(policy.IsRequestInFlight);
    }

    [Fact]
    public void RecoveryTriggersOnceAfterStabilization()
    {
        var attention = CreateSnapshot(
            MachineOverallState.Attention,
            CreateFinding(
                "cpu.usage.high",
                MachineFindingSeverity.Attention));
        var policy = CreatePolicyWithBaseline(attention);
        var stable = CreateSnapshot(MachineOverallState.Stable);

        Assert.False(policy.ObserveTelemetry(
            stable,
            Start.AddSeconds(10),
            isLocalInferenceAvailable: true).ShouldGenerate);

        var recovery = policy.ObserveTelemetry(
            stable,
            Start.AddSeconds(12),
            isLocalInferenceAvailable: true);

        Assert.True(recovery.ShouldGenerate);
        Assert.Equal(
            MachineInsightTriggerReason.Recovery,
            recovery.Reason);

        policy.CompleteRequest(
            recovery,
            insightAccepted: true,
            Start.AddSeconds(13),
            isLocalInferenceAvailable: true);

        Assert.False(policy.ObserveTelemetry(
            stable,
            Start.AddMinutes(3),
            isLocalInferenceAvailable: true).ShouldGenerate);
    }

    [Fact]
    public void FindingSetChangeTriggersWithoutStateChange()
    {
        var policy = CreatePolicyWithStableBaseline();
        var partial = CreateSnapshot(
            MachineOverallState.Stable,
            CreateFinding(
                "data.startup.partial",
                MachineFindingSeverity.Info));

        Assert.False(policy.ObserveTelemetry(
            partial,
            Start.AddSeconds(10),
            isLocalInferenceAvailable: true).ShouldGenerate);

        var decision = policy.ObserveTelemetry(
            partial,
            Start.AddSeconds(12),
            isLocalInferenceAvailable: true);

        Assert.True(decision.ShouldGenerate);
        Assert.Equal(
            MachineInsightTriggerReason.FindingsChanged,
            decision.Reason);
    }

    [Fact]
    public void UnchangedContextDoesNotGenerateRepeatedly()
    {
        var stable = CreateSnapshot(MachineOverallState.Stable);
        var policy = CreatePolicyWithBaseline(stable);

        for (var index = 0; index < 20; index++)
        {
            var decision = policy.ObserveTelemetry(
                stable,
                Start.AddSeconds(10 + index * 2),
                isLocalInferenceAvailable: true);

            Assert.False(decision.ShouldGenerate);
        }
    }

    [Fact]
    public void FingerprintDeduplicatesOrderAndFindingDetailChanges()
    {
        var first = CreateSnapshot(
            MachineOverallState.Warning,
            CreateFinding(
                "memory.usage.high",
                MachineFindingSeverity.Warning,
                "First detail."),
            CreateFinding(
                "cpu.usage.high",
                MachineFindingSeverity.Warning,
                "Another detail."));
        var reordered = CreateSnapshot(
            MachineOverallState.Warning,
            CreateFinding(
                "cpu.usage.high",
                MachineFindingSeverity.Warning,
                "CPU moved within the same severity."),
            CreateFinding(
                "memory.usage.high",
                MachineFindingSeverity.Warning,
                "Memory moved within the same severity."));

        Assert.Equal(
            MachineInsightContextFingerprint.Create(first),
            MachineInsightContextFingerprint.Create(reordered));

        var policy = CreatePolicyWithBaseline(first);
        Assert.False(policy.ObserveTelemetry(
            reordered,
            Start.AddSeconds(10),
            isLocalInferenceAvailable: true).ShouldGenerate);
        Assert.False(policy.ObserveTelemetry(
            reordered,
            Start.AddSeconds(12),
            isLocalInferenceAvailable: true).ShouldGenerate);
    }

    [Fact]
    public void AutomaticCooldownDefersLatestChangedContext()
    {
        var policy = CreatePolicyWithStableBaseline();
        var attention = CreateSnapshot(
            MachineOverallState.Attention,
            CreateFinding(
                "cpu.usage.high",
                MachineFindingSeverity.Attention));
        var warning = CreateSnapshot(
            MachineOverallState.Warning,
            CreateFinding(
                "cpu.usage.high",
                MachineFindingSeverity.Warning));

        policy.ObserveTelemetry(
            attention,
            Start.AddSeconds(10),
            isLocalInferenceAvailable: true);
        var firstRequest = policy.ObserveTelemetry(
            attention,
            Start.AddSeconds(12),
            isLocalInferenceAvailable: true);
        policy.CompleteRequest(
            firstRequest,
            insightAccepted: true,
            Start.AddSeconds(13),
            isLocalInferenceAvailable: true);

        policy.ObserveTelemetry(
            warning,
            Start.AddSeconds(20),
            isLocalInferenceAvailable: true);
        var duringCooldown = policy.ObserveTelemetry(
            warning,
            Start.AddSeconds(22),
            isLocalInferenceAvailable: true);
        var afterCooldown = policy.ObserveTelemetry(
            warning,
            Start.AddSeconds(132),
            isLocalInferenceAvailable: true);

        Assert.False(duringCooldown.ShouldGenerate);
        Assert.True(afterCooldown.ShouldGenerate);
        Assert.Equal(
            MachineInsightTriggerReason.StateChanged,
            afterCooldown.Reason);
    }

    [Fact]
    public void InFlightChangesCoalesceToLatestContext()
    {
        var policy = CreatePolicyWithStableBaseline();
        var attention = CreateSnapshot(
            MachineOverallState.Attention,
            CreateFinding(
                "cpu.usage.high",
                MachineFindingSeverity.Attention));
        var warning = CreateSnapshot(
            MachineOverallState.Warning,
            CreateFinding(
                "cpu.usage.high",
                MachineFindingSeverity.Warning));
        var critical = CreateSnapshot(
            MachineOverallState.Critical,
            CreateFinding(
                "storage.system-volume.low-free-space",
                MachineFindingSeverity.Critical));

        policy.ObserveTelemetry(
            attention,
            Start.AddSeconds(10),
            isLocalInferenceAvailable: true);
        var active = policy.ObserveTelemetry(
            attention,
            Start.AddSeconds(12),
            isLocalInferenceAvailable: true);

        policy.ObserveTelemetry(
            warning,
            Start.AddSeconds(14),
            isLocalInferenceAvailable: true);
        Assert.False(policy.ObserveTelemetry(
            warning,
            Start.AddSeconds(16),
            isLocalInferenceAvailable: true).ShouldGenerate);
        policy.ObserveTelemetry(
            critical,
            Start.AddSeconds(18),
            isLocalInferenceAvailable: true);
        Assert.False(policy.ObserveTelemetry(
            critical,
            Start.AddSeconds(20),
            isLocalInferenceAvailable: true).ShouldGenerate);

        var immediateFollowUp = policy.CompleteRequest(
            active,
            insightAccepted: false,
            Start.AddSeconds(21),
            isLocalInferenceAvailable: true);
        var coalesced = policy.ObserveTelemetry(
            critical,
            Start.AddSeconds(132),
            isLocalInferenceAvailable: true);

        Assert.False(immediateFollowUp.ShouldGenerate);
        Assert.True(coalesced.ShouldGenerate);
        Assert.Equal(
            MachineInsightContextFingerprint.Create(critical),
            coalesced.ContextFingerprint);
    }

    [Fact]
    public void DashboardFirstOpenTriggersOnlyWithoutCurrentInsight()
    {
        var stable = CreateSnapshot(MachineOverallState.Stable);
        var policy = CreatePolicyWithBaseline(stable);

        var first = policy.RequestForDashboard(
            stable,
            Start.AddSeconds(10),
            isLocalInferenceAvailable: true);
        var duplicateWhileActive = policy.RequestForDashboard(
            stable,
            Start.AddSeconds(11),
            isLocalInferenceAvailable: true);

        Assert.True(first.ShouldGenerate);
        Assert.Equal(
            MachineInsightTriggerReason.DashboardOpened,
            first.Reason);
        Assert.False(duplicateWhileActive.ShouldGenerate);

        policy.CompleteRequest(
            first,
            insightAccepted: true,
            Start.AddSeconds(12),
            isLocalInferenceAvailable: true);

        Assert.True(policy.HasInsightForCurrentContext);
        Assert.False(policy.RequestForDashboard(
            stable,
            Start.AddMinutes(3),
            isLocalInferenceAvailable: true).ShouldGenerate);
    }

    [Fact]
    public void OfflineTransitionIsSuppressed()
    {
        var policy = CreatePolicyWithStableBaseline();
        var warning = CreateSnapshot(
            MachineOverallState.Warning,
            CreateFinding(
                "memory.usage.high",
                MachineFindingSeverity.Warning));

        policy.ObserveTelemetry(
            warning,
            Start.AddSeconds(10),
            isLocalInferenceAvailable: false);
        var offline = policy.ObserveTelemetry(
            warning,
            Start.AddSeconds(12),
            isLocalInferenceAvailable: false);
        var merelyOnline = policy.ObserveTelemetry(
            warning,
            Start.AddSeconds(14),
            isLocalInferenceAvailable: true);

        Assert.False(offline.ShouldGenerate);
        Assert.False(merelyOnline.ShouldGenerate);
        Assert.False(policy.IsRequestInFlight);

        Assert.True(policy.RequestForDashboard(
            warning,
            Start.AddSeconds(16),
            isLocalInferenceAvailable: true).ShouldGenerate);
    }

    [Fact]
    public void InitialHydrationEstablishesBaselineWithoutGeneration()
    {
        var policy = new MachineInsightTriggerPolicy();
        var stable = CreateSnapshot(MachineOverallState.Stable);
        var hydrated = CreateSnapshot(
            MachineOverallState.Stable,
            CreateFinding(
                "data.software.packaged.partial",
                MachineFindingSeverity.Info));

        Assert.False(policy.ObserveTelemetry(
            stable,
            Start,
            isLocalInferenceAvailable: true,
            allowAutomaticGeneration: false).ShouldGenerate);
        Assert.False(policy.ObserveTelemetry(
            stable,
            Start.AddSeconds(2),
            isLocalInferenceAvailable: true,
            allowAutomaticGeneration: false).ShouldGenerate);
        Assert.False(policy.ObserveTelemetry(
            hydrated,
            Start.AddSeconds(4),
            isLocalInferenceAvailable: true,
            allowAutomaticGeneration: false).ShouldGenerate);

        policy.EstablishBaseline(hydrated);

        Assert.False(policy.ObserveTelemetry(
            hydrated,
            Start.AddSeconds(6),
            isLocalInferenceAvailable: true).ShouldGenerate);
        Assert.False(policy.IsRequestInFlight);
    }

    [Fact]
    public void ManualRequestBypassesDeduplicationButNotInFlightGuard()
    {
        var stable = CreateSnapshot(MachineOverallState.Stable);
        var policy = CreatePolicyWithBaseline(stable);

        var first = policy.RequestManual(
            stable,
            isLocalInferenceAvailable: true);
        var concurrent = policy.RequestManual(
            stable,
            isLocalInferenceAvailable: true);

        Assert.True(first.ShouldGenerate);
        Assert.Equal(MachineInsightTriggerReason.Manual, first.Reason);
        Assert.False(concurrent.ShouldGenerate);

        policy.CompleteRequest(
            first,
            insightAccepted: true,
            Start.AddSeconds(10),
            isLocalInferenceAvailable: true);

        Assert.True(policy.RequestManual(
            stable,
            isLocalInferenceAvailable: true).ShouldGenerate);
    }

    private static MachineInsightTriggerPolicy
        CreatePolicyWithStableBaseline() =>
        CreatePolicyWithBaseline(
            CreateSnapshot(MachineOverallState.Stable));

    private static MachineInsightTriggerPolicy
        CreatePolicyWithBaseline(MachineFindingsSnapshot snapshot)
    {
        var policy = new MachineInsightTriggerPolicy();

        Assert.False(policy.ObserveTelemetry(
            snapshot,
            Start,
            isLocalInferenceAvailable: true).ShouldGenerate);
        Assert.False(policy.ObserveTelemetry(
            snapshot,
            Start.AddSeconds(2),
            isLocalInferenceAvailable: true).ShouldGenerate);

        return policy;
    }

    private static MachineFindingsSnapshot CreateSnapshot(
        MachineOverallState state,
        params MachineFinding[] findings) =>
        new(state, findings);

    private static MachineFinding CreateFinding(
        string code,
        MachineFindingSeverity severity,
        string detail = "Verified detail.") =>
        new(
            Code: code,
            Severity: severity,
            Title: "Verified finding",
            Detail: detail);
}
