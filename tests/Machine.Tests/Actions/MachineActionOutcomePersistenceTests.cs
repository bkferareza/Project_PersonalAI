using System.Text;
using Machine.Core;

namespace Machine.Tests.Actions;

public sealed class MachineActionOutcomePersistenceTests
{
    [Fact]
    public async Task RealFileRoundTripPreservesUnresolvedReversibleRecovery()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var firstStore = new FileMachineActionOutcomeStore(directory);
            var firstMemory = new MachineActionOutcomeMemory(firstStore);
            var executor = new FileTestExecutor();
            var coordinator = new MachineActionCoordinator(
                new MachineActionExecutorRegistry([executor]),
                firstMemory);
            var plan = CreatePlan(executor);
            executor.OnExecute = () => executor.State = "disabled";

            var result = await coordinator.ExecuteAsync(
                plan, MachineActionApproval.ForExecution(plan));

            Assert.Equal(
                MachineActionResultStatus.SucceededVerified,
                result.Status);
            Assert.True(File.Exists(Path.Combine(
                directory, FileMachineActionOutcomeStore.FileName)));

            var secondStore = new FileMachineActionOutcomeStore(directory);
            var secondMemory = new MachineActionOutcomeMemory(secondStore);
            var reloaded = Assert.Single(await secondMemory.GetAsync());

            Assert.Equal(plan.ActionId, reloaded.ActionId);
            Assert.Equal(MachineActionUndoStatus.Available,
                reloaded.UndoState);
            Assert.Equal(plan.RecoveryPayload, reloaded.RecoveryPayload);
            Assert.Equal(
                MachineActionOutcomeStoreLoadStatus.Loaded,
                secondStore.LastLoadStatus);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectedFileIsPreservedAndStickyWriteBlockPreventsMutation()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(
                directory, FileMachineActionOutcomeStore.FileName);
            var invalidBytes = Encoding.UTF8.GetBytes("{not-json");
            await File.WriteAllBytesAsync(path, invalidBytes);
            var store = new FileMachineActionOutcomeStore(directory);
            var memory = new MachineActionOutcomeMemory(store);
            var executor = new FileTestExecutor();
            var coordinator = new MachineActionCoordinator(
                new MachineActionExecutorRegistry([executor]), memory);
            var plan = CreatePlan(executor);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                coordinator.ExecuteAsync(
                    plan, MachineActionApproval.ForExecution(plan)));

            Assert.Equal(0, executor.ExecuteCount);
            Assert.Equal(invalidBytes, await File.ReadAllBytesAsync(path));
            Assert.Single(Directory.GetFiles(
                directory,
                FileMachineActionOutcomeStore.FileName + ".rejected-*"));
            Assert.Equal(
                MachineActionOutcomeStoreLoadStatus.Corrupt,
                store.LastLoadStatus);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NewerSchemaIsPreservedAndBlocksReplacement()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(
                directory, FileMachineActionOutcomeStore.FileName);
            const string newer = "{\"SchemaVersion\":2,\"Outcomes\":[]}";
            await File.WriteAllTextAsync(path, newer);
            var store = new FileMachineActionOutcomeStore(directory);

            Assert.Null(await store.LoadAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.SaveAsync(new(
                    MachineActionOutcomeMemory.PersistenceSchemaVersion,
                    [])));

            Assert.Equal(newer, await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(
                directory,
                FileMachineActionOutcomeStore.FileName + ".rejected-*"));
            Assert.Equal(
                MachineActionOutcomeStoreLoadStatus.Incompatible,
                store.LastLoadStatus);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RetentionKeepsThreeHundredRecentPlusUnresolvedRecovery()
    {
        var outcomes = Enumerable.Range(0, 305)
            .Select(CreateResolvedOutcome)
            .Append(CreateUnresolvedOutcome())
            .ToArray();
        var store = new SeedStore(new(
            MachineActionOutcomeMemory.PersistenceSchemaVersion,
            outcomes));
        var memory = new MachineActionOutcomeMemory(store);
        var executor = new FileTestExecutor();
        var coordinator = new MachineActionCoordinator(
            new MachineActionExecutorRegistry([executor]), memory);
        var plan = CreatePlan(executor);
        executor.OnExecute = () => executor.State = "disabled";

        await coordinator.ExecuteAsync(
            plan, MachineActionApproval.ForExecution(plan));
        var retained = await memory.GetAsync();

        Assert.Equal(302, retained.Count);
        Assert.Equal(300, retained.Count(item => !item.Reversible));
        Assert.Contains(retained,
            item => item.ActionId == UnresolvedActionId);
        Assert.Contains(retained, item => item.ActionId == plan.ActionId);
    }

    private static MachineActionPlan CreatePlan(FileTestExecutor executor)
    {
        var state = executor.GetState();
        return MachineActionPlan.Create(
            MachineActionCapability.SetStartupEnabled,
            executor.Target,
            "Enabled",
            state.NormalizedState,
            "Disabled",
            "disabled",
            "Startup registration",
            "Disable at future sign-ins",
            "The current process is not terminated",
            reversible: true,
            requiresElevation: false,
            "Re-query the same registration",
            "Affects future sign-ins only",
            state.PreconditionFingerprint,
            new MachineActionRecoveryPayload(1, "exact-provider-state"));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "matasuri-action-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static readonly Guid UnresolvedActionId =
        Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private static MachineActionOutcome CreateResolvedOutcome(int index)
    {
        var started = new DateTimeOffset(
            2026, 8, 24, 0, 0, 0, TimeSpan.Zero).AddMinutes(index);
        return new(
            Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}"),
            new string('a', 64),
            new string('b', 64),
            MachineActionCapability.SetStartupEnabled,
            new(
                MachineActionTargetKind.StartupRegistryRunEntry,
                $"resolved:{index}",
                $"Resolved {index}"),
            "Disable at future sign-ins",
            "disabled",
            started,
            started.AddSeconds(1),
            MachineActionResultStatus.SucceededVerified,
            MachineActionVerificationStatus.Verified,
            Reversible: false,
            MachineActionUndoStatus.NotAvailable,
            UndoStartedAt: null,
            UndoCompletedAt: null,
            MachineActionVerificationStatus.NotAttempted,
            "enabled",
            "disabled",
            new string('c', 64),
            UserApproved: true,
            UndoUserApproved: false,
            RecoveryPayload: null,
            MachineActionRecoveryClassification.NotRequired,
            MachineActionRecoveryClassification.NotRequired,
            FailureCode: null);
    }

    private static MachineActionOutcome CreateUnresolvedOutcome()
    {
        var resolved = CreateResolvedOutcome(999);
        return resolved with
        {
            ActionId = UnresolvedActionId,
            Reversible = true,
            UndoState = MachineActionUndoStatus.Available,
            RecoveryPayload = new(1, "unresolved-exact-provider-state")
        };
    }

    private sealed class FileTestExecutor : IMachineActionExecutor
    {
        internal MachineActionTarget Target { get; } = new(
            MachineActionTargetKind.StartupRegistryRunEntry,
            "hkcu-run:file-test",
            "File test startup app");

        internal string State { get; set; } = "enabled";

        internal Action? OnExecute { get; set; }

        internal int ExecuteCount { get; private set; }

        public MachineActionCapability Capability =>
            MachineActionCapability.SetStartupEnabled;

        public MachineActionTargetKind TargetKind => Target.Kind;

        internal MachineActionTargetState GetState() =>
            MachineActionTargetState.Supported(
                Target, State, "file-provider-v1");

        public Task<MachineActionTargetState> ReadStateAsync(
            MachineActionTarget target,
            MachineActionRecoveryPayload? recoveryPayload,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(GetState());

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
            CancellationToken cancellationToken = default) =>
            Task.FromResult(MachineActionMutationResult.Completed());
    }

    private sealed class SeedStore : IMachineActionOutcomeStore
    {
        internal SeedStore(MachineActionOutcomePersistedState state)
        {
            State = state;
        }

        internal MachineActionOutcomePersistedState State { get; private set; }

        public Task<MachineActionOutcomePersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MachineActionOutcomePersistedState?>(State);

        public Task SaveAsync(
            MachineActionOutcomePersistedState state,
            CancellationToken cancellationToken = default)
        {
            State = state;
            return Task.CompletedTask;
        }
    }
}
