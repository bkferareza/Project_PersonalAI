namespace Machine.Core;

public sealed record MachineSoftwareInventoryExplanationSummary(
    int RegistrationCount,
    bool IsComplete,
    int SkippedEntryCount);
