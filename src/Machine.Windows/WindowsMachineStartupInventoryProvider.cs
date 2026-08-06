using System.Security;
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
            MachineStartupRegistryView registryView)
    {
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
            MachineStartupScope scope)
    {
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
                        var item = MapRegistryEntry(
                            valueName,
                            command,
                            source.Scope,
                            source.StartupRegistryView);

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
                foreach (var filePath in Directory.EnumerateFiles(
                    source.Path,
                    "*",
                    SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var item = MapStartupFolderEntry(
                        Path.GetFileName(filePath),
                        filePath,
                        source.Scope);

                    if (item is not null)
                    {
                        items.Add(item);
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
