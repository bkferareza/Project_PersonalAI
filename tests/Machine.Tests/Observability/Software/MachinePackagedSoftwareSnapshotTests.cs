using Machine.Core;

namespace Machine.Tests;

public sealed class MachinePackagedSoftwareSnapshotTests
{
    [Fact]
    public void ItemConstructorPreservesVerifiedValues()
    {
        var item = new MachinePackagedSoftwareSnapshot(
            DisplayName: "Machine Package",
            PublisherDisplayName: "Machine Publisher",
            PackageFamilyName: "Machine.Package_family",
            PackageFullName:
                "Machine.Package_2.3.4.5_x64__family",
            Version: "2.3.4.5",
            Architecture:
                MachinePackagedSoftwareArchitecture.X64,
            InstalledLocation:
                "C:\\Program Files\\WindowsApps\\Machine.Package",
            IsDevelopmentMode: true,
            IsStub: false);

        Assert.Equal("Machine Package", item.DisplayName);
        Assert.Equal(
            "Machine Publisher",
            item.PublisherDisplayName);
        Assert.Equal(
            "Machine.Package_family",
            item.PackageFamilyName);
        Assert.Equal(
            "Machine.Package_2.3.4.5_x64__family",
            item.PackageFullName);
        Assert.Equal("2.3.4.5", item.Version);
        Assert.Equal(
            MachinePackagedSoftwareArchitecture.X64,
            item.Architecture);
        Assert.Equal(
            "C:\\Program Files\\WindowsApps\\Machine.Package",
            item.InstalledLocation);
        Assert.True(item.IsDevelopmentMode);
        Assert.False(item.IsStub);
    }

    [Fact]
    public void InventoryConstructorPreservesValues()
    {
        MachinePackagedSoftwareSnapshot[] items =
        [
            new(
                DisplayName: "Machine Package",
                PublisherDisplayName: null,
                PackageFamilyName: "Machine.Package_family",
                PackageFullName:
                    "Machine.Package_1.0.0.0_neutral__family",
                Version: "1.0.0.0",
                Architecture:
                    MachinePackagedSoftwareArchitecture.Neutral,
                InstalledLocation: null,
                IsDevelopmentMode: null,
                IsStub: null)
        ];
        var capturedAt = new DateTimeOffset(
            2026,
            8,
            7,
            16,
            0,
            0,
            TimeSpan.Zero);

        var snapshot =
            new MachinePackagedSoftwareInventorySnapshot(
                Items: items,
                IsComplete: false,
                SkippedEntryCount: 3,
                OptionalPropertyFailureCount: 2,
                ExcludedFrameworkPackageCount: 4,
                ExcludedResourcePackageCount: 5,
                CapturedAt: capturedAt);

        Assert.Same(items, snapshot.Items);
        Assert.False(snapshot.IsComplete);
        Assert.Equal(3, snapshot.SkippedEntryCount);
        Assert.Equal(2, snapshot.OptionalPropertyFailureCount);
        Assert.Equal(4, snapshot.ExcludedFrameworkPackageCount);
        Assert.Equal(5, snapshot.ExcludedResourcePackageCount);
        Assert.Equal(capturedAt, snapshot.CapturedAt);
    }
}
