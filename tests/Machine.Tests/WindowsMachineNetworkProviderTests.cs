using System.Net.NetworkInformation;
using Machine.Core;
using Machine.Windows;

namespace Machine.Tests;

public sealed class WindowsMachineNetworkProviderTests
{
    [Theory]
    [InlineData(NetworkInterfaceType.Ethernet, OperationalStatus.Up, true)]
    [InlineData(NetworkInterfaceType.Wireless80211, OperationalStatus.Up, true)]
    [InlineData(NetworkInterfaceType.Ethernet, OperationalStatus.Down, false)]
    [InlineData(NetworkInterfaceType.Loopback, OperationalStatus.Up, false)]
    [InlineData(NetworkInterfaceType.Tunnel, OperationalStatus.Up, false)]
    public void FilteringKeepsActiveAdaptersAndRemovesObviousNoise(
        NetworkInterfaceType interfaceType,
        OperationalStatus operationalStatus,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowsMachineNetworkProvider.ShouldIncludeInterface(
                interfaceType,
                operationalStatus));
    }

    [Theory]
    [InlineData("Ethernet", "Intel(R) Ethernet Controller", false)]
    [InlineData("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter", false)]
    [InlineData("Ethernet-QoS Packet Scheduler-0000", "Intel-QoS Packet Scheduler-0000", true)]
    [InlineData("Ethernet-WFP 802.3 MAC Layer LightWeight Filter-0000", "Intel", true)]
    [InlineData("Wi-Fi", "Native WiFi Filter", true)]
    [InlineData("Local Area Connection* 8", "WAN Miniport (IP)", true)]
    [InlineData("vSwitch (Default Switch)", "Hyper-V Virtual Switch Extension Adapter", true)]
    public void FilteringRemovesOnlyKnownNdisFilterLayers(
        string name,
        string description,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowsMachineNetworkProvider.IsObviousFilterInterface(
                name,
                description));
    }

    [Fact]
    public async Task GetAsyncReturnsActiveInterfacesAndCumulativeCounters()
    {
        var provider = new WindowsMachineNetworkProvider();

        var snapshot = await provider.GetAsync();

        Assert.NotEqual(default, snapshot.CapturedAt);
        Assert.Equal(snapshot.Interfaces.Count,
            snapshot.Aggregate.ActiveInterfaceCount);
        Assert.All(snapshot.Interfaces, networkInterface =>
        {
            Assert.False(string.IsNullOrWhiteSpace(networkInterface.Name));
            Assert.Equal("Up", networkInterface.OperationalStatus);
            Assert.NotEqual("Loopback", networkInterface.InterfaceType);
            Assert.NotEqual("Tunnel", networkInterface.InterfaceType);
            if (networkInterface.BytesReceived is not null)
            {
                Assert.True(networkInterface.BytesReceived >= 0);
            }
            if (networkInterface.BytesSent is not null)
            {
                Assert.True(networkInterface.BytesSent >= 0);
            }
        });
        Assert.Equal(
            snapshot.Interfaces.Where(item => item.BytesReceived is not null)
                .Aggregate(0UL, (total, item) => total + item.BytesReceived!.Value),
            snapshot.Aggregate.TotalBytesReceived);
        Assert.Equal(
            snapshot.Interfaces.Where(item => item.BytesSent is not null)
                .Aggregate(0UL, (total, item) => total + item.BytesSent!.Value),
            snapshot.Aggregate.TotalBytesSent);
    }

    [Fact]
    public async Task SuccessiveSampleHasOnlyNonNegativeOrUnavailableRates()
    {
        var provider = new WindowsMachineNetworkProvider();
        var first = await provider.GetAsync();
        var second = await provider.GetAsync();

        Assert.Equal(
            MachineNetworkActivityClass.Unavailable,
            first.Aggregate.ActivityClass);
        Assert.True(second.Aggregate.ReceiveBytesPerSecond is null or >= 0d);
        Assert.True(second.Aggregate.SendBytesPerSecond is null or >= 0d);
    }

    [Fact]
    public async Task GetAsyncWithPreCancelledTokenThrows()
    {
        var provider = new WindowsMachineNetworkProvider();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.GetAsync(cancellationTokenSource.Token));
    }
}
