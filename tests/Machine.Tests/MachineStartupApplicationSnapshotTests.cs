using Machine.Core;

namespace Machine.Tests;

public sealed class MachineStartupApplicationSnapshotTests
{
    [Fact]
    public void ItemConstructorPreservesVerifiedValues()
    {
        var item = new MachineStartupApplicationSnapshot(
            Name: "Machine Agent",
            CommandOrPath: "\"C:\\Machine\\agent.exe\" --quiet",
            Source: MachineStartupSource.RegistryRunKey,
            Scope: MachineStartupScope.CurrentUser,
            RegistryView: MachineStartupRegistryView.Registry64);

        Assert.Equal("Machine Agent", item.Name);
        Assert.Equal(
            "\"C:\\Machine\\agent.exe\" --quiet",
            item.CommandOrPath);
        Assert.Equal(
            MachineStartupSource.RegistryRunKey,
            item.Source);
        Assert.Equal(MachineStartupScope.CurrentUser, item.Scope);
        Assert.Equal(
            MachineStartupRegistryView.Registry64,
            item.RegistryView);
    }

    [Fact]
    public void InventoryConstructorPreservesValues()
    {
        MachineStartupApplicationSnapshot[] items =
        [
            new(
                Name: "Machine Shortcut",
                CommandOrPath: "C:\\Startup\\Machine Shortcut.lnk",
                Source: MachineStartupSource.StartupFolder,
                Scope: MachineStartupScope.AllUsers,
                RegistryView: null)
        ];
        var capturedAt = new DateTimeOffset(
            2026,
            8,
            7,
            12,
            0,
            0,
            TimeSpan.Zero);

        var snapshot = new MachineStartupInventorySnapshot(
            Items: items,
            IsComplete: false,
            ReadFailureCount: 2,
            CapturedAt: capturedAt);

        Assert.Same(items, snapshot.Items);
        Assert.False(snapshot.IsComplete);
        Assert.Equal(2, snapshot.ReadFailureCount);
        Assert.Equal(capturedAt, snapshot.CapturedAt);
    }
}
