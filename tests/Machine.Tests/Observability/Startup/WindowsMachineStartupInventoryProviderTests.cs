using Machine.Core;
using Machine.Windows;
using Microsoft.Win32;

namespace Machine.Tests;

public sealed class WindowsMachineStartupInventoryProviderTests
{
    [Fact]
    public async Task GetAsyncWithPreCancelledTokenThrows()
    {
        var provider =
            new WindowsMachineStartupInventoryProvider();
        using var cancellationTokenSource =
            new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.GetAsync(
                cancellationTokenSource.Token));
    }

    [Fact]
    public void CreateRegistrySourcesFor64BitWindowsUsesSharedUserKey()
    {
        var sources =
            WindowsMachineStartupInventoryProvider
                .CreateRegistrySources(
                    is64BitOperatingSystem: true);

        Assert.Collection(
            sources,
            source => AssertRegistrySource(
                source,
                RegistryHive.LocalMachine,
                RegistryView.Registry64,
                MachineStartupScope.AllUsers,
                MachineStartupRegistryView.Registry64),
            source => AssertRegistrySource(
                source,
                RegistryHive.LocalMachine,
                RegistryView.Registry32,
                MachineStartupScope.AllUsers,
                MachineStartupRegistryView.Registry32),
            source => AssertRegistrySource(
                source,
                RegistryHive.CurrentUser,
                RegistryView.Default,
                MachineStartupScope.CurrentUser,
                MachineStartupRegistryView.Shared));
    }

    [Fact]
    public void CreateRegistrySourcesFor32BitWindowsUsesOneMachineView()
    {
        var sources =
            WindowsMachineStartupInventoryProvider
                .CreateRegistrySources(
                    is64BitOperatingSystem: false);

        Assert.Collection(
            sources,
            source => AssertRegistrySource(
                source,
                RegistryHive.LocalMachine,
                RegistryView.Registry32,
                MachineStartupScope.AllUsers,
                MachineStartupRegistryView.Registry32),
            source => AssertRegistrySource(
                source,
                RegistryHive.CurrentUser,
                RegistryView.Default,
                MachineStartupScope.CurrentUser,
                MachineStartupRegistryView.Shared));
    }

    [Fact]
    public void CreateStartupFolderSourcesUsesCurrentAndCommonFolders()
    {
        var sources =
            WindowsMachineStartupInventoryProvider
                .CreateStartupFolderSources(
                    "C:\\Users\\Machine\\Startup",
                    "C:\\ProgramData\\Startup");

        Assert.Collection(
            sources,
            source =>
            {
                Assert.Equal(
                    "C:\\Users\\Machine\\Startup",
                    source.Path);
                Assert.Equal(
                    MachineStartupScope.CurrentUser,
                    source.Scope);
            },
            source =>
            {
                Assert.Equal(
                    "C:\\ProgramData\\Startup",
                    source.Path);
                Assert.Equal(
                    MachineStartupScope.AllUsers,
                    source.Scope);
            });
    }

    [Fact]
    public void MapRegistryEntryPreservesUnexpandedCommand()
    {
        var item =
            WindowsMachineStartupInventoryProvider
                .MapRegistryEntry(
                    "  Machine Agent  ",
                    "  %LOCALAPPDATA%\\Machine\\agent.exe --quiet  ",
                    MachineStartupScope.CurrentUser,
                    MachineStartupRegistryView.Shared);

        Assert.NotNull(item);
        Assert.Equal("Machine Agent", item.Name);
        Assert.Equal(
            "%LOCALAPPDATA%\\Machine\\agent.exe --quiet",
            item.CommandOrPath);
        Assert.Equal(
            MachineStartupSource.RegistryRunKey,
            item.Source);
        Assert.Equal(MachineStartupScope.CurrentUser, item.Scope);
        Assert.Equal(
            MachineStartupRegistryView.Shared,
            item.RegistryView);
    }

    [Theory]
    [InlineData(null, "command")]
    [InlineData("", "command")]
    [InlineData("   ", "command")]
    [InlineData("Name", null)]
    [InlineData("Name", "")]
    [InlineData("Name", "   ")]
    public void MapRegistryEntryRejectsMissingValues(
        string? name,
        string? command)
    {
        var item =
            WindowsMachineStartupInventoryProvider
                .MapRegistryEntry(
                    name,
                    command,
                    MachineStartupScope.AllUsers,
                    MachineStartupRegistryView.Registry32);

        Assert.Null(item);
    }

    [Fact]
    public void MapRegistryEntryRejectsNonStringCommand()
    {
        var item =
            WindowsMachineStartupInventoryProvider
                .MapRegistryEntry(
                    "Machine Agent",
                    123,
                    MachineStartupScope.AllUsers,
                    MachineStartupRegistryView.Registry32);

        Assert.Null(item);
    }

    [Fact]
    public void RegistryEntryIsReadOnlyWithoutFineGrainedUnvirtualization()
    {
        var item = WindowsMachineStartupInventoryProvider.MapRegistryEntry(
            "Agent",
            "C:\\Agent\\agent.exe",
            MachineStartupScope.CurrentUser,
            MachineStartupRegistryView.Shared,
            MachineStartupRegistryValueKind.String,
            supportsUnvirtualizedRegistryWrites: false);

        Assert.NotNull(item);
        Assert.Equal(
            MachineStartupActionAvailability.Unsupported,
            item.ActionAvailability);
    }

    [Theory]
    [InlineData(20347, false)]
    [InlineData(20348, true)]
    [InlineData(26200, true)]
    public void RegistryWriteSupportUsesDocumentedWindowsBuildFloor(
        int build,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowsMachineStartupInventoryProvider
                .IsUnvirtualizedRegistryWriteSupported(
                    new Version(10, 0, build)));
    }

    [Fact]
    public void MapStartupFolderEntryUsesFileNameAndExactPath()
    {
        var item =
            WindowsMachineStartupInventoryProvider
                .MapStartupFolderEntry(
                    " Machine Agent.lnk ",
                    " C:\\Startup\\Machine Agent.lnk ",
                    MachineStartupScope.AllUsers);

        Assert.NotNull(item);
        Assert.Equal("Machine Agent", item.Name);
        Assert.Equal(
            "C:\\Startup\\Machine Agent.lnk",
            item.CommandOrPath);
        Assert.Equal(
            MachineStartupSource.StartupFolder,
            item.Source);
        Assert.Equal(MachineStartupScope.AllUsers, item.Scope);
        Assert.Null(item.RegistryView);
    }

    [Theory]
    [InlineData(null, "C:\\Startup\\Agent.lnk")]
    [InlineData("", "C:\\Startup\\Agent.lnk")]
    [InlineData("Agent.lnk", null)]
    [InlineData("Agent.lnk", "   ")]
    [InlineData("desktop.ini", "C:\\Startup\\desktop.ini")]
    [InlineData("DESKTOP.INI", "C:\\Startup\\DESKTOP.INI")]
    public void MapStartupFolderEntryRejectsNonApplications(
        string? fileName,
        string? fullPath)
    {
        var item =
            WindowsMachineStartupInventoryProvider
                .MapStartupFolderEntry(
                    fileName,
                    fullPath,
                    MachineStartupScope.CurrentUser);

        Assert.Null(item);
    }

    [Fact]
    public void OrderItemsUsesStableVerifiedFields()
    {
        MachineStartupApplicationSnapshot[] items =
        [
            CreateItem(
                "Zulu",
                "C:\\Zulu.lnk",
                MachineStartupSource.StartupFolder,
                MachineStartupScope.CurrentUser,
                null),
            CreateItem(
                "alpha",
                "command-b",
                MachineStartupSource.RegistryRunKey,
                MachineStartupScope.CurrentUser,
                MachineStartupRegistryView.Registry64),
            CreateItem(
                "Alpha",
                "command-a",
                MachineStartupSource.RegistryRunKey,
                MachineStartupScope.AllUsers,
                MachineStartupRegistryView.Registry32),
            CreateItem(
                "Alpha",
                "C:\\Alpha.lnk",
                MachineStartupSource.StartupFolder,
                MachineStartupScope.AllUsers,
                null)
        ];

        var ordered =
            WindowsMachineStartupInventoryProvider.OrderItems(items);

        Assert.Equal(
            ["command-b", "command-a", "C:\\Alpha.lnk", "C:\\Zulu.lnk"],
            ordered.Select(item => item.CommandOrPath));
    }

    private static MachineStartupApplicationSnapshot CreateItem(
        string name,
        string commandOrPath,
        MachineStartupSource source,
        MachineStartupScope scope,
        MachineStartupRegistryView? registryView) =>
        new(
            Name: name,
            CommandOrPath: commandOrPath,
            Source: source,
            Scope: scope,
            RegistryView: registryView);

    private static void AssertRegistrySource(
        WindowsMachineStartupInventoryProvider.RegistrySource source,
        RegistryHive expectedHive,
        RegistryView expectedRegistryView,
        MachineStartupScope expectedScope,
        MachineStartupRegistryView expectedStartupRegistryView)
    {
        Assert.Equal(expectedHive, source.Hive);
        Assert.Equal(expectedRegistryView, source.RegistryView);
        Assert.Equal(expectedScope, source.Scope);
        Assert.Equal(
            expectedStartupRegistryView,
            source.StartupRegistryView);
    }
}
