using System.Runtime.InteropServices;
using System.Text;
using Machine.Core;

namespace Machine.Windows;

public sealed class WindowsMachineGpuTelemetryProvider
    : IMachineGpuTelemetryProvider
{
    public const int MaximumGpuCount = 16;

    private readonly INvmlTelemetrySource _source;
    private readonly bool _ownsSource;
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private bool _disposed;

    public WindowsMachineGpuTelemetryProvider()
        : this(new DynamicNvmlTelemetrySource(), ownsSource: true)
    {
    }

    internal WindowsMachineGpuTelemetryProvider(
        INvmlTelemetrySource source,
        bool ownsSource = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _ownsSource = ownsSource;
    }

    public async Task<MachineGpuTelemetrySnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await _captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var capturedAt = DateTimeOffset.UtcNow;
            NvmlCapture capture;
            try
            {
                capture = await Task.Run(
                    () => _source.Capture(cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or
                    BadImageFormatException or
                    EntryPointNotFoundException or
                    InvalidOperationException)
            {
                return new(
                    capturedAt,
                    MachineGpuTelemetryAvailability.Unavailable,
                    [],
                    "nvml.unavailable");
            }

            if (!capture.LibraryAvailable)
            {
                return new(
                    capturedAt,
                    MachineGpuTelemetryAvailability.Unavailable,
                    [],
                    capture.FailureCode ?? "nvml.unavailable");
            }
            if (capture.Devices.Count == 0)
            {
                return new(
                    capturedAt,
                    MachineGpuTelemetryAvailability.Unavailable,
                    [],
                    capture.FailureCode ?? "nvml.no-accessible-device");
            }

            var bounded = capture.Devices.Take(MaximumGpuCount)
                .Select(Map)
                .ToArray();
            var complete = capture.IsComplete &&
                capture.Devices.Count <= MaximumGpuCount &&
                bounded.All(IsComplete);
            return new(
                capturedAt,
                complete
                    ? MachineGpuTelemetryAvailability.Available
                    : MachineGpuTelemetryAvailability.Partial,
                bounded,
                capture.FailureCode);
        }
        finally
        {
            _captureGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_ownsSource)
        {
            _source.Dispose();
        }
        // The managed gate is intentionally not disposed. Shutdown can race
        // a cancelled capture's finally block, and SemaphoreSlim owns no
        // native handle unless its WaitHandle is requested (it never is).
    }

    internal static MachineGpuAdapterTelemetry Map(NvmlDeviceCapture value)
    {
        var memoryTotal = value.MemoryTotalBytes is > 0
            ? value.MemoryTotalBytes
            : null;
        ulong? memoryUsed = value.MemoryUsedBytes is { } used &&
            memoryTotal is { } total
                ? Math.Min(used, total)
                : null;
        return new(
            value.AdapterIndex,
            NormalizeName(value.AdapterName),
            "NVIDIA",
            NormalizePercent(value.GpuUtilizationPercent),
            memoryUsed,
            memoryTotal,
            memoryUsed is { } normalizedUsed &&
                memoryTotal is { } normalizedTotal
                ? normalizedUsed / (double)normalizedTotal * 100d
                : null,
            NormalizeTemperature(value.TemperatureCelsius),
            NormalizeNonNegative(value.BoardPowerWatts),
            value.GraphicsClockMHz,
            value.MemoryClockMHz,
            NormalizePercent(value.FanPercent));
    }

    private static bool IsComplete(MachineGpuAdapterTelemetry adapter) =>
        !string.IsNullOrWhiteSpace(adapter.AdapterName) &&
        adapter.GpuUtilizationPercent is not null &&
        adapter.MemoryUsedBytes is not null &&
        adapter.MemoryTotalBytes is not null &&
        adapter.MemoryUtilizationPercent is not null &&
        adapter.TemperatureCelsius is not null &&
        adapter.BoardPowerWatts is not null &&
        adapter.GraphicsClockMHz is not null &&
        adapter.MemoryClockMHz is not null &&
        adapter.FanPercent is not null;

    private static string? NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var trimmed = value.Trim();
        return trimmed.Length <= 96 ? trimmed : trimmed[..96];
    }

    private static double? NormalizePercent(double? value) =>
        value is { } candidate &&
        double.IsFinite(candidate) &&
        candidate is >= 0d and <= 100d
            ? candidate
            : null;

    private static double? NormalizeTemperature(double? value) =>
        value is { } candidate &&
        double.IsFinite(candidate) &&
        candidate is >= -100d and <= 500d
            ? candidate
            : null;

    private static double? NormalizeNonNegative(double? value) =>
        value is { } candidate &&
        double.IsFinite(candidate) &&
        candidate >= 0d
            ? candidate
            : null;
}
