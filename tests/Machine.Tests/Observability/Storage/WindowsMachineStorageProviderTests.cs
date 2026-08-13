using Machine.Windows;

namespace Machine.Tests;

public sealed class WindowsMachineStorageProviderTests
{
    [Fact]
    public async Task GetAsyncReturnsReadableVolumes()
    {
        var provider = new WindowsMachineStorageProvider();

        var snapshot = await provider.GetAsync();

        Assert.NotEqual(default, snapshot.CapturedAt);
        Assert.NotEmpty(snapshot.Volumes);
        Assert.All(
            snapshot.Volumes,
            volume =>
            {
                Assert.False(string.IsNullOrWhiteSpace(
                    volume.RootPath));
                Assert.True(volume.TotalSizeBytes > 0);
                Assert.InRange(
                    volume.AvailableFreeSpaceBytes,
                    0,
                    volume.TotalSizeBytes);
            });

        var systemRoot = Path.GetPathRoot(
            Environment.SystemDirectory);

        Assert.Contains(
            snapshot.Volumes,
            volume =>
                volume.IsSystemVolume &&
                string.Equals(
                    volume.RootPath,
                    systemRoot,
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetAsyncWithPreCancelledTokenThrows()
    {
        var provider = new WindowsMachineStorageProvider();
        using var cancellationTokenSource =
            new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.GetAsync(
                cancellationTokenSource.Token));
    }
}
