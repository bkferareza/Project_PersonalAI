using System.Runtime.InteropServices;
using Machine.Core;
using Machine.Windows;
using Windows.ApplicationModel;
using WindowsProcessorArchitecture =
    Windows.System.ProcessorArchitecture;

namespace Machine.Tests;

public sealed class WindowsMachinePackagedSoftwareInventoryProviderTests
{
    [Fact]
    public async Task GetAsyncWithPreCancelledTokenThrows()
    {
        var provider =
            new WindowsMachinePackagedSoftwareInventoryProvider();
        using var cancellationTokenSource =
            new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.GetAsync(
                cancellationTokenSource.Token));
    }

    [Fact]
    public void CaptureInventoryPropagatesEnumerationFailure()
    {
        var packages = Enumerable
            .Repeat(0, 1)
            .Select<int, Package>(
                _ => throw new COMException(
                    "Enumeration stopped before a package was read."));

        Assert.Throws<COMException>(
            () => WindowsMachinePackagedSoftwareInventoryProvider
                .CaptureInventory(
                    packages,
                    DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ms-resource:PackageDisplayName")]
    [InlineData(" MS-RESOURCE:PackageDisplayName ")]
    public void MapPackageFallsBackToIdentityName(
        string? displayName)
    {
        var item = Map(CreateValues(
            DisplayName: displayName,
            IdentityName: "  Machine.Identity  "));

        Assert.NotNull(item);
        Assert.Equal("Machine.Identity", item.DisplayName);
    }

    [Fact]
    public void MapPackagePreservesAvailableOptionalValues()
    {
        var item = Map(CreateValues(
            DisplayName: "  Machine Package  ",
            PublisherDisplayName: "  Machine Publisher  ",
            InstalledLocation: "  C:\\WindowsApps\\Machine  ",
            IsDevelopmentMode: true,
            IsStub: true));

        Assert.NotNull(item);
        Assert.Equal("Machine Package", item.DisplayName);
        Assert.Equal(
            "Machine Publisher",
            item.PublisherDisplayName);
        Assert.Equal(
            "C:\\WindowsApps\\Machine",
            item.InstalledLocation);
        Assert.True(item.IsDevelopmentMode);
        Assert.True(item.IsStub);
    }

    [Theory]
    [InlineData(
        WindowsProcessorArchitecture.Neutral,
        MachinePackagedSoftwareArchitecture.Neutral)]
    [InlineData(
        WindowsProcessorArchitecture.X86,
        MachinePackagedSoftwareArchitecture.X86)]
    [InlineData(
        WindowsProcessorArchitecture.X64,
        MachinePackagedSoftwareArchitecture.X64)]
    [InlineData(
        WindowsProcessorArchitecture.Arm,
        MachinePackagedSoftwareArchitecture.Arm)]
    [InlineData(
        WindowsProcessorArchitecture.Arm64,
        MachinePackagedSoftwareArchitecture.Arm64)]
    [InlineData(
        WindowsProcessorArchitecture.X86OnArm64,
        MachinePackagedSoftwareArchitecture.X86OnArm64)]
    [InlineData(
        WindowsProcessorArchitecture.Unknown,
        MachinePackagedSoftwareArchitecture.Unknown)]
    public void MapPackageFormatsVersionAndArchitecture(
        WindowsProcessorArchitecture architecture,
        MachinePackagedSoftwareArchitecture expectedArchitecture)
    {
        var item = Map(CreateValues(
            Version: new(
                Major: 12,
                Minor: 34,
                Build: 56,
                Revision: 78),
            Architecture: architecture));

        Assert.NotNull(item);
        Assert.Equal("12.34.56.78", item.Version);
        Assert.Equal(expectedArchitecture, item.Architecture);
    }

    [Fact]
    public void CreateSnapshotExcludesFrameworkAndResourcePackages()
    {
        WindowsMachinePackagedSoftwareInventoryProvider
            .PackageValues[] packages =
        [
            CreateValues(PackageFullName: "Visible"),
            CreateValues(
                PackageFullName: "Framework",
                IsFramework: true),
            CreateValues(
                PackageFullName: "Resource",
                IsResourcePackage: true),
            CreateValues(
                PackageFullName: "Both",
                IsFramework: true,
                IsResourcePackage: true)
        ];

        var snapshot = CreateSnapshot(packages);

        Assert.Single(snapshot.Items);
        Assert.Equal("Visible", snapshot.Items[0].PackageFullName);
        Assert.Equal(2, snapshot.ExcludedFrameworkPackageCount);
        Assert.Equal(2, snapshot.ExcludedResourcePackageCount);
        Assert.True(snapshot.IsComplete);
        Assert.Equal(0, snapshot.SkippedEntryCount);
    }

    [Fact]
    public void OptionalPropertyFailuresReturnUnavailableValues()
    {
        var optionalString =
            WindowsMachinePackagedSoftwareInventoryProvider
                .ReadOptionalString(
                    () => throw new UnauthorizedAccessException(),
                    out var optionalStringReadFailed);
        var optionalBoolean =
            WindowsMachinePackagedSoftwareInventoryProvider
                .ReadOptionalBoolean(
                    () => throw new COMException(),
                    out var optionalBooleanReadFailed);

        Assert.Null(optionalString);
        Assert.Null(optionalBoolean);
        Assert.True(optionalStringReadFailed);
        Assert.True(optionalBooleanReadFailed);

        var item = Map(CreateValues(
            DisplayName: optionalString,
            PublisherDisplayName: optionalString,
            InstalledLocation: optionalString,
            IsDevelopmentMode: optionalBoolean,
            IsStub: optionalBoolean,
            OptionalPropertyFailureCount: 2));

        Assert.NotNull(item);
        Assert.Equal("Machine.Identity", item.DisplayName);
        Assert.Null(item.PublisherDisplayName);
        Assert.Null(item.InstalledLocation);
        Assert.Null(item.IsDevelopmentMode);
        Assert.Null(item.IsStub);

        var snapshot = CreateSnapshot(
            [CreateValues(OptionalPropertyFailureCount: 2)]);

        Assert.False(snapshot.IsComplete);
        Assert.Equal(0, snapshot.SkippedEntryCount);
        Assert.Equal(2, snapshot.OptionalPropertyFailureCount);
    }

    [Fact]
    public void CreateSnapshotOrdersByNamePublisherThenFullName()
    {
        WindowsMachinePackagedSoftwareInventoryProvider
            .PackageValues[] packages =
        [
            CreateValues(
                DisplayName: "Zulu",
                PublisherDisplayName: "Publisher",
                PackageFullName: "Zulu.Full"),
            CreateValues(
                DisplayName: "alpha",
                PublisherDisplayName: "Publisher B",
                PackageFullName: "Alpha.B"),
            CreateValues(
                DisplayName: "Alpha",
                PublisherDisplayName: "Publisher A",
                PackageFullName: "Alpha.Z"),
            CreateValues(
                DisplayName: "Alpha",
                PublisherDisplayName: "Publisher A",
                PackageFullName: "Alpha.A")
        ];

        var snapshot = CreateSnapshot(packages);

        Assert.Equal(
            ["Alpha.A", "Alpha.Z", "Alpha.B", "Zulu.Full"],
            snapshot.Items.Select(item => item.PackageFullName));
    }

    [Fact]
    public void CreateSnapshotAccountsForMalformedAndUnreadableEntries()
    {
        WindowsMachinePackagedSoftwareInventoryProvider
            .PackageValues[] packages =
        [
            CreateValues(PackageFullName: "Valid.Full"),
            CreateValues(PackageFamilyName: "   ")
        ];
        var capturedAt = new DateTimeOffset(
            2026,
            8,
            7,
            17,
            0,
            0,
            TimeSpan.Zero);

        var snapshot =
            WindowsMachinePackagedSoftwareInventoryProvider
                .CreateSnapshot(
                    packages,
                    skippedEntryCount: 2,
                    capturedAt);

        Assert.Single(snapshot.Items);
        Assert.False(snapshot.IsComplete);
        Assert.Equal(3, snapshot.SkippedEntryCount);
        Assert.Equal(0, snapshot.OptionalPropertyFailureCount);
        Assert.Equal(capturedAt, snapshot.CapturedAt);
    }

    private static MachinePackagedSoftwareSnapshot? Map(
        WindowsMachinePackagedSoftwareInventoryProvider
            .PackageValues values) =>
        WindowsMachinePackagedSoftwareInventoryProvider
            .MapPackage(values);

    private static MachinePackagedSoftwareInventorySnapshot
        CreateSnapshot(
            IEnumerable<WindowsMachinePackagedSoftwareInventoryProvider
                .PackageValues> packages) =>
        WindowsMachinePackagedSoftwareInventoryProvider
            .CreateSnapshot(
                packages,
                skippedEntryCount: 0,
                capturedAt: DateTimeOffset.UnixEpoch);

    private static WindowsMachinePackagedSoftwareInventoryProvider
        .PackageValues CreateValues(
            string? DisplayName = "Machine Package",
            string? PublisherDisplayName = "Machine Publisher",
            string? IdentityName = "Machine.Identity",
            string? PackageFamilyName = "Machine.Identity_family",
            string? PackageFullName =
                "Machine.Identity_1.0.0.0_neutral__family",
            WindowsMachinePackagedSoftwareInventoryProvider
                .PackageVersionValues? Version = null,
            WindowsProcessorArchitecture Architecture =
                WindowsProcessorArchitecture.Neutral,
            string? InstalledLocation = null,
            bool? IsDevelopmentMode = false,
            bool? IsStub = false,
            int OptionalPropertyFailureCount = 0,
            bool IsFramework = false,
            bool IsResourcePackage = false) =>
        new(
            DisplayName: DisplayName,
            PublisherDisplayName: PublisherDisplayName,
            IdentityName: IdentityName,
            PackageFamilyName: PackageFamilyName,
            PackageFullName: PackageFullName,
            Version: Version ?? new(
                Major: 1,
                Minor: 0,
                Build: 0,
                Revision: 0),
            Architecture: Architecture,
            InstalledLocation: InstalledLocation,
            IsDevelopmentMode: IsDevelopmentMode,
            IsStub: IsStub,
            OptionalPropertyFailureCount:
                OptionalPropertyFailureCount,
            IsFramework: IsFramework,
            IsResourcePackage: IsResourcePackage);
}
