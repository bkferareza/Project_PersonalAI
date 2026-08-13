using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Machine.Core;
using Machine.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace Machine.App.Features;

public sealed partial class StorageView
{
    private const int LargeFolderResultCount = 10;
    private const string UnavailableValue = "Unavailable";
    private const double BytesPerMebibyte = 1024d * 1024d;
    private const double BytesPerGibibyte = 1024d * 1024d * 1024d;
    private const double BytesPerTebibyte =
        1024d * 1024d * 1024d * 1024d;
    private static readonly TimeSpan LargeFolderScanTimeBudget =
        TimeSpan.FromSeconds(30);

    private IMachineStorageProvider? _storageProvider;
    private IMachineFolderInspectionProvider? _folderInspectionProvider;
    private CancellationToken _lifetimeCancellationToken;
    private Action? _onSnapshotChanged;
    private CancellationTokenSource? _folderScanCancellationTokenSource;
    private MachineStorageSnapshot? _latestStorageSnapshot;
    private MachineFolderInspectionSnapshot?
        _latestFolderInspectionSnapshot;
    private bool _isFolderScanRunning;
    private bool _isStorageRequestRunning;

    internal MachineStorageSnapshot? LatestStorageSnapshot =>
        _latestStorageSnapshot;

    internal MachineFolderInspectionSnapshot?
        LatestFolderInspectionSnapshot => _latestFolderInspectionSnapshot;

    internal void Initialize(
        IMachineStorageProvider storageProvider,
        IMachineFolderInspectionProvider folderInspectionProvider,
        CancellationToken lifetimeCancellationToken,
        Action onSnapshotChanged)
    {
        ArgumentNullException.ThrowIfNull(storageProvider);
        ArgumentNullException.ThrowIfNull(folderInspectionProvider);
        ArgumentNullException.ThrowIfNull(onSnapshotChanged);
        _storageProvider = storageProvider;
        _folderInspectionProvider = folderInspectionProvider;
        _lifetimeCancellationToken = lifetimeCancellationToken;
        _onSnapshotChanged = onSnapshotChanged;
    }

    internal void Stop() => _folderScanCancellationTokenSource?.Cancel();

    private static bool StorageRootsMatch(
        string first,
        string second) =>
        string.Equals(
            first.Trim().TrimEnd('\\', '/'),
            second.Trim().TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    private static string GetFolderName(string path)
    {
        var trimmedPath = path.Trim()
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        var folderName = Path.GetFileName(trimmedPath);

        return string.IsNullOrWhiteSpace(folderName)
            ? path.Trim()
            : folderName;
    }

    private async void OnRefreshStorageClicked(
        object sender,
        RoutedEventArgs e)
    {
        await LoadAsync(
            isManualRefresh: true,
            cancellationToken: _lifetimeCancellationToken);
    }

    internal async Task LoadAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken)
    {
        if (_isStorageRequestRunning || _storageProvider is null)
        {
            return;
        }

        _isStorageRequestRunning = true;
        UpdateRefreshStorageButtonState();

        if (isManualRefresh)
        {
            RefreshStorageButton.Content = "Refreshing...";
            await Task.Yield();
        }

        try
        {
            var snapshot = await _storageProvider.GetAsync(
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            UpdateStorageOverview(snapshot);
            _latestStorageSnapshot = snapshot;
            _onSnapshotChanged?.Invoke();
            UpdateLargeFolderScanButtonState();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            if (_latestStorageSnapshot is null)
            {
                SystemStorageSummaryText.Text =
                    "System volume unavailable";
                StorageVolumesList.ItemsSource =
                    Array.Empty<StorageVolumeDisplayItem>();
            }

            StorageStatusText.Text =
                "Storage information is temporarily unavailable.";
        }
        finally
        {
            _isStorageRequestRunning = false;

            if (!cancellationToken.IsCancellationRequested)
            {
                RefreshStorageButton.Content = "Refresh storage";
                UpdateRefreshStorageButtonState();
            }
        }
    }

    private void UpdateStorageOverview(
        MachineStorageSnapshot snapshot)
    {
        var displayItems = snapshot.Volumes
            .Select(CreateStorageVolumeDisplayItem)
            .ToArray();

        StorageVolumesList.ItemsSource = displayItems;

        var systemVolume = snapshot.Volumes
            .FirstOrDefault(volume => volume.IsSystemVolume);

        SystemStorageSummaryText.Text = systemVolume is null
            ? "System volume unavailable"
            : $"{systemVolume.RootPath} · " +
                $"{FormatBytes(systemVolume.AvailableFreeSpaceBytes)} " +
                $"free of {FormatBytes(systemVolume.TotalSizeBytes)}";

        StorageStatusText.Text = displayItems.Length == 0
            ? "No readable storage volumes found."
            : string.Empty;
    }

    private static StorageVolumeDisplayItem
        CreateStorageVolumeDisplayItem(
            MachineStorageVolumeSnapshot volume)
    {
        var label = string.IsNullOrWhiteSpace(volume.VolumeLabel)
            ? "No label"
            : volume.VolumeLabel;
        var fileSystem = string.IsNullOrWhiteSpace(volume.FileSystem)
            ? UnavailableValue
            : volume.FileSystem;
        var usedBytes = Math.Max(
            0L,
            volume.TotalSizeBytes - volume.AvailableFreeSpaceBytes);
        var header = volume.IsSystemVolume
            ? $"{volume.RootPath} · System volume"
            : volume.RootPath;

        return new StorageVolumeDisplayItem(
            header,
            $"{label} · {fileSystem}",
            $"{FormatBytes(usedBytes)} used · " +
            $"{FormatBytes(volume.AvailableFreeSpaceBytes)} free · " +
            $"{FormatBytes(volume.TotalSizeBytes)} total");
    }

    private void UpdateRefreshStorageButtonState()
    {
        RefreshStorageButton.IsEnabled =
            !_isStorageRequestRunning &&
            !_lifetimeCancellationToken.IsCancellationRequested;
    }

    private async void OnScanLargeFoldersClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (_isFolderScanRunning)
        {
            return;
        }

        var rootPath = GetSystemStorageRoot();
        if (rootPath is null)
        {
            UpdateLargeFolderScanButtonState();
            return;
        }

        var scanCancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellationToken);
        _folderScanCancellationTokenSource =
            scanCancellationTokenSource;
        _isFolderScanRunning = true;

        ScanLargeFoldersButton.Content = "Scanning...";
        UpdateLargeFolderScanButtonState();
        CancelLargeFolderScanButton.IsEnabled = true;
        LargeFolderScanProgressRing.Visibility =
            Visibility.Visible;
        LargeFolderScanProgressRing.IsActive = true;
        LargeFolderRootText.Text =
            $"Largest folders on {rootPath}";
        LargeFolderScanStatusText.Text =
            $"Scanning {rootPath} for up to 30 seconds...";

        var cancellationToken =
            scanCancellationTokenSource.Token;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (_folderInspectionProvider is null)
            {
                return;
            }
            var snapshot = await _folderInspectionProvider
                .GetLargestTopLevelFoldersAsync(
                    rootPath,
                    LargeFolderResultCount,
                    LargeFolderScanTimeBudget,
                    cancellationToken);

            stopwatch.Stop();
            cancellationToken.ThrowIfCancellationRequested();

            _latestFolderInspectionSnapshot = snapshot;
            UpdateLargeFolderResults(
                snapshot,
                stopwatch.Elapsed >= LargeFolderScanTimeBudget);
            _onSnapshotChanged?.Invoke();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            if (!_lifetimeCancellationToken.IsCancellationRequested)
            {
                LargeFolderScanStatusText.Text =
                    "Folder scan cancelled.";
            }
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            if (cancellationToken.IsCancellationRequested)
            {
                if (!_lifetimeCancellationToken.IsCancellationRequested)
                {
                    LargeFolderScanStatusText.Text =
                        "Folder scan cancelled.";
                }
            }
            else
            {
                Debug.WriteLine(exception);
                LargeFolderScanStatusText.Text =
                    "Large-folder inspection is temporarily unavailable.";
            }
        }
        finally
        {
            stopwatch.Stop();
            _isFolderScanRunning = false;

            if (ReferenceEquals(
                _folderScanCancellationTokenSource,
                scanCancellationTokenSource))
            {
                _folderScanCancellationTokenSource = null;
            }

            scanCancellationTokenSource.Dispose();

            if (!_lifetimeCancellationToken.IsCancellationRequested)
            {
                ScanLargeFoldersButton.Content =
                    "Scan large folders";
                CancelLargeFolderScanButton.IsEnabled = false;
                LargeFolderScanProgressRing.IsActive = false;
                LargeFolderScanProgressRing.Visibility =
                    Visibility.Collapsed;
                UpdateLargeFolderScanButtonState();
            }
        }
    }

    private void OnCancelLargeFolderScanClicked(
        object sender,
        RoutedEventArgs e)
    {
        CancelLargeFolderScanButton.IsEnabled = false;
        _folderScanCancellationTokenSource?.Cancel();
    }

    private void UpdateLargeFolderResults(
        MachineFolderInspectionSnapshot snapshot,
        bool timeLimitReached)
    {
        var displayItems = snapshot.Folders
            .Select(folder => new LargeFolderDisplayItem(
                folder.Path,
                $"{FormatBytes(folder.SizeBytes)} · " +
                $"{folder.FileCount.ToString("N0", CultureInfo.InvariantCulture)} files · " +
                (folder.IsComplete
                    ? "Complete"
                    : "Partial")))
            .ToArray();

        LargeFoldersList.ItemsSource = displayItems;

        var hasPartialResults =
            !snapshot.IsComplete ||
            snapshot.SkippedDirectoryCount > 0 ||
            snapshot.Folders.Any(folder => !folder.IsComplete);

        LargeFolderScanStatusText.Text =
            timeLimitReached && hasPartialResults
                ? $"Partial scan · Time limit reached · " +
                    $"{snapshot.SkippedDirectoryCount} inaccessible directories skipped"
                : displayItems.Length == 0
                    ? "No readable top-level folders found."
                    : !hasPartialResults
                        ? $"Scan complete · {displayItems.Length} folders measured"
                        : snapshot.SkippedDirectoryCount > 0
                            ? $"Partial scan · " +
                                $"{snapshot.SkippedDirectoryCount} inaccessible directories skipped"
                            : "Partial scan";
    }

    private string? GetSystemStorageRoot() =>
        _latestStorageSnapshot?.Volumes
            .FirstOrDefault(volume => volume.IsSystemVolume)
            ?.RootPath;

    private void UpdateLargeFolderScanButtonState()
    {
        var rootPath = GetSystemStorageRoot();

        if (!_isFolderScanRunning)
        {
            LargeFolderRootText.Text = rootPath is null
                ? "Largest folders on system volume"
                : $"Largest folders on {rootPath}";
        }

        ScanLargeFoldersButton.IsEnabled =
            rootPath is not null &&
            !_isFolderScanRunning &&
            !_lifetimeCancellationToken.IsCancellationRequested;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= BytesPerTebibyte)
        {
            return $"{bytes / BytesPerTebibyte:F1} TB";
        }
        if (bytes >= BytesPerGibibyte)
        {
            return $"{bytes / BytesPerGibibyte:F1} GB";
        }
        if (bytes >= BytesPerMebibyte)
        {
            return $"{bytes / BytesPerMebibyte:F1} MB";
        }
        return $"{bytes} B";
    }
}
