using Machine.Core;

namespace Machine.Windows;

public enum WindowsStartupActionPlanStatus
{
    Ready,
    Unsupported,
    PermissionRequired,
    Protected,
    TargetChanged
}

public sealed record WindowsStartupActionPlanResult(
    WindowsStartupActionPlanStatus Status,
    MachineActionPlan? Plan = null,
    string? FailureCode = null);

public sealed class WindowsStartupActionService
{
    private readonly IMachineStartupInventoryProvider _inventoryProvider;
    private readonly MachineActionOutcomeMemory _memory;
    private readonly MachineActionCoordinator _coordinator;
    private readonly WindowsStartupRegistryRunExecutor _registryExecutor;
    private readonly WindowsStartupFolderExecutor _folderExecutor;
    private readonly string _startupFolderPath;

    public WindowsStartupActionService(
        IMachineStartupInventoryProvider inventoryProvider,
        MachineActionOutcomeMemory outcomeMemory)
        : this(
            inventoryProvider,
            outcomeMemory,
            new WindowsStartupRegistryAccessor(),
            GetStartupFolderPath(),
            GetRecoveryDirectory(),
            WindowsStartupRegistryVirtualization.IsSupported)
    {
    }

    internal WindowsStartupActionService(
        IMachineStartupInventoryProvider inventoryProvider,
        MachineActionOutcomeMemory outcomeMemory,
        IWindowsStartupRegistryAccessor registryAccessor,
        string startupFolderPath,
        string actionRecoveryDirectory,
        bool supportsUnvirtualizedRegistryWrites = true)
    {
        ArgumentNullException.ThrowIfNull(inventoryProvider);
        ArgumentNullException.ThrowIfNull(outcomeMemory);
        ArgumentNullException.ThrowIfNull(registryAccessor);
        ArgumentException.ThrowIfNullOrWhiteSpace(startupFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionRecoveryDirectory);
        _inventoryProvider = inventoryProvider;
        _memory = outcomeMemory;
        _startupFolderPath = Path.GetFullPath(startupFolderPath)
            .TrimEnd(Path.DirectorySeparatorChar);
        _registryExecutor = new(
            registryAccessor,
            supportsUnvirtualizedRegistryWrites);
        _folderExecutor = new(
            _startupFolderPath,
            actionRecoveryDirectory);
        _coordinator = new MachineActionCoordinator(
            new MachineActionExecutorRegistry(
                [_registryExecutor, _folderExecutor]),
            outcomeMemory);
    }

    public async Task<WindowsStartupActionPlanResult>
        CreateDisablePlanAsync(
            MachineStartupApplicationSnapshot snapshot,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.StableIdentity) ||
            string.IsNullOrWhiteSpace(
                snapshot.ActionPreconditionFingerprint))
        {
            return new(WindowsStartupActionPlanStatus.Unsupported,
                FailureCode: "missing-verified-action-identity");
        }

        var inventory = await _inventoryProvider.GetAsync(cancellationToken)
            .ConfigureAwait(false);
        var matches = inventory.Items.Where(item => string.Equals(
                item.StableIdentity,
                snapshot.StableIdentity,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            return new(WindowsStartupActionPlanStatus.TargetChanged,
                FailureCode: "target-not-uniquely-present");
        }

        var current = matches[0];
        if (!string.Equals(
                current.ActionPreconditionFingerprint,
                snapshot.ActionPreconditionFingerprint,
                StringComparison.Ordinal))
        {
            return new(WindowsStartupActionPlanStatus.TargetChanged,
                FailureCode: "inventory-precondition-changed");
        }

        var unavailable = GetUnavailableResult(current);
        if (unavailable is not null)
        {
            return unavailable;
        }

        return current.Source switch
        {
            MachineStartupSource.RegistryRunKey =>
                await CreateRegistryPlanAsync(
                    current, cancellationToken).ConfigureAwait(false),
            MachineStartupSource.StartupFolder =>
                await CreateFolderPlanAsync(
                    current, cancellationToken).ConfigureAwait(false),
            _ => new(WindowsStartupActionPlanStatus.Unsupported,
                FailureCode: "unsupported-startup-provider")
        };
    }

    public Task<MachineActionCoordinatorResult> ExecuteAsync(
        MachineActionPlan plan,
        MachineActionApproval? approval,
        CancellationToken cancellationToken = default) =>
        _coordinator.ExecuteAsync(plan, approval, cancellationToken);

    public Task<IReadOnlyList<MachineActionOutcome>> GetOutcomesAsync(
        CancellationToken cancellationToken = default) =>
        _memory.GetAsync(cancellationToken);

    public Task<IReadOnlyList<MachineActionOutcome>>
        ReconcileInProgressAsync(
            CancellationToken cancellationToken = default) =>
        _coordinator.ReconcileInProgressAsync(cancellationToken);

    public Task<MachineActionUndoCoordinatorResult> UndoAsync(
        MachineActionUndoPlan plan,
        MachineActionApproval? approval,
        CancellationToken cancellationToken = default) =>
        _coordinator.UndoAsync(plan, approval, cancellationToken);

    public static MachineActionUndoPlan CreateUndoPlan(
        MachineActionOutcome outcome) =>
        MachineActionUndoPlan.Create(outcome);

    private async Task<WindowsStartupActionPlanResult>
        CreateRegistryPlanAsync(
            MachineStartupApplicationSnapshot current,
            CancellationToken cancellationToken)
    {
        if (current.Scope != MachineStartupScope.CurrentUser ||
            current.RegistryView != MachineStartupRegistryView.Shared ||
            current.RegistryValueName is null ||
            current.RegistryValueData is null ||
            current.RegistryValueKind is not (
                MachineStartupRegistryValueKind.String or
                MachineStartupRegistryValueKind.ExpandString))
        {
            return new(WindowsStartupActionPlanStatus.Unsupported,
                FailureCode: "registry-target-not-allowlisted");
        }

        var target = new MachineActionTarget(
            MachineActionTargetKind.StartupRegistryRunEntry,
            current.StableIdentity!,
            current.Name);
        var recovery = WindowsStartupRecoveryPayload.CreateRegistry(
            current.RegistryValueName,
            current.RegistryValueKind.Value,
            current.RegistryValueData);
        return await CreatePlanAsync(
            current,
            target,
            recovery,
            _registryExecutor,
            StartupPlanPresentation.Registry(current.Name),
            actionId: null,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<WindowsStartupActionPlanResult>
        CreateFolderPlanAsync(
            MachineStartupApplicationSnapshot current,
            CancellationToken cancellationToken)
    {
        if (current.Scope != MachineStartupScope.CurrentUser ||
            current.FileLength is null or < 0 ||
            !WindowsStartupActionState.IsSha256(current.FileSha256))
        {
            return new(WindowsStartupActionPlanStatus.Unsupported,
                FailureCode: "folder-target-not-allowlisted");
        }

        var currentPath = Path.GetFullPath(current.CommandOrPath);
        if (!string.Equals(
                Path.GetDirectoryName(currentPath)?.TrimEnd(
                    Path.DirectorySeparatorChar),
                _startupFolderPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(WindowsStartupActionPlanStatus.Unsupported,
                FailureCode: "folder-target-outside-fixed-root");
        }

        var actionId = Guid.NewGuid();
        var target = new MachineActionTarget(
            MachineActionTargetKind.StartupFolderEntry,
            current.StableIdentity!,
            current.Name);
        var recovery = WindowsStartupRecoveryPayload.CreateFolder(
            Path.GetFileName(currentPath),
            current.FileLength.Value,
            current.FileSha256!,
            actionId);
        return await CreatePlanAsync(
            current,
            target,
            recovery,
            _folderExecutor,
            StartupPlanPresentation.Folder(current.Name),
            actionId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WindowsStartupActionPlanResult>
        CreatePlanAsync(
            MachineStartupApplicationSnapshot current,
            MachineActionTarget target,
            MachineActionRecoveryPayload recovery,
            IMachineActionExecutor executor,
            StartupPlanPresentation presentation,
            Guid? actionId,
            CancellationToken cancellationToken)
    {
        var state = await executor.ReadStateAsync(
                target, recovery, cancellationToken)
            .ConfigureAwait(false);
        if (state.Availability != MachineActionAvailability.Supported)
        {
            return new(
                state.Availability ==
                    MachineActionAvailability.PermissionRequired
                    ? WindowsStartupActionPlanStatus.PermissionRequired
                    : WindowsStartupActionPlanStatus.Unsupported,
                FailureCode: "provider-state-unavailable");
        }

        if (!string.Equals(
                state.PreconditionFingerprint,
                current.ActionPreconditionFingerprint,
                StringComparison.Ordinal) ||
            !string.Equals(
                state.NormalizedState,
                current.ActionNormalizedState,
                StringComparison.Ordinal))
        {
            return new(WindowsStartupActionPlanStatus.TargetChanged,
                FailureCode: "provider-precondition-changed");
        }

        var plan = MachineActionPlan.Create(
            MachineActionCapability.SetStartupEnabled,
            target,
            currentState: presentation.CurrentState,
            currentNormalizedState: state.NormalizedState,
            requestedState: presentation.RequestedState,
            requestedNormalizedState: WindowsStartupActionState.Disabled,
            changeCategory: presentation.ChangeCategory,
            expectedEffect: presentation.ExpectedEffect,
            notAffected: presentation.NotAffected,
            reversible: true,
            requiresElevation: false,
            verification: presentation.Verification,
            limitations: presentation.Limitations,
            preconditionFingerprint: state.PreconditionFingerprint,
            recoveryPayload: recovery,
            actionId: actionId);
        return new(WindowsStartupActionPlanStatus.Ready, plan);
    }

    private static WindowsStartupActionPlanResult? GetUnavailableResult(
        MachineStartupApplicationSnapshot current)
    {
        if (current.IsMatasuri || current.ActionAvailability ==
            MachineStartupActionAvailability.Protected)
        {
            return new(WindowsStartupActionPlanStatus.Protected,
                FailureCode: "matasuri-self-protected");
        }

        return current.ActionAvailability switch
        {
            MachineStartupActionAvailability.Supported => null,
            MachineStartupActionAvailability.PermissionRequired =>
                new(WindowsStartupActionPlanStatus.PermissionRequired,
                    FailureCode: "machine-wide-target-is-read-only"),
            _ => new(WindowsStartupActionPlanStatus.Unsupported,
                FailureCode: "startup-target-is-unsupported")
        };
    }

    private static string GetStartupFolderPath()
    {
        var path = Environment.GetFolderPath(
            Environment.SpecialFolder.Startup);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                "The current-user Startup folder is unavailable.");
        }
        return path;
    }

    private static string GetRecoveryDirectory()
    {
        var local = WindowsKnownFolderPath.GetUnredirectedLocalAppData();
        return Path.Combine(
            local, "Matasuri", "ActionRecovery", "Startup");
    }

    private sealed record StartupPlanPresentation(
        string CurrentState,
        string RequestedState,
        string ChangeCategory,
        string ExpectedEffect,
        string NotAffected,
        string Verification,
        string Limitations)
    {
        internal static StartupPlanPresentation Registry(string name) => new(
            "Registered in the current-user Run key",
            "Remove this current-user Run registration",
            "Current-user Run registration",
            $"{name} will no longer be launched by this Run registration " +
                "at future sign-ins.",
            "No current process is stopped or launched. Application files, " +
                "other startup entries, and machine-wide settings are not " +
                "changed.",
            "Re-read the exact current-user Run value and verify that it is " +
                "absent.",
            "A later application update may recreate the registration.");

        internal static StartupPlanPresentation Folder(string name) => new(
            "Direct file in the current-user Startup folder",
            "Move this file to Matasuri recovery staging",
            "Current-user Startup-folder file",
            $"{name} will no longer be launched from this Startup-folder " +
                "file at future sign-ins.",
            "No current process is stopped or launched. The staged file is " +
                "retained for undo; other startup items and machine-wide " +
                "settings are not changed.",
            "Verify the direct Startup-folder file is absent and its exact " +
                "hash exists in Matasuri recovery staging.",
            "A later application update may recreate the Startup-folder " +
                "file.");
    }
}
