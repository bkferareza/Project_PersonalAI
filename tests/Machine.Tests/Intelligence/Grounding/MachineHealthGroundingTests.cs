using System.Net;
using System.Text;
using System.Text.Json;
using Machine.Core;
using Machine.Ollama;

namespace Machine.Tests;

public sealed class MachineHealthGroundingTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-14T12:00:00Z");

    [Fact]
    public void VerifiedUnexpectedShutdownLanguageIsAllowed()
    {
        Assert.True(IsValid(
            "Windows recorded one unexpected shutdown this week.",
            Health(unexpectedShutdowns: 1)));
    }

    [Fact]
    public void WrittenHealthCountMustMatchVerifiedCount()
    {
        Assert.False(IsValid(
            "Windows recorded two unexpected shutdowns this week.",
            Health(unexpectedShutdowns: 1)));
    }

    [Theory]
    [InlineData("Brownout ang dahilan ng unexpected shutdown.")]
    [InlineData("The power supply caused the unexpected shutdown.")]
    [InlineData("Your PSU is failing.")]
    [InlineData("A driver caused the Windows failure.")]
    public void UnexpectedShutdownEvidenceDoesNotPermitCauseClaims(
        string text)
    {
        Assert.False(IsValid(
            text,
            Health(unexpectedShutdowns: 1)));
    }

    [Fact]
    public void RecordedApplicationIdentityCanBeNamedWithoutBlame()
    {
        var health = Health(
            crashes: 3,
            recurring: new MachineRecurringApplicationFailure(
                "SomeApp.exe", 3, 3, Now.AddHours(-1)));

        Assert.True(IsValid(
            "Windows recorded 3 crashes of SomeApp.exe this week.",
            health,
            ["SomeApp"]));
        Assert.False(IsValid(
            "SomeApp.exe caused system instability.",
            health,
            ["SomeApp"]));
    }

    [Theory]
    [InlineData("The update caused the crashes.")]
    [InlineData("Restarting will fix the crashes.")]
    [InlineData("You should restart now.")]
    [InlineData("Please restart the computer.")]
    [InlineData("Install updates to repair the crashes.")]
    [InlineData("Repair Windows to fix the crashes.")]
    public void HealthEvidenceDoesNotPermitRepairOrUpdateCausation(
        string text)
    {
        Assert.False(IsValid(text, Health(crashes: 3)));
    }

    [Fact]
    public void MissingHealthEvidenceCannotSupportIncidentClaim()
    {
        Assert.False(IsValid(
            "Windows recorded an unexpected shutdown this week.",
            null));
        Assert.False(IsValid(
            "Windows recorded three application crashes this week.",
            Health()));
    }

    [Fact]
    public void PositiveAbsenceClaimsMustMatchVerifiedHealthCounts()
    {
        Assert.False(IsValid(
            "No application crashes were recorded this week.",
            Health(crashes: 1)));
        Assert.False(IsValid(
            "No pending updates are available.",
            Health() with { PendingUpdateCount = 2 }));
        Assert.True(IsValid(
            "No application crashes were recorded this week.",
            Health()));
        Assert.True(IsValid(
            "No update failures were recorded this week.",
            Health()));
        Assert.True(IsValid(
            "No pending updates are available.",
            Health()));
    }

    [Fact]
    public void ReliabilityCountMustMatchVerifiedSevenDayCount()
    {
        var health = Health(crashes: 3);

        Assert.True(IsValid(
            "Windows recorded 3 application crashes this week.",
            health));
        Assert.False(IsValid(
            "Windows recorded 9 application crashes this week.",
            health));
    }

    [Fact]
    public void PendingUpdateCountMustMatchVerifiedCount()
    {
        var health = Health() with { PendingUpdateCount = 2 };

        Assert.True(IsValid("Windows has 2 pending updates.", health));
        Assert.False(IsValid("Windows has 5 pending updates.", health));
    }

    [Fact]
    public void RecurringCrashOrHangCountUsesTheBoundedAggregate()
    {
        var health = Health(
            crashes: 14,
            hangs: 1,
            recurring: new MachineRecurringApplicationFailure(
                "SomeApp.exe", 34, 12, Now.AddHours(-1)));

        Assert.True(IsValid(
            "Windows recorded 12 crashes or hangs of SomeApp.exe this week.",
            health,
            ["SomeApp"]));
        Assert.False(IsValid(
            "Windows recorded 11 crashes or hangs of SomeApp.exe this week.",
            health,
            ["SomeApp"]));
        Assert.False(IsValid(
            "Windows recorded 15 crashes or hangs of SomeApp.exe this week.",
            health,
            ["SomeApp"]));
        Assert.True(IsValid(
            "Windows recorded 15 crashes or hangs this week.",
            health,
            ["SomeApp"]));
    }

    [Fact]
    public void RecurringAggregateDoesNotProveASpecificHang()
    {
        var health = Health(
            crashes: 3,
            recurring: new MachineRecurringApplicationFailure(
                "SomeApp.exe", 3, 3, Now.AddHours(-1)));

        Assert.False(IsValid(
            "Windows recorded 3 hangs of SomeApp.exe this week.",
            health,
            ["SomeApp"]));
    }

    [Fact]
    public void PartialReliabilityCannotSupportNoFailureClaim()
    {
        var health = Health() with
        {
            ReliabilityDataStatus = MachineHealthDataStatus.Partial
        };

        Assert.False(IsValid(
            "No update failures were recorded this week.",
            health));
    }

    [Fact]
    public void RestartClaimRequiresMatchingVerifiedState()
    {
        Assert.True(IsValid(
            "May pending Windows restart na recorded.",
            Health(rebootPending: true)));
        Assert.False(IsValid(
            "May pending Windows restart na recorded.",
            Health(rebootPending: false)));
    }

    [Fact]
    public async Task OllamaPayloadContainsOnlyBoundedSafeHealthContext()
    {
        using var handler = new CapturingHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/")
        };
        var explainer = new OllamaMachineStateExplainer(client, "qwen3.5:4b");
        var incident = new MachineReliabilityIncident(
            Now.AddHours(-1),
            MachineReliabilityIncidentCategory.ApplicationCrash,
            MachineReliabilityIncidentSeverity.Significant,
            "Application Error",
            "C:\\Users\\Person\\Documents\\SomeApp.exe",
            "C:\\Windows\\System32\\fault.dll",
            null,
            1000,
            "application.crash",
            null,
            "11111111-1111-1111-1111-111111111111");
        var health = new MachineHealthInsightContext(
            MachineWindowsUpdateState.UpdatesAvailable,
            2,
            Now.AddMinutes(-38),
            true,
            Enum.GetValues<MachineRebootPendingReason>(),
            new MachineReliabilityWindowSummary(3, 1, 1, 0, 0, 0),
            incident,
            new MachineRecurringApplicationFailure(
                "C:\\Program Files\\SomeApp\\SomeApp.exe",
                4,
                3,
                Now.AddHours(-1)),
            MachineHealthDataStatus.Complete,
            Now.AddMinutes(-2),
            Now.AddMinutes(-3),
            MachineRebootPendingConfidence.Verified);
        var request = Request(health);

        var explanation = await explainer.ExplainAsync(request);

        Assert.Equal(MachineExplanationSource.LocalModel, explanation.Source);
        var payload = GetPayload(handler.RequestJson);
        var healthJson = payload.GetProperty("health");
        Assert.Equal("UpdatesAvailable",
            healthJson.GetProperty("windows_update_state").GetString());
        Assert.Equal(2,
            healthJson.GetProperty("pending_update_count").GetInt32());
        Assert.True(healthJson.GetProperty("reboot_pending").GetBoolean());
        Assert.Equal("Verified",
            healthJson.GetProperty("reboot_confidence").GetString());
        Assert.Equal(Now.AddMinutes(-2).ToString("O"),
            healthJson.GetProperty("reboot_verified_at").GetString());
        Assert.Equal(Now.AddMinutes(-3).ToString("O"),
            healthJson.GetProperty("reliability_verified_at").GetString());
        Assert.Equal(
            MachineHealthInsightProjector.MaximumRebootReasonCount,
            healthJson.GetProperty("reboot_reasons").GetArrayLength());
        Assert.Equal("SomeApp.exe",
            healthJson.GetProperty("most_recent_significant_incident")
                .GetProperty("application_name").GetString());
        Assert.Equal("SomeApp.exe",
            healthJson.GetProperty("recurring_application_failure")
                .GetProperty("application_name").GetString());
        var rawHealth = healthJson.GetRawText();
        Assert.DoesNotContain("C:\\\\Users", rawHealth,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fault.dll", rawHealth,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Correlation", rawHealth,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EventData", rawHealth,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HealthInsightProjectorBoundsReasonsAndHistory()
    {
        var reliability = MachineReliabilityAggregator.Aggregate(
        [
            new MachineReliabilityIncident(
                Now.AddHours(-1),
                MachineReliabilityIncidentCategory.ApplicationCrash,
                MachineReliabilityIncidentSeverity.Significant,
                "Application Error",
                "one.exe",
                null,
                null,
                1000,
                "application.crash"),
            new MachineReliabilityIncident(
                Now.AddHours(-2),
                MachineReliabilityIncidentCategory.ApplicationCrash,
                MachineReliabilityIncidentSeverity.Significant,
                "Application Error",
                "one.exe",
                null,
                null,
                1000,
                "application.crash")
        ], Now);
        var reboot = new MachineRebootPendingSnapshot(
            Now,
            true,
            MachineRebootPendingConfidence.Verified,
            Enum.GetValues<MachineRebootPendingReason>(),
            [],
            false);

        var projected = MachineHealthInsightProjector.Project(
            null,
            reboot,
            reliability);

        Assert.NotNull(projected);
        Assert.True(projected.RebootReasons.Count <=
            MachineHealthInsightProjector.MaximumRebootReasonCount);
        Assert.Equal(Now, projected.RebootVerifiedAt);
        Assert.Equal(Now, projected.ReliabilityVerifiedAt);
        Assert.Equal(
            MachineRebootPendingConfidence.Verified,
            projected.RebootConfidence);
        Assert.NotNull(projected.MostRecentSignificantIncident);
        Assert.NotNull(projected.RecurringApplicationFailure);
    }

    [Fact]
    public void HealthContextExcludesSelfFailureButKeepsThirdPartyFailure()
    {
        var reliability = MachineReliabilityAggregator.Aggregate(
        [
            new MachineReliabilityIncident(
                Now.AddMinutes(-1),
                MachineReliabilityIncidentCategory.ApplicationCrash,
                MachineReliabilityIncidentSeverity.Significant,
                "Application Error",
                "Machine.App.exe",
                null,
                null,
                1000,
                "application.crash"),
            new MachineReliabilityIncident(
                Now.AddMinutes(-2),
                MachineReliabilityIncidentCategory.ApplicationCrash,
                MachineReliabilityIncidentSeverity.Significant,
                "Application Error",
                "Discord.exe",
                null,
                null,
                1000,
                "application.crash")
        ], Now);

        var projected = MachineHealthInsightProjector.Project(
            null,
            null,
            reliability);

        Assert.NotNull(projected);
        Assert.Equal(
            "Discord.exe",
            projected.MostRecentSignificantIncident?.ApplicationName);
        Assert.Equal(
            1,
            projected.ReliabilityLast7Days?.ApplicationCrashCount);
    }

    private static bool IsValid(
        string text,
        MachineHealthInsightContext? health,
        IReadOnlyList<string>? processNames = null) =>
        MachineExplanationValidator.IsValid(
            text,
            processNames ?? [],
            StableFindings(),
            resources: Resources(),
            health: health);

    private static MachineHealthInsightContext Health(
        int crashes = 0,
        int hangs = 0,
        int unexpectedShutdowns = 0,
        bool? rebootPending = false,
        MachineRecurringApplicationFailure? recurring = null) => new(
        MachineWindowsUpdateState.UpToDate,
        0,
        Now,
        rebootPending,
        rebootPending == true
            ? [MachineRebootPendingReason.WindowsUpdate]
            : [],
        new MachineReliabilityWindowSummary(
            crashes,
            hangs,
            unexpectedShutdowns,
            0,
            0,
            0),
        unexpectedShutdowns > 0
            ? new MachineReliabilityIncident(
                Now.AddHours(-1),
                MachineReliabilityIncidentCategory.UnexpectedShutdown,
                MachineReliabilityIncidentSeverity.Significant,
                "EventLog",
                null,
                null,
                null,
                6008,
                "windows.unexpected-shutdown")
            : null,
        recurring,
        MachineHealthDataStatus.Complete);

    private static MachineStateExplanationRequest Request(
        MachineHealthInsightContext health) => new(
        new MachineIdentity("TEST", "Windows 11", "X64"),
        Resources(),
        [new MachineProcessSnapshot(1, "unrelated", 1, 1)],
        Findings: StableFindings(),
        Health: health);

    private static MachineResourceSnapshot Resources() => new(
        20,
        1_000,
        500,
        Now);

    private static MachineFindingsSnapshot StableFindings() => new(
        MachineOverallState.Stable,
        []);

    private static JsonElement GetPayload(JsonElement requestJson)
    {
        var userMessage = requestJson.GetProperty("messages")
            .EnumerateArray()
            .Single(message =>
                message.GetProperty("role").GetString() == "user")
            .GetProperty("content")
            .GetString()!;
        var jsonStart = userMessage.IndexOf('{');
        using var document = JsonDocument.Parse(userMessage[jsonStart..]);
        return document.RootElement.Clone();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public JsonElement RequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(
                cancellationToken);
            using var document = JsonDocument.Parse(body);
            RequestJson = document.RootElement.Clone();
            var responseJson = JsonSerializer.Serialize(new
            {
                model = "qwen3.5:4b",
                message = new
                {
                    role = "assistant",
                    content = "Windows recorded 3 crashes of SomeApp.exe this week."
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseJson,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
