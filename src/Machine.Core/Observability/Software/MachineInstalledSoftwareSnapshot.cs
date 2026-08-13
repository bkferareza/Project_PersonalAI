namespace Machine.Core;

public sealed record MachineInstalledSoftwareSnapshot(
    string Name,
    string? Version,
    string? Publisher,
    string? InstallLocation,
    long? EstimatedSizeBytes,
    MachineSoftwareScope Scope,
    MachineSoftwareRegistryView RegistryView);
