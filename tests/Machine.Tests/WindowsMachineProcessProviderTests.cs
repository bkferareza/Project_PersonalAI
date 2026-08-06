using Machine.Windows;

namespace Machine.Tests;

public sealed class WindowsMachineProcessProviderTests
{
    [Fact]
    public async Task GetTopAsyncReturnsValidSnapshots()
    {
        var provider = new WindowsMachineProcessProvider();

        var snapshots = await provider.GetTopAsync(5);

        Assert.InRange(snapshots.Count, 1, 5);
        Assert.All(
            snapshots,
            snapshot =>
            {
                Assert.True(snapshot.ProcessId > 0);
                Assert.False(string.IsNullOrWhiteSpace(snapshot.Name));
                Assert.InRange(snapshot.CpuUsagePercent, 0d, 100d);
                Assert.True(snapshot.WorkingSetBytes >= 0);
            });
    }

    [Fact]
    public async Task GetTopAsyncWithZeroCountThrows()
    {
        var provider = new WindowsMachineProcessProvider();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => provider.GetTopAsync(0));
    }

    [Fact]
    public async Task GetTopAsyncWithPreCancelledTokenThrows()
    {
        var provider = new WindowsMachineProcessProvider();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.GetTopAsync(
                5,
                cancellationTokenSource.Token));
    }
}
