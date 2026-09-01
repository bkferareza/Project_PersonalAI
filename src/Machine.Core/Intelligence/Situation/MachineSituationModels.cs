namespace Machine.Core;

public enum MachineSituationCategory
{
    Now,
    Recently,
    LearnedNormal,
    Today,
    Forward,
    ActionOutcome,
    LearningConfidence,
    SelfHealth
}

public enum MachineSituationTimeScope
{
    Current,
    Recent,
    Last24Hours,
    Last7Days,
    Today,
    CurrentContext,
    NextObservedHour,
    EndOfDay
}

public enum MachineSituationImportance
{
    Routine,
    Context,
    Notable,
    Important,
    Critical
}

public enum MachineSituationFreshness
{
    Current,
    Recent,
    Historical,
    Stale,
    Unknown
}

public enum MachineSituationEvidenceMaturity
{
    Verified,
    Early,
    Provisional,
    Established,
    Unavailable
}

public sealed record MachineSituationEvidenceItem(
    string Id,
    MachineSituationCategory Category,
    MachineSituationTimeScope TimeScope,
    MachineSituationImportance Importance,
    MachineSituationFreshness Freshness,
    MachineSituationEvidenceMaturity Maturity,
    string Summary,
    IReadOnlyList<string> DisplayValues,
    IReadOnlyList<string> EntityNames,
    bool AllowsCausalLanguage = false);

public sealed record MachineLearningAwareness(
    MachineLearningMemoryState GlobalState,
    long LifetimeAcceptedObservationCount,
    int RetainedObservationCount,
    int LearnedContextCount,
    int CompactProfileCount,
    int EstablishedContextCount,
    MachineLearningContextKey? CurrentContext,
    long CurrentContextSampleCount,
    int CurrentContextObservedDayCount,
    MachineLearningConfidence CurrentContextMaturity,
    MachineLearningFreshness? CurrentContextFreshness,
    MachineLearningEvidenceMaturity CurrentPowerMaturity,
    MachineLearningRecurringPattern? ApplicableRecurringPattern,
    MachineLearningPatternReadinessBlocker PatternReadinessBlocker,
    MachineUsageForecastAvailabilityReason ForecastAvailability,
    double ForecastCoverage);

public sealed record MachineSituationSnapshot(
    int SchemaVersion,
    DateTimeOffset CapturedAt,
    MachineOverallState GlobalPosture,
    int CandidateEvidenceCount,
    IReadOnlyList<MachineSituationEvidenceItem> Evidence,
    MachineLearningAwareness LearningAwareness)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record MachineSituationInput
{
    public MachineFindingsSnapshot? Findings { get; init; }

    public MachineResourceSnapshot? Resources { get; init; }

    public MachineGpuTelemetrySnapshot? Gpu { get; init; }

    public MachineCpuHardwareSnapshot? CpuHardware { get; init; }

    public MachineStorageSnapshot? Storage { get; init; }

    public MachineStorageDeviceHealthCollection? StorageHealth { get; init; }

    public MachineNetworkSnapshot? Network { get; init; }

    public MachineSessionSnapshot? Session { get; init; }

    public MachinePowerEstimate? Power { get; init; }

    public MachineWindowsUpdateSnapshot? WindowsUpdate { get; init; }

    public MachineRebootPendingSnapshot? RebootPending { get; init; }

    public MachineReliabilitySnapshot? Reliability { get; init; }

    public MachineLearningDashboardSnapshot? Learning { get; init; }

    public MachineHistorySnapshot? History { get; init; }

    public MachineTodayEnergyCostProjection? Today { get; init; }

    public MachineTodayLearnedEnergyComparison? TodayComparison { get; init; }

    public MachineUsageForecast? Forecast { get; init; }

    public MachineStartupInventorySnapshot? Startup { get; init; }

    public IReadOnlyList<MachineActionOutcome> ActionOutcomes { get; init; } =
        [];

    public LocalInferenceStatus? InferenceStatus { get; init; }
}
