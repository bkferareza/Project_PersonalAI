namespace Machine.Core;

public static class MachineBriefPromptPolicy
{
    public const string CurrentVersion = "matasuri-brief-v1";

    public const int ResponseSchemaVersion = 1;

    public const int MaximumPointCount = 3;
}

public sealed record MachineBriefRequest(
    MachineSituationSnapshot Situation,
    string ModelIdentity,
    string RuntimeVersion);

public sealed record MachineBriefPoint(
    string Text,
    IReadOnlyList<string> EvidenceIds);

public enum MachineBriefValidationState
{
    Valid,
    Repaired,
    RejectedFallback
}

public enum MachineBriefValidationFailure
{
    None,
    Schema,
    Length,
    EvidenceIdentity,
    NumericGrounding,
    EntityGrounding,
    Causality,
    ActionBoundary,
    ForecastBoundary,
    EnglishOnly
}

public sealed record MachineBriefDiagnostics(
    MachineBriefValidationState ValidationState,
    string ValidationReason,
    bool RepairAttempted,
    int RequestCount,
    int EstimatedInputTokenCount,
    int? PromptTokenCount = null,
    int? OutputTokenCount = null,
    TimeSpan? LoadDuration = null,
    TimeSpan? GenerationDuration = null);

public sealed record MachineBrief(
    string Overall,
    IReadOnlyList<string> OverallEvidenceIds,
    IReadOnlyList<MachineBriefPoint> Points,
    string? Outlook,
    IReadOnlyList<string> OutlookEvidenceIds,
    string Model,
    DateTimeOffset GeneratedAt,
    MachineExplanationSource Source,
    MachineBriefDiagnostics Diagnostics,
    string SituationFingerprint = "");

public sealed record MachineBriefDraft(
    string? Overall,
    IReadOnlyList<string>? OverallEvidenceIds,
    IReadOnlyList<MachineBriefDraftPoint>? Points,
    string? Outlook,
    IReadOnlyList<string>? OutlookEvidenceIds);

public sealed record MachineBriefDraftPoint(
    string? Text,
    IReadOnlyList<string>? EvidenceIds);

public sealed record MachineBriefValidatedContent(
    string Overall,
    IReadOnlyList<string> OverallEvidenceIds,
    IReadOnlyList<MachineBriefPoint> Points,
    string? Outlook,
    IReadOnlyList<string> OutlookEvidenceIds);

public sealed record MachineBriefValidationResult(
    bool IsValid,
    MachineBriefValidationFailure Failure,
    string SafeReason,
    MachineBriefValidatedContent? Content)
{
    public static MachineBriefValidationResult Valid(
        MachineBriefValidatedContent content) => new(
            true,
            MachineBriefValidationFailure.None,
            "Valid",
            content);

    public static MachineBriefValidationResult Rejected(
        MachineBriefValidationFailure failure,
        string safeReason) => new(false, failure, safeReason, null);
}

public interface IMachineBriefGenerator
{
    Task<MachineBrief> GenerateAsync(
        MachineBriefRequest request,
        CancellationToken cancellationToken = default);
}

public enum MachineBriefDecisionKind
{
    None,
    UseCached,
    Generate
}

public sealed record MachineBriefDecision(
    MachineBriefDecisionKind Kind,
    string Fingerprint,
    MachineBrief? CachedBrief)
{
    public bool ShouldGenerate => Kind == MachineBriefDecisionKind.Generate;
}
