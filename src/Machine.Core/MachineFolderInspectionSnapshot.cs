namespace Machine.Core;

public sealed record MachineFolderInspectionSnapshot(
    string RootPath,
    IReadOnlyList<MachineFolderSizeSnapshot> Folders,
    bool IsComplete,
    int SkippedDirectoryCount,
    DateTimeOffset CapturedAt);
