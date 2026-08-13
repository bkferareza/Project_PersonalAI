namespace Machine.Core;

public sealed record MachineStartupInventorySnapshot(
    IReadOnlyList<MachineStartupApplicationSnapshot> Items,
    bool IsComplete,
    int ReadFailureCount,
    DateTimeOffset CapturedAt);
