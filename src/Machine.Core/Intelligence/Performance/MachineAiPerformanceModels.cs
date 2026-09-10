namespace Machine.Core;

public enum MachineAiRequestType
{
    Brief,
    Explain
}

public sealed record MachineAiGenerationMetric(
    DateTimeOffset Timestamp,
    string ModelIdentity,
    string Quantization,
    MachineAiRequestType RequestType,
    string SituationFingerprint,
    int EvidenceItemCount,
    int? InputTokens,
    int? OutputTokens,
    bool ColdLoad,
    TimeSpan? LoadLatency,
    TimeSpan? PromptEvaluationLatency,
    TimeSpan? GenerationLatency,
    TimeSpan TotalLatency,
    double? GenerationTokensPerSecond,
    bool FirstPassGrounded,
    bool RepairAttempted,
    bool RepairSucceeded,
    bool FallbackUsed,
    MachineBriefValidationFailure RejectionCategory);

public sealed record MachineAiQualitySummary(
    int RecentWindowSize,
    int RecentGenerationCount,
    bool HasMeaningfulSample,
    double? FirstPassGroundedPercent,
    double? RepairPercent,
    double? FallbackPercent,
    TimeSpan? MedianResponseTime,
    TimeSpan? P95ResponseTime,
    TimeSpan? MedianColdLoadTime,
    TimeSpan? MedianPromptEvaluationTime,
    double? MedianGenerationTokensPerSecond,
    double? MedianEvidenceItemCount,
    double? MedianInputTokens,
    double? MedianOutputTokens,
    IReadOnlyDictionary<MachineBriefValidationFailure, int>
        RejectionCategories);

public interface IMachineAiMetricRecorder
{
    Task RecordAsync(
        MachineAiGenerationMetric metric,
        CancellationToken cancellationToken = default);
}

public interface IMachineAiMetricStore
{
    Task<MachineAiPerformancePersistedState?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        MachineAiPerformancePersistedState state,
        CancellationToken cancellationToken = default);
}

public sealed record MachineAiPerformancePersistedState(
    int SchemaVersion,
    IReadOnlyList<MachineAiGenerationMetric> Generations,
    DateTimeOffset PersistedAt);
