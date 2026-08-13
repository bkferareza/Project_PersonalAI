using System.Security;
using Machine.Windows;

namespace Machine.Tests;

public sealed class WindowsMachineFolderInspectionProviderTests
{
    [Fact]
    public async Task DirectoryTestSeamReturnsLargestFolders()
    {
        var fixtureDirectory = Path.Combine(
            Path.GetTempPath(),
            $"MachineFolderInspectionTests-{Guid.NewGuid():N}");
        var scanRoot = Path.Combine(fixtureDirectory, "Root");
        var smallDirectory = Path.Combine(scanRoot, "Small");
        var mediumDirectory = Path.Combine(scanRoot, "Medium");
        var largeDirectory = Path.Combine(scanRoot, "Large");
        var reparseTarget = Path.Combine(
            fixtureDirectory,
            "ReparseTarget");
        var reparsePath = Path.Combine(
            largeDirectory,
            "LinkedTarget");
        var reparsePointCreated = false;

        try
        {
            Directory.CreateDirectory(smallDirectory);
            Directory.CreateDirectory(mediumDirectory);
            Directory.CreateDirectory(largeDirectory);
            Directory.CreateDirectory(reparseTarget);

            WriteFile(smallDirectory, "one.bin", 10);
            WriteFile(mediumDirectory, "one.bin", 20);
            WriteFile(mediumDirectory, "two.bin", 30);
            WriteFile(largeDirectory, "one.bin", 40);
            WriteFile(largeDirectory, "two.bin", 50);
            WriteFile(largeDirectory, "three.bin", 60);
            WriteFile(reparseTarget, "must-not-be-counted.bin", 500);

            reparsePointCreated = TryCreateDirectorySymbolicLink(
                reparsePath,
                reparseTarget);

            var provider =
                new WindowsMachineFolderInspectionProvider();

            var snapshot = await provider
                .GetLargestTopLevelFoldersFromDirectoryAsync(
                    scanRoot,
                    count: 3,
                    timeBudget: TimeSpan.FromSeconds(5));

            Assert.Equal(Path.GetFullPath(scanRoot), snapshot.RootPath);
            Assert.True(snapshot.IsComplete);
            Assert.Equal(0, snapshot.SkippedDirectoryCount);
            Assert.NotEqual(default, snapshot.CapturedAt);
            Assert.Collection(
                snapshot.Folders,
                folder =>
                {
                    Assert.Equal(
                        Path.GetFullPath(largeDirectory),
                        folder.Path);
                    Assert.Equal(150, folder.SizeBytes);
                    Assert.Equal(3, folder.FileCount);
                    Assert.True(folder.IsComplete);
                },
                folder =>
                {
                    Assert.Equal(
                        Path.GetFullPath(mediumDirectory),
                        folder.Path);
                    Assert.Equal(50, folder.SizeBytes);
                    Assert.Equal(2, folder.FileCount);
                    Assert.True(folder.IsComplete);
                },
                folder =>
                {
                    Assert.Equal(
                        Path.GetFullPath(smallDirectory),
                        folder.Path);
                    Assert.Equal(10, folder.SizeBytes);
                    Assert.Equal(1, folder.FileCount);
                    Assert.True(folder.IsComplete);
                });

            var limitedSnapshot = await provider
                .GetLargestTopLevelFoldersFromDirectoryAsync(
                    scanRoot,
                    count: 2,
                    timeBudget: TimeSpan.FromSeconds(5));

            Assert.Equal(2, limitedSnapshot.Folders.Count);
            Assert.Equal(
                Path.GetFullPath(largeDirectory),
                limitedSnapshot.Folders[0].Path);
            Assert.Equal(
                Path.GetFullPath(mediumDirectory),
                limitedSnapshot.Folders[1].Path);

            if (reparsePointCreated)
            {
                Assert.True(
                    WindowsMachineFolderInspectionProvider
                        .IsReparsePoint(
                            File.GetAttributes(reparsePath)));
            }
        }
        finally
        {
            DeleteTestFixture(
                fixtureDirectory,
                reparsePath,
                reparsePointCreated);
        }
    }

    [Fact]
    public void ReparsePointGuardRecognizesAttribute()
    {
        Assert.True(
            WindowsMachineFolderInspectionProvider.IsReparsePoint(
                FileAttributes.Directory |
                FileAttributes.ReparsePoint));
        Assert.False(
            WindowsMachineFolderInspectionProvider.IsReparsePoint(
                FileAttributes.Directory));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task InvalidCountThrows(int count)
    {
        var provider =
            new WindowsMachineFolderInspectionProvider();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => provider
                .GetLargestTopLevelFoldersAsync(
                    GetSystemDriveRoot(),
                    count,
                    TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task InvalidTimeBudgetsThrow()
    {
        var provider =
            new WindowsMachineFolderInspectionProvider();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => provider
                .GetLargestTopLevelFoldersAsync(
                    GetSystemDriveRoot(),
                    count: 1,
                    timeBudget: TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => provider
                .GetLargestTopLevelFoldersAsync(
                    GetSystemDriveRoot(),
                    count: 1,
                    timeBudget: TimeSpan.FromMinutes(2) +
                        TimeSpan.FromTicks(1)));
    }

    [Fact]
    public async Task PublicMethodRejectsNonDriveRoot()
    {
        var provider =
            new WindowsMachineFolderInspectionProvider();
        var nonRootPath = Path.GetFullPath(Path.GetTempPath());

        Assert.False(string.Equals(
            nonRootPath,
            Path.GetPathRoot(nonRootPath),
            StringComparison.OrdinalIgnoreCase));
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GetLargestTopLevelFoldersAsync(
                nonRootPath,
                count: 1,
                timeBudget: TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task PreCancelledTokenThrows()
    {
        var provider =
            new WindowsMachineFolderInspectionProvider();
        using var cancellationTokenSource =
            new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider
                .GetLargestTopLevelFoldersAsync(
                    GetSystemDriveRoot(),
                    count: 1,
                    timeBudget: TimeSpan.FromSeconds(1),
                    cancellationTokenSource.Token));
    }

    private static string GetSystemDriveRoot() =>
        Path.GetPathRoot(Environment.SystemDirectory) ??
        throw new InvalidOperationException(
            "The Windows system drive root is unavailable.");

    private static void WriteFile(
        string directory,
        string fileName,
        int size) =>
        File.WriteAllBytes(
            Path.Combine(directory, fileName),
            new byte[size]);

    private static bool TryCreateDirectorySymbolicLink(
        string linkPath,
        string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception)
            when (exception is IOException or
                UnauthorizedAccessException or
                NotSupportedException or
                SecurityException)
        {
            return false;
        }
    }

    private static void DeleteTestFixture(
        string fixtureDirectory,
        string reparsePath,
        bool reparsePointCreated)
    {
        if (reparsePointCreated && Directory.Exists(reparsePath))
        {
            try
            {
                Directory.Delete(reparsePath);
            }
            catch (Exception exception)
                when (exception is IOException or
                    UnauthorizedAccessException)
            {
            }
        }

        if (Directory.Exists(fixtureDirectory))
        {
            Directory.Delete(
                fixtureDirectory,
                recursive: true);
        }
    }
}
