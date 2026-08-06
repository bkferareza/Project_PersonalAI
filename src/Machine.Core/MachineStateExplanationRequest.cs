namespace Machine.Core;

public sealed record MachineStateExplanationRequest(
    MachineIdentity Identity,
    MachineResourceSnapshot Resources,
    IReadOnlyList<MachineProcessSnapshot> TopProcesses);
