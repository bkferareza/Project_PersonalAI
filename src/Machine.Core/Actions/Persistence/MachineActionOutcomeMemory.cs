namespace Machine.Core;

public sealed class MachineActionOutcomeMemory
{
    public const int PersistenceSchemaVersion = 1;
    public const int RecentOutcomeRetentionCount = 300;
    public const int MaximumPersistedOutcomeCount = 4_096;

    private readonly IMachineActionOutcomeStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<MachineActionOutcome> _outcomes = [];
    private bool _loaded;

    public MachineActionOutcomeMemory(IMachineActionOutcomeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task<IReadOnlyList<MachineActionOutcome>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _outcomes.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<MachineActionOutcome?> FindAsync(
        Guid actionId,
        CancellationToken cancellationToken = default)
    {
        var outcomes = await GetAsync(cancellationToken)
            .ConfigureAwait(false);
        return outcomes.SingleOrDefault(item => item.ActionId == actionId);
    }

    internal async Task UpsertAsync(
        MachineActionOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var updated = _outcomes
                .Where(item => item.ActionId != outcome.ActionId)
                .Append(outcome)
                .ToArray();
            var retained = Retain(updated);
            if (retained.Count > MaximumPersistedOutcomeCount)
            {
                throw new InvalidOperationException(
                    "Unresolved action recovery records exceed the safe " +
                    "persistence bound; no mutation may start.");
            }

            var state = new MachineActionOutcomePersistedState(
                PersistenceSchemaVersion,
                retained);
            await _store.SaveAsync(state, cancellationToken)
                .ConfigureAwait(false);
            _outcomes = retained;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        var state = await _store.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        _outcomes = state?.Outcomes?.ToArray() ?? [];
        _loaded = true;
    }

    private static IReadOnlyList<MachineActionOutcome> Retain(
        IReadOnlyList<MachineActionOutcome> outcomes)
    {
        var unresolved = outcomes.Where(RequiresRecoveryRetention).ToArray();
        var recentResolved = outcomes
            .Where(item => !RequiresRecoveryRetention(item))
            .OrderByDescending(item => item.CompletedAt ?? item.StartedAt)
            .Take(RecentOutcomeRetentionCount);
        return unresolved
            .Concat(recentResolved)
            .OrderBy(item => item.StartedAt)
            .ThenBy(item => item.ActionId)
            .ToArray();
    }

    private static bool RequiresRecoveryRetention(
        MachineActionOutcome outcome) =>
        outcome.Result is MachineActionResultStatus.InProgress or
            MachineActionResultStatus.RecoveryUnknown ||
        outcome.UndoState is MachineActionUndoStatus.Available or
            MachineActionUndoStatus.InProgress or
            MachineActionUndoStatus.Failed or
            MachineActionUndoStatus.ChangedButVerificationFailed or
            MachineActionUndoStatus.TargetChanged or
            MachineActionUndoStatus.RecoveryUnknown;

    internal static MachinePersistenceValidationResult ValidatePersistedState(
        MachineActionOutcomePersistedState state)
    {
        if (state.SchemaVersion > PersistenceSchemaVersion)
        {
            return MachinePersistenceValidationResult.Incompatible;
        }

        if (state.SchemaVersion != PersistenceSchemaVersion ||
            state.Outcomes is null ||
            state.Outcomes.Count > MaximumPersistedOutcomeCount ||
            state.Outcomes.Any(item => !IsSafe(item)) ||
            state.Outcomes.Select(item => item.ActionId).Distinct().Count() !=
                state.Outcomes.Count)
        {
            return MachinePersistenceValidationResult.Rejected;
        }

        return MachinePersistenceValidationResult.Accepted;
    }

    private static bool IsSafe(MachineActionOutcome? outcome)
    {
        if (outcome is null || outcome.ActionId == Guid.Empty ||
            outcome.Target is null ||
            !MachineActionGuard.IsAllowlisted(
                outcome.Capability, outcome.Target.Kind) ||
            !IsText(outcome.Target.StableIdentity, 2_048) ||
            !IsText(outcome.Target.DisplayName, 256) ||
            !MachineActionGuard.IsFingerprint(outcome.PlanFingerprint) ||
            !MachineActionGuard.IsFingerprint(
                outcome.PreconditionFingerprint) ||
            !IsText(outcome.RequestedEffect, 1_024) ||
            !IsText(outcome.RequestedNormalizedState, 4_096) ||
            !IsText(outcome.PreviousNormalizedState, 4_096) ||
            outcome.StartedAt == default || !outcome.UserApproved ||
            !Enum.IsDefined(outcome.Result) ||
            outcome.Result is MachineActionResultStatus.TargetChanged or
                MachineActionResultStatus.Unsupported or
                MachineActionResultStatus.PermissionRequired or
                MachineActionResultStatus.NotApproved ||
            !Enum.IsDefined(outcome.VerificationResult) ||
            !Enum.IsDefined(outcome.UndoState) ||
            outcome.UndoState is MachineActionUndoStatus.NotApproved or
                MachineActionUndoStatus.Unsupported or
                MachineActionUndoStatus.PermissionRequired ||
            !Enum.IsDefined(outcome.UndoVerificationResult) ||
            !Enum.IsDefined(outcome.RecoveryClassification) ||
            !Enum.IsDefined(outcome.UndoRecoveryClassification) ||
            !IsOptionalText(outcome.ResultingNormalizedState, 4_096) ||
            !IsOptionalText(outcome.FailureCode, 256))
        {
            return false;
        }

        if (outcome.Result == MachineActionResultStatus.InProgress)
        {
            if (outcome.CompletedAt is not null)
            {
                return false;
            }
        }
        else if (outcome.CompletedAt is null ||
            outcome.CompletedAt < outcome.StartedAt)
        {
            return false;
        }

        if (outcome.ResultingPreconditionFingerprint is not null &&
            !MachineActionGuard.IsFingerprint(
                outcome.ResultingPreconditionFingerprint))
        {
            return false;
        }

        if (!outcome.Reversible &&
            outcome.UndoState != MachineActionUndoStatus.NotAvailable)
        {
            return false;
        }

        if (outcome.UndoState == MachineActionUndoStatus.InProgress &&
            (!outcome.UndoUserApproved || outcome.UndoStartedAt is null))
        {
            return false;
        }

        try
        {
            MachineActionGuard.RequireRecovery(
                outcome.RecoveryPayload,
                nameof(outcome.RecoveryPayload));
        }
        catch (ArgumentException)
        {
            return false;
        }

        return true;
    }

    private static bool IsText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;

    private static bool IsOptionalText(string? value, int maximumLength) =>
        value is null || value.Length <= maximumLength;
}
