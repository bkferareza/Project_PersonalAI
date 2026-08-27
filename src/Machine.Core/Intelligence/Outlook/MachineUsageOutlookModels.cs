namespace Machine.Core;

public sealed record MachineUsageOutlookRequest(
    MachineUsageForecast Forecast,
    MachineLearningMemoryState GlobalLearningState,
    long CurrentContextSampleCount,
    int CurrentContextObservedDayCount,
    int TotalProfileCount,
    int EstablishedProfileCount,
    IReadOnlyList<MachineLearningRecurringPattern> RelevantPatterns);

public sealed record MachineUsageOutlook(
    string Text,
    string Model,
    DateTimeOffset GeneratedAt,
    MachineExplanationSource Source);

public enum MachineUsageOutlookDecisionKind
{
    None,
    UseCached,
    Generate
}

public sealed record MachineUsageOutlookDecision(
    MachineUsageOutlookDecisionKind Kind,
    string Fingerprint,
    MachineUsageOutlook? CachedOutlook)
{
    public bool ShouldGenerate =>
        Kind == MachineUsageOutlookDecisionKind.Generate;
}
