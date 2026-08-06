using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security;
using Machine.Core;

[assembly: InternalsVisibleTo("Machine.Tests")]

namespace Machine.Windows;

public sealed class WindowsMachineFolderInspectionProvider
    : IMachineFolderInspectionProvider
{
    private static readonly TimeSpan MaximumTimeBudget =
        TimeSpan.FromMinutes(2);

    private static readonly string[] PriorityDirectoryNames =
    [
        "Users",
        "Program Files",
        "Program Files (x86)",
        "ProgramData",
        "Windows"
    ];

    private int _scanInProgress;

    public Task<MachineFolderInspectionSnapshot>
        GetLargestTopLevelFoldersAsync(
            string rootPath,
            int count,
            TimeSpan timeBudget,
            CancellationToken cancellationToken = default)
    {
        ValidateArguments(rootPath, count, timeBudget);
        cancellationToken.ThrowIfCancellationRequested();

        return RunExclusiveAsync(
            () =>
            {
                var location = ValidateScanLocation(
                    rootPath,
                    requireDriveRoot: true);

                return Scan(
                    location.ScanRoot,
                    location.VolumeRoot,
                    count,
                    timeBudget,
                    cancellationToken);
            },
            cancellationToken);
    }

    internal Task<MachineFolderInspectionSnapshot>
        GetLargestTopLevelFoldersFromDirectoryAsync(
            string rootPath,
            int count,
            TimeSpan timeBudget,
            CancellationToken cancellationToken = default)
    {
        ValidateArguments(rootPath, count, timeBudget);
        cancellationToken.ThrowIfCancellationRequested();

        return RunExclusiveAsync(
            () =>
            {
                var location = ValidateScanLocation(
                    rootPath,
                    requireDriveRoot: false);

                return Scan(
                    location.ScanRoot,
                    location.VolumeRoot,
                    count,
                    timeBudget,
                    cancellationToken);
            },
            cancellationToken);
    }

    internal static bool IsReparsePoint(
        FileAttributes attributes) =>
        (attributes & FileAttributes.ReparsePoint) != 0;

    private async Task<MachineFolderInspectionSnapshot>
        RunExclusiveAsync(
            Func<MachineFolderInspectionSnapshot> scan,
            CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(
                ref _scanInProgress,
                1,
                0) != 0)
        {
            throw new InvalidOperationException(
                "A folder inspection is already running.");
        }

        try
        {
            return await Task.Run(
                scan,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _scanInProgress, 0);
        }
    }

    private static MachineFolderInspectionSnapshot Scan(
        string scanRoot,
        string volumeRoot,
        int count,
        TimeSpan timeBudget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var candidateResult = EnumerateCandidates(
            scanRoot,
            volumeRoot,
            stopwatch,
            timeBudget,
            cancellationToken);
        var folderSnapshots = new List<MachineFolderSizeSnapshot>();
        var skippedDirectoryCount =
            candidateResult.SkippedDirectoryCount;
        var isComplete = candidateResult.IsComplete;

        foreach (var candidate in candidateResult.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (HasTimeBudgetExpired(stopwatch, timeBudget))
            {
                isComplete = false;
                break;
            }

            var measurement = MeasureFolder(
                candidate,
                volumeRoot,
                stopwatch,
                timeBudget,
                cancellationToken);

            skippedDirectoryCount +=
                measurement.SkippedDirectoryCount;
            isComplete &= measurement.IsComplete;

            if (measurement.Snapshot is not null)
            {
                folderSnapshots.Add(measurement.Snapshot);
            }

            if (measurement.TimeBudgetExpired)
            {
                isComplete = false;
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var orderedFolders = folderSnapshots
            .OrderByDescending(folder => folder.SizeBytes)
            .ThenByDescending(folder => folder.IsComplete)
            .ThenBy(
                folder => folder.Path,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                folder => folder.Path,
                StringComparer.Ordinal)
            .Take(count)
            .ToArray();

        return new MachineFolderInspectionSnapshot(
            RootPath: scanRoot,
            Folders: orderedFolders,
            IsComplete: isComplete,
            SkippedDirectoryCount: skippedDirectoryCount,
            CapturedAt: DateTimeOffset.UtcNow);
    }

    private static CandidateEnumerationResult EnumerateCandidates(
        string scanRoot,
        string volumeRoot,
        Stopwatch stopwatch,
        TimeSpan timeBudget,
        CancellationToken cancellationToken)
    {
        var candidates = new List<DirectoryInfo>();
        var skippedDirectoryCount = 0;
        var isComplete = true;

        try
        {
            foreach (var directory in
                new DirectoryInfo(scanRoot).EnumerateDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (HasTimeBudgetExpired(stopwatch, timeBudget))
                {
                    isComplete = false;
                    break;
                }

                try
                {
                    if (IsReparsePoint(directory.Attributes) ||
                        !IsOnVolume(
                            directory.FullName,
                            volumeRoot))
                    {
                        continue;
                    }

                    candidates.Add(directory);
                }
                catch (Exception exception)
                    when (IsFileSystemAccessException(exception))
                {
                    skippedDirectoryCount++;
                    isComplete = false;
                }
            }
        }
        catch (Exception exception)
            when (IsFileSystemAccessException(exception))
        {
            skippedDirectoryCount++;
            isComplete = false;
        }

        var orderedCandidates = candidates
            .OrderBy(directory => GetPriority(directory.Name))
            .ThenBy(
                directory => directory.Name,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                directory => directory.FullName,
                StringComparer.Ordinal)
            .ToArray();

        return new CandidateEnumerationResult(
            Candidates: orderedCandidates,
            IsComplete: isComplete,
            SkippedDirectoryCount: skippedDirectoryCount);
    }

    private static FolderMeasurement MeasureFolder(
        DirectoryInfo topLevelDirectory,
        string volumeRoot,
        Stopwatch stopwatch,
        TimeSpan timeBudget,
        CancellationToken cancellationToken)
    {
        var directories = new Stack<DirectoryInfo>();
        directories.Push(topLevelDirectory);

        long sizeBytes = 0;
        long fileCount = 0;
        var skippedDirectoryCount = 0;
        var isComplete = true;
        var topLevelDirectoryWasReadable = false;
        var timeBudgetExpired = false;

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (HasTimeBudgetExpired(stopwatch, timeBudget))
            {
                isComplete = false;
                timeBudgetExpired = true;
                break;
            }

            var currentDirectory = directories.Pop();

            try
            {
                currentDirectory.Refresh();

                if (IsReparsePoint(currentDirectory.Attributes) ||
                    !IsOnVolume(
                        currentDirectory.FullName,
                        volumeRoot))
                {
                    continue;
                }
            }
            catch (Exception exception)
                when (IsFileSystemAccessException(exception))
            {
                isComplete = false;
                skippedDirectoryCount++;
                continue;
            }

            var isTopLevelDirectory = string.Equals(
                currentDirectory.FullName,
                topLevelDirectory.FullName,
                StringComparison.OrdinalIgnoreCase);

            try
            {
                foreach (var entry in
                    currentDirectory.EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (isTopLevelDirectory)
                    {
                        topLevelDirectoryWasReadable = true;
                    }

                    if (HasTimeBudgetExpired(
                            stopwatch,
                            timeBudget))
                    {
                        isComplete = false;
                        timeBudgetExpired = true;
                        break;
                    }

                    try
                    {
                        var attributes = entry.Attributes;

                        if (IsReparsePoint(attributes) ||
                            !IsOnVolume(entry.FullName, volumeRoot))
                        {
                            continue;
                        }

                        if (entry is DirectoryInfo directory)
                        {
                            directories.Push(directory);
                            continue;
                        }

                        if (entry is not FileInfo file)
                        {
                            continue;
                        }

                        var fileLength = file.Length;
                        if (fileLength < 0)
                        {
                            isComplete = false;
                            continue;
                        }

                        sizeBytes += fileLength;
                        fileCount++;
                    }
                    catch (Exception exception)
                        when (IsFileSystemAccessException(exception))
                    {
                        isComplete = false;

                        if (entry is DirectoryInfo)
                        {
                            skippedDirectoryCount++;
                        }
                    }
                }

                if (isTopLevelDirectory)
                {
                    topLevelDirectoryWasReadable = true;
                }
            }
            catch (Exception exception)
                when (IsFileSystemAccessException(exception))
            {
                isComplete = false;
                skippedDirectoryCount++;
            }

            if (timeBudgetExpired)
            {
                break;
            }
        }

        var snapshot = topLevelDirectoryWasReadable ||
            timeBudgetExpired
                ? new MachineFolderSizeSnapshot(
                    Path: topLevelDirectory.FullName,
                    SizeBytes: sizeBytes,
                    FileCount: fileCount,
                    IsComplete: isComplete)
                : null;

        return new FolderMeasurement(
            Snapshot: snapshot,
            IsComplete: isComplete,
            TimeBudgetExpired: timeBudgetExpired,
            SkippedDirectoryCount: skippedDirectoryCount);
    }

    private static ScanLocation ValidateScanLocation(
        string rootPath,
        bool requireDriveRoot)
    {
        string fullPath;
        string volumeRoot;

        try
        {
            fullPath = Path.GetFullPath(rootPath);
            volumeRoot = Path.GetPathRoot(fullPath) ??
                throw new ArgumentException(
                    "The path has no drive root.",
                    nameof(rootPath));
            volumeRoot = Path.GetFullPath(volumeRoot);
        }
        catch (Exception exception)
            when (IsInvalidPathException(exception))
        {
            throw new ArgumentException(
                "The folder inspection path is invalid.",
                nameof(rootPath),
                exception);
        }

        if (fullPath.StartsWith(
                @"\\",
                StringComparison.Ordinal) ||
            (requireDriveRoot &&
             !string.Equals(
                 fullPath,
                 volumeRoot,
                 StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                requireDriveRoot
                    ? "The path must resolve to a local drive root."
                    : "The test scan path must be local.",
                nameof(rootPath));
        }

        try
        {
            var drive = new DriveInfo(volumeRoot);

            if (drive.DriveType == DriveType.Network ||
                !drive.IsReady ||
                drive.TotalSize <= 0 ||
                !Directory.Exists(fullPath))
            {
                throw new ArgumentException(
                    "The path must be on an existing ready local drive.",
                    nameof(rootPath));
            }
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception exception)
            when (IsFileSystemAccessException(exception))
        {
            throw new ArgumentException(
                "The path must be on an existing ready local drive.",
                nameof(rootPath),
                exception);
        }

        return new ScanLocation(
            ScanRoot: fullPath,
            VolumeRoot: volumeRoot);
    }

    private static void ValidateArguments(
        string rootPath,
        int count,
        TimeSpan timeBudget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        if (count is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (timeBudget <= TimeSpan.Zero ||
            timeBudget > MaximumTimeBudget)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeBudget));
        }
    }

    private static bool IsOnVolume(
        string path,
        string volumeRoot)
    {
        var pathRoot = Path.GetPathRoot(path);

        return pathRoot is not null &&
            string.Equals(
                Path.GetFullPath(pathRoot),
                volumeRoot,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasTimeBudgetExpired(
        Stopwatch stopwatch,
        TimeSpan timeBudget) =>
        stopwatch.Elapsed >= timeBudget;

    private static int GetPriority(string directoryName)
    {
        for (var index = 0;
             index < PriorityDirectoryNames.Length;
             index++)
        {
            if (string.Equals(
                    directoryName,
                    PriorityDirectoryNames[index],
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return PriorityDirectoryNames.Length;
    }

    private static bool IsInvalidPathException(
        Exception exception) =>
        exception is ArgumentException or
            IOException or
            NotSupportedException or
            SecurityException;

    private static bool IsFileSystemAccessException(
        Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            NotSupportedException or
            SecurityException;

    private readonly record struct ScanLocation(
        string ScanRoot,
        string VolumeRoot);

    private readonly record struct CandidateEnumerationResult(
        IReadOnlyList<DirectoryInfo> Candidates,
        bool IsComplete,
        int SkippedDirectoryCount);

    private readonly record struct FolderMeasurement(
        MachineFolderSizeSnapshot? Snapshot,
        bool IsComplete,
        bool TimeBudgetExpired,
        int SkippedDirectoryCount);
}
