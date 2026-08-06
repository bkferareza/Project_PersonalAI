namespace Machine.Core;

public sealed record OllamaRunningModel(
    string Name,
    string? ParameterSize,
    string? QuantizationLevel,
    long SizeBytes,
    long SizeVramBytes,
    int ContextLength,
    DateTimeOffset? ExpiresAt);
