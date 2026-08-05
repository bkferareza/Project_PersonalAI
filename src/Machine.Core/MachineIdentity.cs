namespace Machine.Core;

public sealed record MachineIdentity(
    string DeviceName,
    string OperatingSystem,
    string Architecture);
