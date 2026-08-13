using Machine.Core;
using Machine.Windows;

namespace Machine.Tests;

public sealed class WindowsMachineGpuTelemetryProviderTests
{
    [Fact]
    public async Task NvmlUnavailableReturnsCleanUnavailableSnapshot()
    {
        var source = new FakeNvmlSource(new(
            false,
            false,
            [],
            "nvml.library-not-found"));
        using var provider = new WindowsMachineGpuTelemetryProvider(source);

        var snapshot = await provider.GetAsync();

        Assert.Equal(
            MachineGpuTelemetryAvailability.Unavailable,
            snapshot.Availability);
        Assert.Empty(snapshot.Adapters);
        Assert.Equal("nvml.library-not-found", snapshot.FailureCode);
    }

    [Fact]
    public async Task ZeroGpusReturnsCleanUnavailableSnapshot()
    {
        using var provider = new WindowsMachineGpuTelemetryProvider(
            new FakeNvmlSource(new(true, true, [], "nvml.no-device")));

        var snapshot = await provider.GetAsync();

        Assert.Equal(
            MachineGpuTelemetryAvailability.Unavailable,
            snapshot.Availability);
        Assert.Equal("nvml.no-device", snapshot.FailureCode);
    }

    [Fact]
    public async Task OneCompleteGpuMapsNvmlUnits()
    {
        using var provider = new WindowsMachineGpuTelemetryProvider(
            new FakeNvmlSource(new(
                true,
                true,
                [Device(0, " NVIDIA GeForce RTX 3070 ")])));

        var snapshot = await provider.GetAsync();
        var adapter = Assert.Single(snapshot.Adapters);

        Assert.Equal(
            MachineGpuTelemetryAvailability.Available,
            snapshot.Availability);
        Assert.Equal("NVIDIA GeForce RTX 3070", adapter.AdapterName);
        Assert.Equal("NVIDIA", adapter.Vendor);
        Assert.Equal(42, adapter.GpuUtilizationPercent);
        Assert.Equal(4UL * 1024 * 1024 * 1024,
            adapter.MemoryUsedBytes);
        Assert.Equal(8UL * 1024 * 1024 * 1024,
            adapter.MemoryTotalBytes);
        Assert.Equal(50, adapter.MemoryUtilizationPercent);
        Assert.Equal(54, adapter.TemperatureCelsius);
        Assert.Equal(108, adapter.BoardPowerWatts);
        Assert.Equal(1845u, adapter.GraphicsClockMHz);
        Assert.Equal(7000u, adapter.MemoryClockMHz);
        Assert.Equal(34, adapter.FanPercent);
    }

    [Fact]
    public async Task MultipleGpusRemainOrderedByNvmlIndex()
    {
        using var provider = new WindowsMachineGpuTelemetryProvider(
            new FakeNvmlSource(new(
                true,
                true,
                [Device(0, "GPU 0"), Device(1, "GPU 1")])));

        var snapshot = await provider.GetAsync();

        Assert.Equal([0, 1], snapshot.Adapters.Select(item =>
            item.AdapterIndex));
    }

    [Fact]
    public async Task UnsupportedMetricsRemainNullAndSnapshotIsPartial()
    {
        var device = Device(0, "Partial GPU") with
        {
            TemperatureCelsius = null,
            BoardPowerWatts = null,
            FanPercent = null,
            GraphicsClockMHz = null,
            MemoryClockMHz = null
        };
        using var provider = new WindowsMachineGpuTelemetryProvider(
            new FakeNvmlSource(new(true, false, [device])));

        var snapshot = await provider.GetAsync();
        var adapter = Assert.Single(snapshot.Adapters);

        Assert.Equal(
            MachineGpuTelemetryAvailability.Partial,
            snapshot.Availability);
        Assert.Null(adapter.TemperatureCelsius);
        Assert.Null(adapter.BoardPowerWatts);
        Assert.Null(adapter.FanPercent);
        Assert.Null(adapter.GraphicsClockMHz);
        Assert.Null(adapter.MemoryClockMHz);
    }

    [Fact]
    public async Task InvalidNativeValuesAreUnavailableRatherThanZero()
    {
        var device = Device(0, "Invalid GPU") with
        {
            GpuUtilizationPercent = 101,
            MemoryUsedBytes = 12,
            MemoryTotalBytes = 8,
            TemperatureCelsius = double.NaN,
            BoardPowerWatts = -1,
            FanPercent = -10
        };
        using var provider = new WindowsMachineGpuTelemetryProvider(
            new FakeNvmlSource(new(true, false, [device])));

        var adapter = Assert.Single(
            (await provider.GetAsync()).Adapters);

        Assert.Null(adapter.GpuUtilizationPercent);
        Assert.Equal(8UL, adapter.MemoryUsedBytes);
        Assert.Equal(100, adapter.MemoryUtilizationPercent);
        Assert.Null(adapter.TemperatureCelsius);
        Assert.Null(adapter.BoardPowerWatts);
        Assert.Null(adapter.FanPercent);
    }

    [Fact]
    public async Task NativeFailureMapsToUnavailable()
    {
        using var provider = new WindowsMachineGpuTelemetryProvider(
            new FakeNvmlSource(null)
            {
                ThrowOnCapture = true
            });

        var snapshot = await provider.GetAsync();

        Assert.Equal(
            MachineGpuTelemetryAvailability.Unavailable,
            snapshot.Availability);
        Assert.Equal("nvml.unavailable", snapshot.FailureCode);
    }

    [Fact]
    public async Task CancellationIsHonoredBeforeNativeCapture()
    {
        var source = new FakeNvmlSource(new(true, true, []));
        using var provider = new WindowsMachineGpuTelemetryProvider(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.GetAsync(cancellation.Token));

        Assert.Equal(0, source.CaptureCount);
    }

    [Fact]
    public void OwnedNativeSourceIsDisposedExactlyOnce()
    {
        var source = new FakeNvmlSource(new(true, true, []));
        var provider = new WindowsMachineGpuTelemetryProvider(
            source,
            ownsSource: true);

        provider.Dispose();
        provider.Dispose();

        Assert.Equal(1, source.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() =>
            provider.GetAsync().GetAwaiter().GetResult());
    }

    private static NvmlDeviceCapture Device(int index, string name) => new(
        index,
        name,
        42,
        4UL * 1024 * 1024 * 1024,
        8UL * 1024 * 1024 * 1024,
        54,
        108,
        1845,
        7000,
        34);

    private sealed class FakeNvmlSource(NvmlCapture? capture)
        : INvmlTelemetrySource
    {
        public bool ThrowOnCapture { get; set; }
        public int CaptureCount { get; private set; }
        public int DisposeCount { get; private set; }

        public NvmlCapture Capture(CancellationToken cancellationToken)
        {
            CaptureCount++;
            if (ThrowOnCapture)
            {
                throw new InvalidOperationException("Native failure.");
            }
            return capture ?? new(false, false, []);
        }

        public void Dispose() => DisposeCount++;
    }
}
