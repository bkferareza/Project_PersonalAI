using Machine.Core;
using Machine.Windows;

namespace Machine.Tests;

public sealed class WindowsMachineSoftwareInventoryProviderTests
{
    [Fact]
    public async Task GetAsyncReturnsValidOrderedSnapshot()
    {
        var provider =
            new WindowsMachineSoftwareInventoryProvider();

        var snapshot = await provider.GetAsync();

        Assert.NotEqual(default, snapshot.CapturedAt);
        Assert.True(snapshot.SkippedEntryCount >= 0);
        Assert.All(
            snapshot.Items,
            item =>
            {
                Assert.False(string.IsNullOrWhiteSpace(item.Name));
                Assert.True(
                    item.EstimatedSizeBytes is null or >= 0);
                Assert.True(Enum.IsDefined(item.Scope));
                Assert.True(Enum.IsDefined(item.RegistryView));
            });

        for (var index = 1;
             index < snapshot.Items.Count;
             index++)
        {
            Assert.True(Compare(
                    snapshot.Items[index - 1],
                    snapshot.Items[index]) <= 0,
                "Software registrations are not in the required order.");
        }
    }

    [Fact]
    public async Task GetAsyncWithPreCancelledTokenThrows()
    {
        var provider =
            new WindowsMachineSoftwareInventoryProvider();
        using var cancellationTokenSource =
            new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.GetAsync(
                cancellationTokenSource.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MapRegistrationRejectsEmptyDisplayName(
        string? displayName)
    {
        var item = Map(CreateValues(DisplayName: displayName));

        Assert.Null(item);
    }

    [Fact]
    public void MapRegistrationRejectsSystemComponentsAndChildren()
    {
        Assert.Null(Map(CreateValues(SystemComponent: 1)));
        Assert.Null(Map(CreateValues(
            ParentKeyName: "Parent Registration")));
    }

    [Theory]
    [InlineData("Update")]
    [InlineData("Hotfix")]
    [InlineData("Security Update")]
    [InlineData(" security update ")]
    public void MapRegistrationRejectsUpdateReleaseTypes(
        string releaseType)
    {
        var item = Map(CreateValues(ReleaseType: releaseType));

        Assert.Null(item);
    }

    [Fact]
    public void MapRegistrationConvertsValidEstimatedKilobytes()
    {
        var item = Map(CreateValues(
            DisplayName: "  Valid Tool  ",
            DisplayVersion: " 1.2.3 ",
            Publisher: " Publisher ",
            InstallLocation: " C:\\Tools ",
            EstimatedSize: 123));

        Assert.NotNull(item);
        Assert.Equal("Valid Tool", item.Name);
        Assert.Equal("1.2.3", item.Version);
        Assert.Equal("Publisher", item.Publisher);
        Assert.Equal("C:\\Tools", item.InstallLocation);
        Assert.Equal(123L * 1024L, item.EstimatedSizeBytes);
        Assert.Equal(
            MachineSoftwareScope.LocalMachine,
            item.Scope);
        Assert.Equal(
            MachineSoftwareRegistryView.Registry64,
            item.RegistryView);
    }

    [Fact]
    public void MapRegistrationToleratesMalformedOptionalValues()
    {
        var malformedValue = new object();

        var item = Map(CreateValues(
            DisplayVersion: malformedValue,
            Publisher: malformedValue,
            InstallLocation: malformedValue,
            EstimatedSize: "not-a-number",
            SystemComponent: malformedValue,
            ParentKeyName: malformedValue,
            ReleaseType: malformedValue));

        Assert.NotNull(item);
        Assert.Null(item.Version);
        Assert.Null(item.Publisher);
        Assert.Null(item.InstallLocation);
        Assert.Null(item.EstimatedSizeBytes);

        var overflowItem = Map(CreateValues(
            EstimatedSize: ulong.MaxValue));

        Assert.NotNull(overflowItem);
        Assert.Null(overflowItem.EstimatedSizeBytes);
    }

    private static MachineInstalledSoftwareSnapshot? Map(
        WindowsMachineSoftwareInventoryProvider.RegistrationValues
            values) =>
        WindowsMachineSoftwareInventoryProvider.MapRegistration(
            values,
            MachineSoftwareScope.LocalMachine,
            MachineSoftwareRegistryView.Registry64);

    private static WindowsMachineSoftwareInventoryProvider
        .RegistrationValues CreateValues(
            string? DisplayName = "Valid Tool",
            object? DisplayVersion = null,
            object? Publisher = null,
            object? InstallLocation = null,
            object? EstimatedSize = null,
            object? SystemComponent = null,
            object? ParentKeyName = null,
            object? ReleaseType = null) =>
        new(
            DisplayName: DisplayName,
            DisplayVersion: DisplayVersion,
            Publisher: Publisher,
            InstallLocation: InstallLocation,
            EstimatedSize: EstimatedSize,
            SystemComponent: SystemComponent,
            ParentKeyName: ParentKeyName,
            ReleaseType: ReleaseType);

    private static int Compare(
        MachineInstalledSoftwareSnapshot left,
        MachineInstalledSoftwareSnapshot right)
    {
        var comparison = StringComparer.OrdinalIgnoreCase.Compare(
            left.Name,
            right.Name);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.OrdinalIgnoreCase.Compare(
            left.Publisher ?? string.Empty,
            right.Publisher ?? string.Empty);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.OrdinalIgnoreCase.Compare(
            left.Version ?? string.Empty,
            right.Version ?? string.Empty);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Scope.CompareTo(right.Scope);

        return comparison != 0
            ? comparison
            : left.RegistryView.CompareTo(right.RegistryView);
    }
}
