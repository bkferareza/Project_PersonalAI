namespace Machine.Core;

public sealed record MachineSoftwareInventorySnapshot(
    IReadOnlyList<MachineInstalledSoftwareSnapshot> Items,
    bool IsComplete,
    int SkippedEntryCount,
    DateTimeOffset CapturedAt);
