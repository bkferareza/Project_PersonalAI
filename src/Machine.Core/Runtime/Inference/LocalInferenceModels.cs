namespace Machine.Core;

public enum LocalInferenceModelState
{
    Asleep,
    Loading,
    Ready,
    Generating,
    Faulted
}

public enum LocalInferenceMessageRole
{
    System,
    User,
    Assistant
}

public enum LocalInferenceFailureKind
{
    RuntimeUnavailable,
    ModelUnavailable,
    Timeout,
    ProcessExited,
    InvalidResponse,
    Transport
}

public sealed record LocalInferenceMessage(
    LocalInferenceMessageRole Role,
    string Content);

public sealed record LocalInferenceRequest(
    string Model,
    IReadOnlyList<LocalInferenceMessage> Messages,
    int ContextLength,
    int MaximumOutputTokens,
    double Temperature,
    bool DisableReasoning = true,
    TimeSpan? Timeout = null);

public sealed record LocalInferenceFailure(
    LocalInferenceFailureKind Kind,
    string SafeMessage);

public sealed record LocalInferenceResult(
    string? Text,
    string? Model,
    bool ContainsToolCalls = false,
    LocalInferenceFailure? Failure = null,
    int? PromptTokenCount = null,
    int? OutputTokenCount = null,
    TimeSpan? LoadDuration = null,
    TimeSpan? GenerationDuration = null)
{
    public bool IsSuccess =>
        Failure is null &&
        !string.IsNullOrWhiteSpace(Text) &&
        !string.IsNullOrWhiteSpace(Model);
}

public sealed record LocalInferenceLoadedModel(
    string Name,
    string? ParameterSize,
    string? Quantization,
    long SizeBytes,
    long ResidentBytes,
    int ContextLength,
    DateTimeOffset? ExpiresAt);

public sealed record LocalInferenceStatus(
    bool IsRuntimeAvailable,
    string RuntimeName,
    string? RuntimeVersion,
    LocalInferenceModelState ModelState,
    IReadOnlyList<LocalInferenceLoadedModel> LoadedModels,
    int? ProcessId,
    bool IsProcessOwned,
    DateTimeOffset CapturedAt,
    LocalInferenceFailure? Failure = null,
    DateTimeOffset? StartedAt = null,
    int? Port = null,
    string? Backend = null,
    int? ContextLength = null,
    string? ModelSha256 = null,
    TimeSpan? ResidencyRemaining = null,
    IReadOnlyList<string>? Diagnostics = null,
    TimeSpan? LastLoadDuration = null,
    TimeSpan? LastGenerationDuration = null,
    string? ConfiguredModelName = null,
    string? ConfiguredQuantization = null,
    long? ConfiguredModelSizeBytes = null);

public sealed record LocalInferenceStartResult(
    bool IsAvailable,
    bool WasStarted,
    bool IsProcessOwned);
