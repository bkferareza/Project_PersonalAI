namespace Machine.Core;

public enum MachineInsightKind
{
    Routine,
    LearnedEnergyDeviation,
    MachineFinding
}

public enum MachineInsightImportance
{
    Routine,
    Useful,
    Notable,
    Important
}

public sealed record MachineInsightExplainContext(
    string CandidateId,
    MachineInsightKind Kind,
    string Title,
    string PrimaryText,
    string SecondaryText,
    string EvidenceSummary,
    double? ActualObservedEnergyKilowattHours = null,
    long? ObservedDurationSeconds = null,
    double? ExpectedObservedEnergyKilowattHours = null,
    double? ExpectedLowerEnergyKilowattHours = null,
    double? ExpectedUpperEnergyKilowattHours = null,
    double? DifferenceKilowattHours = null,
    double? DifferencePercent = null,
    double? LearnedCoverage = null,
    MachineLearningEvidenceMaturity? EvidenceMaturity = null,
    decimal? ActualEstimatedCost = null,
    decimal? ExpectedEstimatedCost = null,
    decimal? ExpectedLowerCost = null,
    decimal? ExpectedUpperCost = null,
    string? ElectricityProvider = null,
    string? CurrencyCode = null,
    decimal? RatePerKilowattHour = null,
    DateOnly? RateEffectiveMonth = null);

public sealed record MachineInsightCandidate(
    string Id,
    MachineInsightKind Kind,
    string Title,
    string PrimaryText,
    string SecondaryText,
    string EvidenceSummary,
    MachineInsightImportance Importance,
    DateTimeOffset CreatedAt,
    DateTimeOffset ValidUntil,
    MachineLearningEvidenceMaturity? EvidenceMaturity,
    bool CanSignalNew,
    MachineInsightExplainContext? ExplainContext = null)
{
    public bool IsEligibleAt(DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(Id) &&
        !string.IsNullOrWhiteSpace(Title) &&
        CreatedAt <= now &&
        ValidUntil >= now;
}

public sealed record MachineInsightArbitrationSnapshot(
    MachineInsightCandidate? CurrentInsight,
    bool HasNewUnseenInsight);
