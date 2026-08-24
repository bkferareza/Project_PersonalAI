namespace Machine.Core;

public sealed record MachineActionCoordinatorResult(
    MachineActionResultStatus Status,
    MachineActionOutcome? Outcome = null,
    string? FailureCode = null);

public sealed record MachineActionUndoCoordinatorResult(
    MachineActionUndoStatus Status,
    MachineActionOutcome? Outcome = null,
    string? FailureCode = null);

public sealed class MachineActionCoordinator
{
    private readonly MachineActionExecutorRegistry _executors;
    private readonly MachineActionOutcomeMemory _memory;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MachineActionCoordinator(
        MachineActionExecutorRegistry executors,
        MachineActionOutcomeMemory memory,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(executors);
        ArgumentNullException.ThrowIfNull(memory);
        _executors = executors;
        _memory = memory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<MachineActionCoordinatorResult> ExecuteAsync(
        MachineActionPlan plan,
        MachineActionApproval? approval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await _memory.FindAsync(
                plan.ActionId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                if (!string.Equals(existing.PlanFingerprint,
                    plan.PlanFingerprint, StringComparison.Ordinal))
                {
                    return new(MachineActionResultStatus.TargetChanged);
                }

                if (existing.Result == MachineActionResultStatus.InProgress)
                {
                    existing = await ReconcileOneAsync(
                        existing, cancellationToken).ConfigureAwait(false);
                }

                return new(existing.Result, existing,
                    existing.FailureCode);
            }

            if (!Matches(plan, approval))
            {
                return new(MachineActionResultStatus.NotApproved);
            }

            if (plan.RequiresElevation)
            {
                return new(MachineActionResultStatus.PermissionRequired);
            }

            if (!_executors.TryGet(
                plan.Capability, plan.Target.Kind, out var executor) ||
                executor is null)
            {
                return new(MachineActionResultStatus.Unsupported);
            }

            var before = await TryReadAsync(
                executor, plan.Target, plan.RecoveryPayload,
                cancellationToken).ConfigureAwait(false);
            if (before is null)
            {
                return new(MachineActionResultStatus.Failed,
                    FailureCode: "precondition-read-failed");
            }

            if (before.Availability ==
                MachineActionAvailability.PermissionRequired)
            {
                return new(MachineActionResultStatus.PermissionRequired);
            }

            if (before.Availability == MachineActionAvailability.Unsupported)
            {
                return new(MachineActionResultStatus.Unsupported);
            }

            if (before.Availability != MachineActionAvailability.Supported)
            {
                return new(MachineActionResultStatus.Failed,
                    FailureCode: "invalid-precondition-availability");
            }

            if (!string.Equals(before.PreconditionFingerprint,
                    plan.PreconditionFingerprint,
                    StringComparison.Ordinal) ||
                !string.Equals(before.NormalizedState,
                    plan.CurrentNormalizedState,
                    StringComparison.Ordinal))
            {
                return new(MachineActionResultStatus.TargetChanged);
            }

            var started = new MachineActionOutcome(
                plan.ActionId,
                plan.PlanFingerprint,
                plan.PreconditionFingerprint,
                plan.Capability,
                plan.Target,
                plan.ExpectedEffect,
                plan.RequestedNormalizedState,
                UtcNow(),
                CompletedAt: null,
                MachineActionResultStatus.InProgress,
                MachineActionVerificationStatus.NotAttempted,
                plan.Reversible,
                MachineActionUndoStatus.NotAvailable,
                UndoStartedAt: null,
                UndoCompletedAt: null,
                MachineActionVerificationStatus.NotAttempted,
                before.NormalizedState,
                ResultingNormalizedState: null,
                ResultingPreconditionFingerprint: null,
                UserApproved: true,
                UndoUserApproved: false,
                plan.RecoveryPayload,
                MachineActionRecoveryClassification.NotRequired,
                MachineActionRecoveryClassification.NotRequired,
                FailureCode: null);

            // This durable marker and provider recovery data must exist before
            // any deterministic executor is allowed to mutate the target.
            await _memory.UpsertAsync(started, cancellationToken)
                .ConfigureAwait(false);

            string? failureCode = null;
            try
            {
                var mutation = await executor.ExecuteAsync(
                    plan, cancellationToken).ConfigureAwait(false);
                if (!mutation.ProviderReportedSuccess)
                {
                    failureCode = NormalizeFailure(
                        mutation.FailureCode, "executor-reported-failure");
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                failureCode = "executor-exception";
            }

            var after = await TryReadAsync(
                executor, plan.Target, plan.RecoveryPayload,
                cancellationToken).ConfigureAwait(false);
            var completed = CompleteExecution(started, after, failureCode);
            await _memory.UpsertAsync(completed, cancellationToken)
                .ConfigureAwait(false);
            return new(completed.Result, completed, completed.FailureCode);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MachineActionUndoCoordinatorResult> UndoAsync(
        MachineActionUndoPlan plan,
        MachineActionApproval? approval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var outcome = await _memory.FindAsync(
                plan.OriginalActionId, cancellationToken).ConfigureAwait(false);
            if (outcome is null)
            {
                return new(MachineActionUndoStatus.NotAvailable);
            }

            if (outcome.UndoState ==
                MachineActionUndoStatus.SucceededVerified)
            {
                return new(outcome.UndoState, outcome);
            }

            if (outcome.UndoState == MachineActionUndoStatus.InProgress)
            {
                outcome = await ReconcileOneAsync(
                    outcome, cancellationToken).ConfigureAwait(false);
                return new(outcome.UndoState, outcome,
                    outcome.FailureCode);
            }

            if (!Matches(plan, approval))
            {
                return new(MachineActionUndoStatus.NotApproved, outcome);
            }

            if (!_executors.TryGet(
                plan.Capability, plan.Target.Kind, out var executor) ||
                executor is null)
            {
                return new(MachineActionUndoStatus.Unsupported, outcome);
            }

            var before = await TryReadAsync(
                executor, plan.Target, plan.RecoveryPayload,
                cancellationToken).ConfigureAwait(false);
            if (before?.Availability ==
                MachineActionAvailability.PermissionRequired)
            {
                return new(MachineActionUndoStatus.PermissionRequired, outcome);
            }

            if (before is null ||
                before.Availability != MachineActionAvailability.Supported ||
                !string.Equals(before.PreconditionFingerprint,
                    plan.PreconditionFingerprint,
                    StringComparison.Ordinal) ||
                !string.Equals(before.NormalizedState,
                    plan.CurrentNormalizedState,
                    StringComparison.Ordinal))
            {
                var conflict = outcome with
                {
                    UndoState = MachineActionUndoStatus.TargetChanged,
                    UndoStartedAt = UtcNow(),
                    UndoCompletedAt = UtcNow(),
                    UndoVerificationResult =
                        MachineActionVerificationStatus.Failed,
                    UndoUserApproved = true,
                    FailureCode = "undo-precondition-changed"
                };
                await _memory.UpsertAsync(conflict, cancellationToken)
                    .ConfigureAwait(false);
                return new(conflict.UndoState, conflict,
                    conflict.FailureCode);
            }

            var inProgress = outcome with
            {
                UndoState = MachineActionUndoStatus.InProgress,
                UndoStartedAt = UtcNow(),
                UndoCompletedAt = null,
                UndoVerificationResult =
                    MachineActionVerificationStatus.NotAttempted,
                UndoUserApproved = true,
                UndoRecoveryClassification =
                    MachineActionRecoveryClassification.NotRequired,
                FailureCode = null
            };
            await _memory.UpsertAsync(inProgress, cancellationToken)
                .ConfigureAwait(false);

            string? failureCode = null;
            try
            {
                var mutation = await executor.UndoAsync(
                    plan, cancellationToken).ConfigureAwait(false);
                if (!mutation.ProviderReportedSuccess)
                {
                    failureCode = NormalizeFailure(
                        mutation.FailureCode, "undo-executor-failure");
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                failureCode = "undo-executor-exception";
            }

            var after = await TryReadAsync(
                executor, plan.Target, plan.RecoveryPayload,
                cancellationToken).ConfigureAwait(false);
            var completed = CompleteUndo(
                inProgress, plan, after, failureCode);
            await _memory.UpsertAsync(completed, cancellationToken)
                .ConfigureAwait(false);
            return new(completed.UndoState, completed,
                completed.FailureCode);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MachineActionOutcome>>
        ReconcileInProgressAsync(
            CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pending = (await _memory.GetAsync(cancellationToken)
                    .ConfigureAwait(false))
                .Where(item =>
                    item.Result == MachineActionResultStatus.InProgress ||
                    item.UndoState == MachineActionUndoStatus.InProgress)
                .ToArray();
            var reconciled = new List<MachineActionOutcome>(pending.Length);
            foreach (var outcome in pending)
            {
                reconciled.Add(await ReconcileOneAsync(
                    outcome, cancellationToken).ConfigureAwait(false));
            }

            return reconciled;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<MachineActionOutcome> ReconcileOneAsync(
        MachineActionOutcome outcome,
        CancellationToken cancellationToken)
    {
        MachineActionTargetState? current = null;
        if (_executors.TryGet(
            outcome.Capability, outcome.Target.Kind, out var executor) &&
            executor is not null)
        {
            current = await TryReadAsync(
                executor, outcome.Target, outcome.RecoveryPayload,
                cancellationToken)
                .ConfigureAwait(false);
        }

        MachineActionOutcome reconciled;
        if (outcome.Result == MachineActionResultStatus.InProgress)
        {
            if (current?.Availability == MachineActionAvailability.Supported &&
                string.Equals(current.NormalizedState,
                    outcome.RequestedNormalizedState,
                    StringComparison.Ordinal))
            {
                reconciled = outcome with
                {
                    CompletedAt = UtcNow(),
                    Result = MachineActionResultStatus.SucceededVerified,
                    VerificationResult =
                        MachineActionVerificationStatus.Verified,
                    UndoState = outcome.Reversible
                        ? MachineActionUndoStatus.Available
                        : MachineActionUndoStatus.NotAvailable,
                    ResultingNormalizedState = current.NormalizedState,
                    ResultingPreconditionFingerprint =
                        current.PreconditionFingerprint,
                    RecoveryClassification =
                        MachineActionRecoveryClassification.Applied,
                    FailureCode = null
                };
            }
            else if (current?.Availability ==
                    MachineActionAvailability.Supported &&
                string.Equals(current.NormalizedState,
                    outcome.PreviousNormalizedState,
                    StringComparison.Ordinal))
            {
                reconciled = outcome with
                {
                    CompletedAt = UtcNow(),
                    Result = MachineActionResultStatus.Failed,
                    VerificationResult =
                        MachineActionVerificationStatus.Failed,
                    UndoState = MachineActionUndoStatus.NotAvailable,
                    ResultingNormalizedState = current.NormalizedState,
                    ResultingPreconditionFingerprint =
                        current.PreconditionFingerprint,
                    RecoveryClassification =
                        MachineActionRecoveryClassification.NotApplied,
                    FailureCode = "interrupted-action-not-applied"
                };
            }
            else
            {
                reconciled = outcome with
                {
                    CompletedAt = UtcNow(),
                    Result = MachineActionResultStatus.RecoveryUnknown,
                    VerificationResult =
                        MachineActionVerificationStatus.Indeterminate,
                    UndoState = outcome.Reversible
                        ? MachineActionUndoStatus.RecoveryUnknown
                        : MachineActionUndoStatus.NotAvailable,
                    ResultingNormalizedState = current?.NormalizedState,
                    ResultingPreconditionFingerprint =
                        current?.PreconditionFingerprint,
                    RecoveryClassification =
                        MachineActionRecoveryClassification.Unknown,
                    FailureCode = "interrupted-action-state-unknown"
                };
            }
        }
        else
        {
            if (current?.Availability == MachineActionAvailability.Supported &&
                string.Equals(current.NormalizedState,
                    outcome.PreviousNormalizedState,
                    StringComparison.Ordinal))
            {
                reconciled = outcome with
                {
                    UndoState = MachineActionUndoStatus.SucceededVerified,
                    UndoCompletedAt = UtcNow(),
                    UndoVerificationResult =
                        MachineActionVerificationStatus.Verified,
                    UndoRecoveryClassification =
                        MachineActionRecoveryClassification.Applied,
                    FailureCode = null
                };
            }
            else if (current?.Availability ==
                    MachineActionAvailability.Supported &&
                string.Equals(current.NormalizedState,
                    outcome.ResultingNormalizedState,
                    StringComparison.Ordinal))
            {
                reconciled = outcome with
                {
                    UndoState = MachineActionUndoStatus.Available,
                    UndoCompletedAt = UtcNow(),
                    UndoVerificationResult =
                        MachineActionVerificationStatus.Failed,
                    UndoRecoveryClassification =
                        MachineActionRecoveryClassification.NotApplied,
                    FailureCode = "interrupted-undo-not-applied"
                };
            }
            else
            {
                reconciled = outcome with
                {
                    UndoState = MachineActionUndoStatus.RecoveryUnknown,
                    UndoCompletedAt = UtcNow(),
                    UndoVerificationResult =
                        MachineActionVerificationStatus.Indeterminate,
                    UndoRecoveryClassification =
                        MachineActionRecoveryClassification.Unknown,
                    FailureCode = "interrupted-undo-state-unknown"
                };
            }
        }

        await _memory.UpsertAsync(reconciled, cancellationToken)
            .ConfigureAwait(false);
        return reconciled;
    }

    private MachineActionOutcome CompleteExecution(
        MachineActionOutcome outcome,
        MachineActionTargetState? state,
        string? failureCode)
    {
        if (state?.Availability == MachineActionAvailability.Supported &&
            string.Equals(state.NormalizedState,
                outcome.RequestedNormalizedState,
                StringComparison.Ordinal))
        {
            return outcome with
            {
                CompletedAt = UtcNow(),
                Result = MachineActionResultStatus.SucceededVerified,
                VerificationResult =
                    MachineActionVerificationStatus.Verified,
                UndoState = outcome.Reversible
                    ? MachineActionUndoStatus.Available
                    : MachineActionUndoStatus.NotAvailable,
                ResultingNormalizedState = state.NormalizedState,
                ResultingPreconditionFingerprint =
                    state.PreconditionFingerprint,
                FailureCode = null
            };
        }

        var unchanged = state?.Availability ==
                MachineActionAvailability.Supported &&
            string.Equals(state.NormalizedState,
                outcome.PreviousNormalizedState,
                StringComparison.Ordinal);
        return outcome with
        {
            CompletedAt = UtcNow(),
            Result = unchanged
                ? MachineActionResultStatus.Failed
                : MachineActionResultStatus.ChangedButVerificationFailed,
            VerificationResult = state?.Availability ==
                MachineActionAvailability.Supported
                    ? MachineActionVerificationStatus.Failed
                    : MachineActionVerificationStatus.Indeterminate,
            UndoState = outcome.Reversible && !unchanged
                ? MachineActionUndoStatus.Available
                : MachineActionUndoStatus.NotAvailable,
            ResultingNormalizedState = state?.NormalizedState,
            ResultingPreconditionFingerprint =
                state?.PreconditionFingerprint,
            FailureCode = NormalizeFailure(
                failureCode, unchanged
                    ? "postcondition-not-reached"
                    : "postcondition-unverified")
        };
    }

    private MachineActionOutcome CompleteUndo(
        MachineActionOutcome outcome,
        MachineActionUndoPlan plan,
        MachineActionTargetState? state,
        string? failureCode)
    {
        if (state?.Availability == MachineActionAvailability.Supported &&
            string.Equals(state.NormalizedState,
                plan.RestoreNormalizedState,
                StringComparison.Ordinal))
        {
            return outcome with
            {
                UndoState = MachineActionUndoStatus.SucceededVerified,
                UndoCompletedAt = UtcNow(),
                UndoVerificationResult =
                    MachineActionVerificationStatus.Verified,
                FailureCode = null
            };
        }

        var unchanged = state?.Availability ==
                MachineActionAvailability.Supported &&
            string.Equals(state.NormalizedState,
                plan.CurrentNormalizedState,
                StringComparison.Ordinal);
        return outcome with
        {
            UndoState = unchanged
                ? MachineActionUndoStatus.Failed
                : MachineActionUndoStatus.ChangedButVerificationFailed,
            UndoCompletedAt = UtcNow(),
            UndoVerificationResult = state?.Availability ==
                MachineActionAvailability.Supported
                    ? MachineActionVerificationStatus.Failed
                    : MachineActionVerificationStatus.Indeterminate,
            FailureCode = NormalizeFailure(
                failureCode, unchanged
                    ? "undo-postcondition-not-reached"
                    : "undo-postcondition-unverified")
        };
    }

    private static bool Matches(
        MachineActionPlan plan,
        MachineActionApproval? approval) =>
        approval is not null &&
        approval.Kind == MachineActionApprovalKind.Execute &&
        approval.ActionId == plan.ActionId &&
        approval.ReviewId == plan.ActionId &&
        approval.ApprovedAt != default &&
        approval.ApprovedAt >= plan.CreatedAt &&
        string.Equals(approval.PlanFingerprint,
            plan.PlanFingerprint, StringComparison.Ordinal) &&
        string.Equals(approval.PreconditionFingerprint,
            plan.PreconditionFingerprint, StringComparison.Ordinal);

    private static bool Matches(
        MachineActionUndoPlan plan,
        MachineActionApproval? approval) =>
        approval is not null &&
        approval.Kind == MachineActionApprovalKind.Undo &&
        approval.ActionId == plan.OriginalActionId &&
        approval.ReviewId == plan.UndoId &&
        approval.ApprovedAt != default &&
        approval.ApprovedAt >= plan.CreatedAt &&
        string.Equals(approval.PlanFingerprint,
            plan.PlanFingerprint, StringComparison.Ordinal) &&
        string.Equals(approval.PreconditionFingerprint,
            plan.PreconditionFingerprint, StringComparison.Ordinal);

    private static async Task<MachineActionTargetState?> TryReadAsync(
        IMachineActionExecutor executor,
        MachineActionTarget target,
        MachineActionRecoveryPayload? recoveryPayload,
        CancellationToken cancellationToken)
    {
        try
        {
            return await executor.ReadStateAsync(
                    target, recoveryPayload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string NormalizeFailure(
        string? value,
        string fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Length <= 256
                ? value
                : value[..256];

    private DateTimeOffset UtcNow() =>
        _timeProvider.GetUtcNow();
}
