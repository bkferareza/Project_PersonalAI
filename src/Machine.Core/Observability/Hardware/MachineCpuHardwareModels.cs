namespace Machine.Core;

public enum MachineHardwareTelemetryAvailability
{
    Available,
    Partial,
    Unavailable
}

public enum MachinePowerEstimateConfidence
{
    Measured,
    HighEstimate,
    ModerateEstimate,
    LowEstimate,
    Unavailable
}

public sealed record MachineCpuHardwareSnapshot(
    DateTimeOffset CapturedAt,
    string? ProcessorName,
    int? CoreCount,
    int? LogicalProcessorCount,
    double? UtilizationPercent,
    double? ProcessorUtilityPercent,
    double? ProcessorPerformancePercent,
    double? EffectiveClockMHz,
    double? BaseClockMHz,
    double? MaximumReferenceClockMHz,
    double? TemperatureCelsius,
    string? TemperatureSource,
    double? MeasuredPackagePowerWatts,
    double? EstimatedPackagePowerWatts,
    double? EstimatedPackagePowerLowerWatts,
    double? EstimatedPackagePowerUpperWatts,
    MachinePowerEstimateConfidence PowerEstimateConfidence,
    MachineHardwareTelemetryAvailability Availability,
    string? PartialReason = null);

public interface IMachineCpuHardwareProvider
{
    Task<MachineCpuHardwareSnapshot> GetAsync(
        MachineResourceSnapshot resources,
        CancellationToken cancellationToken = default);
}

public sealed record MachinePowerEstimate(
    DateTimeOffset CapturedAt,
    double? EstimatedWallWatts,
    double? EstimatedWallLowerWatts,
    double? EstimatedWallUpperWatts,
    double? EstimatedCpuWatts,
    double? MeasuredGpuBoardWatts,
    double? EstimatedPlatformWatts,
    MachinePowerEstimateConfidence Confidence,
    string? PartialReason = null);

public static class MachineCpuPowerEstimator
{
    // These are explicit software-model assumptions, not sensor readings.
    private const double Ryzen3800XtTdpWatts = 105d;
    private const double Ryzen3800XtIdleWatts = 17d;

    public static (double? Center, double? Lower, double? Upper,
        MachinePowerEstimateConfidence Confidence) Estimate(
        string? processorName, double? utilizationPercent,
        double? performancePercent = null)
    {
        if (utilizationPercent is not { } utilization ||
            !double.IsFinite(utilization))
        {
            return (null, null, null, MachinePowerEstimateConfidence.Unavailable);
        }

        var isReference = processorName?.Contains("Ryzen 7 3800XT",
            StringComparison.OrdinalIgnoreCase) == true;
        if (!isReference)
        {
            return (null, null, null, MachinePowerEstimateConfidence.Unavailable);
        }

        var load = Math.Clamp(utilization, 0d, 100d) / 100d;
        var performance = performancePercent is { } value && double.IsFinite(value)
            ? Math.Clamp(value, 0d, 200d) / 100d
            : 1d;
        var dynamicWatts = (Ryzen3800XtTdpWatts - Ryzen3800XtIdleWatts) *
            Math.Pow(load, 1.35d) * (0.78d + 0.17d * performance);
        var center = Math.Clamp(Ryzen3800XtIdleWatts + dynamicWatts,
            Ryzen3800XtIdleWatts, Ryzen3800XtTdpWatts * 1.18d);
        return (center, Math.Max(0d, center * 0.72d), center * 1.30d,
            MachinePowerEstimateConfidence.HighEstimate);
    }
}

public static class MachinePowerEstimator
{
    public static MachinePowerEstimate Estimate(
        DateTimeOffset capturedAt,
        MachineCpuHardwareSnapshot? cpu,
        MachineGpuAdapterTelemetry? gpu,
        ulong? physicalMemoryBytes,
        int storageDeviceCount)
    {
        var cpuWatts = cpu?.MeasuredPackagePowerWatts ??
            cpu?.EstimatedPackagePowerWatts;
        var gpuWatts = gpu?.BoardPowerWatts;
        if (cpuWatts is null && gpuWatts is null)
        {
            return new(capturedAt, null, null, null, null, null, null,
                MachinePowerEstimateConfidence.Unavailable,
                "CPU and GPU power are unavailable through current safe paths.");
        }

        var memoryGiB = physicalMemoryBytes is { } bytes
            ? bytes / (1024d * 1024d * 1024d) : 0d;
        var platform = 28d + Math.Min(18d, memoryGiB * 0.45d) +
            Math.Min(8d, Math.Max(0, storageDeviceCount) * 2d);
        var componentWatts = (cpuWatts ?? 0d) + (gpuWatts ?? 0d) + platform;
        var lower = componentWatts / 0.92d;
        var upper = componentWatts / 0.78d;
        var center = (lower + upper) / 2d;
        var confidence = gpuWatts is not null && cpuWatts is not null
            ? cpu?.MeasuredPackagePowerWatts is not null
                ? MachinePowerEstimateConfidence.ModerateEstimate
                : MachinePowerEstimateConfidence.ModerateEstimate
            : MachinePowerEstimateConfidence.LowEstimate;
        return new(capturedAt, center, lower, upper, cpuWatts, gpuWatts,
            platform, confidence,
            "Estimated wall power combines components with a 78–92% PSU-efficiency range.");
    }
}
