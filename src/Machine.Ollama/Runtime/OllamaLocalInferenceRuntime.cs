using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Machine.Core;

namespace Machine.Ollama;

public sealed partial class OllamaLocalInferenceRuntime
    : ILocalInferenceRuntime
{
    private const string ChatEndpoint = "api/chat";
    private const string ModelResidency = "10m";
    private readonly HttpClient _inferenceHttpClient;
    private readonly IOllamaRuntimeBootstrapper _bootstrapper;
    private readonly IOllamaStatusProvider _statusProvider;
    private readonly SemaphoreSlim _generationGate = new(1, 1);
    private bool _isOwned;

    public OllamaLocalInferenceRuntime(
        HttpClient statusHttpClient,
        HttpClient inferenceHttpClient)
        : this(
            inferenceHttpClient,
            new OllamaRuntimeBootstrapper(statusHttpClient),
            new OllamaStatusProvider(statusHttpClient))
    {
    }

    public OllamaLocalInferenceRuntime(
        HttpClient inferenceHttpClient,
        IOllamaRuntimeBootstrapper bootstrapper,
        IOllamaStatusProvider statusProvider)
    {
        ArgumentNullException.ThrowIfNull(inferenceHttpClient);
        ArgumentNullException.ThrowIfNull(bootstrapper);
        ArgumentNullException.ThrowIfNull(statusProvider);
        _inferenceHttpClient = inferenceHttpClient;
        _bootstrapper = bootstrapper;
        _statusProvider = statusProvider;
    }

    public async Task<LocalInferenceStartResult> EnsureAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _bootstrapper.EnsureAvailableAsync(
            cancellationToken).ConfigureAwait(false);
        _isOwned |= result.StartedByMachine;
        return new(
            result.IsAvailable,
            result.StartedByMachine,
            _isOwned);
    }

    public async Task<LocalInferenceStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _statusProvider.GetStatusAsync(
            cancellationToken).ConfigureAwait(false);
        var models = snapshot.RunningModels.Select(model =>
            new LocalInferenceLoadedModel(
                model.Name,
                model.ParameterSize,
                model.QuantizationLevel,
                model.SizeBytes,
                model.SizeVramBytes,
                model.ContextLength,
                model.ExpiresAt)).ToArray();
        return new(
            snapshot.IsServiceAvailable,
            "Ollama (transitional)",
            snapshot.Version,
            snapshot.IsServiceAvailable
                ? models.Length == 0
                    ? LocalInferenceModelState.Asleep
                    : LocalInferenceModelState.Ready
                : LocalInferenceModelState.Faulted,
            models,
            ProcessId: null,
            IsProcessOwned: _isOwned,
            snapshot.CapturedAt,
            snapshot.IsServiceAvailable
                ? null
                : new LocalInferenceFailure(
                    LocalInferenceFailureKind.RuntimeUnavailable,
                    "The local inference runtime is unavailable."));
    }

    public async Task<LocalInferenceResult> GenerateAsync(
        LocalInferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        ArgumentNullException.ThrowIfNull(request.Messages);
        cancellationToken.ThrowIfCancellationRequested();

        await _generationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var requestedTimeout = request.Timeout;
            using var timeout = requestedTimeout is not null
                ? CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken)
                : null;
            if (timeout is not null)
            {
                timeout.CancelAfter(requestedTimeout.GetValueOrDefault());
            }

            var effectiveToken = timeout?.Token ?? cancellationToken;
            var chatRequest = new ChatRequest(
                request.Model,
                Stream: false,
                Think: !request.DisableReasoning,
                KeepAlive: ModelResidency,
                request.Messages.Select(MapMessage).ToArray(),
                new ChatOptions(
                    request.Temperature,
                    request.ContextLength,
                    request.MaximumOutputTokens));
            using var response = await _inferenceHttpClient.PostAsJsonAsync(
                ChatEndpoint,
                chatRequest,
                OllamaInferenceJsonSerializerContext.Default.ChatRequest,
                effectiveToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            ChatResponse? chatResponse;
            try
            {
                chatResponse = await response.Content.ReadFromJsonAsync(
                    OllamaInferenceJsonSerializerContext.Default.ChatResponse,
                    effectiveToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return InvalidResponse();
            }

            if (chatResponse?.Message is null)
            {
                return InvalidResponse();
            }

            return new(
                chatResponse.Message.Content,
                chatResponse.Model,
                ContainsToolCalls(chatResponse.Message.ToolCalls));
        }
        finally
        {
            _generationGate.Release();
        }
    }

    public Task RequestUnloadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(
        CancellationToken cancellationToken = default) =>
        _bootstrapper.ShutdownAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _bootstrapper.DisposeAsync().ConfigureAwait(false);
        _generationGate.Dispose();
    }

    private static ChatMessage MapMessage(LocalInferenceMessage message) =>
        new(
            message.Role switch
            {
                LocalInferenceMessageRole.System => "system",
                LocalInferenceMessageRole.User => "user",
                LocalInferenceMessageRole.Assistant => "assistant",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(message), message.Role, null)
            },
            message.Content);

    private static LocalInferenceResult InvalidResponse() =>
        new(
            Text: null,
            Model: null,
            Failure: new LocalInferenceFailure(
                LocalInferenceFailureKind.InvalidResponse,
                "The local model returned an invalid response."));

    private static bool ContainsToolCalls(JsonElement toolCalls) =>
        toolCalls.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => false,
            JsonValueKind.Array => toolCalls.GetArrayLength() > 0,
            _ => true
        };

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("think")] bool Think,
        [property: JsonPropertyName("keep_alive")] string KeepAlive,
        [property: JsonPropertyName("messages")] ChatMessage[] Messages,
        [property: JsonPropertyName("options")] ChatOptions Options);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatOptions(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("num_ctx")] int ContextLength,
        [property: JsonPropertyName("num_predict")]
        int MaximumPredictedTokens);

    private sealed record ChatResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("message")]
        ChatResponseMessage? Message);

    private sealed record ChatResponseMessage(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_calls")] JsonElement ToolCalls);

    [JsonSerializable(typeof(ChatRequest))]
    [JsonSerializable(typeof(ChatResponse))]
    private sealed partial class OllamaInferenceJsonSerializerContext
        : JsonSerializerContext;
}
