using Machine.Core;

namespace Machine.Tests;

public sealed class MachineNetworkThroughputCalculatorTests
{
    [Fact]
    public void FirstSampleIsUnavailable()
    {
        var result = MachineNetworkThroughputCalculator.Calculate(
            null,
            [new MachineNetworkCounterSample("a", 100, 200)],
            TimeSpan.FromSeconds(2));

        Assert.Null(result.ReceiveBytesPerSecond);
        Assert.Null(result.SendBytesPerSecond);
        Assert.Equal(0, result.ReceiveContributingInterfaceCount);
        Assert.Equal(0, result.SendContributingInterfaceCount);
    }

    [Fact]
    public void NormalDeltaAggregatesCurrentInterfaces()
    {
        MachineNetworkCounterSample[] previous =
        [
            new("a", 1_000, 2_000),
            new("b", 4_000, 8_000)
        ];
        MachineNetworkCounterSample[] current =
        [
            new("a", 1_600, 2_400),
            new("b", 5_400, 8_600)
        ];

        var result = MachineNetworkThroughputCalculator.Calculate(
            previous,
            current,
            TimeSpan.FromSeconds(2));

        Assert.Equal(1_000d, result.ReceiveBytesPerSecond);
        Assert.Equal(500d, result.SendBytesPerSecond);
        Assert.Equal(2, result.ReceiveContributingInterfaceCount);
        Assert.Equal(2, result.SendContributingInterfaceCount);
    }

    [Fact]
    public void IrregularElapsedIntervalUsesActualDuration()
    {
        var result = MachineNetworkThroughputCalculator.Calculate(
            [new MachineNetworkCounterSample("a", 100, 200)],
            [new MachineNetworkCounterSample("a", 1_100, 700)],
            TimeSpan.FromSeconds(2.5));

        Assert.Equal(400d, result.ReceiveBytesPerSecond);
        Assert.Equal(200d, result.SendBytesPerSecond);
    }

    [Fact]
    public void ZeroTrafficProducesZeroRates()
    {
        var sample = new MachineNetworkCounterSample("a", 100, 200);

        var result = MachineNetworkThroughputCalculator.Calculate(
            [sample],
            [sample],
            TimeSpan.FromSeconds(3));

        Assert.Equal(0d, result.ReceiveBytesPerSecond);
        Assert.Equal(0d, result.SendBytesPerSecond);
    }

    [Fact]
    public void CounterResetNeverProducesNegativeRates()
    {
        var result = MachineNetworkThroughputCalculator.Calculate(
            [
                new MachineNetworkCounterSample("reset", 1_000, 2_000),
                new MachineNetworkCounterSample("valid", 100, 200)
            ],
            [
                new MachineNetworkCounterSample("reset", 10, 20),
                new MachineNetworkCounterSample("valid", 300, 500)
            ],
            TimeSpan.FromSeconds(2));

        Assert.Equal(100d, result.ReceiveBytesPerSecond);
        Assert.Equal(150d, result.SendBytesPerSecond);
        Assert.True(result.ReceiveBytesPerSecond >= 0d);
        Assert.True(result.SendBytesPerSecond >= 0d);
    }

    [Fact]
    public void DisappearedAndNewInterfacesDoNotInventHistory()
    {
        var result = MachineNetworkThroughputCalculator.Calculate(
            [
                new MachineNetworkCounterSample("gone", 1_000, 2_000),
                new MachineNetworkCounterSample("kept", 500, 600)
            ],
            [
                new MachineNetworkCounterSample("kept", 700, 900),
                new MachineNetworkCounterSample("new", 50_000, 60_000)
            ],
            TimeSpan.FromSeconds(2));

        Assert.Equal(100d, result.ReceiveBytesPerSecond);
        Assert.Equal(150d, result.SendBytesPerSecond);
        Assert.Equal(1, result.ReceiveContributingInterfaceCount);
        Assert.Equal(1, result.SendContributingInterfaceCount);
    }
}
