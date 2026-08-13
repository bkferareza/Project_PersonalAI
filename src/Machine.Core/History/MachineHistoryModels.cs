using System.Text.Json.Serialization;

namespace Machine.Core;

public enum MachineHistoryResolution
{
    FiveMinutes,
    Hour,
    Day,
    Month
}

public enum MachineHistoryRange
{
    Last24Hours,
    Last7Days,
    Last30Days,
    All
}

public enum MachineHistoryDataStatus
{
    Healthy,
    NotYetPersisted,
    RecoveredFromInvalidState,
    PersistenceTemporarilyUnavailable
}

public enum MachineHistoryEventKind
{
    MatasuriSessionStarted,
    MatasuriSessionEnded,
    PreviousSessionInterrupted,
    ActivityBecameActive,
    ActivityBecameIdle,
    MachineStateChanged,
    UnexpectedShutdownRecorded,
    ApplicationFailureRecorded,
    ReliabilityIncidentRecorded,
    WindowsUpdateStateChanged,
    RestartPendingChanged,
    LearningProfileEstablished,
    BroaderPatternEstablished,
    SystemSuspend,
    SystemResumeAutomatic,
    SystemResumeSuspend
}

public sealed record MachineHistoryNumericSummary(
    long SampleCount,
    double Minimum,
    double Maximum,
    double Mean);

public sealed record MachineHistoryStateDurations(
    long StableTicks,
    long AttentionTicks,
    long WarningTicks,
    long CriticalTicks,
    long UnknownTicks)
{
    [JsonIgnore]
    public TimeSpan Stable => TimeSpan.FromTicks(StableTicks);
    [JsonIgnore]
    public TimeSpan Attention => TimeSpan.FromTicks(AttentionTicks);
    [JsonIgnore]
    public TimeSpan Warning => TimeSpan.FromTicks(WarningTicks);
    [JsonIgnore]
    public TimeSpan Critical => TimeSpan.FromTicks(CriticalTicks);
    [JsonIgnore]
    public TimeSpan Unknown => TimeSpan.FromTicks(UnknownTicks);
}

public sealed record MachineHistoryActivityDurations(
    long ActiveTicks,
    long IdleTicks)
{
    [JsonIgnore]
    public TimeSpan Active => TimeSpan.FromTicks(ActiveTicks);
    [JsonIgnore]
    public TimeSpan Idle => TimeSpan.FromTicks(IdleTicks);
}

public sealed record MachineHistoryRollup(
    DateTimeOffset BucketStart,
    DateTimeOffset BucketEnd,
    long ObservedDurationTicks,
    MachineHistoryNumericSummary? CpuUtilizationPercent,
    MachineHistoryNumericSummary? MemoryUtilizationPercent,
    MachineHistoryNumericSummary? NetworkReceiveBytesPerSecond,
    MachineHistoryNumericSummary? NetworkSendBytesPerSecond,
    MachineHistoryNumericSummary? SystemVolumeFreePercent,
    MachineHistoryStateDurations StateDurations,
    MachineHistoryActivityDurations ActivityDurations,
    MachineHistoryNumericSummary? GpuUtilizationPercent = null,
    MachineHistoryNumericSummary? GpuMemoryUtilizationPercent = null,
    MachineHistoryNumericSummary? GpuTemperatureCelsius = null,
    MachineHistoryNumericSummary? GpuBoardPowerWatts = null,
    MachineHistoryNumericSummary? CpuTemperatureCelsius = null,
    MachineHistoryNumericSummary? CpuPackagePowerWatts = null,
    MachineHistoryNumericSummary? StorageTemperatureCelsius = null,
    MachineHistoryNumericSummary? EstimatedSystemPowerWatts = null,
    MachineHistoryNumericSummary? EnergyWattHours = null)
{
    [JsonIgnore]
    public TimeSpan ObservedDuration =>
        TimeSpan.FromTicks(ObservedDurationTicks);
}

public sealed record MachineHistoryObservation(
    DateTimeOffset CapturedAt,
    double? CpuUtilizationPercent,
    double? MemoryUtilizationPercent,
    double? NetworkReceiveBytesPerSecond,
    double? NetworkSendBytesPerSecond,
    MachineUserActivityState? ActivityState,
    MachineOverallState? MachineState,
    double? SystemVolumeFreePercent = null,
    double? GpuUtilizationPercent = null,
    double? GpuMemoryUtilizationPercent = null,
    double? GpuTemperatureCelsius = null,
    double? GpuBoardPowerWatts = null,
    double? CpuTemperatureCelsius = null,
    double? CpuPackagePowerWatts = null,
    double? StorageTemperatureCelsius = null,
    double? EstimatedSystemPowerWatts = null,
    double? EnergyWattHours = null);

public sealed record MachineHistoryEvent(
    DateTimeOffset OccurredAt,
    MachineHistoryEventKind Kind,
    string Title,
    string? Detail,
    string Source,
    string Fingerprint,
    int Count = 1,
    DateTimeOffset? PeriodStart = null,
    DateTimeOffset? PeriodEnd = null);

public sealed record MachineHistorySnapshot(
    MachineHistoryRange Range,
    MachineHistoryResolution Resolution,
    IReadOnlyList<MachineHistoryRollup> Rollups,
    IReadOnlyList<MachineHistoryEvent> Events,
    long TotalObservedDurationTicks,
    DateTimeOffset? FirstObservedAt,
    DateTimeOffset? LastObservedAt,
    DateTimeOffset? LastPersistedAt,
    bool IsDirty,
    MachineHistoryDataStatus DataStatus)
{
    [JsonIgnore]
    public TimeSpan TotalObservedDuration =>
        TimeSpan.FromTicks(TotalObservedDurationTicks);
}

public sealed record MachineHistoryPersistedState(
    int SchemaVersion,
    IReadOnlyList<MachineHistoryRollup> FiveMinuteRollups,
    IReadOnlyList<MachineHistoryRollup> HourlyRollups,
    IReadOnlyList<MachineHistoryRollup> DailyRollups,
    IReadOnlyList<MachineHistoryRollup> MonthlyRollups,
    MachineHistoryRollup? ActiveFiveMinuteRollup,
    MachineHistoryRollup? ActiveHourlyRollup,
    MachineHistoryRollup? ActiveDailyRollup,
    MachineHistoryRollup? ActiveMonthlyRollup,
    IReadOnlyList<MachineHistoryEvent> Events,
    MachineHistoryObservation? LastObservation,
    bool SessionOpen,
    MachineWindowsUpdateState? LastWindowsUpdateState,
    bool? LastRestartPending,
    DateTimeOffset? FirstObservedAt,
    DateTimeOffset? LastObservedAt,
    DateTimeOffset PersistedAt);
