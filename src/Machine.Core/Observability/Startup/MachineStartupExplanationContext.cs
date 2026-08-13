namespace Machine.Core;

public sealed record MachineStartupExplanationContext(
    int RegistrationCount,
    int RegistryRunCount,
    int StartupFolderCount,
    int MachineCount,
    int CurrentUserCount,
    bool IsComplete,
    IReadOnlyList<string> Names);
