using Machine.Core;
using Machine.Windows;

namespace Machine.Tests;

public sealed class WindowsMachineUserActivityProviderTests
{
    [Theory]
    [InlineData(299, MachineUserActivityState.Active)]
    [InlineData(300, MachineUserActivityState.Idle)]
    public void GetStateUsesFiveMinuteIdleThreshold(
        int seconds,
        MachineUserActivityState expected)
    {
        Assert.Equal(expected, WindowsMachineUserActivityProvider.GetState(
            TimeSpan.FromSeconds(seconds)));
    }
}
