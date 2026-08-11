using Machine.Core;

namespace Machine.Tests;

public sealed class MachineNetworkActivityClassifierTests
{
    [Theory]
    [InlineData(null, 0d, MachineNetworkActivityClass.Unavailable)]
    [InlineData(0d, null, MachineNetworkActivityClass.Unavailable)]
    [InlineData(-1d, 0d, MachineNetworkActivityClass.Unavailable)]
    [InlineData(0d, 0d, MachineNetworkActivityClass.Quiet)]
    [InlineData(16_383d, 0d, MachineNetworkActivityClass.Quiet)]
    [InlineData(16_384d, 0d, MachineNetworkActivityClass.Light)]
    [InlineData(1_048_575d, 0d, MachineNetworkActivityClass.Light)]
    [InlineData(1_048_576d, 0d, MachineNetworkActivityClass.Active)]
    public void ClassifyUsesExactDocumentedBoundaries(
        double? receiveBytesPerSecond,
        double? sendBytesPerSecond,
        MachineNetworkActivityClass expected)
    {
        Assert.Equal(
            expected,
            MachineNetworkActivityClassifier.Classify(
                receiveBytesPerSecond,
                sendBytesPerSecond));
    }

    [Fact]
    public void DominantClassRequiresTwelveAvailableSamplesAndNoTie()
    {
        Assert.Null(MachineNetworkActivityClassifier.SelectDominant(7, 4, 0));
        Assert.Null(MachineNetworkActivityClassifier.SelectDominant(6, 6, 0));
        Assert.Equal(
            MachineNetworkActivityClass.Quiet,
            MachineNetworkActivityClassifier.SelectDominant(7, 5, 0));
    }
}
