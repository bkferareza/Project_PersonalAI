using Machine.Core;
using Machine.Windows;

namespace Machine.Tests;

public sealed class MachineRebootPendingTests
{
    private static readonly MachineRebootPendingReason[] SupportedReasons =
    [
        MachineRebootPendingReason.WindowsUpdate,
        MachineRebootPendingReason.ComponentServicing,
        MachineRebootPendingReason.PendingFileRename,
        MachineRebootPendingReason.ComputerRename
    ];

    public static TheoryData<MachineRebootPendingReason> Reasons => new()
    {
        MachineRebootPendingReason.WindowsUpdate,
        MachineRebootPendingReason.ComponentServicing,
        MachineRebootPendingReason.PendingFileRename,
        MachineRebootPendingReason.ComputerRename
    };

    [Theory]
    [MemberData(nameof(Reasons))]
    public void EachVerifiedIndicatorCanEstablishPending(
        MachineRebootPendingReason reason)
    {
        var snapshot = MachineRebootPendingAggregator.Aggregate(
            SupportedReasons.Select(candidate =>
                new MachineRebootPendingIndicator(
                candidate,
                candidate == reason)),
            DateTimeOffset.UnixEpoch);

        Assert.True(snapshot.IsPending);
        Assert.Equal(
            MachineRebootPendingConfidence.Verified,
            snapshot.Confidence);
        Assert.Equal(reason, Assert.Single(snapshot.Reasons));
        Assert.False(snapshot.IsPartial);
    }

    [Fact]
    public void AllVerifiedFalseMeansNoRestartPending()
    {
        var snapshot = MachineRebootPendingAggregator.Aggregate(
            SupportedReasons.Select(reason =>
                new MachineRebootPendingIndicator(reason, false)),
            DateTimeOffset.UnixEpoch);

        Assert.False(snapshot.IsPending);
        Assert.Empty(snapshot.Reasons);
        Assert.Equal(
            MachineRebootPendingConfidence.Verified,
            snapshot.Confidence);
        Assert.False(snapshot.IsPartial);
    }

    [Fact]
    public void TrueEvidenceSurvivesPartialAcquisition()
    {
        var snapshot = MachineRebootPendingAggregator.Aggregate(
        [
            new(MachineRebootPendingReason.WindowsUpdate, true),
            new(MachineRebootPendingReason.ComponentServicing, null),
            new(MachineRebootPendingReason.PendingFileRename, false),
            new(MachineRebootPendingReason.ComputerRename, null)
        ], DateTimeOffset.UnixEpoch);

        Assert.True(snapshot.IsPending);
        Assert.Equal(
            MachineRebootPendingConfidence.Partial,
            snapshot.Confidence);
        Assert.True(snapshot.IsPartial);
        Assert.Equal(
            MachineRebootPendingReason.WindowsUpdate,
            Assert.Single(snapshot.Reasons));
    }

    [Fact]
    public void PartialNegativeEvidenceRemainsUnknown()
    {
        var snapshot = MachineRebootPendingAggregator.Aggregate(
        [
            new(MachineRebootPendingReason.WindowsUpdate, false),
            new(MachineRebootPendingReason.ComponentServicing, null)
        ], DateTimeOffset.UnixEpoch);

        Assert.Null(snapshot.IsPending);
        Assert.Equal(
            MachineRebootPendingConfidence.Partial,
            snapshot.Confidence);
        Assert.True(snapshot.IsPartial);
    }

    [Fact]
    public void AllUnavailableMeansUnknown()
    {
        var snapshot = MachineRebootPendingAggregator.Aggregate(
            SupportedReasons.Select(reason =>
                new MachineRebootPendingIndicator(reason, null)),
            DateTimeOffset.UnixEpoch);

        Assert.Null(snapshot.IsPending);
        Assert.Empty(snapshot.Reasons);
        Assert.Equal(
            MachineRebootPendingConfidence.Unknown,
            snapshot.Confidence);
        Assert.True(snapshot.IsPartial);
    }

    [Fact]
    public void ReasonsAreDeduplicatedAndBounded()
    {
        var snapshot = MachineRebootPendingAggregator.Aggregate(
        [
            new(MachineRebootPendingReason.WindowsUpdate, false),
            new(MachineRebootPendingReason.WindowsUpdate, true),
            new(MachineRebootPendingReason.ComponentServicing, true),
            new(MachineRebootPendingReason.Unknown, true)
        ], DateTimeOffset.UnixEpoch);

        Assert.Equal(2, snapshot.Reasons.Count);
        Assert.Equal(2, snapshot.Indicators.Count);
        Assert.Contains(
            MachineRebootPendingReason.WindowsUpdate,
            snapshot.Reasons);
        Assert.Contains(
            MachineRebootPendingReason.ComponentServicing,
            snapshot.Reasons);
    }

    [Fact]
    public async Task WindowsProviderUsesOnlyNarrowReadSource()
    {
        var source = new RecordingIndicatorSource(
        [
            new(MachineRebootPendingReason.PendingFileRename, true)
        ]);
        var provider = new WindowsMachineRebootPendingProvider(source);

        var snapshot = await provider.GetAsync();

        Assert.Equal(1, source.ReadCount);
        Assert.True(snapshot.IsPending);
        Assert.Equal(
            MachineRebootPendingReason.PendingFileRename,
            Assert.Single(snapshot.Reasons));
    }

    [Fact]
    public async Task PreCancelledWindowsProviderDoesNotReadIndicators()
    {
        var source = new RecordingIndicatorSource([]);
        var provider = new WindowsMachineRebootPendingProvider(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetAsync(cancellation.Token));
        Assert.Equal(0, source.ReadCount);
    }

    private sealed class RecordingIndicatorSource(
        IReadOnlyList<MachineRebootPendingIndicator> indicators)
        : IWindowsRebootIndicatorSource
    {
        public int ReadCount { get; private set; }

        public IReadOnlyList<MachineRebootPendingIndicator> ReadIndicators(
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return indicators;
        }
    }
}
