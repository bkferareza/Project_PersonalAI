using Machine.Core;

namespace Machine.Tests;

public sealed class MachineFolderSizeSnapshotTests
{
    [Fact]
    public void FolderConstructorPreservesValues()
    {
        var folder = new MachineFolderSizeSnapshot(
            Path: "C:\\Users",
            SizeBytes: 123_456_789,
            FileCount: 321,
            IsComplete: false);

        Assert.Equal("C:\\Users", folder.Path);
        Assert.Equal(123_456_789, folder.SizeBytes);
        Assert.Equal(321, folder.FileCount);
        Assert.False(folder.IsComplete);
    }

    [Fact]
    public void InspectionConstructorPreservesValues()
    {
        MachineFolderSizeSnapshot[] folders =
        [
            new(
                Path: "C:\\Windows",
                SizeBytes: 987_654_321,
                FileCount: 654,
                IsComplete: true)
        ];
        var capturedAt = new DateTimeOffset(
            2026,
            8,
            6,
            13,
            0,
            0,
            TimeSpan.Zero);

        var snapshot = new MachineFolderInspectionSnapshot(
            RootPath: "C:\\",
            Folders: folders,
            IsComplete: false,
            SkippedDirectoryCount: 7,
            CapturedAt: capturedAt);

        Assert.Equal("C:\\", snapshot.RootPath);
        Assert.Same(folders, snapshot.Folders);
        Assert.False(snapshot.IsComplete);
        Assert.Equal(7, snapshot.SkippedDirectoryCount);
        Assert.Equal(capturedAt, snapshot.CapturedAt);
    }
}
