using Machine.Windows;

namespace Machine.Tests;

public sealed class WindowsMachineResourceProviderTests
{
    [Fact]
    public async Task GetAsyncReturnsValidSnapshot()
    {
        var provider = new WindowsMachineResourceProvider();

        var snapshot = await provider.GetAsync();

        Assert.InRange(snapshot.CpuUsagePercent, 0d, 100d);
        Assert.True(snapshot.TotalMemoryBytes > 0);
        Assert.True(snapshot.UsedMemoryBytes <= snapshot.TotalMemoryBytes);
        Assert.NotEqual(default, snapshot.CapturedAt);
    }

    [Fact]
    public async Task GetAsyncWithPreCancelledTokenThrows()
    {
        var provider = new WindowsMachineResourceProvider();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.GetAsync(cancellationTokenSource.Token));
    }
}
