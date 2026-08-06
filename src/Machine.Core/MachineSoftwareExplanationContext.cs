namespace Machine.Core;

public sealed record MachineSoftwareExplanationContext(
    MachineSoftwareInventoryExplanationSummary? ClassicDesktop,
    MachineSoftwareInventoryExplanationSummary? PackagedApplications);
