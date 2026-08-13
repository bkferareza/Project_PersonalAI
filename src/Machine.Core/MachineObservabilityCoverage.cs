namespace Machine.Core;

public enum MachineObservabilityCoverageStatus
{
    Complete,
    InitialImplementation,
    Planned
}

public sealed record MachineObservabilityCapability(
    string Key,
    string DisplayName,
    MachineObservabilityCoverageStatus Status,
    bool IsReadOnly);

public static class MachineObservabilityCoverage
{
    public const string V1CompletionDeclaration =
        "READ_ONLY_OBSERVABILITY_V1_COMPLETE";

    public static IReadOnlyList<MachineObservabilityCapability> V1 { get; } =
    [
        Complete("resources", "CPU and memory resources"),
        Complete("processes", "Top processes"),
        Complete("storage", "Storage"),
        Complete("software", "Traditional and packaged software"),
        Complete("startup", "Startup applications"),
        Complete("network-session", "Network and session"),
        Complete("activity", "Active and idle input state"),
        Complete("uptime", "Windows and Matasuri uptime"),
        Complete("windows-update", "Windows Update"),
        Complete("reboot-pending", "Restart pending"),
        Complete("reliability", "Reliability history"),
        Complete("services", "Windows services"),
        Complete("tasks", "Scheduled tasks"),
        Complete("devices-drivers", "Devices and drivers"),
        Complete("sleep-resume", "Suspend and resume boundaries"),
        Complete("ollama-runtime", "Local Ollama runtime")
    ];

    public static IReadOnlyList<MachineObservabilityCapability> V2 { get; } =
    [
        new("gpu", "GPU telemetry",
            MachineObservabilityCoverageStatus.InitialImplementation, true),
        new("cpu-hardware", "CPU hardware sensors",
            MachineObservabilityCoverageStatus.Planned, true),
        new("storage-smart", "Storage SMART telemetry",
            MachineObservabilityCoverageStatus.Planned, true),
        new("power-energy", "Power and energy estimation",
            MachineObservabilityCoverageStatus.Planned, true)
    ];

    private static MachineObservabilityCapability Complete(
        string key,
        string displayName) => new(
            key,
            displayName,
            MachineObservabilityCoverageStatus.Complete,
            IsReadOnly: true);
}
