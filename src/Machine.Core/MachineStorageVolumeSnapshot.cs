namespace Machine.Core;

public sealed record MachineStorageVolumeSnapshot(
    string RootPath,
    string? VolumeLabel,
    string? FileSystem,
    long TotalSizeBytes,
    long AvailableFreeSpaceBytes,
    bool IsSystemVolume);
