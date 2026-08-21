using Machine.Core;
using Microsoft.UI.Xaml;

namespace Machine.App.Features;

public sealed partial class HardwareView
{
    private const double BytesPerGibibyte = 1024d * 1024d * 1024d;

    internal void Update(MachineGpuTelemetrySnapshot? gpu) => Update(gpu,
        null, null, new(DateTimeOffset.UtcNow, null, null, null, null,
            null, null, MachinePowerEstimateConfidence.Unavailable),
        new(0, 0, false), new(0, null, 0, 0), null,
        new(DateOnly.FromDateTime(DateTime.Today), 0d, null,
            MachineCostCoverage.Unavailable, TimeSpan.Zero, null, null,
            0, null));

    internal void Update(MachineGpuTelemetrySnapshot? gpu,
        MachineCpuHardwareSnapshot? cpu,
        MachineStorageDeviceHealthCollection? storage,
        MachinePowerEstimate power,
        MachineEnergySnapshot energy,
        MachineHistoryEnergyCostSummary historyEnergy,
        ElectricityRateSnapshot? rate,
        MachineTodayEnergyCostProjection todayHistoryEnergy)
    {
        var adapter = gpu?.Adapters.FirstOrDefault();
        CpuProcessorNameText.Text = cpu?.ProcessorName ?? "Processor telemetry unavailable";
        CpuProviderStatusText.Text = cpu?.Availability == MachineHardwareTelemetryAvailability.Available
            ? "Windows metadata and safe frequency telemetry" : "Partial Windows hardware telemetry";
        CpuLoadText.Text = FormatPercent(cpu?.UtilizationPercent);
        CpuClockText.Text = cpu?.EffectiveClockMHz is { } clock ? $"{clock / 1000d:F1} GHz" : "Unavailable";
        CpuPowerText.Text = cpu?.EstimatedPackagePowerWatts is { } watts ? $"~{watts:F0} W" : "Unavailable";
        CpuTemperatureText.Text = cpu?.TemperatureCelsius is { } temperature ? $"{temperature:F0} C" : "Unavailable";
        CpuTemperatureNoteText.Visibility = cpu?.TemperatureCelsius is null ? Visibility.Visible : Visibility.Collapsed;

        GpuAdapterNameText.Text = adapter?.AdapterName ?? "Graphics telemetry unavailable";
        GpuProviderStatusText.Text = adapter is null ? "Driver telemetry unavailable" : "NVML driver telemetry";
        GpuUtilizationText.Text = FormatPercent(adapter?.GpuUtilizationPercent);
        GpuMemoryText.Text = adapter?.MemoryUsedBytes is { } used && adapter.MemoryTotalBytes is { } total ? $"{used / BytesPerGibibyte:F1} / {total / BytesPerGibibyte:F1} GB" : "Unavailable";
        GpuTemperatureText.Text = adapter?.TemperatureCelsius is { } gpuTemp ? $"{gpuTemp:F0} C" : "Unavailable";
        GpuPowerText.Text = adapter?.BoardPowerWatts is { } gpuPower ? $"{gpuPower:F0} W" : "Unavailable";
        GpuGraphicsClockText.Text = adapter?.GraphicsClockMHz is { } graphics ? $"{graphics:N0} MHz" : "Unavailable";
        GpuMemoryClockText.Text = adapter?.MemoryClockMHz is { } memory ? $"{memory:N0} MHz" : "Unavailable";

        PowerWallText.Text = power.EstimatedWallWatts is { } wall ? $"~{wall:F0} W" : "Unavailable";
        PowerRangeText.Text = power.EstimatedWallLowerWatts is { } lower && power.EstimatedWallUpperWatts is { } upper ? $"{lower:F0}-{upper:F0} W likely range" : "Range unavailable";
        PowerConfidenceText.Text = FormatConfidence(power.Confidence);
        var sessionCost = MachineElectricityCostCalculator.Calculate(
            energy.SessionWattHours, rate);
        var todayCost = todayHistoryEnergy.EstimatedCost;
        EnergySessionText.Text = energy.HasObservedEnergy
            ? $"{energy.SessionWattHours / 1000d:F3} kWh\n{FormatEstimatedCost(sessionCost, rate)}"
            : "Unavailable";
        EnergyTodayText.Text = todayHistoryEnergy.HasObservedEnergy
            ? $"{todayHistoryEnergy.ObservedEnergyWattHours / 1000d:F3} kWh\n{FormatEstimatedCost(todayCost, rate)}"
            : energy.HasObservedEnergy
                ? $"{energy.TodayWattHours / 1000d:F3} kWh\n{FormatEstimatedCost(todayCost, rate)}"
                : "Unavailable";
        PowerEvidenceText.Text = BuildEvidence(power) + (rate is null
            ? "\nPublished residential reference rate unavailable; electricity cost is unavailable."
            : $"\nPublished residential reference · {rate.ProviderName} · {rate.CurrencyCode} {rate.RatePerKWh:F4}/kWh · {rate.EffectiveMonth:MMMM yyyy}" +
              $"\n30 observed days\n{historyEnergy.ObservedWattHours / 1000d:F3} kWh\n" +
              FormatEstimatedCost(historyEnergy.EstimatedCost, rate));

        StorageHealthText.Text = storage?.Devices.Count > 0 ? $"{storage.Devices.Count:N0} Windows-reported physical storage device" + (storage.Devices.Count == 1 ? string.Empty : "s") : "Physical storage health unavailable";
        StorageDevicesList.ItemsSource = storage?.Devices.Select(device => new StorageDeviceDisplayItem(device.DisplayName,
            string.Join(" / ", new[] { device.BusType, device.MediaType, device.SizeBytes is { } size ? $"{size / BytesPerGibibyte:F0} GB" : null }.Where(value => !string.IsNullOrWhiteSpace(value))),
            string.Join(" / ", new[] { device.OperationalStatus is { } status ? $"Windows operational status {status}" : null, device.WindowsHealthStatus is { } health ? $"Health {health}" : null, device.TemperatureCelsius is { } deviceTemp ? $"{deviceTemp:F0} C" : "Temperature unavailable" }.Where(value => !string.IsNullOrWhiteSpace(value))))) .ToArray() ?? [];
    }

    private static string FormatPercent(double? value) => value is { } percent ? $"{percent:F0}%" : "Unavailable";
    private static string FormatEstimatedCost(decimal? cost, ElectricityRateSnapshot? rate) => cost is { } value && rate is not null
        ? $"~{(string.Equals(rate.CurrencyCode, "PHP", StringComparison.OrdinalIgnoreCase) ? "₱" : $"{rate.CurrencyCode} ")}{value:F2} estimated"
        : "Estimated cost unavailable";
    private static string FormatConfidence(MachinePowerEstimateConfidence value) => value switch { MachinePowerEstimateConfidence.Measured => "Measured component evidence", MachinePowerEstimateConfidence.HighEstimate => "High estimate confidence", MachinePowerEstimateConfidence.ModerateEstimate => "Moderate estimate confidence", MachinePowerEstimateConfidence.LowEstimate => "Low estimate confidence", _ => "Estimate quality unavailable" };
    private static string BuildEvidence(MachinePowerEstimate power) => string.Join("\n", new[] { power.MeasuredGpuBoardWatts is { } gpu ? $"Measured GPU board power / {gpu:F0} W" : null, power.EstimatedCpuWatts is { } cpu ? $"Estimated CPU package / ~{cpu:F0} W" : null, power.EstimatedPlatformWatts is { } platform ? $"Estimated platform/base / ~{platform:F0} W" : null }.Where(value => value is not null)) switch { "" => "Component evidence is unavailable.", var text => text };
}
