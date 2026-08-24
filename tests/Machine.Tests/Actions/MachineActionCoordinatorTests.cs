using Machine.Core;

namespace Machine.Tests.Actions;

public sealed class MachineActionCoordinatorTests
{
    [Fact]
    public void PlanContainsTheExactReviewedEffectAndStableFingerprint()
    {
        var context = new TestContext();
        var actionId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(
            2026, 8, 25, 2, 0, 0, TimeSpan.Zero);
        var state = context.Executor.GetState();
        var first = MachineActionPlan.Create(
            MachineActionCapability.SetStartupEnabled,
            context.Executor.Target,
            "Enabled",
            state.NormalizedState,
            "Disabled",
            "disabled",
            "Startup registration",
            "Disable at future sign-ins",
            "The current process is not terminated",
            reversible: true,
            requiresElevation: false,
            "Re-query the same startup registration",
            "Affects future sign-ins only",
            state.PreconditionFingerprint,
            new MachineActionRecoveryPayload(1, "opaque-recovery"),
            actionId,
            createdAt);
        var identical = MachineActionPlan.Create(
            first.Capability,
            first.Target,
            first.CurrentState,
            first.CurrentNormalizedState,
            first.RequestedState,
            first.RequestedNormalizedState,
            first.ChangeCategory,
            first.ExpectedEffect,
            first.NotAffected,
            first.Reversible,
            first.RequiresElevation,
            first.Verification,
            first.Limitations,
            first.PreconditionFingerprint,
            first.RecoveryPayload,
            actionId,
            createdAt);

        Assert.Equal("Enabled", first.CurrentState);
        Assert.Equal("Disabled", first.RequestedState);
        Assert.Equal("Startup registration", first.ChangeCategory);
        Assert.Equal("Disable at future sign-ins", first.ExpectedEffect);
        Assert.Equal(
            "The current process is not terminated", first.NotAffected);
        Assert.True(first.Reversible);
        Assert.False(first.RequiresElevation);
        Assert.Equal(
            "Re-query the same startup registration", first.Verification);
        Assert.Equal("Affects future sign-ins only", first.Limitations);
        Assert.Equal(first.PlanFingerprint, identical.PlanFingerprint);
        Assert.Equal(64, first.PlanFingerprint.Length);
    }

    [Fact]
    public void NonAllowlistedCapabilityCannotEnterTheRegistryOrPlan()
    {
        var context = new TestContext();
        var state = context.Executor.GetState();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MachineActionPlan.Create(
                (MachineActionCapability)999,
                context.Executor.Target,
                "Enabled",
                state.NormalizedState,
                "Disabled",
                "disabled",
                "Startup registration",
                "Disable at future sign-ins",
                "Current process remains running",
                true,
                false,
                "Re-query target",
                string.Empty,
                state.PreconditionFingerprint));
    }

    [Fact]
    public async Task ExecutionRequiresApprovalAndDoesNotRecordOrMutate()
    {
        var context = new TestContext();
        var plan = context.CreatePlan();

        var result = await context.Coordinator.ExecuteAsync(plan, null);

        Assert.Equal(MachineActionResultStatus.NotApproved, result.Status);
        Assert.Equal(0, context.Executor.ExecuteCount);
        Assert.Empty(await context.Memory.GetAsync());
    }

    [Fact]
    public async Task ApprovalIsBoundToTheExactReviewedPlan()
    {
        var context = new TestContext();
        var reviewed = context.CreatePlan();
        var different = context.CreatePlan();

        var result = await context.Coordinator.ExecuteAsync(
            different,
            MachineActionApproval.ForExecution(reviewed));

        Assert.Equal(MachineActionResultStatus.NotApproved, result.Status);
        Assert.Equal(0, context.Executor.ExecuteCount);
    }

    [Fact]
    public async Task StalePlanIsBlockedBeforeMutation()
    {
        var context = new TestContext();
        var plan = context.CreatePlan();
        context.Executor.State = "externally-changed";

        var result = await context.ExecuteApprovedAsync(plan);

        Assert.Equal(MachineActionResultStatus.TargetChanged, result.Status);
        Assert.Equal(0, context.Executor.ExecuteCount);
        Assert.Empty(await context.Memory.GetAsync());
    }

    [Fact]
    public async Task MissingExplicitExecutorIsUnsupported()
    {
        var store = new TestStore();
        var memory = new MachineActionOutcomeMemory(store);
        var coordinator = new MachineActionCoordinator(
            new MachineActionExecutorRegistry([]), memory);
        var context = new TestContext();
        var plan = context.CreatePlan();

        var result = await coordinator.ExecuteAsync(
            plan, MachineActionApproval.ForExecution(plan));

        Assert.Equal(MachineActionResultStatus.Unsupported, result.Status);
        Assert.Empty(await memory.GetAsync());
    }

    [Fact]
    public async Task ElevationRequiredPlanRemainsReadOnly()
    {
        var context = new TestContext();
        var plan = context.CreatePlan(requiresElevation: true);

        var result = await context.ExecuteApprovedAsync(plan);

        Assert.Equal(
            MachineActionResultStatus.PermissionRequired,
            result.Status);
        Assert.Equal(0, context.Executor.ReadCount);
        Assert.Equal(0, context.Executor.ExecuteCount);
    }

    [Fact]
    public async Task ExecutorPermissionResultRemainsReadOnly()
    {
        var context = new TestContext();
        var plan = context.CreatePlan();
        context.Executor.Availability =
            MachineActionAvailability.PermissionRequired;

        var result = await context.ExecuteApprovedAsync(plan);

        Assert.Equal(
            MachineActionResultStatus.PermissionRequired,
            result.Status);
        Assert.Equal(0, context.Executor.ExecuteCount);
    }

    [Fact]
    public async Task InProgressRecoveryIsPersistedBeforeMutationAndSuccessIsVerified()
    {
        var context = new TestContext();
        var plan = context.CreatePlan();
        context.Executor.OnExecute = () =>
        {
            var pending = Assert.Single(context.Store.State!.Outcomes);
            Assert.Equal(
                MachineActionResultStatus.InProgress,
                pending.Result);
            Assert.Equal(plan.RecoveryPayload, pending.RecoveryPayload);
            context.Executor.State = plan.RequestedNormalizedState;
        };

        var result = await context.ExecuteApprovedAsync(plan);

        Assert.Equal(
            MachineActionResultStatus.SucceededVerified,
            result.Status);
        Assert.Equal(MachineActionVerificationStatus.Verified,
            result.Outcome!.VerificationResult);
        Assert.Equal(2, context.Executor.ReadCount);
        Assert.Equal(1, context.Executor.ExecuteCount);
        Assert.Equal(2, context.Store.SaveCount);
    }

    [Fact]
    public async Task FailedPostconditionIsNeverReportedAsSuccess()
    {
        var context = new TestContext();
        var plan = context.CreatePlan();

        var result = await context.ExecuteApprovedAsync(plan);

        Assert.Equal(MachineActionResultStatus.Failed, result.Status);
        Assert.Equal(
            MachineActionVerificationStatus.Failed,
            result.Outcome!.VerificationResult);
        Assert.NotEqual(
            MachineActionResultStatus.SucceededVerified,
            result.Outcome.Result);
        Assert.Equal(2, context.Executor.ReadCount);
    }

    [Fact]
    public async Task UnexpectedChangedStateIsVerificationFailureNotSuccess()
    {
        var context = new TestContext();
        var plan = context.CreatePlan();
        context.Executor.OnExecute = () =>
            context.Executor.State = "unexpected";

        var result = await context.ExecuteApprovedAsync(plan);

        Assert.Equal(
            MachineActionResultStatus.ChangedButVerificationFailed,
            result.Status);
        Assert.Equal(
            MachineActionVerificationStatus.Failed,
            result.Outcome!.VerificationResult);
        Assert.Equal(
            MachineActionUndoStatus.Available,
            result.Outcome.UndoState);
    }

    [Fact]
    public async Task UndoRequiresItsOwnExactApprovalAndVerification()
    {
        var context = new TestContext();
        var plan = context.CreatePlan();
        context.Executor.OnExecute = () =>
            context.Executor.State = plan.RequestedNormalizedState;
        var executed = await context.ExecuteApprovedAsync(plan);
        var undo = MachineActionUndoPlan.Create(executed.Outcome!);

        var blocked = await context.Coordinator.UndoAsync(undo, null);

        Assert.Equal(MachineActionUndoStatus.NotApproved, blocked.Status);
        Assert.Equal(0, context.Executor.UndoCount);

        context.Executor.OnUndo = () =>
        {
            Assert.Equal(
                MachineActionUndoStatus.InProgress,
                Assert.Single(context.Store.State!.Outcomes).UndoState);
            context.Executor.State = undo.RestoreNormalizedState;
        };
        var restored = await context.Coordinator.UndoAsync(
            undo, MachineActionApproval.ForUndo(undo));

        Assert.Equal(
            MachineActionUndoStatus.SucceededVerified,
            restored.Status);
        Assert.Equal(
            MachineActionVerificationStatus.Verified,
            restored.Outcome!.UndoVerificationResult);
        Assert.True(restored.Outcome.UndoUserApproved);
        Assert.Equal(1, context.Executor.UndoCount);
        Assert.Single(await context.Memory.GetAsync());
    }

    [Fact]
    public async Task InterruptedUndoIsReconciledWithoutRepeatingMutation()
    {
        var context = new TestContext();
        var plan = context.CreatePlan();
        context.Executor.OnExecute = () =>
            context.Executor.State = plan.RequestedNormalizedState;
        var executed = await context.ExecuteApprovedAsync(plan);
        var interrupted = executed.Outcome! with
        {
            UndoState = MachineActionUndoStatus.InProgress,
            UndoStartedAt = DateTimeOffset.UtcNow,
            UndoCompletedAt = null,
            UndoUserApproved = true
        };
        context.Store.State = new(
            MachineActionOutcomeMemory.PersistenceSchemaVersion,
            [interrupted]);
        context.Executor.State = interrupted.PreviousNormalizedState;
        var restartedMemory = new MachineActionOutcomeMemory(context.Store);
        var restarted = new MachineActionCoordinator(
            new MachineActionExecutorRegistry([context.Executor]),
            restartedMemory);

        var reconciled = Assert.Single(
            await restarted.ReconcileInProgressAsync());

        Assert.Equal(
            MachineActionUndoStatus.SucceededVerified,
            reconciled.UndoState);
        Assert.Equal(
            MachineActionRecoveryClassification.Applied,
            reconciled.UndoRecoveryClassification);
        Assert.Equal(0, context.Executor.UndoCount);
    }

    [Fact]
    public async Task UndoConflictDoesNotMutateExternallyChangedTarget()
    {
        var context = new TestContext();
        var plan = context.CreatePlan();
        context.Executor.OnExecute = () =>
            context.Executor.State = plan.RequestedNormalizedState;
        var executed = await context.ExecuteApprovedAsync(plan);
        var undo = MachineActionUndoPlan.Create(executed.Outcome!);
        context.Executor.State = "external-change";

        var result = await context.Coordinator.UndoAsync(
            undo, MachineActionApproval.ForUndo(undo));

        Assert.Equal(MachineActionUndoStatus.TargetChanged, result.Status);
        Assert.Equal(0, context.Executor.UndoCount);
        Assert.Equal(
            "undo-precondition-changed",
            result.Outcome!.FailureCode);
    }

    [Fact]
    public async Task SuccessfulRetryReturnsOneOutcomeWithoutRepeatingMutation()
    {
        var context = new TestContext();
        var plan = context.CreatePlan();
        context.Executor.OnExecute = () =>
            context.Executor.State = plan.RequestedNormalizedState;
        var approval = MachineActionApproval.ForExecution(plan);

        var first = await context.Coordinator.ExecuteAsync(plan, approval);
        var retry = await context.Coordinator.ExecuteAsync(plan, approval);

        Assert.Equal(
            MachineActionResultStatus.SucceededVerified,
            first.Status);
        Assert.Equal(first.Outcome, retry.Outcome);
        Assert.Equal(1, context.Executor.ExecuteCount);
        Assert.Single(await context.Memory.GetAsync());
    }

    [Theory]
    [InlineData("disabled", MachineActionResultStatus.SucceededVerified,
        MachineActionRecoveryClassification.Applied)]
    [InlineData("enabled", MachineActionResultStatus.Failed,
        MachineActionRecoveryClassification.NotApplied)]
    [InlineData("unknown", MachineActionResultStatus.RecoveryUnknown,
        MachineActionRecoveryClassification.Unknown)]
    public async Task RestartReconcilesInProgressWithoutRepeatingMutation(
        string currentState,
        MachineActionResultStatus expectedStatus,
        MachineActionRecoveryClassification expectedRecovery)
    {
        var context = new TestContext();
        var plan = context.CreatePlan();
        context.Store.State = new(
            MachineActionOutcomeMemory.PersistenceSchemaVersion,
            [CreateInProgress(plan)]);
        context.Executor.State = currentState;

        var reconciled = Assert.Single(
            await context.Coordinator.ReconcileInProgressAsync());

        Assert.Equal(expectedStatus, reconciled.Result);
        Assert.Equal(expectedRecovery,
            reconciled.RecoveryClassification);
        Assert.Equal(0, context.Executor.ExecuteCount);
        Assert.Single(await context.Memory.GetAsync());
    }

    [Fact]
    public async Task ExecutionPathHasNoInferenceDependency()
    {
        Assert.DoesNotContain(
            typeof(MachineActionCoordinator).GetConstructors()
                .SelectMany(item => item.GetParameters()),
            parameter => parameter.ParameterType ==
                typeof(IMachineStateExplainer));

        var context = new TestContext();
        var plan = context.CreatePlan();
        context.Executor.OnExecute = () =>
            context.Executor.State = plan.RequestedNormalizedState;
        var inferenceInvocations = 0;

        await context.ExecuteApprovedAsync(plan);

        Assert.Equal(0, inferenceInvocations);
    }

    private static MachineActionOutcome CreateInProgress(
        MachineActionPlan plan) => new(
        plan.ActionId,
        plan.PlanFingerprint,
        plan.PreconditionFingerprint,
        plan.Capability,
        plan.Target,
        plan.ExpectedEffect,
        plan.RequestedNormalizedState,
        plan.CreatedAt,
        CompletedAt: null,
        MachineActionResultStatus.InProgress,
        MachineActionVerificationStatus.NotAttempted,
        Reversible: true,
        MachineActionUndoStatus.NotAvailable,
        UndoStartedAt: null,
        UndoCompletedAt: null,
        MachineActionVerificationStatus.NotAttempted,
        plan.CurrentNormalizedState,
        ResultingNormalizedState: null,
        ResultingPreconditionFingerprint: null,
        UserApproved: true,
        UndoUserApproved: false,
        plan.RecoveryPayload,
        MachineActionRecoveryClassification.NotRequired,
        MachineActionRecoveryClassification.NotRequired,
        FailureCode: null);

    private sealed class TestContext
    {
        internal TestContext()
        {
            Store = new();
            Memory = new(Store);
            Executor = new();
            Coordinator = new(
                new MachineActionExecutorRegistry([Executor]),
                Memory);
        }

        internal TestStore Store { get; }

        internal MachineActionOutcomeMemory Memory { get; }

        internal TestExecutor Executor { get; }

        internal MachineActionCoordinator Coordinator { get; }

        internal MachineActionPlan CreatePlan(
            bool requiresElevation = false)
        {
            var current = Executor.GetState();
            return MachineActionPlan.Create(
                MachineActionCapability.SetStartupEnabled,
                Executor.Target,
                currentState: "Enabled",
                currentNormalizedState: current.NormalizedState,
                requestedState: "Disabled",
                requestedNormalizedState: "disabled",
                changeCategory: "Startup registration",
                expectedEffect: "Disable at future sign-ins",
                notAffected: "The current process is not terminated",
                reversible: true,
                requiresElevation,
                verification: "Re-query the same startup registration",
                limitations: "Affects future sign-ins only",
                current.PreconditionFingerprint,
                new MachineActionRecoveryPayload(1, "opaque-recovery"));
        }

        internal Task<MachineActionCoordinatorResult> ExecuteApprovedAsync(
            MachineActionPlan plan) => Coordinator.ExecuteAsync(
                plan, MachineActionApproval.ForExecution(plan));
    }

    private sealed class TestExecutor : IMachineActionExecutor
    {
        internal MachineActionTarget Target { get; } = new(
            MachineActionTargetKind.StartupRegistryRunEntry,
            "hkcu-run:test",
            "Test startup app");

        public MachineActionCapability Capability =>
            MachineActionCapability.SetStartupEnabled;

        public MachineActionTargetKind TargetKind => Target.Kind;

        internal string State { get; set; } = "enabled";

        internal MachineActionAvailability Availability { get; set; } =
            MachineActionAvailability.Supported;

        internal int ReadCount { get; private set; }

        internal int ExecuteCount { get; private set; }

        internal int UndoCount { get; private set; }

        internal Action? OnExecute { get; set; }

        internal Action? OnUndo { get; set; }

        internal MachineActionTargetState GetState() => new(
            Availability,
            State,
            MachineActionFingerprint.CreatePrecondition(
                Target, State, "provider-v1"));

        public Task<MachineActionTargetState> ReadStateAsync(
            MachineActionTarget target,
            MachineActionRecoveryPayload? recoveryPayload,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(GetState());
        }

        public Task<MachineActionMutationResult> ExecuteAsync(
            MachineActionPlan plan,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            OnExecute?.Invoke();
            return Task.FromResult(MachineActionMutationResult.Completed());
        }

        public Task<MachineActionMutationResult> UndoAsync(
            MachineActionUndoPlan plan,
            CancellationToken cancellationToken = default)
        {
            UndoCount++;
            OnUndo?.Invoke();
            return Task.FromResult(MachineActionMutationResult.Completed());
        }
    }

    private sealed class TestStore : IMachineActionOutcomeStore
    {
        internal MachineActionOutcomePersistedState? State { get; set; }

        internal int SaveCount { get; private set; }

        public Task<MachineActionOutcomePersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(State);

        public Task SaveAsync(
            MachineActionOutcomePersistedState state,
            CancellationToken cancellationToken = default)
        {
            State = state with { Outcomes = state.Outcomes.ToArray() };
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
