using Machine.Core;

namespace Machine.AiEvaluation;

public sealed record MatasuriEvaluationExpectation(
    IReadOnlyList<string> RequiredEvidenceIds,
    IReadOnlyList<string> AllowedEvidenceIds,
    IReadOnlyList<string> ForbiddenEvidenceIds,
    bool OutlookAvailable,
    bool UncertaintyRequired = false);

public sealed record MatasuriEvaluationScenario(
    string Id,
    string Category,
    MachineSituationSnapshot Situation,
    MatasuriEvaluationExpectation Expectation,
    bool HeldOut = true);

public sealed record MatasuriScenarioResult(
    string ScenarioId,
    string Category,
    bool FirstPassStructurallyValid,
    bool FirstPassGroundingValid,
    MachineBriefValidationFailure FirstPassFailure,
    bool RepairRequired,
    bool RepairSucceeded,
    bool FallbackRequired,
    bool RequiredEvidenceCovered,
    bool ForbiddenEvidenceSelected,
    bool OutlookExpectationMet,
    long TotalLatencyMilliseconds,
    long? ColdLoadMilliseconds,
    long? PromptEvaluationMilliseconds,
    long? GenerationMilliseconds,
    int? PromptTokens,
    int? OutputTokens,
    double? TokensPerSecond,
    string Overall,
    IReadOnlyList<string> Points,
    string? Outlook,
    IReadOnlyList<string> FinalEvidenceIds);

public sealed record MatasuriModelEvaluationReport(
    int SchemaVersion,
    DateTimeOffset EvaluatedAt,
    string ModelName,
    string Quantization,
    long ModelSizeBytes,
    string ModelSha256,
    string RuntimeVersion,
    string RuntimeCommit,
    int TotalScenarios,
    int FirstPassStructurallyValid,
    int FirstPassGroundingValid,
    int RepairRequired,
    int RepairSucceeded,
    int FallbackRequired,
    int UnsupportedNumericAttempts,
    int UnsupportedEntityAttempts,
    int UnsupportedCausalAttempts,
    int UnsupportedActionAttempts,
    int RequiredEvidenceCovered,
    int ForbiddenEvidenceSelections,
    double AverageLatencyMilliseconds,
    double MedianLatencyMilliseconds,
    double P95LatencyMilliseconds,
    long? ColdLoadMilliseconds,
    long? ModelResidentBytes,
    long? ProcessWorkingSetBytes,
    long UnloadLatencyMilliseconds,
    IReadOnlyList<MatasuriScenarioResult> Scenarios);
