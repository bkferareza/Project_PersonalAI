namespace Machine.Core;

public sealed record MachineFolderSizeSnapshot(
    string Path,
    long SizeBytes,
    long FileCount,
    bool IsComplete);
