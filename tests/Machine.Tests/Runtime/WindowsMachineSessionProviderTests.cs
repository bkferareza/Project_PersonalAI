using Machine.Core;
using Machine.Windows;

namespace Machine.Tests;

public sealed class WindowsMachineSessionProviderTests
{
    [Fact]
    public void MonotonicConversionsUseElapsedValuesWithoutWallClockMath()
    {
        Assert.Equal(
            TimeSpan.FromDays(3),
            WindowsMachineSessionProvider.ConvertSystemUptimeMilliseconds(
                (long)TimeSpan.FromDays(3).TotalMilliseconds));
        Assert.Equal(
            TimeSpan.FromSeconds(2.5),
            WindowsMachineSessionProvider.CalculateMonotonicElapsed(
                1_000,
                3_500,
                1_000));
        Assert.Equal(
            TimeSpan.Zero,
            WindowsMachineSessionProvider.CalculateMonotonicElapsed(
                3_500,
                1_000,
                1_000));
    }

    [Fact]
    public async Task GetAsyncPreservesAuthoritativeInputStateAndIdleAge()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var activity = new MachineUserActivitySnapshot(
            TimeSpan.FromMinutes(8),
            MachineUserActivityState.Idle,
            capturedAt);
        var provider = new WindowsMachineSessionProvider(
            new StubUserActivityProvider(activity));

        var snapshot = await provider.GetAsync();

        Assert.Equal(MachineUserActivityState.Idle,
            snapshot.CurrentUserInputState);
        Assert.Equal(TimeSpan.FromMinutes(8),
            snapshot.CurrentUserIdleDuration);
        Assert.True(snapshot.SystemUptime >= TimeSpan.Zero);
        Assert.True(snapshot.MachineUptime >= TimeSpan.Zero);
        Assert.NotEqual(default, snapshot.CapturedAt);
    }

    [Fact]
    public async Task GetAsyncWithPreCancelledTokenThrows()
    {
        var provider = new WindowsMachineSessionProvider(
            new StubUserActivityProvider(new MachineUserActivitySnapshot(
                TimeSpan.Zero,
                MachineUserActivityState.Active,
                DateTimeOffset.UtcNow)));
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.GetAsync(cancellationTokenSource.Token));
    }

    private sealed class StubUserActivityProvider(
        MachineUserActivitySnapshot snapshot) : IMachineUserActivityProvider
    {
        public Task<MachineUserActivitySnapshot> GetAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(snapshot);
        }
    }
}
