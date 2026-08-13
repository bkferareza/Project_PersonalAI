using Machine.Core;

namespace Machine.Tests;

public sealed class MachineProcessSnapshotTests
{
    [Fact]
    public void ConstructorPreservesValues()
    {
        var snapshot = new MachineProcessSnapshot(
            ProcessId: 8420,
            Name: "sample-process",
            CpuUsagePercent: 38.2d,
            WorkingSetBytes: 6_871_947_674L);

        Assert.Equal(8420, snapshot.ProcessId);
        Assert.Equal("sample-process", snapshot.Name);
        Assert.Equal(38.2d, snapshot.CpuUsagePercent);
        Assert.Equal(6_871_947_674L, snapshot.WorkingSetBytes);
    }
}
