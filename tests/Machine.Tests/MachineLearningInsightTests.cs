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
            .ToArray());

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
