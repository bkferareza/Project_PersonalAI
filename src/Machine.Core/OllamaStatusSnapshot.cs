namespace Machine.Core;

public sealed record OllamaStatusSnapshot(
    bool IsServiceAvailable,
    string? Version,
    bool IsRunningModelStatusAvailable,
    IReadOnlyList<OllamaRunningModel> RunningModels,
    DateTimeOffset CapturedAt);
