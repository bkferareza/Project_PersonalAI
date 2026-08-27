using Machine.Core;
using Machine.Windows;

namespace Machine.Tests;

public sealed class MachineReliabilityTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-14T12:00:00Z");

    [Fact]
    public void AggregateCountsAllSupportedCategoriesAcrossWindows()
    {
        var snapshot = MachineReliabilityAggregator.Aggregate(
        [
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddHours(-1), "one.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationHang,
                Now.AddDays(-2), "two.exe"),
            Incident(MachineReliabilityIncidentCategory.UnexpectedShutdown,
                Now.AddDays(-6)),
            Incident(MachineReliabilityIncidentCategory.UpdateFailure,
                Now.AddDays(-8)),
            Incident(MachineReliabilityIncidentCategory.InstallFailure,
                Now.AddDays(-9)),
            Incident(MachineReliabilityIncidentCategory.HardwareFailure,
                Now.AddDays(-20)),
            Incident(MachineReliabilityIncidentCategory.WindowsFailure,
                Now.AddDays(-29))
        ], Now);

        Assert.Equal(1, snapshot.Summary.Last24Hours.ApplicationCrashCount);
        Assert.Equal(1, snapshot.Summary.Last7Days.ApplicationHangCount);
        Assert.Equal(1,
            snapshot.Summary.Last7Days.UnexpectedShutdownCount);
        Assert.Equal(2, snapshot.Summary.Last30Days.UpdateFailureCount);
        Assert.Equal(1, snapshot.Summary.Last30Days.HardwareFailureCount);
        Assert.Equal(1, snapshot.Summary.Last30Days.OtherFailureCount);
        Assert.Equal(7, snapshot.Summary.Last30Days.TotalIncidentCount);
    }

    [Fact]
    public void AggregateIgnoresEventsOutsideThirtyDayWindow()
    {
        var snapshot = MachineReliabilityAggregator.Aggregate(
        [
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddDays(-31), "old.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddDays(-1), "recent.exe")
        ], Now);

        Assert.Single(snapshot.Incidents);
        Assert.Equal("recent.exe", snapshot.Incidents[0].ApplicationName);
    }

    [Fact]
    public void KernelPowerAndEventLogForSameEpisodeDeduplicate()
    {
        var first = Incident(
            MachineReliabilityIncidentCategory.UnexpectedShutdown,
            Now.AddMinutes(-20),
            source: "Microsoft-Windows-Kernel-Power",
            eventId: 41);
        var second = Incident(
            MachineReliabilityIncidentCategory.UnexpectedShutdown,
            Now.AddMinutes(-17),
            source: "EventLog",
            eventId: 6008);

        var snapshot = MachineReliabilityAggregator.Aggregate(
            [first, second], Now);

        Assert.Single(snapshot.Incidents);
        Assert.Equal(
            1,
            snapshot.Summary.Last24Hours.UnexpectedShutdownCount);
    }

    [Fact]
    public void UnexpectedShutdownsOutsideDeduplicationWindowStaySeparate()
    {
        var snapshot = MachineReliabilityAggregator.Aggregate(
        [
            Incident(MachineReliabilityIncidentCategory.UnexpectedShutdown,
                Now.AddMinutes(-30), eventId: 41),
            Incident(MachineReliabilityIncidentCategory.UnexpectedShutdown,
                Now.AddMinutes(-24), eventId: 6008)
        ], Now);

        Assert.Equal(2, snapshot.Incidents.Count);
    }

    [Fact]
    public void SameSourceUnexpectedShutdownsInsideWindowStaySeparate()
    {
        var snapshot = MachineReliabilityAggregator.Aggregate(
        [
            Incident(MachineReliabilityIncidentCategory.UnexpectedShutdown,
                Now.AddMinutes(-20),
                source: "Microsoft-Windows-Kernel-Power", eventId: 41),
            Incident(MachineReliabilityIncidentCategory.UnexpectedShutdown,
                Now.AddMinutes(-17),
                source: "Microsoft-Windows-Kernel-Power", eventId: 41)
        ], Now);

        Assert.Equal(2, snapshot.Incidents.Count);
    }

    [Fact]
    public void RelatedApplicationEventsWithinWindowDeduplicate()
    {
        var snapshot = MachineReliabilityAggregator.Aggregate(
        [
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddMinutes(-10), "C:\\Program Files\\Foo\\foo.exe",
                source: "Application Error", eventId: 1000),
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddMinutes(-9), "foo.exe",
                source: "Windows Error Reporting", eventId: 1001)
        ], Now);

        var incident = Assert.Single(snapshot.Incidents);
        Assert.Equal("foo.exe", incident.ApplicationName);
        Assert.Equal(1, snapshot.Summary.Last24Hours.ApplicationCrashCount);
    }

    [Fact]
    public void SeparateApplicationCrashesOutsideWindowRemainSeparate()
    {
        var snapshot = MachineReliabilityAggregator.Aggregate(
        [
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddMinutes(-20), "foo.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddMinutes(-17), "foo.exe")
        ], Now);

        Assert.Equal(2, snapshot.Incidents.Count);
    }

    [Fact]
    public void SameSourceApplicationCrashesInsideWindowRemainSeparate()
    {
        var snapshot = MachineReliabilityAggregator.Aggregate(
        [
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddMinutes(-10), "foo.exe",
                source: "Application Error", eventId: 1000),
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddMinutes(-9), "foo.exe",
                source: "Application Error", eventId: 1000)
        ], Now);

        Assert.Equal(2, snapshot.Incidents.Count);
    }

    [Fact]
    public void SameTimestampDifferentApplicationsRemainSeparate()
    {
        var snapshot = MachineReliabilityAggregator.Aggregate(
        [
            Incident(MachineReliabilityIncidentCategory.ApplicationHang,
                Now.AddMinutes(-5), "one.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationHang,
                Now.AddMinutes(-5), "two.exe")
        ], Now);

        Assert.Equal(2, snapshot.Incidents.Count);
    }

    [Fact]
    public void RecurringApplicationRequiresTwoCrashOrHangIncidents()
    {
        var snapshot = MachineReliabilityAggregator.Aggregate(
        [
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddDays(-1), "foo.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationHang,
                Now.AddDays(-2), "FOO.EXE"),
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddDays(-3), "bar.exe")
        ], Now);

        var recurring = Assert.Single(
            snapshot.Summary.RecurringApplications);
        Assert.Equal("foo.exe", recurring.ApplicationName,
            ignoreCase: true);
        Assert.Equal(2, recurring.IncidentCountLast7Days);
        Assert.Equal(2, recurring.IncidentCountLast30Days);
    }

    [Fact]
    public void MatasuriFailuresRemainRetainedButDoNotEnterMachineSummary()
    {
        var snapshot = MachineReliabilityAggregator.Aggregate(
        [
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddHours(-1), "Machine.App.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationHang,
                Now.AddHours(-2), "MACHINE.APP.EXE"),
            Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddHours(-3), "third-party.exe"),
            Incident(MachineReliabilityIncidentCategory.ApplicationHang,
                Now.AddHours(-4), "third-party.exe")
        ], Now);

        Assert.Equal(4, snapshot.Incidents.Count);
        Assert.Equal(1, snapshot.Summary.Last7Days.ApplicationCrashCount);
        Assert.Equal(1, snapshot.Summary.Last7Days.ApplicationHangCount);
        Assert.Equal(
            "third-party.exe",
            Assert.Single(snapshot.Summary.RecurringApplications)
                .ApplicationName);
        Assert.Equal(
            2,
            snapshot.Incidents.Count(
                MatasuriRuntimeIdentityPolicy.IsOwnedRuntimeIncident));
    }

    [Theory]
    [InlineData("Machine.App.exe")]
    [InlineData("C:\\Matasuri\\Machine.App.exe")]
    [InlineData("848F7F02-C9D0-4C05-BD8B-B04298378EE4")]
    [InlineData("848F7F02-C9D0-4C05-BD8B-B04298378EE4_1z32rh13vfry6")]
    [InlineData("848F7F02-C9D0-4C05-BD8B-B04298378EE4_1z32rh13vfry6!App")]
    [InlineData("848F7F02-C9D0-4C05-BD8B-B04298378EE4_1.0.0.0_x64__1z32rh13vfry6")]
    public void ExactMatasuriRuntimeIdentitiesAreRecognized(string identity)
    {
        Assert.True(
            MatasuriRuntimeIdentityPolicy.IsOwnedApplicationIdentity(
                identity));
    }

    [Theory]
    [InlineData("OtherMachine.App.exe")]
    [InlineData("Machine.App.Helper.exe")]
    [InlineData("Machine.Application.exe")]
    [InlineData("848F7F02-C9D0-4C05-BD8B-B04298378EE4_otherpublisher")]
    [InlineData("praid:App")]
    public void SimilarlyNamedUnrelatedIdentitiesAreNotExcluded(
        string identity)
    {
        Assert.False(
            MatasuriRuntimeIdentityPolicy.IsOwnedApplicationIdentity(
                identity));
    }

    [Fact]
    public void NormalizeApplicationIdentityStripsPathsAndRejectsUnsafeData()
    {
        Assert.Equal(
            "foo.exe",
            MachineReliabilityAggregator.NormalizeApplicationIdentity(
                "C:\\Users\\Person\\Documents\\foo.exe"));
        Assert.Null(
            MachineReliabilityAggregator.NormalizeApplicationIdentity(
                "https://example.test/private"));
        Assert.Null(
            MachineReliabilityAggregator.NormalizeApplicationIdentity(
                "foo.exe --document C:\\Users\\Person\\secret.txt"));
    }

    [Fact]
    public void RetainedHistoryIsBoundedButSummaryUsesBoundedQueryEvidence()
    {
        var incidents = Enumerable.Range(0, 120)
            .Select(index => Incident(
                MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddMinutes(-(index * 3)),
                $"app-{index}.exe"));

        var snapshot = MachineReliabilityAggregator.Aggregate(incidents, Now);

        Assert.Equal(
            MachineReliabilityAggregator.MaximumIncidentCount,
            snapshot.Incidents.Count);
        Assert.Equal(
            120,
            snapshot.Summary.Last24Hours.ApplicationCrashCount);
    }

    [Fact]
    public void CategoryMilestonesSurviveTheRetainedIncidentBound()
    {
        var shutdownAt = Now.AddDays(-20);
        var incidents = Enumerable.Range(0, 110)
            .Select(index => Incident(
                MachineReliabilityIncidentCategory.ApplicationCrash,
                Now.AddMinutes(-(index * 3)),
                $"app-{index}.exe"))
            .Append(Incident(
                MachineReliabilityIncidentCategory.UnexpectedShutdown,
                shutdownAt));

        var snapshot = MachineReliabilityAggregator.Aggregate(incidents, Now);

        Assert.Equal(
            MachineReliabilityAggregator.MaximumIncidentCount,
            snapshot.Incidents.Count);
        Assert.DoesNotContain(snapshot.Incidents, incident =>
            incident.Category ==
                MachineReliabilityIncidentCategory.UnexpectedShutdown);
        Assert.Equal(
            shutdownAt,
            snapshot.LastUnexpectedShutdownAt);
    }

    [Theory]
    [InlineData("Application Error", 1000,
        MachineReliabilityIncidentCategory.ApplicationCrash)]
    [InlineData("Application Hang", 1002,
        MachineReliabilityIncidentCategory.ApplicationHang)]
    [InlineData("Microsoft-Windows-Kernel-Power", 41,
        MachineReliabilityIncidentCategory.UnexpectedShutdown)]
    [InlineData("EventLog", 6008,
        MachineReliabilityIncidentCategory.UnexpectedShutdown)]
    [InlineData("Microsoft-Windows-WHEA-Logger", 18,
        MachineReliabilityIncidentCategory.HardwareFailure)]
    [InlineData("Microsoft-Windows-WindowsUpdateClient", 20,
        MachineReliabilityIncidentCategory.InstallFailure)]
    [InlineData("Microsoft-Windows-WindowsUpdateClient", 25,
        MachineReliabilityIncidentCategory.UpdateFailure)]
    [InlineData("Microsoft-Windows-WindowsUpdateClient", 35,
        MachineReliabilityIncidentCategory.UpdateFailure)]
    [InlineData("Microsoft-Windows-WER-SystemErrorReporting", 1001,
        MachineReliabilityIncidentCategory.WindowsFailure)]
    public void StructuredEventMappingUsesProviderAndEventId(
        string provider,
        int eventId,
        MachineReliabilityIncidentCategory expectedCategory)
    {
        var record = new WindowsReliabilityEventRecord(
            provider,
            eventId,
            Now,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppName"] = "C:\\Program Files\\Foo\\foo.exe",
                ["ModuleName"] = "C:\\Windows\\System32\\bar.dll",
                ["BugcheckCode"] = "0x9F",
                ["updateTitle"] = "Cumulative Update KB5001234"
            });

        var incident = WindowsEventLogReliabilitySource.MapEvent(record);

        Assert.NotNull(incident);
        Assert.Equal(expectedCategory, incident.Category);
    }

    [Fact]
    public void UnsupportedEventIsIgnored()
    {
        var record = new WindowsReliabilityEventRecord(
            "Unrelated Provider",
            999,
            Now,
            new Dictionary<string, string?>());

        Assert.Null(WindowsEventLogReliabilitySource.MapEvent(record));
    }

    [Fact]
    public void WindowsErrorReportingUsesProblemSignatureApplicationFields()
    {
        var reportId = Guid.NewGuid();
        var record = new WindowsReliabilityEventRecord(
            "Windows Error Reporting",
            1001,
            Now,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["EventName"] = "APPCRASH",
                ["P1"] = "C:\\Program Files\\Foo\\foo.exe",
                ["P4"] = "C:\\Windows\\System32\\bar.dll",
                ["ReportId"] = reportId.ToString("D")
            });

        var incident = WindowsEventLogReliabilitySource.MapEvent(record);

        Assert.NotNull(incident);
        Assert.Equal(
            MachineReliabilityIncidentCategory.ApplicationCrash,
            incident.Category);
        Assert.Equal("foo.exe", incident.ApplicationName);
        Assert.Equal("bar.dll", incident.FaultModule);
        Assert.Equal(reportId.ToString("D"), incident.CorrelationId);
    }

    [Fact]
    public void StructuredXmlParserRetainsOnlyRequiredFields()
    {
        var xml = """
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
              <System>
                <Provider Name="Application Error" />
                <EventID>1000</EventID>
                <TimeCreated SystemTime="2026-08-14T11:30:00.0000000Z" />
                <Computer>private-device</Computer>
              </System>
              <EventData>
                <Data Name="AppName">C:\Program Files\Foo\foo.exe</Data>
                <Data Name="DocumentPath">C:\Users\Person\secret.docx</Data>
              </EventData>
              <RenderingInfo><Message>Localized private message</Message></RenderingInfo>
            </Event>
            """;

        var record = WindowsEventLogReliabilitySource.ParseEventXml(xml);
        var incident = WindowsEventLogReliabilitySource.MapEvent(record!);

        Assert.NotNull(record);
        Assert.Equal("Application Error", record.ProviderName);
        Assert.DoesNotContain("DocumentPath", record.Data.Keys);
        Assert.DoesNotContain(
            "secret",
            string.Join('|', record.Data.Values),
            StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(incident);
        Assert.Equal("foo.exe", incident.ApplicationName);
        Assert.DoesNotContain(
            "secret",
            string.Join('|', new[]
            {
                incident.ApplicationName,
                incident.FaultModule,
                incident.FailureCode,
                incident.CorrelationId
            }.Where(value => value is not null)),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReliabilityProviderCachesSuccessfulAcquisition()
    {
        var now = Now;
        var source = new RecordingReliabilitySource(() =>
            new WindowsReliabilityAcquisition(
            [
                Incident(MachineReliabilityIncidentCategory.ApplicationCrash,
                    now.AddMinutes(-1), "foo.exe")
            ], 3, 0));
        var provider = new WindowsMachineReliabilityProvider(
            source,
            () => now,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(1));

        var first = await provider.GetAsync();
        now = now.AddMinutes(9);
        var cached = await provider.GetAsync();

        Assert.Same(first, cached);
        Assert.Equal(1, source.CaptureCount);
    }

    [Fact]
    public async Task ReliabilityProviderPreservesLastGoodOnFailure()
    {
        var now = Now;
        var source = new RecordingReliabilitySource(call => call == 1
            ? new WindowsReliabilityAcquisition(
                [Incident(
                    MachineReliabilityIncidentCategory.ApplicationCrash,
                    now.AddMinutes(-1), "foo.exe")], 3, 0)
            : throw new IOException("Simulated failure."));
        var provider = new WindowsMachineReliabilityProvider(
            source,
            () => now,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(1));

        var first = await provider.GetAsync();
        now = now.AddMinutes(11);
        var preserved = await provider.GetAsync();

        Assert.Equal(first.VerifiedAt, preserved.VerifiedAt);
        Assert.Single(preserved.Incidents);
        Assert.Equal(MachineHealthDataStatus.Partial, preserved.DataStatus);
    }

    private static MachineReliabilityIncident Incident(
        MachineReliabilityIncidentCategory category,
        DateTimeOffset occurredAt,
        string? applicationName = null,
        string source = "Synthetic",
        int? eventId = 1) => new(
        occurredAt,
        category,
        category is MachineReliabilityIncidentCategory.HardwareFailure or
            MachineReliabilityIncidentCategory.WindowsFailure
                ? MachineReliabilityIncidentSeverity.Severe
                : MachineReliabilityIncidentSeverity.Significant,
        source,
        applicationName,
        null,
        category is MachineReliabilityIncidentCategory.UpdateFailure or
            MachineReliabilityIncidentCategory.InstallFailure
                ? "KB5001234"
                : null,
        eventId,
        category.ToString());

    private sealed class RecordingReliabilitySource
        : IWindowsReliabilitySource
    {
        private readonly Func<int, WindowsReliabilityAcquisition> _capture;

        public RecordingReliabilitySource(
            Func<WindowsReliabilityAcquisition> capture)
            : this(_ => capture())
        {
        }

        public RecordingReliabilitySource(
            Func<int, WindowsReliabilityAcquisition> capture)
        {
            _capture = capture;
        }

        public int CaptureCount { get; private set; }

        public Task<WindowsReliabilityAcquisition> CaptureAsync(
            CancellationToken cancellationToken)
        {
            CaptureCount++;
            return Task.FromResult(_capture(CaptureCount));
        }
    }
}
