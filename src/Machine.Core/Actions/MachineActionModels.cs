namespace Machine.Core;

public enum MachineActionCapability
{
    SetStartupEnabled = 1
}

public enum MachineActionTargetKind
{
    StartupRegistryRunEntry = 1,
    StartupFolderEntry = 2
}

public enum MachineActionAvailability
{
    Supported,
    Unsupported,
    PermissionRequired
}

public enum MachineActionResultStatus
{
    InProgress,
    SucceededVerified,
    Failed,
    ChangedButVerificationFailed,
    TargetChanged,
    Unsupported,
    PermissionRequired,
    NotApproved,
    RecoveryUnknown
}

public enum MachineActionVerificationStatus
{
    NotAttempted,
    Verified,
    Failed,
    Indeterminate
}

public enum MachineActionUndoStatus
{
    NotAvailable,
    NotApproved,
    Unsupported,
    PermissionRequired,
    Available,
    InProgress,
    SucceededVerified,
    Failed,
    ChangedButVerificationFailed,
    TargetChanged,
    RecoveryUnknown
}

public enum MachineActionRecoveryClassification
{
    NotRequired,
    Applied,
    NotApplied,
    Unknown
}

public enum MachineActionApprovalKind
{
    Execute,
    Undo
}

public sealed record MachineActionTarget(
    MachineActionTargetKind Kind,
    string StableIdentity,
    string DisplayName);

public sealed record MachineActionRecoveryPayload(
    int Version,
    string ProviderData);

public sealed record MachineActionTargetState(
    MachineActionAvailability Availability,
    string NormalizedState,
    string PreconditionFingerprint)
{
    public static MachineActionTargetState Supported(
        MachineActionTarget target,
        string normalizedState,
        params string[] providerEvidence) =>
        new(
            MachineActionAvailability.Supported,
            normalizedState,
            MachineActionFingerprint.CreatePrecondition(
                target,
                normalizedState,
                providerEvidence));
}

public sealed class MachineActionPlan
{
    private MachineActionPlan(
        Guid actionId,
        MachineActionCapability capability,
        MachineActionTarget target,
        string currentState,
        string currentNormalizedState,
        string requestedState,
        string requestedNormalizedState,
        string changeCategory,
        string expectedEffect,
        string notAffected,
        bool reversible,
        bool requiresElevation,
        string verification,
        string limitations,
        string preconditionFingerprint,
        MachineActionRecoveryPayload? recoveryPayload,
        DateTimeOffset createdAt)
    {
        ActionId = actionId;
        Capability = capability;
        Target = target;
        CurrentState = currentState;
        CurrentNormalizedState = currentNormalizedState;
        RequestedState = requestedState;
        RequestedNormalizedState = requestedNormalizedState;
        ChangeCategory = changeCategory;
        ExpectedEffect = expectedEffect;
        NotAffected = notAffected;
        Reversible = reversible;
        RequiresElevation = requiresElevation;
        Verification = verification;
        Limitations = limitations;
        PreconditionFingerprint = preconditionFingerprint;
        RecoveryPayload = recoveryPayload;
        CreatedAt = createdAt;
        PlanFingerprint = MachineActionFingerprint.CreatePlan(this);
    }

    public Guid ActionId { get; }

    public MachineActionCapability Capability { get; }

    public MachineActionTarget Target { get; }

    public string CurrentState { get; }

    public string CurrentNormalizedState { get; }

    public string RequestedState { get; }

    public string RequestedNormalizedState { get; }

    public string ChangeCategory { get; }

    public string ExpectedEffect { get; }

    public string NotAffected { get; }

    public bool Reversible { get; }

    public bool RequiresElevation { get; }

    public string Verification { get; }

    public string Limitations { get; }

    public string PreconditionFingerprint { get; }

    public MachineActionRecoveryPayload? RecoveryPayload { get; }

    public DateTimeOffset CreatedAt { get; }

    public string PlanFingerprint { get; }

    public static MachineActionPlan Create(
        MachineActionCapability capability,
        MachineActionTarget target,
        string currentState,
        string currentNormalizedState,
        string requestedState,
        string requestedNormalizedState,
        string changeCategory,
        string expectedEffect,
        string notAffected,
        bool reversible,
        bool requiresElevation,
        string verification,
        string limitations,
        string preconditionFingerprint,
        MachineActionRecoveryPayload? recoveryPayload = null,
        Guid? actionId = null,
        DateTimeOffset? createdAt = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        MachineActionGuard.RequireAllowlisted(capability, target.Kind);
        MachineActionGuard.RequireText(target.StableIdentity, 2_048,
            nameof(target));
        MachineActionGuard.RequireText(target.DisplayName, 256,
            nameof(target));
        MachineActionGuard.RequireText(currentState, 512,
            nameof(currentState));
        MachineActionGuard.RequireText(currentNormalizedState, 4_096,
            nameof(currentNormalizedState));
        MachineActionGuard.RequireText(requestedState, 512,
            nameof(requestedState));
        MachineActionGuard.RequireText(requestedNormalizedState, 4_096,
            nameof(requestedNormalizedState));
        MachineActionGuard.RequireText(changeCategory, 128,
            nameof(changeCategory));
        MachineActionGuard.RequireText(expectedEffect, 1_024,
            nameof(expectedEffect));
        MachineActionGuard.RequireText(notAffected, 1_024,
            nameof(notAffected));
        MachineActionGuard.RequireText(verification, 1_024,
            nameof(verification));
        MachineActionGuard.RequireOptionalText(limitations, 1_024,
            nameof(limitations));
        MachineActionGuard.RequireFingerprint(preconditionFingerprint,
            nameof(preconditionFingerprint));
        MachineActionGuard.RequireRecovery(recoveryPayload,
            nameof(recoveryPayload));

        var resolvedActionId = actionId ?? Guid.NewGuid();
        if (resolvedActionId == Guid.Empty)
        {
            throw new ArgumentException(
                "An action identifier is required.", nameof(actionId));
        }

        var resolvedCreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        if (resolvedCreatedAt == default)
        {
            throw new ArgumentException(
                "A plan creation time is required.", nameof(createdAt));
        }

        return new(
            resolvedActionId,
            capability,
            target,
            currentState,
            currentNormalizedState,
            requestedState,
            requestedNormalizedState,
            changeCategory,
            expectedEffect,
            notAffected,
            reversible,
            requiresElevation,
            verification,
            limitations,
            preconditionFingerprint,
            recoveryPayload,
            resolvedCreatedAt);
    }
}

public sealed class MachineActionUndoPlan
{
    private MachineActionUndoPlan(
        Guid undoId,
        MachineActionOutcome outcome,
        DateTimeOffset createdAt)
    {
        UndoId = undoId;
        OriginalActionId = outcome.ActionId;
        Capability = outcome.Capability;
        Target = outcome.Target;
        CurrentNormalizedState = outcome.ResultingNormalizedState!;
        RestoreNormalizedState = outcome.PreviousNormalizedState;
        PreconditionFingerprint = outcome.ResultingPreconditionFingerprint!;
        RecoveryPayload = outcome.RecoveryPayload;
        CreatedAt = createdAt;
        PlanFingerprint = MachineActionFingerprint.CreateUndoPlan(this);
    }

    public Guid UndoId { get; }

    public Guid OriginalActionId { get; }

    public MachineActionCapability Capability { get; }

    public MachineActionTarget Target { get; }

    public string CurrentNormalizedState { get; }

    public string RestoreNormalizedState { get; }

    public string PreconditionFingerprint { get; }

    public MachineActionRecoveryPayload? RecoveryPayload { get; }

    public DateTimeOffset CreatedAt { get; }

    public string PlanFingerprint { get; }

    public static MachineActionUndoPlan Create(
        MachineActionOutcome outcome,
        Guid? undoId = null,
        DateTimeOffset? createdAt = null)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (!outcome.Reversible ||
            outcome.UndoState is MachineActionUndoStatus.NotAvailable or
                MachineActionUndoStatus.SucceededVerified or
                MachineActionUndoStatus.InProgress ||
            string.IsNullOrWhiteSpace(outcome.ResultingNormalizedState) ||
            !MachineActionGuard.IsFingerprint(
                outcome.ResultingPreconditionFingerprint))
        {
            throw new InvalidOperationException(
                "The action does not currently have a safe undo plan.");
        }

        var resolvedUndoId = undoId ?? Guid.NewGuid();
        if (resolvedUndoId == Guid.Empty)
        {
            throw new ArgumentException(
                "An undo identifier is required.", nameof(undoId));
        }

        return new(resolvedUndoId, outcome,
            createdAt ?? DateTimeOffset.UtcNow);
    }
}

public sealed class MachineActionApproval
{
    private MachineActionApproval(
        MachineActionApprovalKind kind,
        Guid actionId,
        Guid reviewId,
        string planFingerprint,
        string preconditionFingerprint,
        DateTimeOffset approvedAt)
    {
        Kind = kind;
        ActionId = actionId;
        ReviewId = reviewId;
        PlanFingerprint = planFingerprint;
        PreconditionFingerprint = preconditionFingerprint;
        ApprovedAt = approvedAt;
    }

    public MachineActionApprovalKind Kind { get; }

    public Guid ActionId { get; }

    public Guid ReviewId { get; }

    public string PlanFingerprint { get; }

    public string PreconditionFingerprint { get; }

    public DateTimeOffset ApprovedAt { get; }

    public static MachineActionApproval ForExecution(
        MachineActionPlan plan,
        DateTimeOffset? approvedAt = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new MachineActionApproval(
            MachineActionApprovalKind.Execute,
            plan.ActionId,
            plan.ActionId,
            plan.PlanFingerprint,
            plan.PreconditionFingerprint,
            approvedAt ?? DateTimeOffset.UtcNow);
    }

    public static MachineActionApproval ForUndo(
        MachineActionUndoPlan plan,
        DateTimeOffset? approvedAt = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new MachineActionApproval(
            MachineActionApprovalKind.Undo,
            plan.OriginalActionId,
            plan.UndoId,
            plan.PlanFingerprint,
            plan.PreconditionFingerprint,
            approvedAt ?? DateTimeOffset.UtcNow);
    }
}

internal static class MachineActionGuard
{
    internal static bool IsAllowlisted(
        MachineActionCapability capability,
        MachineActionTargetKind targetKind) =>
        capability == MachineActionCapability.SetStartupEnabled &&
        targetKind is MachineActionTargetKind.StartupRegistryRunEntry or
            MachineActionTargetKind.StartupFolderEntry;

    internal static void RequireAllowlisted(
        MachineActionCapability capability,
        MachineActionTargetKind targetKind)
    {
        if (!IsAllowlisted(capability, targetKind))
        {
            throw new ArgumentOutOfRangeException(nameof(capability),
                "The capability and target-kind pair is not allowlisted.");
        }
    }

    internal static void RequireText(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength)
        {
            throw new ArgumentException(
                "A bounded non-empty value is required.", parameterName);
        }
    }

    internal static void RequireOptionalText(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (value is not null && value.Length > maximumLength)
        {
            throw new ArgumentException(
                "The value exceeds its persistence bound.", parameterName);
        }
    }

    internal static bool IsFingerprint(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    internal static void RequireFingerprint(
        string? value,
        string parameterName)
    {
        if (!IsFingerprint(value))
        {
            throw new ArgumentException(
                "A SHA-256 fingerprint is required.", parameterName);
        }
    }

    internal static void RequireRecovery(
        MachineActionRecoveryPayload? payload,
        string parameterName)
    {
        if (payload is null)
        {
            return;
        }

        if (payload.Version is < 1 or > 16 ||
            string.IsNullOrWhiteSpace(payload.ProviderData) ||
            payload.ProviderData.Length > 16_384)
        {
            throw new ArgumentException(
                "The recovery payload is not bounded and versioned.",
                parameterName);
        }
    }
}
