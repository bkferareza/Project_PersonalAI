namespace Machine.Core;

public sealed record MachineDeviceSnapshot(
    string DisplayName,
    string DeviceClass,
    string? Manufacturer,
    bool IsPresent,
    bool? IsEnabled,
    int? ProblemCode,
    string? DriverProvider,
    string? DriverVersion,
    DateOnly? DriverDate)
{
    public bool HasWindowsReportedProblem =>
        ProblemCode is > 0;
}

public sealed record MachineDeviceInventorySnapshot(
    IReadOnlyList<MachineDeviceSnapshot> Items,
    bool IsComplete,
    int ReadFailureCount,
    int TruncatedItemCount,
    DateTimeOffset CapturedAt);
