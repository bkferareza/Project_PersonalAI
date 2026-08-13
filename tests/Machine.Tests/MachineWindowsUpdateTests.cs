using Machine.Core;
using Machine.Windows;

namespace Machine.Tests;

public sealed class MachineWindowsUpdateTests
{
    [Theory]
    [InlineData(0, 0, false, MachineWindowsUpdateState.UpToDate)]
    [InlineData(3, 0, false, MachineWindowsUpdateState.UpdatesAvailable)]
    [InlineData(3, 1, false, MachineWindowsUpdateState.InstallPending)]
    [InlineData(0, 0, true, MachineWindowsUpdateState.RestartRequired)]
    public void EvaluateStateMapsVerifiedSearchResults(
        int pendingCount,
        int downloadedCount,
        bool restartRequired,
        MachineWindowsUpdateState expected)
    {
        var actual = MachineWindowsUpdatePolicy.EvaluateState(
            serviceAvailable: true,
            searchSucceeded: true,
            pendingCount,
            downloadedCount,
            restartRequired);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, -1)]
    public void EvaluateStateNeverInventsUpToDateOnUnavailableData(
        bool serviceAvailable,
        bool searchSucceeded,
        int pendingCount)
    {
        Assert.Equal(
            MachineWindowsUpdateState.Unknown,
            MachineWindowsUpdatePolicy.EvaluateState(
                serviceAvailable,
                searchSucceeded,
                pendingCount,
                downloadedPendingUpdateCount: 0,
                restartRequired: false));
    }

    [Fact]
    public void NormalizeHistoryDeduplicatesAndBoundsNewestEntries()
    {
        var now = DateTimeOffset.Parse("2026-08-14T12:00:00Z");
        var entries = Enumerable.Range(0, 40)
            .Select(index => new MachineWindowsUpdateHistoryEntry(
                now.AddMinutes(-index),
                $"Cumulative Update KB{5000000 + index}",
                "Updates",
                null,
                MachineWindowsUpdateHistoryResult.Succeeded))
            .Append(new MachineWindowsUpdateHistoryEntry(
                now,
                " Cumulative   Update KB5000000 ",
                "Updates",
                "kb5000000",
                MachineWindowsUpdateHistoryResult.Succeeded));

        var normalized = MachineWindowsUpdatePolicy.NormalizeHistory(entries);

        Assert.Equal(
            MachineWindowsUpdatePolicy.MaximumHistoryCount,
            normalized.Count);
        Assert.Single(normalized, entry =>
            entry.KnowledgeBaseId == "KB5000000");
        Assert.Equal(now, normalized[0].OccurredAt);
    }

    [Theory]
    [InlineData(0, MachineWindowsUpdateHistoryResult.Unknown)]
    [InlineData(1, MachineWindowsUpdateHistoryResult.InProgress)]
    [InlineData(2, MachineWindowsUpdateHistoryResult.Succeeded)]
    [InlineData(3, MachineWindowsUpdateHistoryResult.SucceededWithErrors)]
    [InlineData(4, MachineWindowsUpdateHistoryResult.Failed)]
    [InlineData(5, MachineWindowsUpdateHistoryResult.Cancelled)]
    [InlineData(99, MachineWindowsUpdateHistoryResult.Unknown)]
    public void OperationResultMappingIsExplicit(
        int code,
        MachineWindowsUpdateHistoryResult expected)
    {
        Assert.Equal(
            expected,
            WindowsUpdateComSnapshotSource.MapOperationResultCode(code));
    }

    [Fact]
    public async Task ProviderReusesCachedResultUntilRefreshInterval()
    {
        var now = DateTimeOffset.Parse("2026-08-14T12:00:00Z");
        var source = new RecordingUpdateSource(_ =>
            Task.FromResult(CreateSnapshot(now, 0)));
        var provider = CreateProvider(source, () => now);

        var first = await provider.GetAsync();
        now = now.AddMinutes(44);
        var cached = await provider.GetAsync();

        Assert.Same(first, cached);
        Assert.Equal(1, source.CaptureCount);
    }

    [Fact]
    public async Task ProviderRefreshesAfterThrottleWindow()
    {
        var now = DateTimeOffset.Parse("2026-08-14T12:00:00Z");
        var source = new RecordingUpdateSource(_ =>
            Task.FromResult(CreateSnapshot(now, 0)));
        var provider = CreateProvider(source, () => now);

        await provider.GetAsync();
        now = now.AddMinutes(46);
        var refreshed = await provider.GetAsync();

        Assert.Equal(2, source.CaptureCount);
        Assert.Equal(now, refreshed.VerifiedAt);
    }

    [Fact]
    public async Task FailedRefreshPreservesLastVerifiedSnapshot()
    {
        var now = DateTimeOffset.Parse("2026-08-14T12:00:00Z");
        var source = new RecordingUpdateSource(call => call == 1
            ? Task.FromResult(CreateSnapshot(now, 3))
            : Task.FromException<MachineWindowsUpdateSnapshot>(
                new IOException("Simulated failure.")));
        var provider = CreateProvider(source, () => now);

        var first = await provider.GetAsync();
        now = now.AddMinutes(46);
        var preserved = await provider.GetAsync();

        Assert.Equal(first.VerifiedAt, preserved.VerifiedAt);
        Assert.Equal(3, preserved.PendingUpdateCount);
        Assert.Equal(
            MachineWindowsUpdateState.UpdatesAvailable,
            preserved.UpdateState);
        Assert.Equal(
            MachineWindowsUpdateRefreshStatus.CachedAfterFailure,
            preserved.RefreshStatus);
        Assert.Equal(MachineHealthDataStatus.Partial, preserved.DataStatus);
    }

    [Fact]
    public async Task FirstFailedRefreshIsUnknownNotUpToDate()
    {
        var now = DateTimeOffset.Parse("2026-08-14T12:00:00Z");
        var source = new RecordingUpdateSource(_ =>
            Task.FromException<MachineWindowsUpdateSnapshot>(
                new IOException("Simulated failure.")));
        var provider = CreateProvider(source, () => now);

        var snapshot = await provider.GetAsync();

        Assert.Null(snapshot.VerifiedAt);
        Assert.Null(snapshot.PendingUpdateCount);
        Assert.Equal(MachineWindowsUpdateState.Unknown, snapshot.UpdateState);
        Assert.Equal(MachineHealthDataStatus.Unavailable, snapshot.DataStatus);
    }

    [Fact]
    public async Task CancellationDoesNotReplaceLastGoodSnapshot()
    {
        var now = DateTimeOffset.Parse("2026-08-14T12:00:00Z");
        var source = new RecordingUpdateSource(call => call == 1
            ? Task.FromResult(CreateSnapshot(now, 1))
            : Task.Delay(Timeout.InfiniteTimeSpan)
                .ContinueWith(
                    _ => CreateSnapshot(now, 2),
                    TaskScheduler.Default));
        var provider = CreateProvider(source, () => now);
        var first = await provider.GetAsync();
        now = now.AddMinutes(46);
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetAsync(cancellation.Token));

        Assert.Equal(1, first.PendingUpdateCount);
    }

    [Fact]
    public async Task PreCancelledRequestDoesNotInvokeSource()
    {
        var source = new RecordingUpdateSource(_ =>
            Task.FromResult(CreateSnapshot(DateTimeOffset.UtcNow, 0)));
        var provider = CreateProvider(source, () => DateTimeOffset.UtcNow);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetAsync(cancellation.Token));
        Assert.Equal(0, source.CaptureCount);
    }

    private static WindowsMachineUpdateProvider CreateProvider(
        IWindowsUpdateSnapshotSource source,
        Func<DateTimeOffset> clock) => new(
        source,
        clock,
        TimeSpan.FromMinutes(45),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromSeconds(1));

    private static MachineWindowsUpdateSnapshot CreateSnapshot(
        DateTimeOffset capturedAt,
        int pendingCount) => new(
        capturedAt,
        capturedAt,
        true,
        capturedAt.AddHours(-1),
        capturedAt.AddDays(-1),
        pendingCount,
        0,
        pendingCount == 0
            ? MachineWindowsUpdateState.UpToDate
            : MachineWindowsUpdateState.UpdatesAvailable,
        [],
        MachineHealthDataStatus.Complete,
        MachineWindowsUpdateRefreshStatus.Verified);

    private sealed class RecordingUpdateSource(
        Func<int, Task<MachineWindowsUpdateSnapshot>> capture)
        : IWindowsUpdateSnapshotSource
    {
        public int CaptureCount { get; private set; }

        public Task<MachineWindowsUpdateSnapshot> CaptureAsync(
            CancellationToken cancellationToken)
        {
            CaptureCount++;
            return capture(CaptureCount);
        }
    }
}
