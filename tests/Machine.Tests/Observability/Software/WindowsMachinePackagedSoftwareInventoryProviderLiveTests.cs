using Machine.Windows;
using Xunit.Abstractions;

namespace Machine.Tests;

public sealed class WindowsMachinePackagedSoftwareInventoryProviderLiveTests
{
    private readonly ITestOutputHelper _output;

    public WindowsMachinePackagedSoftwareInventoryProviderLiveTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task GetAsyncReturnsReadOnlyCurrentUserSnapshot()
    {
        var provider =
            new WindowsMachinePackagedSoftwareInventoryProvider();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var snapshot = await provider.GetAsync();

        stopwatch.Stop();

        Assert.NotEqual(default, snapshot.CapturedAt);
        Assert.True(snapshot.SkippedEntryCount >= 0);
        Assert.True(snapshot.OptionalPropertyFailureCount >= 0);
        Assert.True(snapshot.ExcludedFrameworkPackageCount >= 0);
        Assert.True(snapshot.ExcludedResourcePackageCount >= 0);
        Assert.Equal(
            snapshot.SkippedEntryCount == 0 &&
                snapshot.OptionalPropertyFailureCount == 0,
            snapshot.IsComplete);
        Assert.Equal(
            snapshot.Items,
            WindowsMachinePackagedSoftwareInventoryProvider
                .OrderItems(snapshot.Items));
        Assert.All(
            snapshot.Items,
            item =>
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(item.DisplayName));
                Assert.False(
                    string.IsNullOrWhiteSpace(
                        item.PackageFamilyName));
                Assert.False(
                    string.IsNullOrWhiteSpace(item.PackageFullName));
                Assert.False(string.IsNullOrWhiteSpace(item.Version));
                Assert.True(Enum.IsDefined(item.Architecture));
            });
        var developmentPackageCount = snapshot.Items.Count(
            item => item.IsDevelopmentMode == true);
        var stubPackageCount = snapshot.Items.Count(
            item => item.IsStub == true);

        _output.WriteLine(
            $"Displayed packages: {snapshot.Items.Count}");
        _output.WriteLine(
            $"Excluded framework packages: " +
            $"{snapshot.ExcludedFrameworkPackageCount}");
        _output.WriteLine(
            $"Excluded resource packages: " +
            $"{snapshot.ExcludedResourcePackageCount}");
        _output.WriteLine(
            $"Development packages: " +
            $"{developmentPackageCount}");
        _output.WriteLine(
            $"Stub packages: " +
            $"{stubPackageCount}");
        _output.WriteLine(
            $"Skipped entries: {snapshot.SkippedEntryCount}");
        _output.WriteLine(
            $"Optional property failures: " +
            $"{snapshot.OptionalPropertyFailureCount}");
        _output.WriteLine(
            snapshot.IsComplete ? "Complete" : "Partial");
        _output.WriteLine(
            $"Provider duration: {stopwatch.ElapsedMilliseconds} ms");
    }
}
