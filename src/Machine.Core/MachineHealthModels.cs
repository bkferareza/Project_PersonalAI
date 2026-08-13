namespace Machine.Core;

public enum MachineHealthDataStatus
{
    Complete,
    Partial,
    Unavailable
}

public enum MachineWindowsUpdateState
{
    UpToDate,
    UpdatesAvailable,
    InstallPending,
    RestartRequired,
    Unknown
}

public enum MachineWindowsUpdateHistoryResult
{
    Succeeded,
    SucceededWithErrors,
    Failed,
    Cancelled,
    InProgress,
    Unknown
}

public enum MachineWindowsUpdateRefreshStatus
{
    Verified,
    CachedAfterFailure,
    Unavailable
}

public sealed record MachineWindowsUpdateHistoryEntry(
    DateTimeOffset OccurredAt,
    string Title,
    string? Category,
    string? KnowledgeBaseId,
    MachineWindowsUpdateHistoryResult Result);

public sealed record MachineWindowsUpdateSnapshot(
    DateTimeOffset CapturedAt,
    DateTimeOffset? VerifiedAt,
    bool? UpdateServiceAvailable,
    DateTimeOffset? LastSuccessfulUpdateScan,
    DateTimeOffset? LastSuccessfulUpdateInstall,
    int? PendingUpdateCount,
    int? PendingImportantUpdateCount,
    MachineWindowsUpdateState UpdateState,
    IReadOnlyList<MachineWindowsUpdateHistoryEntry> RecentUpdateHistory,
    MachineHealthDataStatus DataStatus,
    MachineWindowsUpdateRefreshStatus RefreshStatus,
    string? FailureCode = null);

public enum MachineRebootPendingReason
{
    WindowsUpdate,
    ComponentServicing,
    PendingFileRename,
    ComputerRename,
    Unknown
}

public enum MachineRebootPendingConfidence
{
    Verified,
    Partial,
    Unknown
}

public sealed record MachineRebootPendingIndicator(
    MachineRebootPendingReason Reason,
    bool? IsPresent);

public sealed record MachineRebootPendingSnapshot(
    DateTimeOffset CapturedAt,
    bool? IsPending,
    MachineRebootPendingConfidence Confidence,
    IReadOnlyList<MachineRebootPendingReason> Reasons,
    IReadOnlyList<MachineRebootPendingIndicator> Indicators,
    bool IsPartial);

public enum MachineReliabilityIncidentCategory
{
    ApplicationCrash,
    ApplicationHang,
    UnexpectedShutdown,
    WindowsFailure,
    HardwareFailure,
    UpdateFailure,
    InstallFailure,
    Unknown
}

// This describes historical incident significance. It is deliberately
// independent from current Machine finding severity.
public enum MachineReliabilityIncidentSeverity
{
    Notice,
    Significant,
    Severe
}

public sealed record MachineReliabilityIncident(
    DateTimeOffset OccurredAt,
    MachineReliabilityIncidentCategory Category,
    MachineReliabilityIncidentSeverity Severity,
    string Source,
    string? ApplicationName,
    string? FaultModule,
    string? UpdateIdentifier,
    int? EventId,
    string SummaryCode,
    string? FailureCode = null,
    string? CorrelationId = null);

public sealed record MachineReliabilityWindowSummary(
    int ApplicationCrashCount,
    int ApplicationHangCount,
    int UnexpectedShutdownCount,
    int UpdateFailureCount,
    int HardwareFailureCount,
    int OtherFailureCount)
{
    public int TotalIncidentCount =>
        ApplicationCrashCount +
        ApplicationHangCount +
        UnexpectedShutdownCount +
        UpdateFailureCount +
        HardwareFailureCount +
        OtherFailureCount;
}

public sealed record MachineRecurringApplicationFailure(
    string ApplicationName,
    int IncidentCountLast30Days,
    int IncidentCountLast7Days,
    DateTimeOffset LastOccurredAt);

public sealed record MachineReliabilitySummary(
    MachineReliabilityWindowSummary Last24Hours,
    MachineReliabilityWindowSummary Last7Days,
    MachineReliabilityWindowSummary Last30Days,
    MachineReliabilityIncident? MostRecentIncident,
    IReadOnlyList<MachineRecurringApplicationFailure>
        RecurringApplications);

public sealed record MachineReliabilitySnapshot(
    DateTimeOffset CapturedAt,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset WindowStart,
    MachineHealthDataStatus DataStatus,
    int ReadFailureCount,
    IReadOnlyList<MachineReliabilityIncident> Incidents,
    MachineReliabilitySummary Summary,
    DateTimeOffset? LastUnexpectedShutdownAt,
    DateTimeOffset? LastVerifiedHardwareFailureAt,
    string? FailureCode = null);

public sealed record MachineHealthInsightContext(
    MachineWindowsUpdateState? UpdateState,
    int? PendingUpdateCount,
    DateTimeOffset? UpdateVerifiedAt,
    bool? IsRebootPending,
    IReadOnlyList<MachineRebootPendingReason> RebootReasons,
    MachineReliabilityWindowSummary? ReliabilityLast7Days,
    MachineReliabilityIncident? MostRecentSignificantIncident,
    MachineRecurringApplicationFailure? RecurringApplicationFailure,
    MachineHealthDataStatus ReliabilityDataStatus,
    DateTimeOffset? RebootVerifiedAt = null,
    DateTimeOffset? ReliabilityVerifiedAt = null,
    MachineRebootPendingConfidence RebootConfidence =
        MachineRebootPendingConfidence.Unknown);
