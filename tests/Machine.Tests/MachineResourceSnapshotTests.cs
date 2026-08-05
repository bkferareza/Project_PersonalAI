using Machine.Core;

namespace Machine.Tests;

public sealed class MachineResourceSnapshotTests
{
    [Fact]
    public void ConstructorPreservesValues()
    {
        var capturedAt = new DateTimeOffset(
            2026,
            8,
            6,
            10,
            30,
            0,
            TimeSpan.Zero);

        var snapshot = new MachineResourceSnapshot(
            CpuUsagePercent: 18.4d,
            TotalMemoryBytes: 34_359_738_368UL,
            UsedMemoryBytes: 19_112_837_120UL,
            CapturedAt: capturedAt);

        Assert.Equal(18.4d, snapshot.CpuUsagePercent);
        Assert.Equal(34_359_738_368UL, snapshot.TotalMemoryBytes);
        Assert.Equal(19_112_837_120UL, snapshot.UsedMemoryBytes);
        Assert.Equal(capturedAt, snapshot.CapturedAt);
    }
}
