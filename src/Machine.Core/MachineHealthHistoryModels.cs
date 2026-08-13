namespace Machine.Core;

public enum MachineHealthHistoryDataStatus
{
    Healthy,
    NotYetPersisted,
    RecoveredFromInvalidState,
    PersistenceTemporarilyUnavailable
}

public sealed record MachineWindowsUpdateMemory(
    MachineWindowsUpdateState State,
    int? PendingUpdateCount,
    DateTimeOffset VerifiedAt,
    DateTimeOffset? LastSuccessfulInstall,
    MachineHealthDataStatus DataStatus);

public sealed record MachineRebootPendingMemory(
    bool? IsPending,
    IReadOnlyList<MachineRebootPendingReason> Reasons,
    DateTimeOffset VerifiedAt,
    bool IsPartial);

public sealed record MachineReliabilityMemory(
    MachineReliabilitySummary Summary,
    IReadOnlyList<MachineReliabilityIncident> RecentIncidents,
    DateTimeOffset? LastUnexpectedShutdown,
    DateTimeOffset? LastVerifiedHardwareFailure,
    MachineHealthDataStatus DataStatus,
    DateTimeOffset VerifiedAt);

public sealed record MachineHealthHistorySnapshot(
    MachineWindowsUpdateMemory? WindowsUpdate,
    MachineRebootPendingMemory? RebootPending,
    MachineReliabilityMemory? Reliability,
    long LifetimeObservedIncidentCount,
    DateTimeOffset? FirstObservedAt,
    DateTimeOffset? LastObservedAt,
    DateTimeOffset? LastPersistedAt,
    bool IsDirty,
    MachineHealthHistoryDataStatus DataStatus);

public sealed record MachineHealthHistoryPersistedState(
    int SchemaVersion,
    MachineWindowsUpdateMemory? WindowsUpdate,
    MachineRebootPendingMemory? RebootPending,
    MachineReliabilityMemory? Reliability,
    long LifetimeObservedIncidentCount,
    IReadOnlyList<string> KnownIncidentFingerprints,
    DateTimeOffset? FirstObservedAt,
    DateTimeOffset? LastObservedAt,
    DateTimeOffset PersistedAt);
