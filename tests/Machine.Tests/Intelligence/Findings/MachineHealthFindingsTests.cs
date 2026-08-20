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
    public void RepeatedApplicationFailureRemainsLocalizedOutsideCrashLoop(
        int count,
        bool expectedFinding)
    {
        var incidents = Enumerable.Range(0, count)
            .Select(index => Incident(
                MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddHours(-(index + 1)),
                "repeat.exe"));

        var findings = Evaluate(incidents);

        Assert.Equal(
            expectedFinding,
            findings.Findings.Any(finding => finding.Code ==
                "health.reliability.application-recurrence"));
        Assert.Equal(
            MachineOverallState.Stable,
            findings.OverallState);

        var recurrence = findings.Findings.SingleOrDefault(finding =>
            finding.Code == "health.reliability.application-recurrence");
        Assert.Equal(
            expectedFinding
                ? MachineFindingPostureImpact.Local
                : (MachineFindingPostureImpact?)null,
            recurrence?.PostureImpact);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void RepeatedUpdateFailureRemainsLocalizedOutsideCurrentFailure(
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
        Assert.Equal(MachineOverallState.Stable, findings.OverallState);
        var recurrence = findings.Findings.SingleOrDefault(finding =>
            finding.Code == "health.reliability.update-failures-repeated");
        Assert.Equal(
            expectedAttention
                ? MachineFindingPostureImpact.Local
                : (MachineFindingPostureImpact?)null,
            recurrence?.PostureImpact);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void UnexpectedShutdownRemainsLocalizedOutsideCurrentWindow(
        int count,
        bool expectedAttention)
    {
        var incidents = Enumerable.Range(0, count)
            .Select(index => Incident(
                MachineReliabilityIncidentCategory.UnexpectedShutdown,
                Now.AddDays(-1).AddHours(-(index + 1))));

        var findings = Evaluate(incidents);

        Assert.Equal(
            expectedAttention,
            findings.Findings.Any(finding => finding.Code ==
                "health.reliability.unexpected-shutdowns-repeated"));
        Assert.Equal(MachineOverallState.Stable, findings.OverallState);
        var recurrence = findings.Findings.SingleOrDefault(finding =>
            finding.Code == "health.reliability.unexpected-shutdowns-repeated");
        Assert.Equal(
            expectedAttention
                ? MachineFindingPostureImpact.Local
                : (MachineFindingPostureImpact?)null,
            recurrence?.PostureImpact);
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
    public void ActiveApplicationCrashLoopElevatesGlobalPosture()
    {
        var findings = Evaluate(
        [
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddMinutes(-2), "loop.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationHang,
                Now.AddMinutes(-8), "loop.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddMinutes(-14), "loop.exe")
        ]);

        Assert.Equal(MachineOverallState.Attention, findings.OverallState);
        Assert.Contains(findings.Findings, finding => finding.Code ==
            "health.reliability.application-crash-loop");
    }

    [Fact]
    public void IndependentRecentApplicationFailuresElevateGlobalPosture()
    {
        var findings = Evaluate(
        [
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddMinutes(-2), "one.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationHang,
                Now.AddMinutes(-8), "one.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddMinutes(-3), "two.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationHang,
                Now.AddMinutes(-9), "two.exe")
        ]);

        Assert.Equal(MachineOverallState.Attention, findings.OverallState);
        Assert.Contains(findings.Findings, finding => finding.Code ==
            "health.reliability.independent-application-failures");
    }

    [Fact]
    public void RepeatedRecentResidentApplicationFailuresElevatePosture()
    {
        var findings = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: StableResources(),
                Reliability: MachineReliabilityAggregator.Aggregate(
                [
                    Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                        Now.AddMinutes(-2), "Machine.App.exe"),
                    Incident(MachineReliabilityIncidentCategory.ApplicationHang,
                        Now.AddMinutes(-12), "Machine.App.exe")
                ], Now),
                ResidentApplicationIdentity: "Machine.App.exe"));

        Assert.Equal(MachineOverallState.Attention, findings.OverallState);
        Assert.Contains(findings.Findings, finding => finding.Code ==
            "health.reliability.resident-application-crash-loop");
    }

    [Fact]
    public void CurrentRepeatedUpdateFailureElevatesGlobalPosture()
    {
        var findings = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: StableResources(),
                WindowsUpdate: CurrentUpdateFailure(),
                Reliability: MachineReliabilityAggregator.Aggregate(
                [
                    Incident(MachineReliabilityIncidentCategory.UpdateFailure,
                        Now.AddMinutes(-10), updateIdentifier: "KB5001001"),
                    Incident(MachineReliabilityIncidentCategory.UpdateFailure,
                        Now.AddMinutes(-25), updateIdentifier: "KB5001002"),
                    Incident(MachineReliabilityIncidentCategory.InstallFailure,
                        Now.AddMinutes(-40), updateIdentifier: "KB5001003")
                ], Now)));

        Assert.Equal(MachineOverallState.Attention, findings.OverallState);
        Assert.Contains(findings.Findings, finding => finding.Code ==
            "health.reliability.update-failures-current");
    }

    [Fact]
    public void VeryRecentUnexpectedShutdownElevatesGlobalPosture()
    {
        var findings = Evaluate(
        [
            Incident(MachineReliabilityIncidentCategory.UnexpectedShutdown,
                Now.AddHours(-2))
        ]);

        Assert.Equal(MachineOverallState.Attention, findings.OverallState);
        Assert.Contains(findings.Findings, finding => finding.Code ==
            "health.reliability.unexpected-shutdowns-current");
    }

    [Fact]
    public void HistoricReliabilityFindingDoesNotChangeIncidentAccounting()
    {
        var incidents = Enumerable.Range(0, 12)
            .Select(index => Incident(
                MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddDays(-index / 2d).AddHours(-1), "historic.exe"))
            .ToArray();
        var reliability = MachineReliabilityAggregator.Aggregate(incidents, Now);
        var findings = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: StableResources(), Reliability: reliability));

        Assert.Equal(incidents.Length, reliability.Incidents.Count);
        Assert.Equal(MachineOverallState.Stable, findings.OverallState);
        var recurrence = Assert.Single(findings.Findings, finding =>
            finding.Code == "health.reliability.application-recurrence");
        Assert.Equal(MachineFindingPostureImpact.Local,
            recurrence.PostureImpact);
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

    private static MachineWindowsUpdateSnapshot CurrentUpdateFailure() => new(
        CapturedAt: Now,
        VerifiedAt: Now,
        UpdateServiceAvailable: false,
        LastSuccessfulUpdateScan: null,
        LastSuccessfulUpdateInstall: null,
        PendingUpdateCount: null,
        PendingImportantUpdateCount: null,
        UpdateState: MachineWindowsUpdateState.Unknown,
        RecentUpdateHistory: [],
        DataStatus: MachineHealthDataStatus.Complete,
        RefreshStatus: MachineWindowsUpdateRefreshStatus.Verified,
        FailureCode: "0x8024001E");

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
