using System.Globalization;
using System.Security;
using Machine.Core;
using Microsoft.Win32;

namespace Machine.Windows;

public sealed class WindowsMachineSoftwareInventoryProvider
    : IMachineSoftwareInventoryProvider
{
    private const string UninstallRegistryPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const long BytesPerKilobyte = 1024;

    private static readonly RegistrySource[] RegistrySources =
    [
        new(
            RegistryHive.LocalMachine,
            RegistryView.Registry64,
            MachineSoftwareScope.LocalMachine,
            MachineSoftwareRegistryView.Registry64),
        new(
            RegistryHive.LocalMachine,
            RegistryView.Registry32,
            MachineSoftwareScope.LocalMachine,
            MachineSoftwareRegistryView.Registry32),
        new(
            RegistryHive.CurrentUser,
            RegistryView.Registry64,
            MachineSoftwareScope.CurrentUser,
            MachineSoftwareRegistryView.Registry64),
        new(
            RegistryHive.CurrentUser,
            RegistryView.Registry32,
            MachineSoftwareScope.CurrentUser,
            MachineSoftwareRegistryView.Registry32)
    ];

    public Task<MachineSoftwareInventorySnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => CaptureInventory(cancellationToken),
            cancellationToken);
    }

    internal static MachineInstalledSoftwareSnapshot?
        MapRegistration(
            RegistrationValues values,
            MachineSoftwareScope scope,
            MachineSoftwareRegistryView registryView)
    {
        ArgumentNullException.ThrowIfNull(values);

        var name = ReadOptionalString(values.DisplayName);

        if (name is null ||
            IsSystemComponent(values.SystemComponent) ||
            ReadOptionalString(values.ParentKeyName) is not null ||
            IsUpdateReleaseType(values.ReleaseType))
        {
            return null;
        }

        return new MachineInstalledSoftwareSnapshot(
            Name: name,
            Version: ReadOptionalString(values.DisplayVersion),
            Publisher: ReadOptionalString(values.Publisher),
            InstallLocation: ReadOptionalString(
                values.InstallLocation),
            EstimatedSizeBytes: ConvertEstimatedSizeToBytes(
                values.EstimatedSize),
            Scope: scope,
            RegistryView: registryView);
    }

    private static MachineSoftwareInventorySnapshot CaptureInventory(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = new List<MachineInstalledSoftwareSnapshot>();
        var skippedEntryCount = 0;
        var isComplete = true;

        foreach (var source in RegistrySources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(
                    source.Hive,
                    source.RegistryView);
                using var uninstallKey = baseKey.OpenSubKey(
                    UninstallRegistryPath,
                    writable: false);

                if (uninstallKey is null)
                {
                    continue;
                }

                foreach (var subKeyName in
                    uninstallKey.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        using var registrationKey =
                            uninstallKey.OpenSubKey(
                                subKeyName,
                                writable: false);

                        if (registrationKey is null)
                        {
                            skippedEntryCount++;
                            isComplete = false;
                            continue;
                        }

                        var values = ReadRegistrationValues(
                            registrationKey);
                        var item = MapRegistration(
                            values,
                            source.Scope,
                            source.SoftwareRegistryView);

                        if (item is not null)
                        {
                            items.Add(item);
                        }
                    }
                    catch (Exception exception)
                        when (IsRegistryReadException(exception))
                    {
                        skippedEntryCount++;
                        isComplete = false;
                    }
                }
            }
            catch (Exception exception)
                when (IsRegistryReadException(exception))
            {
                skippedEntryCount++;
                isComplete = false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var orderedItems = items
            .OrderBy(
                item => item.Name,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item => item.Publisher ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item => item.Version ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Scope)
            .ThenBy(item => item.RegistryView)
            .ThenBy(
                item => item.Name,
                StringComparer.Ordinal)
            .ThenBy(
                item => item.Publisher ?? string.Empty,
                StringComparer.Ordinal)
            .ThenBy(
                item => item.Version ?? string.Empty,
                StringComparer.Ordinal)
            .ToArray();

        return new MachineSoftwareInventorySnapshot(
            Items: orderedItems,
            IsComplete: isComplete,
            SkippedEntryCount: skippedEntryCount,
            CapturedAt: DateTimeOffset.UtcNow);
    }

    private static RegistrationValues ReadRegistrationValues(
        RegistryKey registrationKey) =>
        new(
            DisplayName: ReadValue(
                registrationKey,
                "DisplayName"),
            DisplayVersion: ReadValue(
                registrationKey,
                "DisplayVersion"),
            Publisher: ReadValue(
                registrationKey,
                "Publisher"),
            InstallLocation: ReadValue(
                registrationKey,
                "InstallLocation"),
            EstimatedSize: ReadValue(
                registrationKey,
                "EstimatedSize"),
            SystemComponent: ReadValue(
                registrationKey,
                "SystemComponent"),
            ParentKeyName: ReadValue(
                registrationKey,
                "ParentKeyName"),
            ReleaseType: ReadValue(
                registrationKey,
                "ReleaseType"));

    private static object? ReadValue(
        RegistryKey registrationKey,
        string valueName) =>
        registrationKey.GetValue(
            valueName,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames);

    private static string? ReadOptionalString(object? value)
    {
        if (value is not string text)
        {
            return null;
        }

        var trimmedText = text.Trim();

        return trimmedText.Length == 0
            ? null
            : trimmedText;
    }

    private static bool IsSystemComponent(object? value) =>
        TryConvertNonNegativeInteger(value, out var numericValue) &&
        numericValue == 1;

    private static bool IsUpdateReleaseType(object? value)
    {
        var releaseType = ReadOptionalString(value);

        return releaseType is not null &&
            (string.Equals(
                 releaseType,
                 "Update",
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 releaseType,
                 "Hotfix",
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 releaseType,
                 "Security Update",
                 StringComparison.OrdinalIgnoreCase));
    }

    private static long? ConvertEstimatedSizeToBytes(object? value)
    {
        if (!TryConvertNonNegativeInteger(
                value,
                out var estimatedSizeKilobytes) ||
            estimatedSizeKilobytes >
                (ulong)(long.MaxValue / BytesPerKilobyte))
        {
            return null;
        }

        return checked(
            (long)estimatedSizeKilobytes * BytesPerKilobyte);
    }

    private static bool TryConvertNonNegativeInteger(
        object? value,
        out ulong numericValue)
    {
        switch (value)
        {
            case byte byteValue:
                numericValue = byteValue;
                return true;
            case ushort ushortValue:
                numericValue = ushortValue;
                return true;
            case uint uintValue:
                numericValue = uintValue;
                return true;
            case ulong ulongValue:
                numericValue = ulongValue;
                return true;
            case sbyte sbyteValue when sbyteValue >= 0:
                numericValue = (ulong)sbyteValue;
                return true;
            case short shortValue when shortValue >= 0:
                numericValue = (ulong)shortValue;
                return true;
            case int intValue when intValue >= 0:
                numericValue = (ulong)intValue;
                return true;
            case long longValue when longValue >= 0:
                numericValue = (ulong)longValue;
                return true;
            case string text when ulong.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedValue):
                numericValue = parsedValue;
                return true;
            default:
                numericValue = 0;
                return false;
        }
    }

    private static bool IsRegistryReadException(
        Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            SecurityException;

    internal sealed record RegistrationValues(
        object? DisplayName,
        object? DisplayVersion,
        object? Publisher,
        object? InstallLocation,
        object? EstimatedSize,
        object? SystemComponent,
        object? ParentKeyName,
        object? ReleaseType);

    private readonly record struct RegistrySource(
        RegistryHive Hive,
        RegistryView RegistryView,
        MachineSoftwareScope Scope,
        MachineSoftwareRegistryView SoftwareRegistryView);
}
