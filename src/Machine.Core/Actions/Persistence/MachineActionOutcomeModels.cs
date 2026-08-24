namespace Machine.Core;

public sealed record MachineActionOutcome(
    Guid ActionId,
    string PlanFingerprint,
    string PreconditionFingerprint,
    MachineActionCapability Capability,
    MachineActionTarget Target,
    string RequestedEffect,
    string RequestedNormalizedState,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    MachineActionResultStatus Result,
    MachineActionVerificationStatus VerificationResult,
    bool Reversible,
    MachineActionUndoStatus UndoState,
    DateTimeOffset? UndoStartedAt,
    DateTimeOffset? UndoCompletedAt,
    MachineActionVerificationStatus UndoVerificationResult,
    string PreviousNormalizedState,
    string? ResultingNormalizedState,
    string? ResultingPreconditionFingerprint,
    bool UserApproved,
    bool UndoUserApproved,
    MachineActionRecoveryPayload? RecoveryPayload,
    MachineActionRecoveryClassification RecoveryClassification,
    MachineActionRecoveryClassification UndoRecoveryClassification,
    string? FailureCode);

public sealed record MachineActionOutcomePersistedState(
    int SchemaVersion,
    IReadOnlyList<MachineActionOutcome> Outcomes);

public enum MachineActionOutcomeStoreLoadStatus
{
    NotAttempted,
    NotFound,
    Loaded,
    Corrupt,
    Incompatible,
    Unavailable
}

public interface IMachineActionOutcomeStore
{
    Task<MachineActionOutcomePersistedState?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        MachineActionOutcomePersistedState state,
        CancellationToken cancellationToken = default);
}

public interface IMachineActionOutcomeStoreDiagnostics
{
    MachineActionOutcomeStoreLoadStatus LastLoadStatus { get; }
}
