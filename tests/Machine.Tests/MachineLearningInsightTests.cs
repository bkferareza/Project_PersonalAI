using System.Net;
using System.Text;
using System.Text.Json;
using Machine.Core;
using Machine.Ollama;

namespace Machine.Tests;

public sealed class MachineLearningInsightTests
{
    [Fact]
    public async Task LearnedMemoryPayloadIsRelevantAndBounded()
    {
        using var handler = new CapturingHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/")
        };
        var explainer = new OllamaMachineStateExplainer(client, "qwen3.5:4b");

        await explainer.ExplainAsync(CreateRequest() with
        {
            LearnedContext = CreateContext()
        });

        var learned = GetPayload(handler.Json).GetProperty("learned_context");
        var baseline = learned.GetProperty("current_baseline");
        Assert.Equal("Established", baseline.GetProperty("confidence").GetString());
        Assert.Equal(247,
            baseline.GetProperty("lifetime_sample_count").GetInt64());

        var profile = learned.GetProperty("matching_profile");
        Assert.Equal("Fresh", profile.GetProperty("freshness").GetString());
        Assert.Equal("Quiet", profile
            .GetProperty("dominant_network_activity_class").GetString());
        Assert.Equal(24, profile
            .GetProperty("dominant_network_activity_count").GetInt64());
        Assert.Equal(31, profile
            .GetProperty("network_observation_count").GetInt64());

        var pattern = learned.GetProperty("matching_broader_pattern");
        Assert.Equal(3, pattern.GetProperty("member_profile_count").GetInt32());
        Assert.Equal(2, learned.GetProperty("recent_episodes").GetArrayLength());

        var json = learned.GetRawText();
        Assert.DoesNotContain("journal", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("baselines", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profiles", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("interface", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProvisionalProfileIsSentWithoutEstablishedAuthority()
    {
        using var handler = new CapturingHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/")
        };
        var explainer = new OllamaMachineStateExplainer(client, "qwen3.5:4b");
        var context = CreateContext(includePattern: false);
        var provisional = context.MatchingProfile! with
        {
            Confidence = MachineLearningConfidence.Provisional
        };

        await explainer.ExplainAsync(CreateRequest() with
        {
            LearnedContext = context with { MatchingProfile = provisional }
        });

        var learned = GetPayload(handler.Json).GetProperty("learned_context");
        Assert.Equal("Provisional", learned.GetProperty("matching_profile")
            .GetProperty("confidence").GetString());
        Assert.Equal(JsonValueKind.Null,
            learned.GetProperty("matching_broader_pattern").ValueKind);
    }

    [Fact]
    public void PersonalizedLanguageRequiresFreshEstablishedProfile()
    {
        var findings = new MachineFindingsSnapshot(MachineOverallState.Stable, []);
        var context = CreateContext();
        var stale = context with
        {
            MatchingProfile = context.MatchingProfile! with
            {
                Freshness = MachineLearningFreshness.Stale
            }
        };

        Assert.False(MachineExplanationValidator.IsValid(
            "Karaniwan ang CPU ko ngayon.", [], findings));
        Assert.True(MachineExplanationValidator.IsValid(
            "Karaniwan ang CPU ko ngayon.", [], findings,
            learnedContext: context));
        Assert.False(MachineExplanationValidator.IsValid(
            "Karaniwan ang CPU ko ngayon.", [], findings,
            learnedContext: stale));
        Assert.True(MachineExplanationValidator.IsValid(
            "Historical typical CPU ko dati ang range na ito.", [], findings,
            learnedContext: stale));
    }

    [Fact]
    public void BroaderPatternLanguageRequiresActualEstablishedPattern()
    {
        var findings = new MachineFindingsSnapshot(MachineOverallState.Stable, []);

        Assert.False(MachineExplanationValidator.IsValid(
            "May learned pattern over time sa Active behavior ko.", [], findings,
            learnedContext: CreateContext(includePattern: false)));
        Assert.True(MachineExplanationValidator.IsValid(
            "May learned pattern over time sa Active behavior ko.", [], findings,
            learnedContext: CreateContext()));
    }

    [Fact]
    public void LearnedEvidenceNeverAuthorizesAbsoluteOrAnomalyClaims()
    {
        var findings = new MachineFindingsSnapshot(MachineOverallState.Stable, []);
        var context = CreateContext();

        Assert.False(MachineExplanationValidator.IsValid(
            "Ganito ako palagi.", [], findings,
            learnedContext: context));
        Assert.False(MachineExplanationValidator.IsValid(
            "This is always my behavior.", [], findings,
            learnedContext: context));
        Assert.False(MachineExplanationValidator.IsValid(
            "Unusual ito compared with my pattern.", [], findings,
            learnedContext: context));
        Assert.False(MachineExplanationValidator.IsValid(
            "May problema compared with my pattern.", [], findings,
            learnedContext: context));
        Assert.False(MachineExplanationValidator.IsValid(
            "Ito ang normal ko.", [], findings));
    }

    [Fact]
    public void LearnedPercentageRangesMustMatchSuppliedCompactMemory()
    {
        var findings = new MachineFindingsSnapshot(MachineOverallState.Stable, []);
        var resources = new MachineResourceSnapshot(
            20,
            100,
            50,
            DateTimeOffset.UnixEpoch);
        var context = CreateContext();

        Assert.True(MachineExplanationValidator.IsValid(
            "CPU is typically 8-16% in this learned context.",
            [],
            findings,
            resources: resources,
            learnedContext: context));
        Assert.True(MachineExplanationValidator.IsValid(
            "Memory usage is typically 44-52% in this learned context.",
            [],
            findings,
            resources: resources,
            learnedContext: context));
        Assert.False(MachineExplanationValidator.IsValid(
            "CPU is typically 8-90% in this learned context.",
            [],
            findings,
            resources: resources,
            learnedContext: context));
        Assert.False(MachineExplanationValidator.IsValid(
            "Memory usage is typically 10-52% in this learned context.",
            [],
            findings,
            resources: resources,
            learnedContext: context));
    }

    [Fact]
    public async Task EstablishedEvidencePermitsGroundedUsualBehaviorInsight()
    {
        const string insight = "Karaniwan ang CPU ko ngayon.";
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
        Assert.DoesNotContain("ip_address", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mac_address", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("remote_endpoint", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PersonalizedNetworkClaimsRequireProfileNetworkEvidence()
    {
        var findings = new MachineFindingsSnapshot(
            MachineOverallState.Stable,
            []);
        var network = new MachineNetworkInsightContext(
            MachineNetworkActivityClass.Quiet,
            100,
            50);
        var context = CreateContext();
        var withoutNetworkEvidence = context with
        {
            MatchingProfile = context.MatchingProfile! with
            {
                DominantNetworkActivityClass = null,
                DominantNetworkActivityCount = 0,
                NetworkObservationCount = 0
            }
        };

        Assert.False(MachineExplanationValidator.IsValid(
            "Karaniwan ang network activity ko ngayon.", [], findings,
            learnedContext: withoutNetworkEvidence, network: network));
        Assert.True(MachineExplanationValidator.IsValid(
            "Karaniwan ang network activity ko ngayon.", [], findings,
            learnedContext: context, network: network));
        Assert.False(MachineExplanationValidator.IsValid(
            "May nagda-download sa network.", [], findings,
            learnedContext: context, network: network));
        Assert.False(MachineExplanationValidator.IsValid(
            "Chrome is using the network.", [], findings,
            learnedContext: context, network: network));
    }

    [Fact]
    public void BroaderNetworkClaimUsesBroaderPatternEvidence()
    {
        var findings = new MachineFindingsSnapshot(
            MachineOverallState.Stable,
            []);
        var network = new MachineNetworkInsightContext(
            MachineNetworkActivityClass.Quiet,
            100,
            50);
        var context = CreateContext();
        var profileWithoutNetwork = context.MatchingProfile! with
        {
            DominantNetworkActivityClass = null,
            DominantNetworkActivityCount = 0,
            NetworkObservationCount = 0
        };

        Assert.True(MachineExplanationValidator.IsValid(
            "The learned pattern has similar network behavior over time.",
            [],
            findings,
            learnedContext: context with
            {
                MatchingProfile = profileWithoutNetwork
            },
            network: network));

        Assert.False(MachineExplanationValidator.IsValid(
            "The learned pattern has similar network behavior over time.",
            [],
            findings,
            learnedContext: context with
            {
                MatchingBroaderPattern = context.MatchingBroaderPattern! with
                {
                    DominantNetworkActivityClass = null,
                    DominantNetworkActivityCount = 0,
                    NetworkObservationCount = 0
                }
            },
            network: network));
    }

    private static MachineStateExplanationRequest CreateRequest() => new(
        new MachineIdentity("private", "private", "x64"),
        new MachineResourceSnapshot(20, 100, 50, DateTimeOffset.UtcNow),
        [], Findings: new MachineFindingsSnapshot(MachineOverallState.Stable, []));

    private static MachineLearnedContext CreateContext(
        bool includePattern = true)
    {
        var first = new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero);
        var last = first.AddDays(8);
        var cpuRange = new MachineLearningRange(8, 16);
        var memoryRange = new MachineLearningRange(44, 52);
        var baseline = new MachineLearningBaseline(
            3,
            MachineUserActivityState.Active,
            247,
            11,
            2,
            47,
            3,
            first,
            last,
            9,
            MachineLearningConfidence.Established,
            NetworkQuietSampleCount: 24,
            NetworkLightSampleCount: 7,
            ObservedDurationTicks: TimeSpan.FromMinutes(123.5).Ticks,
            AdaptiveCpuMean: 12,
            AdaptiveCpuStandardDeviation: 2,
            AdaptiveMemoryMean: 48,
            AdaptiveMemoryStandardDeviation: 2,
            AdaptiveSampleCount: 247,
            AdaptiveLastUpdatedAt: last,
            Freshness: MachineLearningFreshness.Fresh);
        var profile = new MachineLearningContextProfile(
            3,
            MachineUserActivityState.Active,
            MachineLearningConfidence.Established,
            MachineLearningFreshness.Fresh,
            247,
            TimeSpan.FromMinutes(123.5).Ticks,
            9,
            first,
            last,
            new MachineLearningMetricProfile(12, 2, cpuRange),
            new MachineLearningMetricProfile(48, 2, memoryRange),
            MachineNetworkActivityClass.Quiet,
            24,
            31,
            first,
            last,
            last);
        var pattern = includePattern
            ? new MachineLearningRecurringPattern(
                MachineUserActivityState.Active,
                2,
                5,
                false,
                [
                    new MachineLearningContextKey(2, MachineUserActivityState.Active),
                    new MachineLearningContextKey(3, MachineUserActivityState.Active),
                    new MachineLearningContextKey(4, MachineUserActivityState.Active)
                ],
                MachineLearningConfidence.Established,
                MachineLearningFreshness.Fresh,
                741,
                9,
                cpuRange,
                memoryRange,
                MachineNetworkActivityClass.Quiet,
                72,
                93,
                first,
                last)
            : null;

        return new MachineLearnedContext(
            baseline,
            profile,
            pattern,
            Enumerable.Range(0, 4).Select(index =>
                new MachineLearningEpisodeSummary(
                    MachineUserActivityState.Active,
                    MachineOverallState.Stable,
                    index + 1,
                    10,
                    20,
                    40,
                    ["cpu.usage.high:Attention"],
                    null)).ToArray());
    }

    private static JsonElement GetPayload(JsonElement request)
    {
        var content = request.GetProperty("messages").EnumerateArray()
            .Single(message => message.GetProperty("role").GetString() == "user")
            .GetProperty("content").GetString()!;
        using var document = JsonDocument.Parse(
            content[(content.IndexOf('\n') + 1)..]);
        return document.RootElement.Clone();
    }

    private sealed class CapturingHandler(string? insight = null)
        : HttpMessageHandler
    {
        public JsonElement Json { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
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
                        content = insight ??
                            "Stable ang verified condition ngayon."
                    }
                }), Encoding.UTF8, "application/json")
            };
        }
    }
}
