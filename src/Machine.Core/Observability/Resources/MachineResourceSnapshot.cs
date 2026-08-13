namespace Machine.Core;

public sealed record MachineResourceSnapshot(
    double CpuUsagePercent,
    ulong TotalMemoryBytes,
    ulong UsedMemoryBytes,
    DateTimeOffset CapturedAt);
