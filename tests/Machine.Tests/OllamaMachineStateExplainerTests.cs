using System.Net;
using System.Text;
using System.Text.Json;
using Machine.Core;
using Machine.Ollama;

namespace Machine.Tests;

public sealed class OllamaMachineStateExplainerTests
{
    private const string ModelName = "qwen3.5:4b";
    private static readonly Uri LoopbackBaseAddress =
        new("http://127.0.0.1:11434/");

    [Fact]
    public async Task ExplainAsyncSendsRequiredChatRequestAndParsesResponse()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(
                "  Kalma lang, verified load lang ito.  ",
                "qwen3.5:4b-runtime"));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);

        var explanation = await explainer.ExplainAsync(
            CreateExplanationRequest());

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/chat", handler.RequestUri?.AbsolutePath);
        Assert.Equal(ModelName, handler.RequestJson
            .GetProperty("model")
            .GetString());
        Assert.False(handler.RequestJson
            .GetProperty("stream")
            .GetBoolean());
        Assert.False(handler.RequestJson
            .GetProperty("think")
            .GetBoolean());
        Assert.Equal(
            "Kalma lang, verified load lang ito.",
            explanation.Text);
        Assert.Equal("qwen3.5:4b-runtime", explanation.Model);
    }

    [Fact]
    public async Task ExplainAsyncSendsVerifiedMachineSnapshot()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse("Verified snapshot received.", ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);
        var request = new MachineStateExplanationRequest(
            new MachineIdentity(
                "DESKTOP-VERIFIED",
                "Windows 11 Pro",
                "X64"),
            new MachineResourceSnapshot(
                CpuUsagePercent: 42.5,
                TotalMemoryBytes: 34_359_738_368,
                UsedMemoryBytes: 12_884_901_888,
                CapturedAt: new DateTimeOffset(
                    2026,
                    8,
                    6,
                    10,
                    15,
                    0,
                    TimeSpan.Zero)),
            [
                new MachineProcessSnapshot(
                    ProcessId: 4242,
                    Name: "render-worker",
                    CpuUsagePercent: 17.25,
                    WorkingSetBytes: 536_870_912)
            ]);

        await explainer.ExplainAsync(request);

        var userMessage = GetMessageContent(
            handler.RequestJson,
            "user");
        const string payloadPrefix =
            "Explain this verified machine snapshot:";
        Assert.StartsWith(payloadPrefix, userMessage);

        using var payloadDocument = JsonDocument.Parse(
            userMessage[payloadPrefix.Length..].Trim());
        var payloadJson = payloadDocument.RootElement.GetRawText();

        Assert.Contains("DESKTOP-VERIFIED", payloadJson);
        Assert.Contains("42.5", payloadJson);
        Assert.Contains("12884901888", payloadJson);
        Assert.Contains("34359738368", payloadJson);
        Assert.Contains("render-worker", payloadJson);
        Assert.Contains("4242", payloadJson);
    }

    [Fact]
    public async Task ExplainAsyncSendsRequiredSystemGuardrails()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse("Verified facts only.", ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);

        await explainer.ExplainAsync(CreateExplanationRequest());

        var systemMessage = GetMessageContent(
            handler.RequestJson,
            "system");

        Assert.Contains(
            "only the verified machine facts",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Do not claim that you changed, fixed, deleted, stopped, or optimized anything.",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "Never invent a cause",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "Use no more than 80 words.",
            systemMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainAsyncWithEmptyContentThrows()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse("   ", ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => explainer.ExplainAsync(
                CreateExplanationRequest()));
    }

    [Fact]
    public async Task ExplainAsyncWithPreCancelledTokenThrows()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            throw new InvalidOperationException(
                "No request should be sent for caller cancellation."));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => explainer.ExplainAsync(
                CreateExplanationRequest(),
                cancellationTokenSource.Token));
        Assert.Equal(0, handler.CallCount);
    }

    private static MachineStateExplanationRequest
        CreateExplanationRequest() =>
            new(
                new MachineIdentity(
                    "DESKTOP-TEST",
                    "Windows 11",
                    "X64"),
                new MachineResourceSnapshot(
                    CpuUsagePercent: 25,
                    TotalMemoryBytes: 16_000_000_000,
                    UsedMemoryBytes: 8_000_000_000,
                    CapturedAt: new DateTimeOffset(
                        2026,
                        8,
                        6,
                        10,
                        0,
                        0,
                        TimeSpan.Zero)),
                [
                    new MachineProcessSnapshot(
                        ProcessId: 100,
                        Name: "test-process",
                        CpuUsagePercent: 5,
                        WorkingSetBytes: 250_000_000)
                ]);

    private static HttpClient CreateHttpClient(
        HttpMessageHandler handler) =>
        new(handler)
        {
            BaseAddress = LoopbackBaseAddress
        };

    private static HttpResponseMessage ChatResponse(
        string content,
        string model)
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            model,
            message = new
            {
                role = "assistant",
                content
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

    private static string GetMessageContent(
        JsonElement requestJson,
        string role) =>
        requestJson
            .GetProperty("messages")
            .EnumerateArray()
            .Single(message =>
                message.GetProperty("role").GetString() == role)
            .GetProperty("content")
            .GetString() ?? string.Empty;

    private sealed class CapturingHttpMessageHandler(
        Func<HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public JsonElement RequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;

            var requestBody = request.Content is null
                ? throw new InvalidOperationException(
                    "Expected a JSON request body.")
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);
            using var requestDocument = JsonDocument.Parse(
                requestBody);
            RequestJson = requestDocument.RootElement.Clone();

            return responseFactory();
        }
    }
}
