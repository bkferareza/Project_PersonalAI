using Machine.Core;

namespace Machine.Tests;

public sealed class MachineHealthFindingsTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-14T12:00:00Z");

    [Fact]
    public void IsolatedHistoricalCrashDoesNotElevateCurrentState()
    {
        var findings = Evaluate(
        [
            Incident(
                MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddHours(-1),
                "one.exe")
        ]);

        Assert.Equal(MachineOverallState.Stable, findings.OverallState);
        Assert.DoesNotContain(findings.Findings, finding =>
            finding.Code.StartsWith("health.reliability"));
    }

    [Fact]
    public void RoutineRebootPendingIsInformationalOnly()
    {
        var reboot = MachineRebootPendingAggregator.Aggregate(
        [
            new(MachineRebootPendingReason.WindowsUpdate, true),
            new(MachineRebootPendingReason.ComponentServicing, false)
        ], Now);
        var findings = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: StableResources(),
                RebootPending: reboot));

        Assert.Equal(MachineOverallState.Stable, findings.OverallState);
        var finding = Assert.Single(findings.Findings);
        Assert.Equal("health.restart.pending", finding.Code);
        Assert.Equal(MachineFindingSeverity.Info, finding.Severity);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void RepeatedApplicationFailureUsesExactSevenDayThreshold(
        int count,
        bool expectedAttention)
    {
        var incidents = Enumerable.Range(0, count)
            .Select(index => Incident(
                MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddHours(-(index + 1)),
                "repeat.exe"));

        var findings = Evaluate(incidents);

        Assert.Equal(
            expectedAttention,
            findings.Findings.Any(finding => finding.Code ==
                "health.reliability.application-recurrence"));
        Assert.Equal(
            expectedAttention
                ? MachineOverallState.Attention
                : MachineOverallState.Stable,
            findings.OverallState);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void RepeatedUpdateFailureUsesExactSevenDayThreshold(
        int count,
        bool expectedAttention)
    {
        var incidents = Enumerable.Range(0, count)
            .Select(index => Incident(
                MachineReliabilityIncidentCategory.UpdateFailure,
                Now.AddHours(-(index + 1)),
                updateIdentifier: $"KB{5001000 + index}"));

        var findings = Evaluate(incidents);

        Assert.Equal(
            expectedAttention,
            findings.Findings.Any(finding => finding.Code ==
                "health.reliability.update-failures-repeated"));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void UnexpectedShutdownUsesExactRepeatThreshold(
        int count,
        bool expectedAttention)
    {
        var incidents = Enumerable.Range(0, count)
            .Select(index => Incident(
                MachineReliabilityIncidentCategory.UnexpectedShutdown,
                Now.AddHours(-(index + 1))));

        var findings = Evaluate(incidents);

        Assert.Equal(
            expectedAttention,
            findings.Findings.Any(finding => finding.Code ==
                "health.reliability.unexpected-shutdowns-repeated"));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void HardwareErrorsUseExactRepeatThreshold(
        int count,
        bool expectedAttention)
    {
        var incidents = Enumerable.Range(0, count)
            .Select(index => Incident(
                MachineReliabilityIncidentCategory.HardwareFailure,
                Now.AddHours(-(index + 1))));

        var findings = Evaluate(incidents);

        Assert.Equal(
            expectedAttention,
            findings.Findings.Any(finding => finding.Code ==
                "health.reliability.hardware-errors-repeated"));
    }

    [Fact]
    public void PartialHealthDataCreatesOnlyInformationalFindings()
    {
        var reliability = MachineReliabilityAggregator.Aggregate(
            [],
            Now,
            MachineHealthDataStatus.Partial,
            readFailureCount: 1);
        var reboot = MachineRebootPendingAggregator.Aggregate(
        [
            new(MachineRebootPendingReason.WindowsUpdate, false),
            new(MachineRebootPendingReason.ComponentServicing, null)
        ], Now);
        var update = new MachineWindowsUpdateSnapshot(
            Now,
            Now.AddMinutes(-30),
            true,
            null,
            null,
            1,
            null,
            MachineWindowsUpdateState.UpdatesAvailable,
            [],
            MachineHealthDataStatus.Partial,
            MachineWindowsUpdateRefreshStatus.CachedAfterFailure);

        var findings = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: StableResources(),
                WindowsUpdate: update,
                RebootPending: reboot,
                Reliability: reliability));

        Assert.Equal(MachineOverallState.Stable, findings.OverallState);
        Assert.Equal(3, findings.Findings.Count);
        Assert.All(findings.Findings, finding =>
            Assert.Equal(MachineFindingSeverity.Info, finding.Severity));
    }

    [Fact]
    public void ExistingCpuFindingSeverityIsUnchangedByHealthHistory()
    {
        var reliability = MachineReliabilityAggregator.Aggregate(
        [
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddHours(-1), "repeat.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddHours(-2), "repeat.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddHours(-3), "repeat.exe")
        ], Now);
        var findings = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: StableResources() with
                {
                    CpuUsagePercent = 90
                },
                Reliability: reliability));

        Assert.Equal(MachineOverallState.Warning, findings.OverallState);
        Assert.Equal(
            MachineFindingSeverity.Warning,
            Assert.Single(findings.Findings, finding =>
                finding.Code == "cpu.usage.high").Severity);
    }

    [Fact]
    public void PassiveHealthChangeDoesNotWakeInsightPolicy()
    {
        var policy = new MachineInsightTriggerPolicy();
        var stable = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(Resources: StableResources()));
        policy.EstablishBaseline(stable);
        var healthAttention = Evaluate(
        [
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddHours(-1), "repeat.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddHours(-2), "repeat.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddHours(-3), "repeat.exe")
        ]);

        var first = policy.ObserveTelemetry(
            healthAttention, Now, isOllamaOnline: true);
        var second = policy.ObserveTelemetry(
            healthAttention, Now.AddSeconds(2), isOllamaOnline: true);

        Assert.False(first.ShouldGenerate);
        Assert.False(second.ShouldGenerate);
        Assert.Equal(
            MachineInsightContextFingerprint.Create(stable),
            MachineInsightContextFingerprint.Create(healthAttention));
    }

    private static MachineFindingsSnapshot Evaluate(
        IEnumerable<MachineReliabilityIncident> incidents) =>
        MachineFindingsEvaluator.Evaluate(new MachineFindingsInput(
            Resources: StableResources(),
            Reliability: MachineReliabilityAggregator.Aggregate(
                incidents,
                Now)));

    private static MachineResourceSnapshot StableResources() => new(
        20,
        1_000,
        500,
        Now);

    private static MachineReliabilityIncident Incident(
        MachineReliabilityIncidentCategory category,
        DateTimeOffset occurredAt,
        string? applicationName = null,
        string? updateIdentifier = null) => new(
        occurredAt,
        category,
        MachineReliabilityIncidentSeverity.Significant,
        "Synthetic",
        applicationName,
        null,
        updateIdentifier,
        1,
        category.ToString());
}
