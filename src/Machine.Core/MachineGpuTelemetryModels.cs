namespace Machine.Core;

public enum MachineGpuTelemetryAvailability
{
    Available,
    Partial,
    Unavailable
}

public sealed record MachineGpuAdapterTelemetry(
    int AdapterIndex,
    string? AdapterName,
    string Vendor,
    double? GpuUtilizationPercent,
    ulong? MemoryUsedBytes,
    ulong? MemoryTotalBytes,
    double? MemoryUtilizationPercent,
    double? TemperatureCelsius,
    double? BoardPowerWatts,
    uint? GraphicsClockMHz,
    uint? MemoryClockMHz,
    double? FanPercent);

public sealed record MachineGpuTelemetrySnapshot(
    DateTimeOffset CapturedAt,
    MachineGpuTelemetryAvailability Availability,
    IReadOnlyList<MachineGpuAdapterTelemetry> Adapters,
    string? FailureCode = null);

public sealed record MachineGpuInsightContext(
    double? UtilizationPercent,
    double? MemoryUtilizationPercent,
    double? TemperatureCelsius,
    double? BoardPowerWatts);
