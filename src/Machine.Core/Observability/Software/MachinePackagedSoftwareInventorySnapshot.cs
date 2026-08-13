namespace Machine.Core;

public sealed record MachinePackagedSoftwareInventorySnapshot(
    IReadOnlyList<MachinePackagedSoftwareSnapshot> Items,
    bool IsComplete,
    int SkippedEntryCount,
    int OptionalPropertyFailureCount,
    int ExcludedFrameworkPackageCount,
    int ExcludedResourcePackageCount,
    DateTimeOffset CapturedAt);
