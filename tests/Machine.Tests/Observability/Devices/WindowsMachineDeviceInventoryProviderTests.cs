using Machine.Core;
using Machine.Windows;

namespace Machine.Tests;

public sealed class WindowsMachineDeviceInventoryProviderTests
{
    [Fact]
    public async Task ProjectsDeviceAndDriverFieldsWithoutUniqueIdentity()
    {
        var source = new FakeDeviceSource(new(
        [
            Device(
                "NVIDIA GeForce RTX 3070",
                "Display adapters",
                problem: null),
            Device(
                "Problem device",
                "System devices",
                problem: 22,
                enabled: false)
        ],
        0,
        true,
        DateTimeOffset.Parse("2026-08-14T00:00:00Z")));

        var snapshot = await new WindowsMachineDeviceInventoryProvider(
            source).GetAsync();

        Assert.True(snapshot.IsComplete);
        Assert.Equal(2, snapshot.Items.Count);
        var display = snapshot.Items.Single(item =>
            item.DisplayName == "NVIDIA GeForce RTX 3070");
        Assert.Equal("NVIDIA", display.Manufacturer);
        Assert.Equal("NVIDIA", display.DriverProvider);
        Assert.Equal("32.0.15.7688", display.DriverVersion);
        Assert.Equal(new DateOnly(2026, 8, 1), display.DriverDate);
        Assert.False(display.HasWindowsReportedProblem);
        var problem = snapshot.Items.Single(item =>
            item.DisplayName == "Problem device");
        Assert.True(problem.HasWindowsReportedProblem);
        Assert.Equal(22, problem.ProblemCode);
        Assert.False(problem.IsEnabled);
        var properties = typeof(MachineDeviceSnapshot).GetProperties();
        Assert.DoesNotContain(properties, property =>
            property.Name.Contains("Serial", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Instance", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Location", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MissingPropertiesAndDisappearingDevicesRemainPartial()
    {
        var source = new FakeDeviceSource(new(
        [
            Device("Usable", "USB devices", problem: null),
            new(null, "Unknown", null, true, null, null,
                null, null, null)
        ],
        2,
        false,
        DateTimeOffset.UtcNow));

        var snapshot = await new WindowsMachineDeviceInventoryProvider(
            source).GetAsync();

        Assert.False(snapshot.IsComplete);
        Assert.Single(snapshot.Items);
        Assert.Equal(3, snapshot.ReadFailureCount);
    }

    [Fact]
    public async Task SourceAccessFailureReturnsEmptyPartialSnapshot()
    {
        var source = new FakeDeviceSource(null)
        {
            ThrowOnCapture = true
        };

        var snapshot = await new WindowsMachineDeviceInventoryProvider(
            source).GetAsync();

        Assert.Empty(snapshot.Items);
        Assert.False(snapshot.IsComplete);
        Assert.Equal(1, snapshot.ReadFailureCount);
    }

    [Fact]
    public async Task PreCancelledRequestDoesNotEnumerateDevices()
    {
        var source = new FakeDeviceSource(null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new WindowsMachineDeviceInventoryProvider(source).GetAsync(
                cancellation.Token));

        Assert.Equal(0, source.CaptureCount);
    }

    private static NativeDeviceRecord Device(
        string name,
        string deviceClass,
        uint? problem,
        bool? enabled = true) => new(
        name,
        deviceClass,
        "NVIDIA",
        true,
        enabled,
        problem,
        "NVIDIA",
        "32.0.15.7688",
        new DateOnly(2026, 8, 1));

    private sealed class FakeDeviceSource(NativeDeviceCapture? capture)
        : IWindowsDeviceInventorySource
    {
        public bool ThrowOnCapture { get; set; }
        public int CaptureCount { get; private set; }

        public NativeDeviceCapture Capture(
            CancellationToken cancellationToken)
        {
            CaptureCount++;
            if (ThrowOnCapture)
            {
                throw new InvalidOperationException("Access failed.");
            }
            return capture ?? new([], 0, true, DateTimeOffset.UtcNow);
        }
    }
}
