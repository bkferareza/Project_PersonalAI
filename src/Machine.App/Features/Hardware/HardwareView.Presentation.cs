using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Machine.Core;
using Machine.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace Machine.App.Features;

public sealed partial class HardwareView
{
    private const double BytesPerGibibyte =
        1024d * 1024d * 1024d;

    internal void Update(MachineGpuTelemetrySnapshot? snapshot)
    {
        var adapter = snapshot?.Adapters.FirstOrDefault();
        if (adapter is null)
        {
            GpuAdapterNameText.Text = "Graphics telemetry unavailable";
            GpuProviderStatusText.Text = snapshot?.FailureCode ==
                    "nvml.no-device"
                ? "No accessible NVIDIA adapter was reported. Device inventory remains available."
                : "Detailed GPU telemetry unavailable for this adapter.";
            GpuUtilizationText.Text = "—";
            GpuMemoryText.Text = "—";
            GpuTemperatureText.Text = "—";
            GpuPowerText.Text = "—";
            GpuGraphicsClockText.Text = "—";
            GpuMemoryClockText.Text = "—";
            GpuFanText.Text = "Unavailable";
            return;
        }

        GpuAdapterNameText.Text = adapter.AdapterName ??
            "NVIDIA graphics adapter";
        GpuProviderStatusText.Text = snapshot!.Availability ==
                MachineGpuTelemetryAvailability.Available
            ? "Verified through the installed NVIDIA NVML driver interface"
            : "Partial telemetry from the installed NVIDIA NVML driver interface";
        GpuUtilizationText.Text = FormatPercent(
            adapter.GpuUtilizationPercent);
        GpuMemoryText.Text = adapter.MemoryUsedBytes is { } used &&
            adapter.MemoryTotalBytes is { } total
                ? $"{used / BytesPerGibibyte:F1} / " +
                    $"{total / BytesPerGibibyte:F1} GB"
                : "—";
        GpuTemperatureText.Text = adapter.TemperatureCelsius is { } temperature
            ? $"{temperature:F0} °C"
            : "—";
        GpuPowerText.Text = adapter.BoardPowerWatts is { } power
            ? $"{power:F0} W"
            : "—";
        GpuGraphicsClockText.Text = adapter.GraphicsClockMHz is { } graphics
            ? $"{graphics:N0} MHz"
            : "—";
        GpuMemoryClockText.Text = adapter.MemoryClockMHz is { } memory
            ? $"{memory:N0} MHz"
            : "—";
        GpuFanText.Text = adapter.FanPercent is { } fan
            ? $"{fan:F0}% of reported maximum"
            : "Fan telemetry unavailable";
    }

    internal void Update(MachineGpuTelemetrySnapshot? gpu,
        MachineCpuHardwareSnapshot? cpu,
        MachineStorageDeviceHealthCollection? storage,
        MachinePowerEstimate power,
        MachineEnergySnapshot energy)
    {
        Update(gpu);
        CpuProcessorNameText.Text = cpu?.ProcessorName ??
            "Processor telemetry unavailable";
        CpuLoadText.Text = FormatPercent(cpu?.UtilizationPercent);
        CpuClockText.Text = cpu?.EffectiveClockMHz is { } clock
            ? $"{clock / 1000d:F1} GHz"
            : "Unavailable";
        CpuPowerText.Text = cpu?.EstimatedPackagePowerWatts is { } watts
            ? $"~{watts:F0} W estimated"
            : "Unavailable";
        CpuTemperatureText.Text = cpu?.TemperatureCelsius is { } temperature
            ? $"{temperature:F0} °C"
            : "CPU package temperature unavailable through the current safe telemetry path";
        PowerWallText.Text = power.EstimatedWallWatts is { } wall
            ? $"~{wall:F0} W estimated"
            : "Estimated wall power unavailable";
        PowerRangeText.Text = power.EstimatedWallLowerWatts is { } lower &&
            power.EstimatedWallUpperWatts is { } upper
                ? $"Likely range {lower:F0}–{upper:F0} W · {power.Confidence}"
                : power.PartialReason ?? "Awaiting enough verified component evidence";
        EnergySessionText.Text = energy.HasObservedEnergy
            ? $"Session {energy.SessionWattHours / 1000d:F3} kWh estimated · Today {energy.TodayWattHours / 1000d:F3} kWh"
            : "Energy begins after an observed power interval";
        StorageHealthText.Text = storage?.Devices.Count > 0
            ? $"{storage.Devices.Count:N0} Windows-reported physical storage device" +
                (storage.Devices.Count == 1 ? string.Empty : "s")
            : "Physical storage health unavailable through Windows Storage Management";
    }

    private static string FormatPercent(double? value) =>
        value is { } percentage ? $"{percentage:F0}%" : "—";
}
