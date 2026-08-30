using System.Net;
using System.Text;
using System.Text.Json;
using Machine.Core;
using Machine.Ollama;

namespace Machine.Tests;

public sealed class OllamaMachineUsageOutlookGeneratorTests
{
    private const string ModelName = "qwen3.5:4b";
    private const string ValidOutlook =
        "For the next observed hour, projected energy is 0.150 kWh, estimated at about ₱2.22.";
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 14, 0, 0, TimeSpan.Zero);
    private static readonly Uri Loopback =
        new("http://127.0.0.1:11434/");

    [Fact]
    public async Task OutlookUsesSharedChatPipelineAndTenMinuteResidency()
    {
        using var handler = new CaptureHandler(() =>
            ChatResponse(ValidOutlook, "qwen3.5:4b-runtime"));
        using var client = Client(handler);
        var generator = new OllamaMachineStateExplainer(client, ModelName);

        var outlook = await generator.GenerateAsync(Request());

        Assert.Equal(1, handler.CallCount);
        Assert.Equal("/api/chat", handler.RequestUri?.AbsolutePath);
        Assert.Equal(ModelName,
            handler.Json.GetProperty("model").GetString());
        Assert.Equal("10m",
            handler.Json.GetProperty("keep_alive").GetString());
        Assert.False(handler.Json.GetProperty("stream").GetBoolean());
        Assert.False(handler.Json.GetProperty("think").GetBoolean());
        var options = handler.Json.GetProperty("options");
        Assert.Equal(2048, options.GetProperty("num_ctx").GetInt32());
        Assert.Equal(128, options.GetProperty("num_predict").GetInt32());
        Assert.Equal(ValidOutlook, outlook.Text);
        Assert.Equal("qwen3.5:4b-runtime", outlook.Model);
        Assert.Equal(MachineExplanationSource.LocalModel, outlook.Source);
    }

    [Fact]
    public async Task OutlookPayloadContainsOnlyBoundedPrecomputedEvidence()
    {
        using var handler = new CaptureHandler(() =>
            ChatResponse(ValidOutlook, ModelName));
        using var client = Client(handler);
        var generator = new OllamaMachineStateExplainer(client, ModelName);

        await generator.GenerateAsync(Request());

        var payload = UserPayload(handler.Json);
        Assert.Equal("Active",
            payload.GetProperty("global_learning_state").GetString());
        var context = payload.GetProperty("current_context");
        Assert.Equal("10 PM",
            context.GetProperty("local_hour").GetString());
        Assert.Equal("Provisional",
            context.GetProperty("context_maturity").GetString());
        Assert.Equal("240 samples across 4 distinct observed days",
            context.GetProperty("evidence").GetString());
        var next = payload.GetProperty("next_observed_hour");
        Assert.Equal("0.150 kWh",
            next.GetProperty("energy").GetString());
        Assert.Equal("0.140–0.160 kWh",
            next.GetProperty("energy_range").GetString());
        Assert.Equal("₱2.22",
            next.GetProperty("estimated_cost").GetString());
        Assert.Equal("₱2.07–₱2.37",
            next.GetProperty("estimated_cost_range").GetString());
        Assert.Equal("₱14.7833/kWh",
            payload.GetProperty("published_rate_reference")
                .GetProperty("rate").GetString());
        Assert.Equal(2,
            payload.GetProperty("relevant_established_patterns")
                .GetArrayLength());

        var json = payload.GetRawText();
        Assert.DoesNotContain("rollup", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw_observation", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("process", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ip_address", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coordinate", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("meralco_html", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OutlookInstructionsRemoveArithmeticAndActionAuthority()
    {
        using var handler = new CaptureHandler(() =>
            ChatResponse(ValidOutlook, ModelName));
        using var client = Client(handler);
        var generator = new OllamaMachineStateExplainer(client, ModelName);

        await generator.GenerateAsync(Request());

        var system = Message(handler.Json, "system");
        Assert.Contains("English only", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("concise natural English", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Taglish", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Filipino", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("already complete", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never calculate a new value", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never translate currency", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("provided formatted monetary values", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing value", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not provide an end-of-day number", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("needs to spend or pay", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not repeat UI labels mechanically", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one to three short declarative sentences", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("observed PC electricity", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("household bill", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never produce commands", system,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingForecastValuesRemainJsonNull()
    {
        using var handler = new CaptureHandler(() =>
            ChatResponse(
                "There is not enough learned evidence for an end-of-day projection yet.",
                ModelName));
        using var client = Client(handler);
        var generator = new OllamaMachineStateExplainer(client, ModelName);
        var original = Request();
        var request = original with
        {
            Forecast = original.Forecast with
            {
                RemainingDayExpectedEnergyKilowattHours = null,
                RemainingDayLowerKilowattHours = null,
                RemainingDayUpperKilowattHours = null,
                ProjectedEndOfDayObservedEnergyKilowattHours = null,
                ProjectedEndOfDayLowerKilowattHours = null,
                ProjectedEndOfDayUpperKilowattHours = null,
                ProjectedEndOfDayEstimatedCost = null,
                ProjectedEndOfDayCostLower = null,
                ProjectedEndOfDayCostUpper = null,
                ForecastMaturity =
                    MachineLearningEvidenceMaturity.Insufficient,
                ForecastCoverage = 0d,
                AvailabilityReason = MachineUsageForecastAvailabilityReason
                    .NoHistoricalActivityEvidence,
                RateReference = null,
                NextObservedHourEstimatedCost = null,
                NextObservedHourEstimatedCostLower = null,
                NextObservedHourEstimatedCostUpper = null
            }
        };

        await generator.GenerateAsync(request);

        var payload = UserPayload(handler.Json);
        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("end_of_day").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("published_rate_reference").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("next_observed_hour")
                .GetProperty("estimated_cost").ValueKind);
    }

    [Fact]
    public async Task PartialEndOfDayForecastOmitsNumbersFromModelContext()
    {
        using var handler = new CaptureHandler(() =>
            ChatResponse(
                "There is not enough learned coverage for a reliable end-of-day projection yet.",
                ModelName));
        using var client = Client(handler);
        var generator = new OllamaMachineStateExplainer(client, ModelName);
        var original = Request();
        var request = original with
        {
            Forecast = original.Forecast with
            {
                ForecastCoverage = 0.04d,
                AvailabilityReason = MachineUsageForecastAvailabilityReason
                    .PartialFutureCoverage
            }
        };

        await generator.GenerateAsync(request);

        var payload = UserPayload(handler.Json);
        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("end_of_day").ValueKind);
        Assert.Contains("Insufficient for conversational end-of-day",
            payload.GetProperty("forecast_availability").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("11.83", payload.GetRawText(),
            StringComparison.Ordinal);
        Assert.Equal(0.800d,
            request.Forecast.ProjectedEndOfDayObservedEnergyKilowattHours);
        Assert.Equal(11.83m,
            request.Forecast.ProjectedEndOfDayEstimatedCost);
        Assert.True(request.Forecast.HasEndOfDayForecast);
    }

    [Fact]
    public async Task InventedNumericClaimFallsBackWithoutChangingForecast()
    {
        using var handler = new CaptureHandler(() =>
            ChatResponse(
                "The projected energy is 999.999 kWh.",
                ModelName));
        using var client = Client(handler);
        var generator = new OllamaMachineStateExplainer(client, ModelName);
        var request = Request();
        var before = request.Forecast;

        var outlook = await generator.GenerateAsync(request);

        Assert.Equal(MachineExplanationSource.DeterministicFallback,
            outlook.Source);
        Assert.DoesNotContain("999.999", outlook.Text);
        Assert.Same(before, request.Forecast);
    }

    [Fact]
    public async Task ToolCallAndMalformedResponseUseDeterministicFallback()
    {
        using var toolHandler = new CaptureHandler(ToolResponse);
        using var toolClient = Client(toolHandler);
        var toolGenerator = new OllamaMachineStateExplainer(
            toolClient,
            ModelName);
        var toolResult = await toolGenerator.GenerateAsync(Request());
        Assert.Equal(MachineExplanationSource.DeterministicFallback,
            toolResult.Source);

        using var malformedHandler = new CaptureHandler(() =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{", Encoding.UTF8,
                    "application/json")
            });
        using var malformedClient = Client(malformedHandler);
        var malformedGenerator = new OllamaMachineStateExplainer(
            malformedClient,
            ModelName);
        var malformed = await malformedGenerator.GenerateAsync(Request());
        Assert.Equal(MachineExplanationSource.DeterministicFallback,
            malformed.Source);
    }

    [Fact]
    public async Task TransportFailureAndCallerCancellationRemainObservable()
    {
        using var failureHandler = new CaptureHandler(() =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var failureClient = Client(failureHandler);
        var generator = new OllamaMachineStateExplainer(
            failureClient,
            ModelName);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            generator.GenerateAsync(Request()));

        using var cancellationHandler = new CaptureHandler(() =>
            throw new InvalidOperationException("Must not send."));
        using var cancellationClient = Client(cancellationHandler);
        var cancelledGenerator = new OllamaMachineStateExplainer(
            cancellationClient,
            ModelName);
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            cancelledGenerator.GenerateAsync(Request(), source.Token));
        Assert.Equal(0, cancellationHandler.CallCount);
    }

    private static MachineUsageOutlookRequest Request()
    {
        var rate = Rate();
        var today = new MachineTodayLearnedEnergyComparison(
            new DateOnly(2026, 8, 28),
            0.500d,
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(4),
            1d,
            0.520d,
            0.450d,
            0.600d,
            MachineTodayLearnedEnergyComparisonState.WithinLearnedRange,
            MachineLearningEvidenceMaturity.Provisional,
            -0.020d,
            -3.85d,
            7.39m,
            7.69m,
            6.65m,
            8.87m,
            rate);
        var usage = new MachineLearnedHourlyUsageProfile(
            22,
            0.7d,
            0.3d,
            TimeSpan.FromMinutes(42),
            TimeSpan.FromMinutes(18),
            TimeSpan.FromHours(1),
            7,
            4,
            1d,
            MachineLearningEvidenceMaturity.Provisional);
        var forecast = new MachineUsageForecast(
            Now,
            new(22, MachineUserActivityState.Active),
            MachineLearningConfidence.Provisional,
            MachineLearningEvidenceMaturity.Provisional,
            usage,
            150d,
            140d,
            160d,
            0.150d,
            0.140d,
            0.160d,
            2.22m,
            2.07m,
            2.37m,
            today,
            TimeSpan.FromHours(2),
            0.300d,
            0.280d,
            0.320d,
            0.800d,
            0.780d,
            0.820d,
            11.83m,
            11.53m,
            12.12m,
            MachineLearningEvidenceMaturity.Provisional,
            1d,
            MachineUsageForecastAvailabilityReason.Available,
            rate);
        return new(
            forecast,
            MachineLearningMemoryState.Active,
            240,
            4,
            33,
            0,
            [Pattern(20), Pattern(21), Pattern(22)]);
    }

    private static MachineLearningRecurringPattern Pattern(int hour) => new(
        MachineUserActivityState.Active,
        hour,
        (hour + 2) % 24,
        hour + 2 >= 24,
        [
            new(hour, MachineUserActivityState.Active),
            new((hour + 1) % 24, MachineUserActivityState.Active)
        ],
        MachineLearningConfidence.Established,
        MachineLearningFreshness.Fresh,
        480,
        10,
        new(10, 20),
        new(45, 55),
        MachineNetworkActivityClass.Light,
        400,
        480,
        Now.AddDays(-10),
        Now);

    private static ElectricityRateSnapshot Rate() => new(
        1,
        "Meralco",
        "PHP",
        14.7833m,
        new DateOnly(2026, 8, 1),
        Now,
        Now.AddDays(30),
        "official-test",
        MachinePowerEstimateConfidence.HighEstimate,
        MachinePowerEstimateConfidence.HighEstimate);

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = Loopback };

    private static HttpResponseMessage ChatResponse(
        string content,
        string model)
    {
        var json = JsonSerializer.Serialize(new
        {
            model,
            message = new { role = "assistant", content }
        });
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8,
                "application/json")
        };
    }

    private static HttpResponseMessage ToolResponse()
    {
        var json = JsonSerializer.Serialize(new
        {
            model = ModelName,
            message = new
            {
                role = "assistant",
                content = ValidOutlook,
                tool_calls = new[]
                {
                    new { function = new { name = "not_allowed" } }
                }
            }
        });
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8,
                "application/json")
        };
    }

    private static string Message(JsonElement request, string role) =>
        request.GetProperty("messages")
            .EnumerateArray()
            .Single(item => item.GetProperty("role").GetString() == role)
            .GetProperty("content")
            .GetString() ?? string.Empty;

    private static JsonElement UserPayload(JsonElement request)
    {
        const string prefix =
            "Interpret this precomputed Matasuri usage outlook:";
        var content = Message(request, "user");
        Assert.StartsWith(prefix, content);
        using var document = JsonDocument.Parse(
            content[prefix.Length..].Trim());
        return document.RootElement.Clone();
    }

    private sealed class CaptureHandler(
        Func<HttpResponseMessage> response)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public JsonElement Json { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            RequestUri = request.RequestUri;
            var content = await request.Content!.ReadAsStringAsync(
                cancellationToken);
            using var document = JsonDocument.Parse(content);
            Json = document.RootElement.Clone();
            return response();
        }
    }
}
