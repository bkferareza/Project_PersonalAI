using Machine.Core;

namespace Machine.Tests;

public sealed class MachineInstalledSoftwareSnapshotTests
{
    [Fact]
    public void ItemConstructorPreservesValues()
    {
        var item = new MachineInstalledSoftwareSnapshot(
            Name: "Machine Tool",
            Version: "2.4.1",
            Publisher: "Machine Publisher",
            InstallLocation: "C:\\Program Files\\Machine Tool",
            EstimatedSizeBytes: 123_456_789,
            Scope: MachineSoftwareScope.LocalMachine,
            RegistryView: MachineSoftwareRegistryView.Registry64);

        Assert.Equal("Machine Tool", item.Name);
        Assert.Equal("2.4.1", item.Version);
        Assert.Equal("Machine Publisher", item.Publisher);
        Assert.Equal(
            "C:\\Program Files\\Machine Tool",
            item.InstallLocation);
        Assert.Equal(123_456_789, item.EstimatedSizeBytes);
        Assert.Equal(
            MachineSoftwareScope.LocalMachine,
            item.Scope);
        Assert.Equal(
            MachineSoftwareRegistryView.Registry64,
            item.RegistryView);
    }

    [Fact]
    public void InventoryConstructorPreservesValues()
    {
        MachineInstalledSoftwareSnapshot[] items =
        [
            new(
                Name: "Current User Tool",
                Version: null,
                Publisher: null,
                InstallLocation: null,
                EstimatedSizeBytes: null,
                Scope: MachineSoftwareScope.CurrentUser,
                RegistryView:
                    MachineSoftwareRegistryView.Registry32)
        ];
        var capturedAt = new DateTimeOffset(
            2026,
            8,
            6,
            14,
            0,
            0,
            TimeSpan.Zero);

        var snapshot = new MachineSoftwareInventorySnapshot(
            Items: items,
            IsComplete: false,
            SkippedEntryCount: 3,
            CapturedAt: capturedAt);

        Assert.Same(items, snapshot.Items);
        Assert.False(snapshot.IsComplete);
        Assert.Equal(3, snapshot.SkippedEntryCount);
        Assert.Equal(capturedAt, snapshot.CapturedAt);
    }
}
