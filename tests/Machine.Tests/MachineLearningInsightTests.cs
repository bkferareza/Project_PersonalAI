using System.Net;
using System.Text;
using System.Text.Json;
using Machine.Core;
using Machine.Ollama;

namespace Machine.Tests;

public sealed class MachineLearningInsightTests
{
    [Fact]
    public async Task EstablishedLearnedContextIsBoundedInInsightPayload()
    {
        using var handler = new CapturingHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/")
        };
        var explainer = new OllamaMachineStateExplainer(client, "qwen3.5:4b");
        var request = CreateRequest() with { LearnedContext = CreateContext() };

        await explainer.ExplainAsync(request);

        var learned = GetPayload(handler.Json).GetProperty("learned_context");
        Assert.Equal("Established", learned.GetProperty("confidence").GetString());
        Assert.Equal(247, learned.GetProperty("sample_count").GetInt64());
        Assert.Equal("Quiet", learned
            .GetProperty("dominant_network_activity_class").GetString());
        Assert.Equal(24, learned
            .GetProperty("dominant_network_activity_count").GetInt64());
        Assert.Equal(31, learned
            .GetProperty("network_observation_count").GetInt64());
        Assert.Equal(3, learned.GetProperty("recent_episodes").GetArrayLength());
    }

    [Fact]
    public async Task InsufficientLearnedContextIsNotSent()
    {
        using var handler = new CapturingHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/")
        };
        var explainer = new OllamaMachineStateExplainer(client, "qwen3.5:4b");
        var request = CreateRequest() with
        {
            LearnedContext = CreateContext() with
            {
                Confidence = MachineLearningConfidence.Provisional
            }
        };

        await explainer.ExplainAsync(request);

        Assert.Equal(JsonValueKind.Null, GetPayload(handler.Json)
            .GetProperty("learned_context").ValueKind);
    }

    [Fact]
    public void PersonalizedLanguageRequiresEstablishedEvidence()
    {
        var findings = new MachineFindingsSnapshot(MachineOverallState.Stable, []);
        Assert.False(MachineExplanationValidator.IsValid(
            "Karaniwan ang CPU ko ngayon.", [], findings));
        Assert.True(MachineExplanationValidator.IsValid(
            "Karaniwan ang CPU ko ngayon.", [], findings,
            learnedContext: CreateContext()));
    }

    [Fact]
    public async Task EstablishedEvidencePermitsGroundedUsualBehaviorInsight()
    {
        const string insight =
            "Karaniwan ang CPU ko ngayon.";
        using var handler = new CapturingHandler(insight);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/")
        };
        var explainer = new OllamaMachineStateExplainer(client, "qwen3.5:4b");

        var result = await explainer.ExplainAsync(CreateRequest() with
        {
            LearnedContext = CreateContext()
        });

        Assert.Equal(insight, result.Text);
        Assert.Equal(MachineExplanationSource.LocalModel, result.Source);
    }

    [Fact]
    public async Task InsightPayloadIncludesOnlyBoundedNetworkAndSessionContext()
    {
        using var handler = new CapturingHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/")
        };
        var explainer = new OllamaMachineStateExplainer(client, "qwen3.5:4b");
        var request = CreateRequest() with
        {
            LearnedContext = CreateContext(),
            Network = new MachineNetworkInsightContext(
                MachineNetworkActivityClass.Quiet,
                12_345,
                678),
            Session = new MachineSessionInsightContext(
                TimeSpan.FromDays(3),
                TimeSpan.FromHours(9))
        };

        await explainer.ExplainAsync(request);

        var payload = GetPayload(handler.Json);
        var network = payload.GetProperty("network");
        Assert.Equal("Quiet", network.GetProperty("activity_class").GetString());
        Assert.Equal(12_345,
            network.GetProperty("receive_bytes_per_second").GetDouble());
        Assert.Equal(678,
            network.GetProperty("send_bytes_per_second").GetDouble());
        var session = payload.GetProperty("session");
        Assert.Equal(259_200,
            session.GetProperty("system_uptime_seconds").GetInt64());
        Assert.Equal(32_400,
            session.GetProperty("machine_uptime_seconds").GetInt64());

        var json = payload.GetRawText();
        Assert.DoesNotContain("interface", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ip_address", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mac_address", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("remote_endpoint", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PersonalizedNetworkClaimsRequireNetworkEvidence()
    {
        var findings = new MachineFindingsSnapshot(
            MachineOverallState.Stable,
            []);
        var network = new MachineNetworkInsightContext(
            MachineNetworkActivityClass.Quiet,
            100,
            50);
        var withoutNetworkEvidence = CreateContext() with
        {
            DominantNetworkActivityClass = null,
            DominantNetworkActivityCount = 0,
            NetworkObservationCount = 0
        };

        Assert.False(MachineExplanationValidator.IsValid(
            "Karaniwan ang network activity ko ngayon.",
            [],
            findings,
            learnedContext: withoutNetworkEvidence,
            network: network));
        Assert.False(MachineExplanationValidator.IsValid(
            "Tahimik ang network activity kumpara sa observed pattern.",
            [],
            findings,
            learnedContext: withoutNetworkEvidence,
            network: network));
        Assert.True(MachineExplanationValidator.IsValid(
            "Karaniwan ang network activity ko ngayon.",
            [],
            findings,
            learnedContext: CreateContext(),
            network: network));
        Assert.False(MachineExplanationValidator.IsValid(
            "May nagda-download sa network.",
            [],
            findings,
            learnedContext: CreateContext(),
            network: network));
        Assert.False(MachineExplanationValidator.IsValid(
            "May nagda-download.",
            [],
            findings,
            learnedContext: CreateContext(),
            network: network));
        Assert.False(MachineExplanationValidator.IsValid(
            "Chrome is using the network.",
            [],
            findings,
            learnedContext: CreateContext(),
            network: network));
    }

    private static MachineStateExplanationRequest CreateRequest() => new(
        new MachineIdentity("private", "private", "x64"),
        new MachineResourceSnapshot(20, 100, 50, DateTimeOffset.UtcNow),
        [], Findings: new MachineFindingsSnapshot(MachineOverallState.Stable, []));

    private static MachineLearnedContext CreateContext() => new(
        MachineUserActivityState.Active, 3,
        MachineLearningConfidence.Established, 247, 11, 2, 47, 3,
        Enumerable.Range(0, 4).Select(index => new MachineLearningEpisodeSummary(
            MachineUserActivityState.Active, MachineOverallState.Stable,
            index + 1, 10, 20, 40, ["cpu.usage.high:Attention"], null))
            .ToArray(),
        MachineNetworkActivityClass.Quiet,
        24,
        31);

    private static JsonElement GetPayload(JsonElement request)
    {
        var content = request.GetProperty("messages").EnumerateArray()
            .Single(message => message.GetProperty("role").GetString() == "user")
            .GetProperty("content").GetString()!;
        using var document = JsonDocument.Parse(content[(content.IndexOf('\n') + 1)..]);
        return document.RootElement.Clone();
    }

    private sealed class CapturingHandler(string? insight = null) : HttpMessageHandler
    {
        public JsonElement Json { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!
                .ReadAsStringAsync(cancellationToken));
            Json = document.RootElement.Clone();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    model = "qwen3.5:4b",
                    message = new
                    {
                        content = insight ?? "Stable ang verified condition ngayon."
                    }
                }), Encoding.UTF8, "application/json")
            };
        }
    }
}
