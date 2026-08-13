namespace Machine.Core;

public sealed record MachineFindingsSnapshot(
    MachineOverallState OverallState,
    IReadOnlyList<MachineFinding> Findings);
