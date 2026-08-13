namespace Machine.Core;

public enum MachineServiceState
{
    Stopped,
    StartPending,
    StopPending,
    Running,
    ContinuePending,
    PausePending,
    Paused,
    Unknown
}

public enum MachineServiceStartType
{
    Boot,
    System,
    Automatic,
    AutomaticDelayed,
    Manual,
    Disabled,
    Unknown
}

public enum MachineServiceCategory
{
    Service,
    Driver,
    Adapter,
    FileSystemRecognizer,
    Unknown
}

public sealed record MachineServiceSnapshot(
    string Name,
    string DisplayName,
    MachineServiceState State,
    MachineServiceStartType StartType,
    MachineServiceCategory Category,
    int? ProcessId);

public sealed record MachineServiceInventorySnapshot(
    IReadOnlyList<MachineServiceSnapshot> Items,
    bool IsComplete,
    int ReadFailureCount,
    int TruncatedItemCount,
    DateTimeOffset CapturedAt);
