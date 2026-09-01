using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Machine.Core;
using Machine.Inference;

namespace Machine.Tests;

// Adapts deterministic HTTP capture fixtures to the runtime-neutral generator
// request. No production type depends on this test transport.
internal sealed class LocalMachineIntelligenceGeneratorTestHarness
    : IMachineStateExplainer, IMachineBriefGenerator
{
    private readonly LocalMachineIntelligenceGenerator _generator;

    public LocalMachineIntelligenceGeneratorTestHarness(
        HttpClient captureClient,
        string modelName)
    {
        ArgumentNullException.ThrowIfNull(captureClient);
        _generator = new(
            new HttpCaptureInferenceRuntime(captureClient),
            modelName);
    }

    public Task<MachineStateExplanation> ExplainAsync(
        MachineStateExplanationRequest request,
        CancellationToken cancellationToken = default) =>
        _generator.ExplainAsync(request, cancellationToken);

    public Task<MachineBrief> GenerateAsync(
        MachineBriefRequest request,
        CancellationToken cancellationToken = default) =>
        _generator.GenerateAsync(request, cancellationToken);

    private sealed class HttpCaptureInferenceRuntime(HttpClient client)
        : ILocalInferenceRuntime
    {
        public Task<LocalInferenceStartResult> EnsureAvailableAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalInferenceStartResult(true, false, false));

        public Task<LocalInferenceStatus> GetStatusAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalInferenceStatus(
                true,
                "Test capture runtime",
                "1",
                LocalInferenceModelState.Ready,
                [],
                null,
                false,
                DateTimeOffset.UtcNow));

        public async Task<LocalInferenceResult> GenerateAsync(
            LocalInferenceRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var captureRequest = new CaptureRequest(
                request.Model,
                request.DisableReasoning,
                request.Timeout is { } timeout
                    ? (long)timeout.TotalMilliseconds
                    : null,
                request.Messages.Select(message => new CaptureMessage(
                    message.Role.ToString().ToLowerInvariant(),
                    message.Content)).ToArray(),
                new CaptureOptions(
                    request.ContextLength,
                    request.MaximumOutputTokens,
                    request.Temperature));
            using var response = await client.PostAsJsonAsync(
                "capture",
                captureRequest,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            CaptureResponse? parsed;
            try
            {
                parsed = await response.Content.ReadFromJsonAsync<
                    CaptureResponse>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return InvalidResponse();
            }

            if (parsed?.Message is null)
            {
                return InvalidResponse();
            }

            return new(
                parsed.Message.Content,
                parsed.Model,
                ContainsToolCalls(parsed.Message.ToolCalls));
        }

        public Task RequestUnloadAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ShutdownAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static LocalInferenceResult InvalidResponse() => new(
            null,
            null,
            Failure: new LocalInferenceFailure(
                LocalInferenceFailureKind.InvalidResponse,
                "Invalid test response."));

        private static bool ContainsToolCalls(JsonElement value) =>
            value.ValueKind switch
            {
                JsonValueKind.Undefined or JsonValueKind.Null => false,
                JsonValueKind.Array => value.GetArrayLength() > 0,
                _ => true
            };
    }

    private sealed record CaptureRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("disable_reasoning")]
        bool DisableReasoning,
        [property: JsonPropertyName("timeout_ms")] long? TimeoutMilliseconds,
        [property: JsonPropertyName("messages")]
        CaptureMessage[] Messages,
        [property: JsonPropertyName("options")] CaptureOptions Options);

    private sealed record CaptureMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record CaptureOptions(
        [property: JsonPropertyName("context_length")] int ContextLength,
        [property: JsonPropertyName("maximum_output_tokens")]
        int MaximumOutputTokens,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record CaptureResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("message")]
        CaptureResponseMessage? Message);

    private sealed record CaptureResponseMessage(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_calls")] JsonElement ToolCalls);
}
