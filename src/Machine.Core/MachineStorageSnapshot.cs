namespace Machine.Core;

public sealed record MachineStorageSnapshot(
    IReadOnlyList<MachineStorageVolumeSnapshot> Volumes,
    DateTimeOffset CapturedAt);
