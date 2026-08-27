using System.Security;
using System.Security.Cryptography;
using Machine.Core;
using Microsoft.Win32;

namespace Machine.Windows;

public sealed class WindowsMachineStartupInventoryProvider
    : IMachineStartupInventoryProvider
{
    private const string RunRegistryPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private static readonly RegistrySource[] RegistrySources =
        CreateRegistrySources(
            Environment.Is64BitOperatingSystem);

    private static readonly bool SupportsUnvirtualizedRegistryWrites =
        IsUnvirtualizedRegistryWriteSupported(
            Environment.OSVersion.Version);

    public Task<MachineStartupInventorySnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => CaptureInventory(cancellationToken),
            cancellationToken);
    }

    internal static MachineStartupApplicationSnapshot?
        MapRegistryEntry(
            string? name,
            object? command,
            MachineStartupScope scope,
            MachineStartupRegistryView registryView,
            MachineStartupRegistryValueKind? valueKind = null,
            bool supportsUnvirtualizedRegistryWrites = true)
    {
        if (valueKind is not null)
        {
            return MapActionableRegistryEntry(
                name,
                command,
                scope,
                registryView,
                valueKind.Value,
                supportsUnvirtualizedRegistryWrites);
        }

        var normalizedName = ReadOptionalString(name);
        var normalizedCommand = ReadOptionalString(command);

        if (normalizedName is null || normalizedCommand is null)
        {
            return null;
        }

        return new MachineStartupApplicationSnapshot(
            Name: normalizedName,
            CommandOrPath: normalizedCommand,
            Source: MachineStartupSource.RegistryRunKey,
            Scope: scope,
            RegistryView: registryView);
    }

    internal static MachineStartupApplicationSnapshot?
        MapStartupFolderEntry(
            string? fileName,
            string? fullPath,
            MachineStartupScope scope,
            string? fixedRoot = null,
            FileAttributes? attributes = null,
            long? fileLength = null,
            string? fileSha256 = null)
    {
        if (fixedRoot is not null && attributes is not null)
        {
            return MapActionableStartupFolderEntry(
                fileName,
                fullPath,
                scope,
                fixedRoot,
                attributes.Value,
                fileLength,
                fileSha256);
        }

        var normalizedFileName = ReadOptionalString(fileName);
        var normalizedPath = ReadOptionalString(fullPath);

        if (normalizedFileName is null ||
            normalizedPath is null ||
            string.Equals(
                normalizedFileName,
                "desktop.ini",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(
            normalizedFileName);

        if (string.IsNullOrWhiteSpace(name))
        {
            name = normalizedFileName;
        }

        return new MachineStartupApplicationSnapshot(
            Name: name,
            CommandOrPath: normalizedPath,
            Source: MachineStartupSource.StartupFolder,
            Scope: scope,
            RegistryView: null);
    }

    private static MachineStartupInventorySnapshot CaptureInventory(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = new List<MachineStartupApplicationSnapshot>();
        var readFailureCount = 0;
        var isComplete = true;

        CaptureRegistryEntries(
            items,
            ref readFailureCount,
            ref isComplete,
            cancellationToken);
        CaptureStartupFolderEntries(
            items,
            ref readFailureCount,
            ref isComplete,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var orderedItems = OrderItems(items);

        return new MachineStartupInventorySnapshot(
            Items: orderedItems,
            IsComplete: isComplete,
            ReadFailureCount: readFailureCount,
            CapturedAt: DateTimeOffset.UtcNow);
    }

    internal static IReadOnlyList<MachineStartupApplicationSnapshot>
        OrderItems(
            IEnumerable<MachineStartupApplicationSnapshot> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items
            .OrderBy(
                item => item.Name,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Source)
            .ThenBy(item => item.Scope)
            .ThenBy(item => item.RegistryView)
            .ThenBy(
                item => item.CommandOrPath,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item => item.Name,
                StringComparer.Ordinal)
            .ThenBy(
                item => item.CommandOrPath,
                StringComparer.Ordinal)
            .ToArray();
    }

    private static void CaptureRegistryEntries(
        List<MachineStartupApplicationSnapshot> items,
        ref int readFailureCount,
        ref bool isComplete,
        CancellationToken cancellationToken)
    {
        foreach (var source in RegistrySources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(
                    source.Hive,
                    source.RegistryView);
                using var runKey = baseKey.OpenSubKey(
                    RunRegistryPath,
                    writable: false);

                if (runKey is null)
                {
                    continue;
                }

                foreach (var valueName in runKey.GetValueNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var command = runKey.GetValue(
                            valueName,
                            defaultValue: null,
                            RegistryValueOptions
                                .DoNotExpandEnvironmentNames);
                        var valueKind = MapRegistryValueKind(
                            runKey.GetValueKind(valueName));
                        var item = MapRegistryEntry(
                            valueName,
                            command,
                            source.Scope,
                            source.StartupRegistryView,
                            valueKind,
                            SupportsUnvirtualizedRegistryWrites);

                        if (item is not null)
                        {
                            items.Add(item);
                        }
                    }
                    catch (Exception exception)
                        when (IsReadException(exception))
                    {
                        readFailureCount++;
                        isComplete = false;
                    }
                }
            }
            catch (Exception exception)
                when (IsReadException(exception))
            {
                readFailureCount++;
                isComplete = false;
            }
        }
    }

    private static void CaptureStartupFolderEntries(
        List<MachineStartupApplicationSnapshot> items,
        ref int readFailureCount,
        ref bool isComplete,
        CancellationToken cancellationToken)
    {
        var folderSources = CreateStartupFolderSources(
            Environment.GetFolderPath(
                Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonStartup));

        foreach (var source in folderSources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(source.Path))
            {
                continue;
            }

            try
            {
                var fixedRoot = Path.GetFullPath(source.Path);
                foreach (var filePath in Directory.EnumerateFileSystemEntries(
                    source.Path,
                    "*",
                    SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(filePath);
                        var isRegularFile =
                            (attributes & FileAttributes.Directory) == 0 &&
                            (attributes & FileAttributes.ReparsePoint) == 0;
                        long? length = null;
                        string? sha256 = null;
                        if (isRegularFile)
                        {
                            var info = new FileInfo(filePath);
                            length = info.Length;
                            using var stream = new FileStream(
                                filePath,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.ReadWrite | FileShare.Delete);
                            sha256 = Convert.ToHexString(
                                    SHA256.HashData(stream))
                                .ToLowerInvariant();
                        }

                        var item = MapStartupFolderEntry(
                            Path.GetFileName(filePath),
                            filePath,
                            source.Scope,
                            fixedRoot,
                            attributes,
                            length,
                            sha256);

                        if (item is not null)
                        {
                            items.Add(item);
                        }
                    }
                    catch (Exception exception)
                        when (IsReadException(exception))
                    {
                        readFailureCount++;
                        isComplete = false;
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (Exception exception)
                when (IsReadException(exception))
            {
                readFailureCount++;
                isComplete = false;
            }
        }
    }

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

    private static bool IsReadException(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            SecurityException;

    private static MachineStartupApplicationSnapshot?
        MapActionableRegistryEntry(
            string? exactName,
            object? command,
            MachineStartupScope scope,
            MachineStartupRegistryView registryView,
            MachineStartupRegistryValueKind valueKind,
            bool supportsUnvirtualizedRegistryWrites = true)
    {
        if (exactName is null)
        {
            return null;
        }

        var displayName = exactName.Trim();
        if (displayName.Length == 0)
        {
            displayName = "(Default)";
        }

        var rawData = command as string;
        var displayCommand = ReadOptionalString(rawData) ??
            "Unsupported non-text registry value";
        var stableIdentity = MachineStartupIdentity.CreateRegistryRunEntry(
            scope, registryView, exactName);
        var isMatasuri = WindowsStartupSelfProtection.IsMatasuri(
            displayName, rawData);
        var kindSupported = valueKind is
            MachineStartupRegistryValueKind.String or
            MachineStartupRegistryValueKind.ExpandString;
        var dataSupported = rawData is { Length: > 0 and <= 8_192 };

        var availability = isMatasuri
            ? MachineStartupActionAvailability.Protected
            : !kindSupported || !dataSupported
                ? MachineStartupActionAvailability.Unsupported
                : scope != MachineStartupScope.CurrentUser ||
                    registryView != MachineStartupRegistryView.Shared
                    ? MachineStartupActionAvailability.PermissionRequired
                    : !supportsUnvirtualizedRegistryWrites
                        ? MachineStartupActionAvailability.Unsupported
                        : MachineStartupActionAvailability.Supported;

        string? normalizedState = null;
        string? fingerprint = null;
        if (kindSupported && dataSupported)
        {
            normalizedState = WindowsStartupActionState.RegistryEnabled(
                valueKind, rawData!);
            var target = new MachineActionTarget(
                MachineActionTargetKind.StartupRegistryRunEntry,
                stableIdentity,
                displayName);
            fingerprint = MachineActionFingerprint.CreatePrecondition(
                target,
                normalizedState,
                exactName,
                ((int)valueKind).ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                rawData!);
        }

        return new MachineStartupApplicationSnapshot(
            Name: displayName,
            CommandOrPath: displayCommand,
            Source: MachineStartupSource.RegistryRunKey,
            Scope: scope,
            RegistryView: registryView,
            StableIdentity: stableIdentity,
            ActionAvailability: availability,
            ActionNormalizedState: normalizedState,
            ActionPreconditionFingerprint: fingerprint,
            RegistryValueName: exactName,
            RegistryValueKind: valueKind,
            RegistryValueData: kindSupported ? rawData : null,
            IsMatasuri: isMatasuri);
    }

    private static MachineStartupApplicationSnapshot?
        MapActionableStartupFolderEntry(
            string? fileName,
            string? fullPath,
            MachineStartupScope scope,
            string fixedRoot,
            FileAttributes attributes,
            long? fileLength,
            string? fileSha256)
    {
        var normalizedFileName = ReadOptionalString(fileName);
        var normalizedPath = ReadOptionalString(fullPath);
        if (normalizedFileName is null || normalizedPath is null ||
            string.Equals(normalizedFileName, "desktop.ini",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string canonicalPath;
        string canonicalRoot;
        try
        {
            canonicalPath = Path.GetFullPath(normalizedPath);
            canonicalRoot = Path.GetFullPath(fixedRoot);
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or
                PathTooLongException)
        {
            return null;
        }

        var direct = string.Equals(
            Path.GetDirectoryName(canonicalPath)?.TrimEnd(
                Path.DirectorySeparatorChar),
            canonicalRoot.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
        var regular = direct &&
            (attributes & FileAttributes.Directory) == 0 &&
            (attributes & FileAttributes.ReparsePoint) == 0 &&
            fileLength is >= 0 &&
            WindowsStartupActionState.IsSha256(fileSha256);
        var name = Path.GetFileNameWithoutExtension(normalizedFileName);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = normalizedFileName;
        }

        var identityPath = canonicalPath.ToUpperInvariant();
        var stableIdentity = MachineStartupIdentity.CreateStartupFolderEntry(
            scope, identityPath);
        var isMatasuri = WindowsStartupSelfProtection.IsMatasuri(
            name, canonicalPath);
        var availability = isMatasuri
            ? MachineStartupActionAvailability.Protected
            : !regular
                ? MachineStartupActionAvailability.Unsupported
                : scope != MachineStartupScope.CurrentUser
                    ? MachineStartupActionAvailability.PermissionRequired
                    : MachineStartupActionAvailability.Supported;
        var normalizedState = regular
            ? WindowsStartupActionState.FolderEnabled(
                fileLength!.Value, fileSha256!)
            : null;
        string? fingerprint = null;
        if (normalizedState is not null)
        {
            var target = new MachineActionTarget(
                MachineActionTargetKind.StartupFolderEntry,
                stableIdentity,
                name);
            fingerprint = MachineActionFingerprint.CreatePrecondition(
                target,
                normalizedState,
                canonicalPath,
                fileLength.GetValueOrDefault().ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                fileSha256!);
        }

        return new MachineStartupApplicationSnapshot(
            Name: name,
            CommandOrPath: canonicalPath,
            Source: MachineStartupSource.StartupFolder,
            Scope: scope,
            RegistryView: null,
            StableIdentity: stableIdentity,
            ActionAvailability: availability,
            ActionNormalizedState: normalizedState,
            ActionPreconditionFingerprint: fingerprint,
            FileLength: fileLength,
            FileSha256: fileSha256,
            IsMatasuri: isMatasuri);
    }

    private static MachineStartupRegistryValueKind MapRegistryValueKind(
        RegistryValueKind valueKind) => valueKind switch
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

    internal static RegistrySource[] CreateRegistrySources(
        bool is64BitOperatingSystem)
    {
        var currentUserSource = new RegistrySource(
            RegistryHive.CurrentUser,
            RegistryView.Default,
            MachineStartupScope.CurrentUser,
            MachineStartupRegistryView.Shared);

        if (!is64BitOperatingSystem)
        {
            return
            [
                new(
                    RegistryHive.LocalMachine,
                    RegistryView.Registry32,
                    MachineStartupScope.AllUsers,
                    MachineStartupRegistryView.Registry32),
                currentUserSource
            ];
        }

        return
        [
            new(
                RegistryHive.LocalMachine,
                RegistryView.Registry64,
                MachineStartupScope.AllUsers,
                MachineStartupRegistryView.Registry64),
            new(
                RegistryHive.LocalMachine,
                RegistryView.Registry32,
                MachineStartupScope.AllUsers,
                MachineStartupRegistryView.Registry32),
            currentUserSource
        ];
    }

    internal static bool IsUnvirtualizedRegistryWriteSupported(
        Version windowsVersion) =>
        windowsVersion.Major > 10 ||
        windowsVersion.Major == 10 &&
        windowsVersion.Build >=
            WindowsStartupRegistryVirtualization.MinimumWindowsBuild;

    internal static StartupFolderSource[]
        CreateStartupFolderSources(
            string currentUserPath,
            string commonPath) =>
        [
            new(
                currentUserPath,
                MachineStartupScope.CurrentUser),
            new(
                commonPath,
                MachineStartupScope.AllUsers)
        ];

    internal readonly record struct RegistrySource(
        RegistryHive Hive,
        RegistryView RegistryView,
        MachineStartupScope Scope,
        MachineStartupRegistryView StartupRegistryView);

    internal readonly record struct StartupFolderSource(
        string Path,
        MachineStartupScope Scope);
}
