using System.ComponentModel;
using Machine.Core;
using Machine.Windows;

namespace Machine.Tests;

public sealed class WindowsMachineServiceInventoryProviderTests
{
    [Theory]
    [InlineData(1u, MachineServiceState.Stopped)]
    [InlineData(2u, MachineServiceState.StartPending)]
    [InlineData(3u, MachineServiceState.StopPending)]
    [InlineData(4u, MachineServiceState.Running)]
    [InlineData(5u, MachineServiceState.ContinuePending)]
    [InlineData(6u, MachineServiceState.PausePending)]
    [InlineData(7u, MachineServiceState.Paused)]
    [InlineData(99u, MachineServiceState.Unknown)]
    public void MapsServiceState(uint value, MachineServiceState expected)
    {
        Assert.Equal(
            expected,
            WindowsMachineServiceInventoryProvider.MapState(value));
    }

    [Theory]
    [InlineData(0u, false, MachineServiceStartType.Boot)]
    [InlineData(1u, false, MachineServiceStartType.System)]
    [InlineData(2u, false, MachineServiceStartType.Automatic)]
    [InlineData(2u, true, MachineServiceStartType.AutomaticDelayed)]
    [InlineData(3u, false, MachineServiceStartType.Manual)]
    [InlineData(4u, false, MachineServiceStartType.Disabled)]
    [InlineData(99u, false, MachineServiceStartType.Unknown)]
    public void MapsStartType(
        uint value,
        bool delayed,
        MachineServiceStartType expected)
    {
        Assert.Equal(
            expected,
            WindowsMachineServiceInventoryProvider.MapStartType(
                value,
                delayed));
    }

    [Fact]
    public async Task EnumerationProjectsOnlySafeReadOnlyFields()
    {
        var source = new FakeServiceSource(
        [
            new("service-b", "Zulu", 0x10, 4, 42),
            new("service-a", "Alpha", 0x01, 1, 0)
        ]);
        source.Configurations["service-a"] = new(3, null);
        source.Configurations["service-b"] = new(2, true);
        var provider = new WindowsMachineServiceInventoryProvider(source);

        var snapshot = await provider.GetAsync();

        Assert.True(snapshot.IsComplete);
        Assert.Collection(
            snapshot.Items,
            item =>
            {
                Assert.Equal("service-a", item.Name);
                Assert.Equal(MachineServiceCategory.Driver, item.Category);
                Assert.Equal(MachineServiceStartType.Manual, item.StartType);
                Assert.Null(item.ProcessId);
            },
            item =>
            {
                Assert.Equal("service-b", item.Name);
                Assert.Equal(MachineServiceCategory.Service, item.Category);
                Assert.Equal(
                    MachineServiceStartType.AutomaticDelayed,
                    item.StartType);
                Assert.Equal(42, item.ProcessId);
            });
        Assert.Equal(2, source.ConfigurationQueryCount);
        Assert.DoesNotContain(
            typeof(MachineServiceSnapshot).GetProperties(),
            property => property.Name.Contains(
                "Command",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AccessDeniedOrDisappearingServiceKeepsPartialStatus()
    {
        var source = new FakeServiceSource(
        [
            new("denied", "Denied", 0x10, 4, 10),
            new("vanished", "Vanished", 0x10, 1, 0)
        ]);
        source.ConfigurationFailures.Add("denied");
        source.ConfigurationFailures.Add("vanished");
        var provider = new WindowsMachineServiceInventoryProvider(source);

        var snapshot = await provider.GetAsync();

        Assert.False(snapshot.IsComplete);
        Assert.Equal(2, snapshot.ReadFailureCount);
        Assert.All(snapshot.Items, item => Assert.Equal(
            MachineServiceStartType.Unknown,
            item.StartType));
        Assert.Equal(MachineServiceState.Running,
            snapshot.Items.Single(item => item.Name == "denied").State);
    }

    [Fact]
    public async Task EnumerationFailureReturnsBoundedPartialSnapshot()
    {
        var source = new FakeServiceSource([])
        {
            EnumerationFailure = true
        };

        var snapshot = await new WindowsMachineServiceInventoryProvider(
            source).GetAsync();

        Assert.Empty(snapshot.Items);
        Assert.False(snapshot.IsComplete);
        Assert.Equal(1, snapshot.ReadFailureCount);
    }

    [Fact]
    public async Task PreCancelledRequestDoesNotCallScmSource()
    {
        var source = new FakeServiceSource([]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new WindowsMachineServiceInventoryProvider(source).GetAsync(
                cancellation.Token));

        Assert.Equal(0, source.EnumerationCount);
    }

    private sealed class FakeServiceSource(
        IReadOnlyList<NativeServiceStatus> statuses)
        : IWindowsServiceInventorySource
    {
        public Dictionary<string, NativeServiceConfiguration>
            Configurations { get; } =
                new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ConfigurationFailures { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public bool EnumerationFailure { get; set; }
        public int EnumerationCount { get; private set; }
        public int ConfigurationQueryCount { get; private set; }

        public IReadOnlyList<NativeServiceStatus> Enumerate(
            CancellationToken cancellationToken)
        {
            EnumerationCount++;
            if (EnumerationFailure)
            {
                throw new Win32Exception(5);
            }
            return statuses;
        }

        public NativeServiceConfiguration QueryConfiguration(
            string serviceName,
            CancellationToken cancellationToken)
        {
            ConfigurationQueryCount++;
            if (ConfigurationFailures.Contains(serviceName))
            {
                throw new Win32Exception(5);
            }
            return Configurations[serviceName];
        }
    }
}
