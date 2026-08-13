using Machine.Core;
using Machine.Windows;

namespace Machine.Tests;

public sealed class WindowsPowerBroadcastMonitorTests
{
    [Theory]
    [InlineData(0x0004u, MachinePowerTransitionKind.Suspend)]
    [InlineData(0x0012u, MachinePowerTransitionKind.ResumeAutomatic)]
    [InlineData(0x0007u, MachinePowerTransitionKind.ResumeSuspend)]
    public void MapsVerifiedPowerBroadcasts(
        uint value,
        MachinePowerTransitionKind expected)
    {
        Assert.True(WindowsPowerBroadcastMonitor.TryMap(value, out var kind));
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(0x1234u)]
    public void IgnoresUnrelatedPowerBroadcasts(uint value)
    {
        Assert.False(WindowsPowerBroadcastMonitor.TryMap(value, out _));
    }
}
