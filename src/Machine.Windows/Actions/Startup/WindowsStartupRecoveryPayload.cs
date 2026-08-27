using System.Text.Json;
using Machine.Core;

namespace Machine.Windows;

internal sealed record WindowsStartupRecoveryData(
    string Provider,
    string? ValueName = null,
    MachineStartupRegistryValueKind? ValueKind = null,
    string? UnexpandedData = null,
    string? FileName = null,
    long? FileLength = null,
    string? FileSha256 = null,
    string? RecoveryFileName = null);

internal static class WindowsStartupRecoveryPayload
{
    internal const int Version = 1;
    internal const string RegistryProvider = "windows-hkcu-run-v1";
    internal const string FolderProvider = "windows-user-startup-folder-v1";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false
    };

    internal static MachineActionRecoveryPayload CreateRegistry(
        string exactValueName,
        MachineStartupRegistryValueKind valueKind,
        string unexpandedData)
    {
        ArgumentNullException.ThrowIfNull(exactValueName);
        ArgumentNullException.ThrowIfNull(unexpandedData);
        var data = new WindowsStartupRecoveryData(
            RegistryProvider,
            ValueName: exactValueName,
            ValueKind: valueKind,
            UnexpandedData: unexpandedData);
        return Create(data);
    }

    internal static MachineActionRecoveryPayload CreateFolder(
        string fileName,
        long fileLength,
        string fileSha256,
        Guid actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileSha256);
        var data = new WindowsStartupRecoveryData(
            FolderProvider,
            FileName: fileName,
            FileLength: fileLength,
            FileSha256: fileSha256,
            RecoveryFileName:
                $"{actionId:N}.startup-recovery");
        return Create(data);
    }

    internal static bool TryReadRegistry(
        MachineActionTarget target,
        MachineActionRecoveryPayload? payload,
        out WindowsStartupRecoveryData? data)
    {
        data = TryRead(payload);
        if (data is null || data.Provider != RegistryProvider ||
            data.ValueName is null || data.ValueName.Length > 1_024 ||
            data.UnexpandedData is null ||
            data.UnexpandedData.Length is 0 or > 8_192 ||
            data.ValueKind is not (
                MachineStartupRegistryValueKind.String or
                MachineStartupRegistryValueKind.ExpandString))
        {
            data = null;
            return false;
        }

        var identity = MachineStartupIdentity.CreateRegistryRunEntry(
            MachineStartupScope.CurrentUser,
            MachineStartupRegistryView.Shared,
            data.ValueName);
        if (!string.Equals(identity, target.StableIdentity,
                StringComparison.Ordinal) ||
            WindowsStartupSelfProtection.IsMatasuri(
                target.DisplayName, data.UnexpandedData))
        {
            data = null;
            return false;
        }

        return true;
    }

    internal static bool TryReadFolder(
        MachineActionTarget target,
        MachineActionRecoveryPayload? payload,
        out WindowsStartupRecoveryData? data)
    {
        data = TryRead(payload);
        if (data is null || data.Provider != FolderProvider ||
            !IsDirectFileName(data.FileName) ||
            data.FileLength is null or < 0 ||
            !WindowsStartupActionState.IsSha256(data.FileSha256) ||
            !IsRecoveryFileName(data.RecoveryFileName) ||
            WindowsStartupSelfProtection.IsMatasuri(
                target.DisplayName, data.FileName))
        {
            data = null;
            return false;
        }

        return true;
    }

    private static MachineActionRecoveryPayload Create(
        WindowsStartupRecoveryData data) =>
        new(Version, JsonSerializer.Serialize(data, Options));

    private static WindowsStartupRecoveryData? TryRead(
        MachineActionRecoveryPayload? payload)
    {
        if (payload is null || payload.Version != Version ||
            string.IsNullOrWhiteSpace(payload.ProviderData))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WindowsStartupRecoveryData>(
                payload.ProviderData, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsDirectFileName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 255 &&
        string.Equals(value, Path.GetFileName(value),
            StringComparison.Ordinal) &&
        value is not "." and not ".." &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static bool IsRecoveryFileName(string? value)
    {
        const string suffix = ".startup-recovery";
        return value?.Length == 32 + suffix.Length &&
            value.EndsWith(suffix, StringComparison.Ordinal) &&
            Guid.TryParseExact(value[..32], "N", out _);
    }
}
