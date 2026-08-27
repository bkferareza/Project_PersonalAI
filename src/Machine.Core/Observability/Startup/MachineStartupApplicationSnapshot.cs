namespace Machine.Core;

public sealed record MachineStartupApplicationSnapshot(
    string Name,
    string CommandOrPath,
    MachineStartupSource Source,
    MachineStartupScope Scope,
    MachineStartupRegistryView? RegistryView,
    string? StableIdentity = null,
    MachineStartupActionAvailability ActionAvailability =
        MachineStartupActionAvailability.Unsupported,
    string? ActionNormalizedState = null,
    string? ActionPreconditionFingerprint = null,
    string? RegistryValueName = null,
    MachineStartupRegistryValueKind? RegistryValueKind = null,
    string? RegistryValueData = null,
    long? FileLength = null,
    string? FileSha256 = null,
    bool IsMatasuri = false);
