using System.Security;
using Machine.Core;

namespace Machine.Windows;

public sealed class WindowsMachineStorageProvider
    : IMachineStorageProvider
{
    public Task<MachineStorageSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => CaptureSnapshot(cancellationToken),
            cancellationToken);
    }

    private static MachineStorageSnapshot CaptureSnapshot(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var systemRoot = Path.GetPathRoot(
            Environment.SystemDirectory);
        var volumes = new List<MachineStorageVolumeSnapshot>();

        foreach (var drive in GetDrives())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (drive.DriveType == DriveType.Network ||
                    !drive.IsReady)
                {
                    continue;
                }

                var rootPath = drive.RootDirectory.FullName;
                var totalSize = drive.TotalSize;
                var availableFreeSpace =
                    drive.AvailableFreeSpace;

                if (string.IsNullOrWhiteSpace(rootPath) ||
                    totalSize <= 0 ||
                    availableFreeSpace < 0 ||
                    availableFreeSpace > totalSize)
                {
                    continue;
                }

                volumes.Add(new MachineStorageVolumeSnapshot(
                    RootPath: rootPath,
                    VolumeLabel: TryReadOptionalValue(
                        () => drive.VolumeLabel),
                    FileSystem: TryReadOptionalValue(
                        () => drive.DriveFormat),
                    TotalSizeBytes: totalSize,
                    AvailableFreeSpaceBytes:
                        availableFreeSpace,
                    IsSystemVolume: string.Equals(
                        rootPath,
                        systemRoot,
                        StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception exception)
                when (IsDriveAccessException(exception))
            {
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var orderedVolumes = volumes
            .OrderByDescending(volume => volume.IsSystemVolume)
            .ThenBy(
                volume => volume.RootPath,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                volume => volume.RootPath,
                StringComparer.Ordinal)
            .ToArray();

        return new MachineStorageSnapshot(
            Volumes: orderedVolumes,
            CapturedAt: DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<DriveInfo> GetDrives()
    {
        try
        {
            return DriveInfo.GetDrives();
        }
        catch (Exception exception)
            when (IsDriveAccessException(exception))
        {
            return Array.Empty<DriveInfo>();
        }
    }

    private static string? TryReadOptionalValue(
        Func<string> readValue)
    {
        try
        {
            var value = readValue();

            return string.IsNullOrWhiteSpace(value)
                ? null
                : value;
        }
        catch (Exception exception)
            when (IsDriveAccessException(exception))
        {
            return null;
        }
    }

    private static bool IsDriveAccessException(
        Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            SecurityException or
            NotSupportedException;
}
