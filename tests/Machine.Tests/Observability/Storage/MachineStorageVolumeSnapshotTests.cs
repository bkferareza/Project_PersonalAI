using Machine.Core;

namespace Machine.Tests;

public sealed class MachineStorageVolumeSnapshotTests
{
    [Fact]
    public void VolumeConstructorPreservesValues()
    {
        var volume = new MachineStorageVolumeSnapshot(
            RootPath: "C:\\",
            VolumeLabel: "System",
            FileSystem: "NTFS",
            TotalSizeBytes: 1_000_000_000_000,
            AvailableFreeSpaceBytes: 250_000_000_000,
            IsSystemVolume: true);

        Assert.Equal("C:\\", volume.RootPath);
        Assert.Equal("System", volume.VolumeLabel);
        Assert.Equal("NTFS", volume.FileSystem);
        Assert.Equal(1_000_000_000_000, volume.TotalSizeBytes);
        Assert.Equal(
            250_000_000_000,
            volume.AvailableFreeSpaceBytes);
        Assert.True(volume.IsSystemVolume);
    }

    [Fact]
    public void SnapshotConstructorPreservesValues()
    {
        MachineStorageVolumeSnapshot[] volumes =
        [
            new(
                RootPath: "D:\\",
                VolumeLabel: null,
                FileSystem: "NTFS",
                TotalSizeBytes: 500_000_000_000,
                AvailableFreeSpaceBytes: 300_000_000_000,
                IsSystemVolume: false)
        ];
        var capturedAt = new DateTimeOffset(
            2026,
            8,
            6,
            12,
            0,
            0,
            TimeSpan.Zero);

        var snapshot = new MachineStorageSnapshot(
            Volumes: volumes,
            CapturedAt: capturedAt);

        Assert.Same(volumes, snapshot.Volumes);
        Assert.Equal(capturedAt, snapshot.CapturedAt);
    }
}
