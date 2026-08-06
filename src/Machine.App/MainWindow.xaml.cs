using System.Diagnostics;
using System.Globalization;
using Machine.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Machine.App;

public sealed partial class MainWindow : Window
{
    private const int CompactWindowWidth = 400;
    private const int CompactWindowHeight = 200;
    private const int ExpandedWindowWidth = 520;
    private const int ExpandedWindowHeight = 760;
    private const int WorkAreaMargin = 16;
    private const int TopProcessCount = 5;
    private const int LargeFolderResultCount = 10;
    private const string UnavailableValue = "Unavailable";
    private const double BytesPerMebibyte =
        1024d * 1024d;
    private const double BytesPerGibibyte =
        1024d * 1024d * 1024d;
    private const double BytesPerTebibyte =
        1024d * 1024d * 1024d * 1024d;

    private static readonly TimeSpan TelemetryRefreshInterval =
        TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProcessRefreshInterval =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OllamaRefreshInterval =
        TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LargeFolderScanTimeBudget =
        TimeSpan.FromSeconds(30);

    private readonly IMachineIdentityProvider _identityProvider;
    private readonly IMachineResourceProvider _resourceProvider;
    private readonly IMachineProcessProvider _processProvider;
    private readonly IOllamaStatusProvider _ollamaStatusProvider;
    private readonly IMachineStateExplainer _machineStateExplainer;
    private readonly IMachineStorageProvider _storageProvider;
    private readonly IMachineFolderInspectionProvider
        _folderInspectionProvider;
    private readonly IMachineSoftwareInventoryProvider
        _softwareInventoryProvider;
    private readonly CancellationTokenSource
        _windowCancellationTokenSource = new();
    private CancellationTokenSource?
        _folderScanCancellationTokenSource;
    private MachineIdentity? _latestIdentity;
    private MachineResourceSnapshot? _latestResourceSnapshot;
    private IReadOnlyList<MachineProcessSnapshot>
        _latestProcessSnapshots =
            Array.Empty<MachineProcessSnapshot>();
    private MachineStorageSnapshot? _latestStorageSnapshot;
    private MachineSoftwareInventorySnapshot?
        _latestSoftwareInventorySnapshot;
    private bool _contentLoadStarted;
    private bool _detailsExpanded;
    private bool _hasSuccessfulExplanation;
    private bool _isOllamaServiceAvailable;
    private bool _isExplanationRequestRunning;
    private bool _isFolderScanRunning;
    private bool _isStorageRequestRunning;
    private bool _isSoftwareInventoryRequestRunning;

    public MainWindow(
        IMachineIdentityProvider identityProvider,
        IMachineResourceProvider resourceProvider,
        IMachineProcessProvider processProvider,
        IOllamaStatusProvider ollamaStatusProvider,
        IMachineStateExplainer machineStateExplainer,
        IMachineStorageProvider storageProvider,
        IMachineFolderInspectionProvider folderInspectionProvider,
        IMachineSoftwareInventoryProvider softwareInventoryProvider)
    {
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(resourceProvider);
        ArgumentNullException.ThrowIfNull(processProvider);
        ArgumentNullException.ThrowIfNull(ollamaStatusProvider);
        ArgumentNullException.ThrowIfNull(machineStateExplainer);
        ArgumentNullException.ThrowIfNull(storageProvider);
        ArgumentNullException.ThrowIfNull(folderInspectionProvider);
        ArgumentNullException.ThrowIfNull(softwareInventoryProvider);

        _identityProvider = identityProvider;
        _resourceProvider = resourceProvider;
        _processProvider = processProvider;
        _ollamaStatusProvider = ollamaStatusProvider;
        _machineStateExplainer = machineStateExplainer;
        _storageProvider = storageProvider;
        _folderInspectionProvider = folderInspectionProvider;
        _softwareInventoryProvider = softwareInventoryProvider;

        InitializeComponent();
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;
    }

    private void OnWindowActivated(
        object sender,
        WindowActivatedEventArgs args)
    {
        Activated -= OnWindowActivated;

        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsMaximizable = false;
                presenter.IsResizable = false;
                presenter.IsMinimizable = true;
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }

        ResizeAndPositionWindow(
            CompactWindowWidth,
            CompactWindowHeight);
    }

    private async void OnMainContentLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_contentLoadStarted)
        {
            return;
        }

        _contentLoadStarted = true;

        try
        {
            await LoadIdentityAsync();

            var cancellationToken =
                _windowCancellationTokenSource.Token;

            await Task.WhenAll(
                RunTelemetryLoopAsync(cancellationToken),
                RunProcessLoopAsync(cancellationToken),
                RunOllamaStatusLoopAsync(cancellationToken),
                LoadStorageAsync(
                    isManualRefresh: false,
                    cancellationToken: cancellationToken),
                LoadSoftwareInventoryAsync(
                    isManualRefresh: false,
                    cancellationToken: cancellationToken));
        }
        finally
        {
            _windowCancellationTokenSource.Dispose();
        }
    }

    private async Task LoadIdentityAsync()
    {
        try
        {
            var identity = await _identityProvider.GetAsync();

            _latestIdentity = identity;

            DeviceNameText.Text = identity.DeviceName;
            OperatingSystemText.Text = identity.OperatingSystem;
            ArchitectureText.Text = identity.Architecture;
            LoadStatusText.Text = string.Empty;
            UpdateExplainMachineStateButtonState();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            DeviceNameText.Text = UnavailableValue;
            OperatingSystemText.Text = UnavailableValue;
            ArchitectureText.Text = UnavailableValue;
            LoadStatusText.Text = "Machine identity could not be loaded.";
        }
    }

    private async Task RunTelemetryLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await RefreshTelemetryAsync(cancellationToken);
                await Task.Delay(
                    TelemetryRefreshInterval,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshTelemetryAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _resourceProvider.GetAsync(
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            _latestResourceSnapshot = snapshot;

            CpuUsageText.Text =
                $"{snapshot.CpuUsagePercent:F1}%";

            var usedMemory =
                snapshot.UsedMemoryBytes / BytesPerGibibyte;
            var totalMemory =
                snapshot.TotalMemoryBytes / BytesPerGibibyte;

            MemoryUsageText.Text =
                $"{usedMemory:F1} GB / {totalMemory:F1} GB";
            TelemetryStatusText.Text = string.Empty;

            PresenceTelemetryText.Text =
                $"CPU {snapshot.CpuUsagePercent:F1}% · " +
                $"Memory {usedMemory:F1} / {totalMemory:F1} GB";

            var memoryUsagePercent =
                snapshot.TotalMemoryBytes == 0
                    ? 100d
                    : snapshot.UsedMemoryBytes /
                        (double)snapshot.TotalMemoryBytes *
                        100d;

            UpdatePresenceState(
                snapshot.CpuUsagePercent,
                memoryUsagePercent);
            UpdateExplainMachineStateButtonState();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            CpuUsageText.Text = UnavailableValue;
            MemoryUsageText.Text = UnavailableValue;
            TelemetryStatusText.Text =
                "Resource telemetry could not be loaded.";
            PresenceStateText.Text = "Status unavailable";
            PresenceTelemetryText.Text =
                "CPU unavailable · Memory unavailable";
            PresenceIndicator.Fill =
                new SolidColorBrush(Colors.Gray);
        }
    }

    private void UpdatePresenceState(
        double cpuUsagePercent,
        double memoryUsagePercent)
    {
        if (cpuUsagePercent >= 90d ||
            memoryUsagePercent >= 90d)
        {
            PresenceStateText.Text = "Under pressure";
            PresenceIndicator.Fill =
                new SolidColorBrush(Colors.Red);
        }
        else if (cpuUsagePercent >= 70d ||
                 memoryUsagePercent >= 80d)
        {
            PresenceStateText.Text = "Busy";
            PresenceIndicator.Fill =
                new SolidColorBrush(Colors.Orange);
        }
        else
        {
            PresenceStateText.Text = "Stable";
            PresenceIndicator.Fill =
                new SolidColorBrush(Colors.Green);
        }
    }

    private async Task RunProcessLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await RefreshProcessesAsync(cancellationToken);
                await Task.Delay(
                    ProcessRefreshInterval,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshProcessesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshots = await _processProvider.GetTopAsync(
                TopProcessCount,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var verifiedSnapshots = snapshots.ToArray();
            _latestProcessSnapshots = verifiedSnapshots;

            TopProcessesList.ItemsSource = verifiedSnapshots
                .Select(snapshot => new ProcessDisplayItem(
                    snapshot.Name,
                    $"PID {snapshot.ProcessId} · " +
                    $"{snapshot.CpuUsagePercent:F1}% CPU · " +
                    FormatBytes(snapshot.WorkingSetBytes)))
                .ToArray();
            ProcessStatusText.Text = string.Empty;
            UpdateExplainMachineStateButtonState();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            ProcessStatusText.Text =
                "Process information is temporarily unavailable.";
        }
    }

    private async Task RunOllamaStatusLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await RefreshOllamaStatusAsync(cancellationToken);
                await Task.Delay(
                    OllamaRefreshInterval,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshOllamaStatusAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _ollamaStatusProvider.GetStatusAsync(
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            UpdateOllamaStatus(snapshot);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            ShowOllamaOffline();
        }
    }

    private void UpdateOllamaStatus(
        OllamaStatusSnapshot snapshot)
    {
        if (!snapshot.IsServiceAvailable)
        {
            ShowOllamaOffline();
            return;
        }

        _isOllamaServiceAvailable = true;
        OllamaServiceStatusText.Text = "Online";
        OllamaVersionText.Text = string.IsNullOrWhiteSpace(
            snapshot.Version)
            ? UnavailableValue
            : snapshot.Version;

        if (!snapshot.IsRunningModelStatusAvailable)
        {
            OllamaPresenceStatusText.Text =
                "Ollama online · Model status unavailable";
            ClearOllamaModels(
                "Loaded-model status is unavailable.");
            UpdateExplainMachineStateButtonState();
            return;
        }

        var displayItems = snapshot.RunningModels
            .Select(CreateOllamaModelDisplayItem)
            .ToArray();

        OllamaRunningModelsList.ItemsSource = displayItems;

        if (displayItems.Length == 0)
        {
            OllamaPresenceStatusText.Text =
                "Ollama online · No model loaded";
            OllamaLoadedModelsStatusText.Text =
                "No models currently loaded.";
            UpdateExplainMachineStateButtonState();
            return;
        }

        OllamaPresenceStatusText.Text = displayItems.Length == 1
            ? $"Ollama online · {displayItems[0].Name} loaded"
            : $"Ollama online · {displayItems.Length} models loaded";
        OllamaLoadedModelsStatusText.Text = string.Empty;
        UpdateExplainMachineStateButtonState();
    }

    private void ShowOllamaOffline()
    {
        _isOllamaServiceAvailable = false;
        OllamaPresenceStatusText.Text = "Ollama offline";
        OllamaServiceStatusText.Text = "Offline";
        OllamaVersionText.Text = UnavailableValue;
        ClearOllamaModels(
            "Loaded-model status is unavailable.");
        UpdateExplainMachineStateButtonState();
    }

    private void ClearOllamaModels(string status)
    {
        OllamaRunningModelsList.ItemsSource =
            Array.Empty<OllamaModelDisplayItem>();
        OllamaLoadedModelsStatusText.Text = status;
    }

    private static OllamaModelDisplayItem
        CreateOllamaModelDisplayItem(
            OllamaRunningModel model)
    {
        var parameterSize = string.IsNullOrWhiteSpace(
            model.ParameterSize)
            ? UnavailableValue
            : model.ParameterSize;
        var quantizationLevel = string.IsNullOrWhiteSpace(
            model.QuantizationLevel)
            ? UnavailableValue
            : model.QuantizationLevel;

        return new OllamaModelDisplayItem(
            model.Name,
            $"{parameterSize} · {quantizationLevel}",
            $"{FormatBytes(model.SizeVramBytes)} VRAM · " +
            $"{model.ContextLength.ToString("N0", CultureInfo.InvariantCulture)} context");
    }

    private async void OnExplainMachineStateClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (_isExplanationRequestRunning)
        {
            return;
        }

        var identity = _latestIdentity;
        var resources = _latestResourceSnapshot;
        var processSnapshots = _latestProcessSnapshots.ToArray();

        if (identity is null ||
            resources is null ||
            processSnapshots.Length == 0 ||
            !_isOllamaServiceAvailable)
        {
            UpdateExplainMachineStateButtonState();
            return;
        }

        _isExplanationRequestRunning = true;
        UpdateExplainMachineStateButtonState();
        ExplainMachineStateButton.Content = "Thinking...";
        MachineExplanationProgressRing.Visibility =
            Visibility.Visible;
        MachineExplanationProgressRing.IsActive = true;
        MachineExplanationStatusText.Text =
            "Reading the current machine state...";

        var cancellationToken =
            _windowCancellationTokenSource.Token;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var request = new MachineStateExplanationRequest(
                identity,
                resources,
                processSnapshots);
            var explanation =
                await _machineStateExplainer.ExplainAsync(
                    request,
                    cancellationToken);

            stopwatch.Stop();
            cancellationToken.ThrowIfCancellationRequested();

            MachineExplanationText.Text = explanation.Text;
            var elapsedSeconds =
                stopwatch.Elapsed.TotalSeconds.ToString(
                    "F1",
                    CultureInfo.InvariantCulture);
            MachineExplanationMetadataText.Text =
                $"Generated locally in {elapsedSeconds}s · " +
                explanation.Model;
            MachineExplanationMetadataText.Visibility =
                Visibility.Visible;
            MachineExplanationStatusText.Text = string.Empty;
            _hasSuccessfulExplanation = true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            Debug.WriteLine(exception);

            if (!_hasSuccessfulExplanation)
            {
                MachineExplanationMetadataText.Text = string.Empty;
                MachineExplanationMetadataText.Visibility =
                    Visibility.Collapsed;
            }

            MachineExplanationStatusText.Text =
                "Machine explanation is temporarily unavailable.";
        }
        finally
        {
            stopwatch.Stop();
            _isExplanationRequestRunning = false;
            if (!cancellationToken.IsCancellationRequested)
            {
                ExplainMachineStateButton.Content =
                    "Explain current state";
                MachineExplanationProgressRing.IsActive = false;
                MachineExplanationProgressRing.Visibility =
                    Visibility.Collapsed;
                UpdateExplainMachineStateButtonState();
            }
        }
    }

    private void UpdateExplainMachineStateButtonState()
    {
        ExplainMachineStateButton.IsEnabled =
            _latestIdentity is not null &&
            _latestResourceSnapshot is not null &&
            _latestProcessSnapshots.Count > 0 &&
            _isOllamaServiceAvailable &&
            !_isExplanationRequestRunning &&
            !_windowCancellationTokenSource.IsCancellationRequested;
    }

    private async void OnRefreshStorageClicked(
        object sender,
        RoutedEventArgs e)
    {
        await LoadStorageAsync(
            isManualRefresh: true,
            cancellationToken:
                _windowCancellationTokenSource.Token);
    }

    private async Task LoadStorageAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken)
    {
        if (_isStorageRequestRunning)
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
            !_windowCancellationTokenSource.IsCancellationRequested;
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
                _windowCancellationTokenSource.Token);
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
            var snapshot = await _folderInspectionProvider
                .GetLargestTopLevelFoldersAsync(
                    rootPath,
                    LargeFolderResultCount,
                    LargeFolderScanTimeBudget,
                    cancellationToken);

            stopwatch.Stop();
            cancellationToken.ThrowIfCancellationRequested();

            UpdateLargeFolderResults(
                snapshot,
                stopwatch.Elapsed >= LargeFolderScanTimeBudget);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            if (!_windowCancellationTokenSource
                .IsCancellationRequested)
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
                if (!_windowCancellationTokenSource
                    .IsCancellationRequested)
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

            if (!_windowCancellationTokenSource
                .IsCancellationRequested)
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
            !_windowCancellationTokenSource.IsCancellationRequested;
    }

    private async void OnRefreshSoftwareClicked(
        object sender,
        RoutedEventArgs e)
    {
        await LoadSoftwareInventoryAsync(
            isManualRefresh: true,
            cancellationToken:
                _windowCancellationTokenSource.Token);
    }

    private async Task LoadSoftwareInventoryAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken)
    {
        if (_isSoftwareInventoryRequestRunning)
        {
            return;
        }

        _isSoftwareInventoryRequestRunning = true;
        if (isManualRefresh)
        {
            RefreshSoftwareButton.Content = "Refreshing...";
        }

        UpdateRefreshSoftwareButtonState();

        if (isManualRefresh)
        {
            await Task.Yield();
        }

        try
        {
            var snapshot = await _softwareInventoryProvider
                .GetAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            UpdateSoftwareInventory(snapshot);
            _latestSoftwareInventorySnapshot = snapshot;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            if (_latestSoftwareInventorySnapshot is null)
            {
                InstalledSoftwareList.ItemsSource =
                    Array.Empty<InstalledSoftwareDisplayItem>();
                SoftwareInventorySummaryText.Text =
                    "0 registrations found\nShowing 0";
            }

            SoftwareInventoryStatusText.Text =
                "Software inventory is temporarily unavailable.";
        }
        finally
        {
            _isSoftwareInventoryRequestRunning = false;

            if (!cancellationToken.IsCancellationRequested)
            {
                RefreshSoftwareButton.Content =
                    "Refresh software";
                UpdateRefreshSoftwareButtonState();
            }
        }
    }

    private void UpdateSoftwareInventory(
        MachineSoftwareInventorySnapshot snapshot)
    {
        ApplySoftwareInventoryFilter(snapshot);

        SoftwareInventoryStatusText.Text = snapshot.Items.Count == 0
            ? "No classic desktop software registrations found."
            : !snapshot.IsComplete
                ? $"Inventory is partial · " +
                    $"{snapshot.SkippedEntryCount} entries skipped"
                : string.Empty;
    }

    private void OnSoftwareSearchTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_latestSoftwareInventorySnapshot is not null)
        {
            ApplySoftwareInventoryFilter(
                _latestSoftwareInventorySnapshot);
        }
    }

    private void ApplySoftwareInventoryFilter(
        MachineSoftwareInventorySnapshot snapshot)
    {
        var searchText = SoftwareSearchBox.Text.Trim();
        var filteredItems = snapshot.Items
            .Where(item =>
                searchText.Length == 0 ||
                item.Name.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(item.Publisher) &&
                    item.Publisher.Contains(
                        searchText,
                        StringComparison.OrdinalIgnoreCase)))
            .Select(CreateInstalledSoftwareDisplayItem)
            .ToArray();

        InstalledSoftwareList.ItemsSource = filteredItems;
        SoftwareInventorySummaryText.Text =
            $"{snapshot.Items.Count} registrations found\n" +
            $"Showing {filteredItems.Length}";
    }

    private static InstalledSoftwareDisplayItem
        CreateInstalledSoftwareDisplayItem(
            MachineInstalledSoftwareSnapshot software)
    {
        var publisher = string.IsNullOrWhiteSpace(software.Publisher)
            ? "Publisher unavailable"
            : software.Publisher.Trim();
        var version = string.IsNullOrWhiteSpace(software.Version)
            ? "Version unavailable"
            : software.Version.Trim();
        var scope = software.Scope switch
        {
            MachineSoftwareScope.LocalMachine => "Machine",
            MachineSoftwareScope.CurrentUser => "Current user",
            _ => UnavailableValue,
        };
        var registryView = software.RegistryView switch
        {
            MachineSoftwareRegistryView.Registry32 =>
                "32-bit registration",
            MachineSoftwareRegistryView.Registry64 =>
                "64-bit registration",
            _ => "Registration view unavailable",
        };
        var estimatedSize =
            software.EstimatedSizeBytes is long bytes && bytes >= 0
                ? FormatBytes(bytes)
                : "Size unavailable";
        var installLocation =
            string.IsNullOrWhiteSpace(software.InstallLocation)
                ? string.Empty
                : $"Installed at {software.InstallLocation.Trim()}";

        return new InstalledSoftwareDisplayItem(
            software.Name,
            $"{publisher} · {version}",
            $"{scope} · {registryView} · {estimatedSize}",
            installLocation);
    }

    private void UpdateRefreshSoftwareButtonState()
    {
        RefreshSoftwareButton.IsEnabled =
            !_isSoftwareInventoryRequestRunning &&
            !_windowCancellationTokenSource.IsCancellationRequested;
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

    private void OnDetailsToggleClicked(
        object sender,
        RoutedEventArgs e)
    {
        _detailsExpanded = !_detailsExpanded;

        DetailsPanel.Visibility = _detailsExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        DetailsToggleButton.Content = _detailsExpanded
            ? "Collapse"
            : "Show details";

        ResizeAndPositionWindow(
            _detailsExpanded
                ? ExpandedWindowWidth
                : CompactWindowWidth,
            _detailsExpanded
                ? ExpandedWindowHeight
                : CompactWindowHeight);
    }

    private void ResizeAndPositionWindow(
        int requestedWidth,
        int requestedHeight)
    {
        var rasterizationScale =
            MainContent.XamlRoot?.RasterizationScale ?? 1d;
        var requestedSize = new SizeInt32(
            Math.Max(
                1,
                (int)Math.Round(
                    requestedWidth * rasterizationScale)),
            Math.Max(
                1,
                (int)Math.Round(
                    requestedHeight * rasterizationScale)));

        DisplayArea? displayArea;

        try
        {
            displayArea = DisplayArea.GetFromWindowId(
                AppWindow.Id,
                DisplayAreaFallback.Nearest);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            TryResizeWindow(requestedSize);
            return;
        }

        if (displayArea is null)
        {
            TryResizeWindow(requestedSize);
            return;
        }

        try
        {
            var workArea = displayArea.WorkArea;
            if (workArea.Width <= 0 || workArea.Height <= 0)
            {
                TryResizeWindow(requestedSize);
                return;
            }

            var maximumWidth = Math.Max(
                1,
                workArea.Width - 2 * WorkAreaMargin);
            var maximumHeight = Math.Max(
                1,
                workArea.Height - 2 * WorkAreaMargin);
            var targetSize = new SizeInt32(
                Math.Min(requestedSize.Width, maximumWidth),
                Math.Min(requestedSize.Height, maximumHeight));

            AppWindow.Resize(targetSize);
            var windowSize = AppWindow.Size;

            var workAreaLeft =
                displayArea.OuterBounds.X + workArea.X;
            var workAreaTop =
                displayArea.OuterBounds.Y + workArea.Y;
            var positionX = Math.Max(
                workAreaLeft,
                workAreaLeft + workArea.Width -
                    windowSize.Width - WorkAreaMargin);
            var positionY = Math.Max(
                workAreaTop,
                workAreaTop + workArea.Height -
                    windowSize.Height - WorkAreaMargin);

            AppWindow.Move(new PointInt32(
                positionX,
                positionY));
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private void TryResizeWindow(SizeInt32 requestedSize)
    {
        try
        {
            AppWindow.Resize(requestedSize);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private void OnWindowClosed(
        object sender,
        WindowEventArgs args)
    {
        _folderScanCancellationTokenSource?.Cancel();
        _windowCancellationTokenSource.Cancel();
    }
}

public sealed record ProcessDisplayItem(
    string Name,
    string Details);

public sealed record OllamaModelDisplayItem(
    string Name,
    string ModelDetails,
    string RuntimeDetails);

public sealed record StorageVolumeDisplayItem(
    string Header,
    string VolumeDetails,
    string CapacityDetails);

public sealed record LargeFolderDisplayItem(
    string Path,
    string Details);

public sealed record InstalledSoftwareDisplayItem(
    string Name,
    string PublisherAndVersion,
    string RegistrationDetails,
    string InstallLocationDetails);
