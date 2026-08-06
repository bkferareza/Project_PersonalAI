namespace Machine.Core;

public sealed record MachineStorageExplanationContext(
    string SystemVolumeRoot,
    long TotalSizeBytes,
    long AvailableSizeBytes,
    MachineFolderScanExplanationContext? LargeFolderScan);
