namespace Machine.Core;

public sealed record MachineStateExplanationRequest(
    MachineIdentity Identity,
    MachineResourceSnapshot Resources,
    IReadOnlyList<MachineProcessSnapshot> TopProcesses,
    MachineStorageExplanationContext? Storage = null,
    MachineSoftwareExplanationContext? Software = null,
    MachineStartupExplanationContext? Startup = null);
