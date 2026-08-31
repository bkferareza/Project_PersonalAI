using System.Net;
using System.Text;
using System.Text.Json;
using Machine.Core;
using Machine.Inference;
using Machine.Ollama;

namespace Machine.Tests;

public sealed class LocalInferenceBoundaryTests
{
    [Fact]
    public async Task GeneratorUsesRuntimeNeutralRequest()
    {
        var runtime = new RecordingRuntime(new LocalInferenceResult(
            "No deterministic issue is visible in the current snapshot.",
            "qwen3.5:4b-runtime"));
        var generator = new LocalMachineIntelligenceGenerator(
            runtime,
            "qwen3.5:4b");

        var result = await generator.ExplainAsync(new(
            new MachineIdentity("Matasuri", "Windows 11", "X64"),
            new MachineResourceSnapshot(
                25d,
                16_000_000_000,
                8_000_000_000,
                DateTimeOffset.UnixEpoch),
            [],
            Findings: new MachineFindingsSnapshot(
                MachineOverallState.Stable,
                [])));

        Assert.Equal(MachineExplanationSource.LocalModel, result.Source);
        var request = Assert.IsType<LocalInferenceRequest>(
            runtime.LastRequest);
        Assert.Equal("qwen3.5:4b", request.Model);
        Assert.Equal(4096, request.ContextLength);
        Assert.Equal(96, request.MaximumOutputTokens);
        Assert.Equal(0.1d, request.Temperature);
        Assert.True(request.DisableReasoning);
        Assert.Equal(
            [LocalInferenceMessageRole.System, LocalInferenceMessageRole.User],
            request.Messages.Select(message => message.Role));
    }

    [Fact]
    public async Task TransitionalAdapterOwnsOllamaWireTranslation()
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/")
        };
        var runtime = new OllamaLocalInferenceRuntime(
            client,
            new AvailableBootstrapper(),
            new AvailableStatusProvider());

        var result = await runtime.GenerateAsync(new(
            "qwen3.5:4b",
            [
                new LocalInferenceMessage(
                    LocalInferenceMessageRole.System,
                    "system"),
                new LocalInferenceMessage(
                    LocalInferenceMessageRole.User,
                    "user")
            ],
            4096,
            96,
            0.1d));

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/chat", handler.Uri?.AbsolutePath);
        Assert.Equal("qwen3.5:4b",
            handler.Json.GetProperty("model").GetString());
        Assert.Equal("10m",
            handler.Json.GetProperty("keep_alive").GetString());
        Assert.False(handler.Json.GetProperty("stream").GetBoolean());
        Assert.False(handler.Json.GetProperty("think").GetBoolean());
        Assert.Equal(4096, handler.Json.GetProperty("options")
            .GetProperty("num_ctx").GetInt32());
    }

    private sealed class RecordingRuntime(LocalInferenceResult result)
        : ILocalInferenceRuntime
    {
        public LocalInferenceRequest? LastRequest { get; private set; }

        public Task<LocalInferenceStartResult> EnsureAvailableAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalInferenceStartResult(true, false, true));

        public Task<LocalInferenceStatus> GetStatusAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LocalInferenceResult> GenerateAsync(
            LocalInferenceRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }

        public Task RequestUnloadAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ShutdownAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class AvailableBootstrapper : IOllamaRuntimeBootstrapper
    {
        public Task<OllamaRuntimeBootstrapResult> EnsureAvailableAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new OllamaRuntimeBootstrapResult(
                true,
                false,
                true));

        public Task ShutdownAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class AvailableStatusProvider : IOllamaStatusProvider
    {
        public Task<OllamaStatusSnapshot> GetStatusAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new OllamaStatusSnapshot(
                true,
                "test",
                true,
                [],
                DateTimeOffset.UtcNow));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public JsonElement Json { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            using var document = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken));
            Json = document.RootElement.Clone();
            var response = JsonSerializer.Serialize(new
            {
                model = "qwen3.5:4b-runtime",
                message = new
                {
                    role = "assistant",
                    content = "Grounded local response."
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
