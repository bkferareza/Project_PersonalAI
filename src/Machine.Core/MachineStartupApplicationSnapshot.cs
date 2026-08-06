namespace Machine.Core;

public sealed record MachineStartupApplicationSnapshot(
    string Name,
    string CommandOrPath,
    MachineStartupSource Source,
    MachineStartupScope Scope,
    MachineStartupRegistryView? RegistryView);
