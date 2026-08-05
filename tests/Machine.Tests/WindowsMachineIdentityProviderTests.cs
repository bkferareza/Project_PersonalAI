using Machine.Windows;

namespace Machine.Tests;

public sealed class WindowsMachineIdentityProviderTests
{
    [Fact]
    public async Task GetAsyncReturnsPopulatedIdentity()
    {
        var provider = new WindowsMachineIdentityProvider();

        var identity = await provider.GetAsync();

        Assert.NotNull(identity);
        Assert.False(string.IsNullOrWhiteSpace(identity.DeviceName));
        Assert.False(string.IsNullOrWhiteSpace(identity.OperatingSystem));
        Assert.False(string.IsNullOrWhiteSpace(identity.Architecture));
    }

    [Fact]
    public async Task GetAsyncWithPreCancelledTokenThrows()
    {
        var provider = new WindowsMachineIdentityProvider();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.GetAsync(cancellationTokenSource.Token));
    }
}
