using System.Security;
using Machine.Core;
using Microsoft.Win32;

namespace Machine.Windows;

internal sealed record WindowsStartupRegistryValue(
    MachineStartupRegistryValueKind Kind,
    string? UnexpandedData,
    string? ExactValueName = null);

internal interface IWindowsStartupRegistryAccessor
{
    WindowsStartupRegistryValue? Read(string exactValueName);

    void Delete(
        string exactValueName,
        MachineStartupRegistryValueKind expectedKind,
        string expectedUnexpandedData);

    void Write(
        string exactValueName,
        MachineStartupRegistryValueKind kind,
        string unexpandedData);
}

internal sealed class WindowsStartupRegistryAccessor
    : IWindowsStartupRegistryAccessor
{
    private const string RunRegistryPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public WindowsStartupRegistryValue? Read(string exactValueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(
            RegistryHive.CurrentUser, RegistryView.Default);
        using var runKey = baseKey.OpenSubKey(
            RunRegistryPath, writable: false);
        if (runKey is null)
        {
            return null;
        }

        var actualName = runKey.GetValueNames().SingleOrDefault(name =>
            string.Equals(name, exactValueName,
                StringComparison.OrdinalIgnoreCase));
        if (actualName is null)
        {
            return null;
        }

        var kind = Map(runKey.GetValueKind(actualName));
        var value = runKey.GetValue(
            actualName,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames);
        return new(kind, value as string, actualName);
    }

    public void Delete(
        string exactValueName,
        MachineStartupRegistryValueKind expectedKind,
        string expectedUnexpandedData)
    {
        using var baseKey = RegistryKey.OpenBaseKey(
            RegistryHive.CurrentUser, RegistryView.Default);
        using var runKey = baseKey.OpenSubKey(
            RunRegistryPath, writable: true) ??
            throw new IOException("The fixed HKCU Run key is unavailable.");
        if (!runKey.GetValueNames().Contains(
                exactValueName, StringComparer.Ordinal) ||
            Map(runKey.GetValueKind(exactValueName)) != expectedKind ||
            !string.Equals(
                runKey.GetValue(
                    exactValueName,
                    defaultValue: null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames)
                    as string,
                expectedUnexpandedData,
                StringComparison.Ordinal))
        {
            throw new IOException(
                "The exact HKCU Run value changed before deletion.");
        }
        runKey.DeleteValue(exactValueName, throwOnMissingValue: true);
    }

    public void Write(
        string exactValueName,
        MachineStartupRegistryValueKind kind,
        string unexpandedData)
    {
        using var baseKey = RegistryKey.OpenBaseKey(
            RegistryHive.CurrentUser, RegistryView.Default);
        using var runKey = baseKey.OpenSubKey(
            RunRegistryPath, writable: true) ??
            throw new IOException("The fixed HKCU Run key is unavailable.");
        if (runKey.GetValueNames().Any(name => string.Equals(
                name, exactValueName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new IOException(
                "The HKCU Run restore destination is occupied.");
        }
        runKey.SetValue(
            exactValueName,
            unexpandedData,
            kind switch
            {
                MachineStartupRegistryValueKind.String =>
                    RegistryValueKind.String,
                MachineStartupRegistryValueKind.ExpandString =>
                    RegistryValueKind.ExpandString,
                _ => throw new InvalidOperationException(
                    "Only String and ExpandString are allowlisted.")
            });
    }

    private static MachineStartupRegistryValueKind Map(
        RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.String => MachineStartupRegistryValueKind.String,
        RegistryValueKind.ExpandString =>
            MachineStartupRegistryValueKind.ExpandString,
        RegistryValueKind.Binary => MachineStartupRegistryValueKind.Binary,
        RegistryValueKind.DWord => MachineStartupRegistryValueKind.DWord,
        RegistryValueKind.MultiString =>
            MachineStartupRegistryValueKind.MultiString,
        RegistryValueKind.QWord => MachineStartupRegistryValueKind.QWord,
        RegistryValueKind.None => MachineStartupRegistryValueKind.None,
        _ => MachineStartupRegistryValueKind.Unknown
    };
}

internal sealed class WindowsStartupRegistryRunExecutor
    : IMachineActionExecutor
{
    private readonly IWindowsStartupRegistryAccessor _registry;
    private readonly bool _supportsUnvirtualizedRegistryWrites;

    internal WindowsStartupRegistryRunExecutor(
        IWindowsStartupRegistryAccessor registry,
        bool supportsUnvirtualizedRegistryWrites)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _supportsUnvirtualizedRegistryWrites =
            supportsUnvirtualizedRegistryWrites;
    }

    public MachineActionCapability Capability =>
        MachineActionCapability.SetStartupEnabled;

    public MachineActionTargetKind TargetKind =>
        MachineActionTargetKind.StartupRegistryRunEntry;

    public Task<MachineActionTargetState> ReadStateAsync(
        MachineActionTarget target,
        MachineActionRecoveryPayload? recoveryPayload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_supportsUnvirtualizedRegistryWrites)
        {
            return Task.FromResult(Unavailable(
                target,
                MachineActionAvailability.Unsupported,
                "unvirtualized-hkcu-run-unavailable"));
        }

        if (!WindowsStartupRecoveryPayload.TryReadRegistry(
                target, recoveryPayload, out var recovery) ||
            recovery is null)
        {
            return Task.FromResult(Unavailable(
                target, MachineActionAvailability.Unsupported,
                "unsupported-target"));
        }

        try
        {
            var value = _registry.Read(recovery.ValueName!);
            return Task.FromResult(value is null
                ? MachineActionTargetState.Supported(
                    target,
                    WindowsStartupActionState.Disabled,
                    recovery.ValueName!)
                : ReadPresent(target, value));
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException or
                SecurityException)
        {
            return Task.FromResult(Unavailable(
                target, MachineActionAvailability.PermissionRequired,
                "permission-required"));
        }
    }

    public Task<MachineActionMutationResult> ExecuteAsync(
        MachineActionPlan plan,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_supportsUnvirtualizedRegistryWrites ||
            plan.RequestedNormalizedState !=
                WindowsStartupActionState.Disabled ||
            !WindowsStartupRecoveryPayload.TryReadRegistry(
                plan.Target, plan.RecoveryPayload, out var recovery) ||
            recovery is null)
        {
            return Task.FromResult(
                MachineActionMutationResult.Failed("unsupported-target"));
        }

        try
        {
            var current = _registry.Read(recovery.ValueName!);
            if (!Matches(current, recovery))
            {
                return Task.FromResult(MachineActionMutationResult.Failed(
                    "target-changed"));
            }

            _registry.Delete(
                recovery.ValueName!,
                recovery.ValueKind!.Value,
                recovery.UnexpandedData!);
            return Task.FromResult(_registry.Read(recovery.ValueName!) is null
                ? MachineActionMutationResult.Completed()
                : MachineActionMutationResult.Failed("delete-not-verified"));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                SecurityException)
        {
            return Task.FromResult(MachineActionMutationResult.Failed(
                "registry-disable-failed"));
        }
    }

    public Task<MachineActionMutationResult> UndoAsync(
        MachineActionUndoPlan plan,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_supportsUnvirtualizedRegistryWrites ||
            !WindowsStartupRecoveryPayload.TryReadRegistry(
                plan.Target, plan.RecoveryPayload, out var recovery) ||
            recovery is null)
        {
            return Task.FromResult(
                MachineActionMutationResult.Failed("unsupported-target"));
        }

        try
        {
            if (_registry.Read(recovery.ValueName!) is not null)
            {
                return Task.FromResult(MachineActionMutationResult.Failed(
                    "restore-conflict"));
            }

            _registry.Write(
                recovery.ValueName!,
                recovery.ValueKind!.Value,
                recovery.UnexpandedData!);
            return Task.FromResult(Matches(
                    _registry.Read(recovery.ValueName!), recovery)
                ? MachineActionMutationResult.Completed()
                : MachineActionMutationResult.Failed(
                    "restore-not-verified"));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                SecurityException)
        {
            return Task.FromResult(MachineActionMutationResult.Failed(
                "registry-restore-failed"));
        }
    }

    private static MachineActionTargetState ReadPresent(
        MachineActionTarget target,
        WindowsStartupRegistryValue value)
    {
        var normalized = value.UnexpandedData is null
            ? $"enabled|unsupported-kind={(int)value.Kind}"
            : WindowsStartupActionState.RegistryEnabled(
                value.Kind, value.UnexpandedData);
        return MachineActionTargetState.Supported(
            target,
            normalized,
            value.ExactValueName ?? string.Empty,
            ((int)value.Kind).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            value.UnexpandedData ?? string.Empty);
    }

    private static MachineActionTargetState Unavailable(
        MachineActionTarget target,
        MachineActionAvailability availability,
        string state) => new(
            availability,
            state,
            MachineActionFingerprint.CreatePrecondition(
                target, state, "fixed-hkcu-run"));

    private static bool Matches(
        WindowsStartupRegistryValue? value,
        WindowsStartupRecoveryData recovery) =>
        value is not null &&
        string.Equals(value.ExactValueName,
            recovery.ValueName, StringComparison.Ordinal) &&
        value.Kind == recovery.ValueKind &&
        string.Equals(value.UnexpandedData,
            recovery.UnexpandedData, StringComparison.Ordinal);
}
