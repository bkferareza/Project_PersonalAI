namespace Machine.Core;

public enum MachineScheduledTaskState
{
    Unknown,
    Disabled,
    Queued,
    Ready,
    Running
}
public enum MachineScheduledTaskTriggerCategory
{
    Event,
    Time,
    Calendar,
    Idle,
    Registration,
    Boot,
    Logon,
    Session,
    Custom,
    Unknown
}

public sealed record MachineScheduledTaskSnapshot(
    string Name,
    string Path,
    bool Enabled,
    MachineScheduledTaskState State,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? NextRunAt,
    int? LastResult,
    IReadOnlyList<MachineScheduledTaskTriggerCategory> TriggerCategories,
    string? Author,
    string? ExecutableName)
{
    public bool LastRunFailed =>
        MachineScheduledTaskPolicy.IsFailedResult(LastResult);
}

public sealed record MachineScheduledTaskInventorySnapshot(
    IReadOnlyList<MachineScheduledTaskSnapshot> Items,
    bool IsComplete,
    int ReadFailureCount,
    int TruncatedItemCount,
    DateTimeOffset CapturedAt);

public static class MachineScheduledTaskPolicy
{
    private const int SchedulerStatusMinimum = 0x00041300;
    private const int SchedulerStatusMaximum = 0x00041308;

    public static bool IsFailedResult(int? value) =>
        value is { } result &&
        result != 0 &&
        result is < SchedulerStatusMinimum or > SchedulerStatusMaximum;
}
