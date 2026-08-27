using System.Security;
using System.Security.Cryptography;
using Machine.Core;

namespace Machine.Windows;

internal sealed class WindowsStartupFolderExecutor
    : IMachineActionExecutor
{
    private readonly string _startupRoot;
    private readonly string _recoveryRoot;

    internal WindowsStartupFolderExecutor(
        string startupRoot,
        string recoveryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startupRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryRoot);
        _startupRoot = Path.GetFullPath(startupRoot)
            .TrimEnd(Path.DirectorySeparatorChar);
        _recoveryRoot = Path.GetFullPath(recoveryRoot)
            .TrimEnd(Path.DirectorySeparatorChar);
    }

    public MachineActionCapability Capability =>
        MachineActionCapability.SetStartupEnabled;

    public MachineActionTargetKind TargetKind =>
        MachineActionTargetKind.StartupFolderEntry;

    public Task<MachineActionTargetState> ReadStateAsync(
        MachineActionTarget target,
        MachineActionRecoveryPayload? recoveryPayload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolve(target, recoveryPayload, out var recovery,
                out var originalPath, out var stagedPath))
        {
            return Task.FromResult(Unavailable(
                target, MachineActionAvailability.Unsupported,
                "unsupported-target"));
        }

        try
        {
            var original = ReadRegularFile(originalPath!);
            var staged = ReadRegularFile(stagedPath!);
            if (original is not null && staged is null)
            {
                return Task.FromResult(MachineActionTargetState.Supported(
                    target,
                    WindowsStartupActionState.FolderEnabled(
                        original.Value.Length, original.Value.Sha256),
                    originalPath!,
                    original.Value.Length.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    original.Value.Sha256));
            }

            if (original is null && staged is not null &&
                Matches(staged.Value, recovery!))
            {
                return Task.FromResult(MachineActionTargetState.Supported(
                    target,
                    WindowsStartupActionState.Disabled,
                    recovery!.FileName!,
                    recovery.RecoveryFileName!,
                    staged.Value.Sha256));
            }

            var state = original is null && staged is null
                ? WindowsStartupActionState.Missing
                : "conflict";
            return Task.FromResult(MachineActionTargetState.Supported(
                target, state, recovery!.FileName!));
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException or
                SecurityException)
        {
            return Task.FromResult(Unavailable(
                target, MachineActionAvailability.PermissionRequired,
                "permission-required"));
        }
        catch (IOException)
        {
            return Task.FromResult(Unavailable(
                target, MachineActionAvailability.Unsupported,
                "file-unavailable"));
        }
    }

    public Task<MachineActionMutationResult> ExecuteAsync(
        MachineActionPlan plan,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (plan.RequestedNormalizedState !=
                WindowsStartupActionState.Disabled ||
            !TryResolve(plan.Target, plan.RecoveryPayload,
                out var recovery, out var originalPath, out var stagedPath))
        {
            return Task.FromResult(
                MachineActionMutationResult.Failed("unsupported-target"));
        }

        try
        {
            if (!IsSafeRoot(_startupRoot) ||
                !EnsureSafeRecoveryRoot() ||
                File.Exists(stagedPath) || Directory.Exists(stagedPath))
            {
                return Task.FromResult(MachineActionMutationResult.Failed(
                    "recovery-conflict"));
            }

            var current = ReadRegularFile(originalPath!);
            if (current is null || !Matches(current.Value, recovery!))
            {
                return Task.FromResult(MachineActionMutationResult.Failed(
                    "target-changed"));
            }

            File.Move(originalPath!, stagedPath!);
            var staged = ReadRegularFile(stagedPath!);
            return Task.FromResult(
                !File.Exists(originalPath) && staged is not null &&
                Matches(staged.Value, recovery!)
                    ? MachineActionMutationResult.Completed()
                    : MachineActionMutationResult.Failed(
                        "move-not-verified"));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                SecurityException)
        {
            return Task.FromResult(MachineActionMutationResult.Failed(
                "startup-file-disable-failed"));
        }
    }

    public Task<MachineActionMutationResult> UndoAsync(
        MachineActionUndoPlan plan,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolve(plan.Target, plan.RecoveryPayload,
                out var recovery, out var originalPath, out var stagedPath))
        {
            return Task.FromResult(
                MachineActionMutationResult.Failed("unsupported-target"));
        }

        try
        {
            if (!IsSafeRoot(_startupRoot) ||
                !IsSafeRoot(_recoveryRoot) ||
                File.Exists(originalPath) || Directory.Exists(originalPath))
            {
                return Task.FromResult(MachineActionMutationResult.Failed(
                    "restore-conflict"));
            }

            var staged = ReadRegularFile(stagedPath!);
            if (staged is null || !Matches(staged.Value, recovery!))
            {
                return Task.FromResult(MachineActionMutationResult.Failed(
                    "recovery-changed"));
            }

            File.Move(stagedPath!, originalPath!);
            var restored = ReadRegularFile(originalPath!);
            return Task.FromResult(
                !File.Exists(stagedPath) && restored is not null &&
                Matches(restored.Value, recovery!)
                    ? MachineActionMutationResult.Completed()
                    : MachineActionMutationResult.Failed(
                        "restore-not-verified"));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                SecurityException)
        {
            return Task.FromResult(MachineActionMutationResult.Failed(
                "startup-file-restore-failed"));
        }
    }

    private bool TryResolve(
        MachineActionTarget target,
        MachineActionRecoveryPayload? payload,
        out WindowsStartupRecoveryData? recovery,
        out string? originalPath,
        out string? stagedPath)
    {
        originalPath = null;
        stagedPath = null;
        if (!WindowsStartupRecoveryPayload.TryReadFolder(
                target, payload, out recovery) || recovery is null)
        {
            return false;
        }

        originalPath = Path.GetFullPath(
            Path.Combine(_startupRoot, recovery.FileName!));
        stagedPath = Path.GetFullPath(
            Path.Combine(_recoveryRoot, recovery.RecoveryFileName!));
        if (!IsDirectChild(_startupRoot, originalPath) ||
            !IsDirectChild(_recoveryRoot, stagedPath))
        {
            return false;
        }

        var identity = MachineStartupIdentity.CreateStartupFolderEntry(
            MachineStartupScope.CurrentUser,
            originalPath.ToUpperInvariant());
        return string.Equals(identity, target.StableIdentity,
            StringComparison.Ordinal);
    }

    private bool EnsureSafeRecoveryRoot()
    {
        if (!Directory.Exists(_recoveryRoot))
        {
            var existingAncestor = Path.GetDirectoryName(_recoveryRoot);
            while (!string.IsNullOrWhiteSpace(existingAncestor) &&
                !Directory.Exists(existingAncestor))
            {
                existingAncestor = Path.GetDirectoryName(existingAncestor);
            }
            if (string.IsNullOrWhiteSpace(existingAncestor) ||
                !IsSafeRoot(existingAncestor))
            {
                return false;
            }
            Directory.CreateDirectory(_recoveryRoot);
        }
        return IsSafeRoot(_recoveryRoot);
    }

    private static bool IsSafeRoot(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        var ancestor = new DirectoryInfo(path).Parent;
        while (ancestor is not null)
        {
            if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
            ancestor = ancestor.Parent;
        }
        return true;
    }

    private static bool IsDirectChild(string root, string path) =>
        string.Equals(
            Path.GetDirectoryName(path)?.TrimEnd(
                Path.DirectorySeparatorChar),
            root.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static (long Length, string Sha256)? ReadRegularFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) != 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
        {
            return null;
        }

        var info = new FileInfo(path);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        return (info.Length,
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    private static bool Matches(
        (long Length, string Sha256) file,
        WindowsStartupRecoveryData recovery) =>
        file.Length == recovery.FileLength &&
        string.Equals(file.Sha256, recovery.FileSha256,
            StringComparison.Ordinal);

    private static MachineActionTargetState Unavailable(
        MachineActionTarget target,
        MachineActionAvailability availability,
        string state) => new(
            availability,
            state,
            MachineActionFingerprint.CreatePrecondition(
                target, state, "fixed-user-startup-folder"));
}
