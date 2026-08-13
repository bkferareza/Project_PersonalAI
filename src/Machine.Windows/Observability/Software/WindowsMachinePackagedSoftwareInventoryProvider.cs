using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using Machine.Core;
using Windows.ApplicationModel;
using Windows.Foundation.Metadata;
using Windows.Management.Deployment;
using WindowsProcessorArchitecture =
    Windows.System.ProcessorArchitecture;

namespace Machine.Windows;

public sealed class WindowsMachinePackagedSoftwareInventoryProvider
    : IMachinePackagedSoftwareInventoryProvider
{
    private const string PackageRuntimeClassName =
        "Windows.ApplicationModel.Package";
    private const string UnresolvedResourcePrefix =
        "ms-resource:";

    public Task<MachinePackagedSoftwareInventorySnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => CaptureInventory(cancellationToken),
            cancellationToken);
    }

    internal static MachinePackagedSoftwareSnapshot? MapPackage(
        PackageValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var identityName = ReadOptionalText(values.IdentityName);
        var displayName = ReadOptionalLocalizedText(
                values.DisplayName) ??
            identityName;
        var packageFamilyName = ReadOptionalText(
            values.PackageFamilyName);
        var packageFullName = ReadOptionalText(
            values.PackageFullName);

        if (displayName is null ||
            packageFamilyName is null ||
            packageFullName is null)
        {
            return null;
        }

        return new MachinePackagedSoftwareSnapshot(
            DisplayName: displayName,
            PublisherDisplayName: ReadOptionalLocalizedText(
                values.PublisherDisplayName),
            PackageFamilyName: packageFamilyName,
            PackageFullName: packageFullName,
            Version: FormatVersion(values.Version),
            Architecture: MapArchitecture(values.Architecture),
            InstalledLocation: ReadOptionalText(
                values.InstalledLocation),
            IsDevelopmentMode: values.IsDevelopmentMode,
            IsStub: values.IsStub);
    }

    internal static MachinePackagedSoftwareInventorySnapshot
        CreateSnapshot(
            IEnumerable<PackageValues> packages,
            int skippedEntryCount,
            DateTimeOffset capturedAt,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentOutOfRangeException.ThrowIfNegative(
            skippedEntryCount);

        var items = new List<MachinePackagedSoftwareSnapshot>();
        var optionalPropertyFailureCount = 0;
        var excludedFrameworkPackageCount = 0;
        var excludedResourcePackageCount = 0;

        foreach (var package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (package.IsFramework)
            {
                excludedFrameworkPackageCount++;
            }

            if (package.IsResourcePackage)
            {
                excludedResourcePackageCount++;
            }

            if (package.IsFramework || package.IsResourcePackage)
            {
                continue;
            }

            optionalPropertyFailureCount +=
                package.OptionalPropertyFailureCount;

            var item = MapPackage(package);

            if (item is null)
            {
                skippedEntryCount++;
            }
            else
            {
                items.Add(item);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new MachinePackagedSoftwareInventorySnapshot(
            Items: OrderItems(items),
            IsComplete: skippedEntryCount == 0 &&
                optionalPropertyFailureCount == 0,
            SkippedEntryCount: skippedEntryCount,
            OptionalPropertyFailureCount:
                optionalPropertyFailureCount,
            ExcludedFrameworkPackageCount:
                excludedFrameworkPackageCount,
            ExcludedResourcePackageCount:
                excludedResourcePackageCount,
            CapturedAt: capturedAt);
    }

    internal static IReadOnlyList<MachinePackagedSoftwareSnapshot>
        OrderItems(
            IEnumerable<MachinePackagedSoftwareSnapshot> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items
            .OrderBy(
                item => item.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item => item.PublisherDisplayName ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item => item.PackageFullName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item => item.DisplayName,
                StringComparer.Ordinal)
            .ThenBy(
                item => item.PublisherDisplayName ?? string.Empty,
                StringComparer.Ordinal)
            .ThenBy(
                item => item.PackageFullName,
                StringComparer.Ordinal)
            .ToArray();
    }

    internal static string? ReadOptionalString(
        Func<string?> valueReader,
        out bool readFailed)
    {
        ArgumentNullException.ThrowIfNull(valueReader);

        try
        {
            var value = ReadOptionalText(valueReader());
            readFailed = false;
            return value;
        }
        catch (Exception exception)
            when (IsPackageReadException(exception))
        {
            readFailed = true;
            return null;
        }
    }

    internal static bool? ReadOptionalBoolean(
        Func<bool> valueReader,
        out bool readFailed)
    {
        ArgumentNullException.ThrowIfNull(valueReader);

        try
        {
            var value = valueReader();
            readFailed = false;
            return value;
        }
        catch (Exception exception)
            when (IsPackageReadException(exception))
        {
            readFailed = true;
            return null;
        }
    }

    internal static string FormatVersion(
        PackageVersionValues version) =>
        string.Join(
            ".",
            version.Major.ToString(CultureInfo.InvariantCulture),
            version.Minor.ToString(CultureInfo.InvariantCulture),
            version.Build.ToString(CultureInfo.InvariantCulture),
            version.Revision.ToString(CultureInfo.InvariantCulture));

    internal static MachinePackagedSoftwareArchitecture
        MapArchitecture(WindowsProcessorArchitecture architecture) =>
        architecture switch
        {
            WindowsProcessorArchitecture.Neutral =>
                MachinePackagedSoftwareArchitecture.Neutral,
            WindowsProcessorArchitecture.X86 =>
                MachinePackagedSoftwareArchitecture.X86,
            WindowsProcessorArchitecture.X64 =>
                MachinePackagedSoftwareArchitecture.X64,
            WindowsProcessorArchitecture.Arm =>
                MachinePackagedSoftwareArchitecture.Arm,
            WindowsProcessorArchitecture.Arm64 =>
                MachinePackagedSoftwareArchitecture.Arm64,
            WindowsProcessorArchitecture.X86OnArm64 =>
                MachinePackagedSoftwareArchitecture.X86OnArm64,
            _ => MachinePackagedSoftwareArchitecture.Unknown,
        };

    private static MachinePackagedSoftwareInventorySnapshot
        CaptureInventory(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var packageManager = new PackageManager();

        return CaptureInventory(
            packageManager.FindPackagesForUser(string.Empty),
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    internal static MachinePackagedSoftwareInventorySnapshot
        CaptureInventory(
            IEnumerable<Package> packages,
            DateTimeOffset capturedAt,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packages);

        var packageValues = new List<PackageValues>();
        var skippedEntryCount = 0;

        foreach (var package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                packageValues.Add(ReadPackageValues(package));
            }
            catch (Exception exception)
                when (IsPackageReadException(exception))
            {
                skippedEntryCount++;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        return CreateSnapshot(
            packageValues,
            skippedEntryCount,
            capturedAt,
            cancellationToken);
    }

    private static PackageValues ReadPackageValues(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var isFramework = package.IsFramework;
        var isResourcePackage = package.IsResourcePackage;

        if (isFramework || isResourcePackage)
        {
            return PackageValues.CreateExcluded(
                isFramework,
                isResourcePackage);
        }

        var identity = package.Id;
        var version = identity.Version;
        var optionalPropertyFailureCount = 0;
        var displayName = ReadOptionalString(
            () => package.DisplayName,
            out var displayNameReadFailed);
        optionalPropertyFailureCount +=
            displayNameReadFailed ? 1 : 0;
        var publisherDisplayName = ReadOptionalString(
            () => package.PublisherDisplayName,
            out var publisherDisplayNameReadFailed);
        optionalPropertyFailureCount +=
            publisherDisplayNameReadFailed ? 1 : 0;
        var installedLocation = ReadOptionalString(
            () => package.InstalledLocation?.Path,
            out var installedLocationReadFailed);
        optionalPropertyFailureCount +=
            installedLocationReadFailed ? 1 : 0;
        var isDevelopmentMode = ReadOptionalBoolean(
            () => package.IsDevelopmentMode,
            out var developmentModeReadFailed);
        optionalPropertyFailureCount +=
            developmentModeReadFailed ? 1 : 0;
        var isStub = ReadStubFlag(
            package,
            out var stubReadFailed);
        optionalPropertyFailureCount += stubReadFailed ? 1 : 0;

        return new PackageValues(
            DisplayName: displayName,
            PublisherDisplayName: publisherDisplayName,
            IdentityName: identity.Name,
            PackageFamilyName: identity.FamilyName,
            PackageFullName: identity.FullName,
            Version: new PackageVersionValues(
                version.Major,
                version.Minor,
                version.Build,
                version.Revision),
            Architecture: identity.Architecture,
            InstalledLocation: installedLocation,
            IsDevelopmentMode: isDevelopmentMode,
            IsStub: isStub,
            OptionalPropertyFailureCount:
                optionalPropertyFailureCount,
            IsFramework: false,
            IsResourcePackage: false);
    }

    private static bool? ReadStubFlag(
        Package package,
        out bool readFailed)
    {
        try
        {
            if (!ApiInformation.IsPropertyPresent(
                    PackageRuntimeClassName,
                    nameof(Package.IsStub)))
            {
                readFailed = false;
                return null;
            }
        }
        catch (Exception exception)
            when (IsPackageReadException(exception))
        {
            readFailed = true;
            return null;
        }

        return ReadOptionalBoolean(
            () => package.IsStub,
            out readFailed);
    }

    private static string? ReadOptionalText(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmedValue = value.Trim();

        return trimmedValue.Length == 0
            ? null
            : trimmedValue;
    }

    private static string? ReadOptionalLocalizedText(string? value)
    {
        var normalizedValue = ReadOptionalText(value);

        return normalizedValue is null ||
            normalizedValue.StartsWith(
                UnresolvedResourcePrefix,
                StringComparison.OrdinalIgnoreCase)
            ? null
            : normalizedValue;
    }

    private static bool IsPackageReadException(
        Exception exception) =>
        exception is COMException or
            UnauthorizedAccessException or
            SecurityException or
            IOException or
            ArgumentException or
            InvalidOperationException or
            NotSupportedException;

    internal sealed record PackageValues(
        string? DisplayName,
        string? PublisherDisplayName,
        string? IdentityName,
        string? PackageFamilyName,
        string? PackageFullName,
        PackageVersionValues Version,
        WindowsProcessorArchitecture Architecture,
        string? InstalledLocation,
        bool? IsDevelopmentMode,
        bool? IsStub,
        int OptionalPropertyFailureCount,
        bool IsFramework,
        bool IsResourcePackage)
    {
        internal static PackageValues CreateExcluded(
            bool isFramework,
            bool isResourcePackage) =>
            new(
                DisplayName: null,
                PublisherDisplayName: null,
                IdentityName: null,
                PackageFamilyName: null,
                PackageFullName: null,
                Version: default,
                Architecture: WindowsProcessorArchitecture.Unknown,
                InstalledLocation: null,
                IsDevelopmentMode: null,
                IsStub: null,
                OptionalPropertyFailureCount: 0,
                IsFramework: isFramework,
                IsResourcePackage: isResourcePackage);
    }

    internal readonly record struct PackageVersionValues(
        ushort Major,
        ushort Minor,
        ushort Build,
        ushort Revision);
}
