using System.Runtime.InteropServices;
using System.Security;
using Machine.Core;
using Microsoft.Win32;

namespace Machine.Windows;

public sealed class WindowsMachineRebootPendingProvider
    : IMachineRebootPendingProvider
{
    private readonly IWindowsRebootIndicatorSource _source;

    public WindowsMachineRebootPendingProvider()
        : this(new WindowsRebootIndicatorSource())
    {
    }

    internal WindowsMachineRebootPendingProvider(
        IWindowsRebootIndicatorSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public Task<MachineRebootPendingSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(
            () => MachineRebootPendingAggregator.Aggregate(
                _source.ReadIndicators(cancellationToken),
                DateTimeOffset.UtcNow),
            cancellationToken);
    }
}

internal interface IWindowsRebootIndicatorSource
{
    IReadOnlyList<MachineRebootPendingIndicator> ReadIndicators(
        CancellationToken cancellationToken);
}

internal sealed class WindowsRebootIndicatorSource
    : IWindowsRebootIndicatorSource
{
    private const string WindowsUpdateRebootRequiredPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";
    private const string ComponentServicingRebootPendingPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";
    private const string SessionManagerPath =
        @"SYSTEM\CurrentControlSet\Control\Session Manager";
    private const string PendingFileRenameOperations =
        "PendingFileRenameOperations";
    private const string ComputerNamePath =
        @"SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName";
    private const string ActiveComputerNamePath =
        @"SYSTEM\CurrentControlSet\Control\ComputerName\ActiveComputerName";
    private const string ComputerNameValue = "ComputerName";
    private const string SystemInformationProgramId =
        "Microsoft.Update.SystemInfo";

    public IReadOnlyList<MachineRebootPendingIndicator> ReadIndicators(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return
        [
            new(
                MachineRebootPendingReason.WindowsUpdate,
                ReadWindowsUpdateRebootPending()),
            new(
                MachineRebootPendingReason.ComponentServicing,
                ReadKeyExists(ComponentServicingRebootPendingPath)),
            new(
                MachineRebootPendingReason.PendingFileRename,
                ReadPendingFileRename()),
            new(
                MachineRebootPendingReason.ComputerRename,
                ReadComputerRenamePending())
        ];
    }

    private static bool? ReadWindowsUpdateRebootPending()
    {
        var comResult = ReadWindowsUpdateComRebootPending();
        var registryResult = ReadKeyExists(
            WindowsUpdateRebootRequiredPath);
        if (comResult == true || registryResult == true)
        {
            return true;
        }

        return comResult is not null
            ? comResult
            : registryResult;
    }

    private static bool? ReadWindowsUpdateComRebootPending()
    {
        object? systemInformation = null;
        try
        {
            var type = Type.GetTypeFromProgID(
                SystemInformationProgramId,
                throwOnError: false);
            if (type is null)
            {
                return null;
            }

            systemInformation = Activator.CreateInstance(type);
            if (systemInformation is null)
            {
                return null;
            }

            dynamic dynamicSystemInformation = systemInformation;
            return dynamicSystemInformation.RebootRequired is bool pending
                ? pending
                : null;
        }
        catch (Exception exception) when (IsReadException(exception))
        {
            return null;
        }
        finally
        {
            if (systemInformation is not null &&
                Marshal.IsComObject(systemInformation))
            {
                Marshal.FinalReleaseComObject(systemInformation);
            }
        }
    }

    private static bool? ReadPendingFileRename()
    {
        try
        {
            using var baseKey = OpenLocalMachine();
            using var key = baseKey.OpenSubKey(
                SessionManagerPath,
                writable: false);
            if (key is null)
            {
                return null;
            }

            var value = key.GetValue(
                PendingFileRenameOperations,
                defaultValue: null,
                RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value switch
            {
                null => false,
                string[] entries => entries.Any(entry =>
                    !string.IsNullOrWhiteSpace(entry)),
                string entry => !string.IsNullOrWhiteSpace(entry),
                _ => null
            };
        }
        catch (Exception exception) when (IsReadException(exception))
        {
            return null;
        }
    }

    private static bool? ReadComputerRenamePending()
    {
        var configured = ReadRegistryString(
            ComputerNamePath,
            ComputerNameValue);
        var active = ReadRegistryString(
            ActiveComputerNamePath,
            ComputerNameValue);
        if (!configured.WasRead || !active.WasRead ||
            configured.Value is null || active.Value is null)
        {
            return null;
        }

        return !string.Equals(
            configured.Value,
            active.Value,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool? ReadKeyExists(string path)
    {
        try
        {
            using var baseKey = OpenLocalMachine();
            using var key = baseKey.OpenSubKey(path, writable: false);
            return key is not null;
        }
        catch (Exception exception) when (IsReadException(exception))
        {
            return null;
        }
    }

    private static RegistryReadResult ReadRegistryString(
        string path,
        string valueName)
    {
        try
        {
            using var baseKey = OpenLocalMachine();
            using var key = baseKey.OpenSubKey(path, writable: false);
            if (key is null)
            {
                return new RegistryReadResult(false, null);
            }

            var value = key.GetValue(
                valueName,
                defaultValue: null,
                RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            return new RegistryReadResult(
                true,
                string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        }
        catch (Exception exception) when (IsReadException(exception))
        {
            return new RegistryReadResult(false, null);
        }
    }

    private static RegistryKey OpenLocalMachine() =>
        RegistryKey.OpenBaseKey(
            RegistryHive.LocalMachine,
            Environment.Is64BitOperatingSystem
                ? RegistryView.Registry64
                : RegistryView.Registry32);

    private static bool IsReadException(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            SecurityException or
            COMException or
            InvalidCastException;

    private readonly record struct RegistryReadResult(
        bool WasRead,
        string? Value);
}
