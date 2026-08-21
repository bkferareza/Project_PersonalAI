namespace Machine.Core;

public sealed record MachineStorageDeviceHealthSnapshot(
    DateTimeOffset CapturedAt,
    string DeviceIdentity,
    string DisplayName,
    string? Manufacturer,
    string? MediaType,
    string? BusType,
    ulong? SizeBytes,
    string? WindowsHealthStatus,
    string? OperationalStatus,
    double? TemperatureCelsius,
    double? MaximumTemperatureCelsius,
    double? WearPercent,
    ulong? PowerOnHours,
    ulong? ReadErrorsTotal,
    ulong? ReadErrorsCorrected,
    ulong? ReadErrorsUncorrected,
    ulong? WriteErrorsTotal,
    ulong? WriteErrorsCorrected,
    ulong? WriteErrorsUncorrected,
    ulong? StartStopCycleCount,
    ulong? LoadUnloadCycleCount,
    MachineHardwareTelemetryAvailability Availability,
    string? PartialReason = null);

public sealed record MachineStorageDeviceHealthCollection(
    DateTimeOffset CapturedAt,
    IReadOnlyList<MachineStorageDeviceHealthSnapshot> Devices,
    MachineHardwareTelemetryAvailability Availability,
    string? FailureCode = null);

public interface IMachineStorageDeviceHealthProvider
{
    Task<MachineStorageDeviceHealthCollection> GetAsync(
        CancellationToken cancellationToken = default);
}
