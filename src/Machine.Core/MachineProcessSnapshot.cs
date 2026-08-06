namespace Machine.Core;

public sealed record MachineProcessSnapshot(
    int ProcessId,
    string Name,
    double CpuUsagePercent,
    long WorkingSetBytes);
