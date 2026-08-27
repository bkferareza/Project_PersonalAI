using System.Security.Cryptography;
using Machine.Core;
using Machine.Windows;

namespace Machine.Tests;

public sealed class WindowsStartupActionServiceTests
{
    [Fact]
    public void RecoveryRootUsesDocumentedUnredirectedLocalAppData()
    {
        var path = WindowsKnownFolderPath.GetUnredirectedLocalAppData();

        Assert.True(Path.IsPathFullyQualified(path));
        Assert.True(Directory.Exists(path));
        Assert.DoesNotContain(
            $"{Path.DirectorySeparatorChar}Packages" +
                $"{Path.DirectorySeparatorChar}",
            path,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProviderEnrichmentClassifiesOnlyAllowlistedTargetsAsSupported()
    {
        var user = WindowsMachineStartupInventoryProvider.MapRegistryEntry(
            "Agent",
            "%LOCALAPPDATA%\\Agent\\agent.exe",
            MachineStartupScope.CurrentUser,
            MachineStartupRegistryView.Shared,
            MachineStartupRegistryValueKind.ExpandString);
        var machine = WindowsMachineStartupInventoryProvider.MapRegistryEntry(
            "Agent",
            "C:\\Agent\\agent.exe",
            MachineStartupScope.AllUsers,
            MachineStartupRegistryView.Registry64,
            MachineStartupRegistryValueKind.String);
        var unsupported =
            WindowsMachineStartupInventoryProvider.MapRegistryEntry(
                "BinaryAgent",
                new byte[] { 1, 2, 3 },
                MachineStartupScope.CurrentUser,
                MachineStartupRegistryView.Shared,
                MachineStartupRegistryValueKind.Binary);
        var protectedItem =
            WindowsMachineStartupInventoryProvider.MapRegistryEntry(
                "Matasuri",
                "C:\\Matasuri\\Machine.App.exe",
                MachineStartupScope.CurrentUser,
                MachineStartupRegistryView.Shared,
                MachineStartupRegistryValueKind.String);

        Assert.NotNull(user);
        Assert.Equal(MachineStartupActionAvailability.Supported,
            user.ActionAvailability);
        Assert.Equal("%LOCALAPPDATA%\\Agent\\agent.exe",
            user.RegistryValueData);
        Assert.NotNull(user.StableIdentity);
        Assert.NotNull(user.ActionPreconditionFingerprint);
        Assert.Equal(MachineStartupActionAvailability.PermissionRequired,
            machine!.ActionAvailability);
        Assert.Equal(MachineStartupActionAvailability.Unsupported,
            unsupported!.ActionAvailability);
        Assert.Equal(MachineStartupActionAvailability.Protected,
            protectedItem!.ActionAvailability);
        Assert.True(protectedItem.IsMatasuri);
    }

    [Fact]
    public void FolderEnrichmentRejectsReparseAndKeepsCommonReadOnly()
    {
        const string root = "C:\\Users\\Machine\\Startup";
        var user = WindowsMachineStartupInventoryProvider
            .MapStartupFolderEntry(
                "Agent.lnk",
                root + "\\Agent.lnk",
                MachineStartupScope.CurrentUser,
                root,
                FileAttributes.Normal,
                10,
                new string('a', 64));
        var common = WindowsMachineStartupInventoryProvider
            .MapStartupFolderEntry(
                "Agent.lnk",
                "C:\\ProgramData\\Startup\\Agent.lnk",
                MachineStartupScope.AllUsers,
                "C:\\ProgramData\\Startup",
                FileAttributes.Normal,
                10,
                new string('b', 64));
        var reparse = WindowsMachineStartupInventoryProvider
            .MapStartupFolderEntry(
                "Agent.lnk",
                root + "\\Agent.lnk",
                MachineStartupScope.CurrentUser,
                root,
                FileAttributes.ReparsePoint,
                null,
                null);

        Assert.Equal(MachineStartupActionAvailability.Supported,
            user!.ActionAvailability);
        Assert.Equal(MachineStartupActionAvailability.PermissionRequired,
            common!.ActionAvailability);
        Assert.Equal(MachineStartupActionAvailability.Unsupported,
            reparse!.ActionAvailability);
    }

    [Fact]
    public void StableIdentityIncludesExactProviderScopeViewAndName()
    {
        var baseline = MachineStartupIdentity.CreateRegistryRunEntry(
            MachineStartupScope.CurrentUser,
            MachineStartupRegistryView.Shared,
            "ab:c");

        Assert.NotEqual(baseline,
            MachineStartupIdentity.CreateRegistryRunEntry(
                MachineStartupScope.CurrentUser,
                MachineStartupRegistryView.Shared,
                "a:bc"));
        Assert.NotEqual(baseline,
            MachineStartupIdentity.CreateRegistryRunEntry(
                MachineStartupScope.AllUsers,
                MachineStartupRegistryView.Shared,
                "ab:c"));
        Assert.NotEqual(baseline,
            MachineStartupIdentity.CreateRegistryRunEntry(
                MachineStartupScope.CurrentUser,
                MachineStartupRegistryView.Registry64,
                "ab:c"));
        Assert.Equal(64, baseline.Length);
    }

    [Theory]
    [InlineData(MachineStartupRegistryValueKind.String)]
    [InlineData(MachineStartupRegistryValueKind.ExpandString)]
    public async Task RegistryDisableAndUndoPreserveExactKindAndData(
        MachineStartupRegistryValueKind kind)
    {
        using var fixture = new ActionFixture();
        const string valueName = " Agent Exact ";
        const string data = "  %LOCALAPPDATA%\\Agent\\agent.exe --quiet  ";
        fixture.Registry.Values[valueName] = new(kind, data);
        fixture.Provider.Capture = () => RegistrySnapshot(
            valueName, fixture.Registry.Values[valueName]);
        var service = fixture.CreateService();
        var snapshot = RegistrySnapshot(
            valueName, fixture.Registry.Values[valueName]);

        var planned = await service.CreateDisablePlanAsync(snapshot);
        Assert.Equal(WindowsStartupActionPlanStatus.Ready, planned.Status);
        Assert.Equal("Remove this current-user Run registration",
            planned.Plan!.RequestedState);
        Assert.Contains("future sign-ins", planned.Plan.ExpectedEffect);
        Assert.Contains("No current process is stopped or launched",
            planned.Plan.NotAffected);
        Assert.DoesNotContain(data, planned.Plan.ExpectedEffect);
        var executed = await service.ExecuteAsync(
            planned.Plan!, MachineActionApproval.ForExecution(planned.Plan!));

        Assert.Equal(MachineActionResultStatus.SucceededVerified,
            executed.Status);
        Assert.False(fixture.Registry.Values.ContainsKey(valueName));
        Assert.Equal(MachineActionOutcomeMemory.PersistenceSchemaVersion,
            fixture.Store.State!.SchemaVersion);
        var outcome = Assert.Single(await service.GetOutcomesAsync());
        Assert.Equal(MachineActionUndoStatus.Available, outcome.UndoState);

        var undo = WindowsStartupActionService.CreateUndoPlan(outcome);
        var undone = await service.UndoAsync(
            undo, MachineActionApproval.ForUndo(undo));

        Assert.Equal(MachineActionUndoStatus.SucceededVerified,
            undone.Status);
        var restored = fixture.Registry.Values[valueName];
        Assert.Equal(kind, restored.Kind);
        Assert.Equal(data, restored.UnexpandedData);
    }

    [Fact]
    public async Task RegistryPlanRevalidatesInventoryAndUndoRefusesConflict()
    {
        using var fixture = new ActionFixture();
        const string valueName = "Agent";
        fixture.Registry.Values[valueName] = new(
            MachineStartupRegistryValueKind.String, "original");
        var original = RegistrySnapshot(
            valueName, fixture.Registry.Values[valueName]);
        fixture.Provider.Capture = () => RegistrySnapshot(
            valueName, fixture.Registry.Values[valueName]);
        var service = fixture.CreateService();

        fixture.Registry.Values[valueName] = new(
            MachineStartupRegistryValueKind.String, "changed");
        var stale = await service.CreateDisablePlanAsync(original);
        Assert.Equal(WindowsStartupActionPlanStatus.TargetChanged,
            stale.Status);

        var current = RegistrySnapshot(
            valueName, fixture.Registry.Values[valueName]);
        var planned = await service.CreateDisablePlanAsync(current);
        var executed = await service.ExecuteAsync(
            planned.Plan!, MachineActionApproval.ForExecution(planned.Plan!));
        const string conflictingName = "AGENT";
        fixture.Registry.Values[conflictingName] = new(
            MachineStartupRegistryValueKind.String, "conflict");

        var undo = WindowsStartupActionService.CreateUndoPlan(
            executed.Outcome!);
        var undone = await service.UndoAsync(
            undo, MachineActionApproval.ForUndo(undo));

        Assert.Equal(MachineActionUndoStatus.TargetChanged, undone.Status);
        Assert.Equal("conflict",
            fixture.Registry.Values[conflictingName].UnexpandedData);
        Assert.False(fixture.Registry.Values.ContainsKey(valueName));
    }

    [Fact]
    public async Task StartupFolderDisableMovesAndUndoRestoresExactFile()
    {
        using var fixture = new ActionFixture();
        var originalPath = Path.Combine(fixture.StartupRoot, "Agent.lnk");
        var bytes = new byte[] { 10, 20, 30, 40, 50 };
        await File.WriteAllBytesAsync(originalPath, bytes);
        var snapshot = FolderSnapshot(
            originalPath, fixture.StartupRoot);
        fixture.Provider.Capture = () => snapshot;
        var service = fixture.CreateService();

        var planned = await service.CreateDisablePlanAsync(snapshot);
        Assert.Equal("Move this file to Matasuri recovery staging",
            planned.Plan!.RequestedState);
        Assert.Contains("future sign-ins", planned.Plan.ExpectedEffect);
        Assert.DoesNotContain(originalPath, planned.Plan.ExpectedEffect);
        var executed = await service.ExecuteAsync(
            planned.Plan!, MachineActionApproval.ForExecution(planned.Plan!));

        Assert.Equal(MachineActionResultStatus.SucceededVerified,
            executed.Status);
        Assert.False(File.Exists(originalPath));
        var staged = Assert.Single(Directory.GetFiles(
            fixture.RecoveryRoot, "*.startup-recovery"));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(staged));

        var undo = WindowsStartupActionService.CreateUndoPlan(
            executed.Outcome!);
        var undone = await service.UndoAsync(
            undo, MachineActionApproval.ForUndo(undo));

        Assert.Equal(MachineActionUndoStatus.SucceededVerified,
            undone.Status);
        Assert.True(File.Exists(originalPath));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(originalPath));
        Assert.Empty(Directory.GetFiles(fixture.RecoveryRoot));
    }

    [Fact]
    public async Task StartupFolderUndoNeverOverwritesConflict()
    {
        using var fixture = new ActionFixture();
        var originalPath = Path.Combine(fixture.StartupRoot, "Agent.lnk");
        await File.WriteAllTextAsync(originalPath, "original");
        var snapshot = FolderSnapshot(
            originalPath, fixture.StartupRoot);
        fixture.Provider.Capture = () => snapshot;
        var service = fixture.CreateService();
        var planned = await service.CreateDisablePlanAsync(snapshot);
        var executed = await service.ExecuteAsync(
            planned.Plan!, MachineActionApproval.ForExecution(planned.Plan!));
        await File.WriteAllTextAsync(originalPath, "conflict");

        var undo = WindowsStartupActionService.CreateUndoPlan(
            executed.Outcome!);
        var undone = await service.UndoAsync(
            undo, MachineActionApproval.ForUndo(undo));

        Assert.Equal(MachineActionUndoStatus.TargetChanged, undone.Status);
        Assert.Equal("conflict", await File.ReadAllTextAsync(originalPath));
        Assert.Single(Directory.GetFiles(
            fixture.RecoveryRoot, "*.startup-recovery"));
    }

    [Fact]
    public async Task ReconcileUsesPersistedRecoveryAfterInterruptedRegistryAction()
    {
        using var fixture = new ActionFixture();
        const string valueName = "Agent";
        fixture.Registry.Values[valueName] = new(
            MachineStartupRegistryValueKind.String, "exact-command");
        var snapshot = RegistrySnapshot(
            valueName, fixture.Registry.Values[valueName]);
        fixture.Provider.Capture = () => snapshot;
        fixture.Store.FailOnSaveNumber = 2;
        var service = fixture.CreateService();
        var planned = await service.CreateDisablePlanAsync(snapshot);

        await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(
            planned.Plan!, MachineActionApproval.ForExecution(planned.Plan!)));
        Assert.False(fixture.Registry.Values.ContainsKey(valueName));
        Assert.Equal(MachineActionResultStatus.InProgress,
            Assert.Single(fixture.Store.State!.Outcomes).Result);

        fixture.Store.FailOnSaveNumber = null;
        var reconciled = Assert.Single(
            await service.ReconcileInProgressAsync());

        Assert.Equal(MachineActionResultStatus.SucceededVerified,
            reconciled.Result);
        Assert.Equal(MachineActionUndoStatus.Available,
            reconciled.UndoState);
    }

    [Fact]
    public async Task RegistryExecutionWithoutApprovalDoesNotMutateOrRecord()
    {
        using var fixture = new ActionFixture();
        const string valueName = "Agent";
        fixture.Registry.Values[valueName] = new(
            MachineStartupRegistryValueKind.String, "exact-command");
        var snapshot = RegistrySnapshot(
            valueName, fixture.Registry.Values[valueName]);
        fixture.Provider.Capture = () => RegistrySnapshot(
            valueName, fixture.Registry.Values[valueName]);
        var service = fixture.CreateService();
        var planned = await service.CreateDisablePlanAsync(snapshot);

        var result = await service.ExecuteAsync(planned.Plan!, approval: null);

        Assert.Equal(MachineActionResultStatus.NotApproved, result.Status);
        Assert.True(fixture.Registry.Values.ContainsKey(valueName));
        Assert.Null(fixture.Store.State);
    }

    [Fact]
    public async Task UndoWithoutPersistedOutcomeDoesNotRestoreTarget()
    {
        using var fixture = new ActionFixture();
        const string valueName = "Agent";
        fixture.Registry.Values[valueName] = new(
            MachineStartupRegistryValueKind.String, "exact-command");
        var snapshot = RegistrySnapshot(
            valueName, fixture.Registry.Values[valueName]);
        fixture.Provider.Capture = () => snapshot;
        var service = fixture.CreateService();
        var planned = await service.CreateDisablePlanAsync(snapshot);
        var executed = await service.ExecuteAsync(
            planned.Plan!,
            MachineActionApproval.ForExecution(planned.Plan!));
        var undo = WindowsStartupActionService.CreateUndoPlan(
            executed.Outcome!);
        var serviceWithoutOutcome = new WindowsStartupActionService(
            fixture.Provider,
            new MachineActionOutcomeMemory(new RecordingOutcomeStore()),
            fixture.Registry,
            fixture.StartupRoot,
            fixture.RecoveryRoot);

        var result = await serviceWithoutOutcome.UndoAsync(
            undo,
            MachineActionApproval.ForUndo(undo));

        Assert.Equal(MachineActionUndoStatus.NotAvailable, result.Status);
        Assert.False(fixture.Registry.Values.ContainsKey(valueName));
    }

    [Fact]
    public async Task ServiceExplicitlyProtectsMatasuri()
    {
        using var fixture = new ActionFixture();
        var snapshot = WindowsMachineStartupInventoryProvider.MapRegistryEntry(
            "Matasuri",
            "C:\\Matasuri\\Machine.App.exe",
            MachineStartupScope.CurrentUser,
            MachineStartupRegistryView.Shared,
            MachineStartupRegistryValueKind.String)!;
        fixture.Provider.Capture = () => snapshot;
        var service = fixture.CreateService();

        var result = await service.CreateDisablePlanAsync(snapshot);

        Assert.Equal(WindowsStartupActionPlanStatus.Protected, result.Status);
        Assert.Null(result.Plan);
    }

    [Fact]
    public async Task ServiceKeepsMachineWideRegistrationReadOnly()
    {
        using var fixture = new ActionFixture();
        var snapshot = WindowsMachineStartupInventoryProvider.MapRegistryEntry(
            "MachineAgent",
            "C:\\Agent\\agent.exe",
            MachineStartupScope.AllUsers,
            MachineStartupRegistryView.Registry64,
            MachineStartupRegistryValueKind.String)!;
        fixture.Provider.Capture = () => snapshot;
        var service = fixture.CreateService();

        var result = await service.CreateDisablePlanAsync(snapshot);

        Assert.Equal(WindowsStartupActionPlanStatus.PermissionRequired,
            result.Status);
        Assert.Null(result.Plan);
        Assert.Empty(fixture.Registry.Values);
    }

    [Fact]
    public async Task ServiceRejectsVirtualizedRegistryMutation()
    {
        using var fixture = new ActionFixture();
        const string valueName = "Agent";
        fixture.Registry.Values[valueName] = new(
            MachineStartupRegistryValueKind.String, "exact-command");
        var snapshot = RegistrySnapshot(
            valueName, fixture.Registry.Values[valueName]);
        fixture.Provider.Capture = () => snapshot;
        var service = fixture.CreateService(
            supportsUnvirtualizedRegistryWrites: false);

        var result = await service.CreateDisablePlanAsync(snapshot);

        Assert.Equal(WindowsStartupActionPlanStatus.Unsupported,
            result.Status);
        Assert.Null(result.Plan);
        Assert.True(fixture.Registry.Values.ContainsKey(valueName));
        Assert.Null(fixture.Store.State);
    }

    private static MachineStartupApplicationSnapshot RegistrySnapshot(
        string valueName,
        WindowsStartupRegistryValue value) =>
        WindowsMachineStartupInventoryProvider.MapRegistryEntry(
            valueName,
            value.UnexpandedData,
            MachineStartupScope.CurrentUser,
            MachineStartupRegistryView.Shared,
            value.Kind)!;

    private static MachineStartupApplicationSnapshot FolderSnapshot(
        string path,
        string root)
    {
        var info = new FileInfo(path);
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
        return WindowsMachineStartupInventoryProvider.MapStartupFolderEntry(
            info.Name,
            info.FullName,
            MachineStartupScope.CurrentUser,
            root,
            info.Attributes,
            info.Length,
            hash)!;
    }

    private sealed class ActionFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), $"matasuri-startup-actions-{Guid.NewGuid():N}");

        internal ActionFixture()
        {
            StartupRoot = Path.Combine(_root, "Startup");
            RecoveryRoot = Path.Combine(_root, "ActionRecovery");
            Directory.CreateDirectory(StartupRoot);
        }

        internal string StartupRoot { get; }

        internal string RecoveryRoot { get; }

        internal FakeRegistryAccessor Registry { get; } = new();

        internal DelegateInventoryProvider Provider { get; } = new();

        internal RecordingOutcomeStore Store { get; } = new();

        internal WindowsStartupActionService CreateService(
            bool supportsUnvirtualizedRegistryWrites = true) => new(
                Provider,
                new MachineActionOutcomeMemory(Store),
                Registry,
                StartupRoot,
                RecoveryRoot,
                supportsUnvirtualizedRegistryWrites);

        public void Dispose()
        {
            if (Directory.Exists(_root) &&
                Path.GetFileName(_root).StartsWith(
                    "matasuri-startup-actions-", StringComparison.Ordinal))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class FakeRegistryAccessor
        : IWindowsStartupRegistryAccessor
    {
        internal Dictionary<string, WindowsStartupRegistryValue> Values
            { get; } = new(StringComparer.Ordinal);

        public WindowsStartupRegistryValue? Read(string exactValueName) =>
            Values.Keys.SingleOrDefault(name => string.Equals(
                name, exactValueName, StringComparison.OrdinalIgnoreCase)) is
                { } actualName
                ? Values[actualName] with { ExactValueName = actualName }
                : null;

        public void Delete(
            string exactValueName,
            MachineStartupRegistryValueKind expectedKind,
            string expectedUnexpandedData)
        {
            if (!Values.TryGetValue(exactValueName, out var value) ||
                value.Kind != expectedKind ||
                value.UnexpandedData != expectedUnexpandedData ||
                !Values.Remove(exactValueName))
            {
                throw new IOException("Missing exact registry value.");
            }
        }

        public void Write(
            string exactValueName,
            MachineStartupRegistryValueKind kind,
            string unexpandedData)
        {
            if (!Values.TryAdd(
                exactValueName, new(kind, unexpandedData)))
            {
                throw new IOException("Registry restore conflict.");
            }
        }
    }

    private sealed class DelegateInventoryProvider
        : IMachineStartupInventoryProvider
    {
        internal Func<MachineStartupApplicationSnapshot> Capture { get; set; }
            = () => throw new InvalidOperationException();

        public Task<MachineStartupInventorySnapshot> GetAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new MachineStartupInventorySnapshot(
                [Capture()], true, 0, DateTimeOffset.UtcNow));
        }
    }

    private sealed class RecordingOutcomeStore : IMachineActionOutcomeStore
    {
        internal MachineActionOutcomePersistedState? State { get; private set; }

        internal int? FailOnSaveNumber { get; set; }

        private int SaveCount { get; set; }

        public Task<MachineActionOutcomePersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(State);

        public Task SaveAsync(
            MachineActionOutcomePersistedState state,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (SaveCount == FailOnSaveNumber)
            {
                throw new IOException("Simulated interrupted persistence.");
            }
            State = state;
            return Task.CompletedTask;
        }
    }
}
