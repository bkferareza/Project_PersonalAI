using System.Diagnostics;
using System.Globalization;
using Machine.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace Machine.App;

public sealed partial class MainWindow : Window
{
    private const int ExpandedWindowWidth = 520;
    private const int ExpandedWindowHeight = 760;
    private const int WorkAreaMargin = 24;
    private const int TopProcessCount = 5;
    private const int LargeFolderResultCount = 10;
    private const int ExplanationLargeFolderCount = 3;
    private const int ExplanationStartupNameCount = 5;
    private const int FindingsDisplayCount = 8;
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
    private readonly IMachinePackagedSoftwareInventoryProvider
        _packagedSoftwareInventoryProvider;
    private readonly IMachineStartupInventoryProvider
        _startupInventoryProvider;
    private readonly MachineInsightTriggerPolicy
        _insightTriggerPolicy = new();
    private readonly CompactPresenceInteraction
        _compactPresenceInteraction = new();
    private readonly CancellationTokenSource
        _windowCancellationTokenSource = new();
    private readonly SystemBackdrop _dashboardBackdrop;
    private readonly UISettings _uiSettings = new();
    private readonly NativeAmbientOrbWindow _ambientOrbWindow;
    private readonly DispatcherQueueTimer _ambientOrbTimer;
    private CancellationTokenSource?
        _folderScanCancellationTokenSource;
    private MachineIdentity? _latestIdentity;
    private MachineResourceSnapshot? _latestResourceSnapshot;
    private IReadOnlyList<MachineProcessSnapshot>
        _latestProcessSnapshots =
            Array.Empty<MachineProcessSnapshot>();
    private MachineStorageSnapshot? _latestStorageSnapshot;
    private MachineFolderInspectionSnapshot?
        _latestFolderInspectionSnapshot;
    private MachineSoftwareInventorySnapshot?
        _latestSoftwareInventorySnapshot;
    private MachinePackagedSoftwareInventorySnapshot?
        _latestPackagedSoftwareInventorySnapshot;
    private MachineStartupInventorySnapshot?
        _latestStartupInventorySnapshot;
    private MachineFindingsSnapshot _latestFindingsSnapshot =
        MachineFindingsEvaluator.Evaluate(new());
    private bool _contentLoadStarted;
    private bool _detailsExpanded;
    private bool _hasSuccessfulExplanation;
    private bool _initialContextHydrationCompleted;
    private bool _windowPresentationConfigured;
    private bool _isOllamaServiceAvailable;
    private bool _isExplanationRequestRunning;
    private bool _isFolderScanRunning;
    private bool _isStorageRequestRunning;
    private bool _isSoftwareInventoryRequestRunning;
    private bool _isPackagedSoftwareInventoryRequestRunning;
    private bool _isStartupInventoryRequestRunning;
    private MachineOverallState _latestOverallState =
        MachineOverallState.Unknown;
    private CompactPresencePresentation?
        _appliedCompactPresentation;
    private CompactPresenceVisualMode?
        _activePresenceVisualMode;
    private bool _showNewInsightBloom;
    private bool _isAnimationSettingsChangeSubscribed;

    public MainWindow(
        IMachineIdentityProvider identityProvider,
        IMachineResourceProvider resourceProvider,
        IMachineProcessProvider processProvider,
        IOllamaStatusProvider ollamaStatusProvider,
        IMachineStateExplainer machineStateExplainer,
        IMachineStorageProvider storageProvider,
        IMachineFolderInspectionProvider folderInspectionProvider,
        IMachineSoftwareInventoryProvider softwareInventoryProvider,
        IMachinePackagedSoftwareInventoryProvider
            packagedSoftwareInventoryProvider,
        IMachineStartupInventoryProvider startupInventoryProvider)
    {
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(resourceProvider);
        ArgumentNullException.ThrowIfNull(processProvider);
        ArgumentNullException.ThrowIfNull(ollamaStatusProvider);
        ArgumentNullException.ThrowIfNull(machineStateExplainer);
        ArgumentNullException.ThrowIfNull(storageProvider);
        ArgumentNullException.ThrowIfNull(folderInspectionProvider);
        ArgumentNullException.ThrowIfNull(softwareInventoryProvider);
        ArgumentNullException.ThrowIfNull(
            packagedSoftwareInventoryProvider);
        ArgumentNullException.ThrowIfNull(startupInventoryProvider);

        _identityProvider = identityProvider;
        _resourceProvider = resourceProvider;
        _processProvider = processProvider;
        _ollamaStatusProvider = ollamaStatusProvider;
        _machineStateExplainer = machineStateExplainer;
        _storageProvider = storageProvider;
        _folderInspectionProvider = folderInspectionProvider;
        _softwareInventoryProvider = softwareInventoryProvider;
        _packagedSoftwareInventoryProvider =
            packagedSoftwareInventoryProvider;
        _startupInventoryProvider = startupInventoryProvider;

        InitializeComponent();
        _dashboardBackdrop = SystemBackdrop!;
        _ambientOrbWindow = new NativeAmbientOrbWindow(
            OpenDashboardFromAmbientOrb);
        _ambientOrbWindow.NewInsightCompleted += OnNewInsightBloomCompleted;
        _ambientOrbTimer =
            MainContent.DispatcherQueue.CreateTimer();
        _ambientOrbTimer.Interval = _ambientOrbWindow.FrameInterval;
        _ambientOrbTimer.IsRepeating = true;
        _ambientOrbTimer.Tick += OnAmbientOrbTimerTick;
        ApplyPresenceVisualMode(force: true);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            _uiSettings.AnimationsEnabledChanged +=
                OnSystemAnimationsEnabledChanged;
            _isAnimationSettingsChangeSubscribed = true;
        }
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;
    }

    private void OnWindowActivated(
        object sender,
        WindowActivatedEventArgs args)
    {
        if (_windowPresentationConfigured)
        {
            return;
        }

        _windowPresentationConfigured = true;

        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsMaximizable = false;
                presenter.IsResizable = false;
                presenter.IsMinimizable = true;
                presenter.SetBorderAndTitleBar(false, false);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }

        ApplyCompactPresentation(force: true);
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

            var telemetryLoop =
                RunTelemetryLoopAsync(cancellationToken);
            var processLoop =
                RunProcessLoopAsync(cancellationToken);
            var ollamaStatusLoop =
                RunOllamaStatusLoopAsync(cancellationToken);

            await Task.WhenAll(
                LoadStorageAsync(
                    isManualRefresh: false,
                    cancellationToken: cancellationToken),
                LoadSoftwareInventoryAsync(
                    isManualRefresh: false,
                    cancellationToken: cancellationToken),
                LoadPackagedSoftwareInventoryAsync(
                    isManualRefresh: false,
                    cancellationToken: cancellationToken),
                LoadStartupInventoryAsync(
                    isManualRefresh: false,
                    cancellationToken: cancellationToken));

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _insightTriggerPolicy.EstablishBaseline(
                _latestFindingsSnapshot);
            _initialContextHydrationCompleted = true;
            TryRequestDashboardInsight();

            await Task.WhenAll(
                telemetryLoop,
                processLoop,
                ollamaStatusLoop);
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
            TryRequestDashboardInsight();
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
            TelemetryStatusText.Visibility = Visibility.Collapsed;

            ReevaluateFindings(observeInsightTriggers: true);
            UpdateExplainMachineStateButtonState();
            TryRequestDashboardInsight();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            if (_latestResourceSnapshot is null)
            {
                CpuUsageText.Text = UnavailableValue;
                MemoryUsageText.Text = UnavailableValue;
            }

            TelemetryStatusText.Text =
                "Resource telemetry could not be loaded.";
            TelemetryStatusText.Visibility = Visibility.Visible;
        }
    }

    private void UpdatePresenceState(
        MachineOverallState overallState)
    {
        _latestOverallState = overallState;
        ApplyPresenceVisualMode();
    }

    private static Brush GetStateBrush(
        MachineOverallState overallState)
    {
        var resourceKey = overallState switch
        {
            MachineOverallState.Stable => "SystemFillColorSuccessBrush",
            MachineOverallState.Attention => "SystemFillColorCautionBrush",
            MachineOverallState.Warning or MachineOverallState.Critical =>
                "SystemFillColorCriticalBrush",
            _ => "TextFillColorSecondaryBrush"
        };

        return (Brush)Application.Current.Resources[resourceKey];
    }

    private void ReevaluateFindings(
        bool observeInsightTriggers = false)
    {
        var snapshot = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: _latestResourceSnapshot,
                Storage: _latestStorageSnapshot,
                FolderInspection: _latestFolderInspectionSnapshot,
                ClassicSoftware: _latestSoftwareInventorySnapshot,
                PackagedSoftware:
                    _latestPackagedSoftwareInventorySnapshot,
                Startup: _latestStartupInventorySnapshot));

        _latestFindingsSnapshot = snapshot;
        UpdatePresenceState(snapshot.OverallState);
        UpdateCurrentFindings(snapshot);

        if (!observeInsightTriggers)
        {
            return;
        }

        var decision = _insightTriggerPolicy.ObserveTelemetry(
            snapshot,
            DateTimeOffset.UtcNow,
            IsInsightContextAvailable(),
            allowAutomaticGeneration:
                _initialContextHydrationCompleted);

        StartInsightGeneration(decision);
    }

    private void UpdateCurrentFindings(
        MachineFindingsSnapshot snapshot)
    {
        FindingsOverallStateText.Text =
            snapshot.OverallState.ToString();
        FindingsOverallStateText.Foreground =
            GetStateBrush(snapshot.OverallState);

        var displayItems = snapshot.Findings
            .Take(FindingsDisplayCount)
            .Select(finding => new MachineFindingDisplayItem(
                Header: $"{finding.Severity} · {finding.Title}",
                Detail: finding.Detail))
            .ToArray();

        CurrentFindingsList.ItemsSource = displayItems;
        FindingsSummaryText.Text = snapshot.OverallState ==
            MachineOverallState.Unknown
                ? "Resource telemetry and readable " +
                    "system-volume data are unavailable."
                : displayItems.Length == 0
                    ? "No deterministic issues currently detected."
                    : string.Empty;
        FindingsSummaryText.Visibility =
            FindingsSummaryText.Text.Length == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
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
            TryRequestDashboardInsight();
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
            ClearOllamaModels(
                "Loaded-model status is unavailable.");
            UpdateExplainMachineStateButtonState();
            TryRequestDashboardInsight();
            return;
        }

        var displayItems = snapshot.RunningModels
            .Select(CreateOllamaModelDisplayItem)
            .ToArray();

        OllamaRunningModelsList.ItemsSource = displayItems;

        if (displayItems.Length == 0)
        {
            OllamaLoadedModelsStatusText.Text =
                "No models currently loaded.";
            UpdateExplainMachineStateButtonState();
            TryRequestDashboardInsight();
            return;
        }

        OllamaLoadedModelsStatusText.Text = string.Empty;
        UpdateExplainMachineStateButtonState();
        TryRequestDashboardInsight();
    }

    private void ShowOllamaOffline()
    {
        _isOllamaServiceAvailable = false;
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
        var decision = _insightTriggerPolicy.RequestManual(
            _latestFindingsSnapshot,
            IsInsightContextAvailable());

        if (!decision.ShouldGenerate)
        {
            UpdateExplainMachineStateButtonState();
            return;
        }

        await GenerateInsightAsync(decision);
    }

    private void TryRequestDashboardInsight()
    {
        if (!_detailsExpanded)
        {
            return;
        }

        var decision =
            _insightTriggerPolicy.RequestForDashboard(
                _latestFindingsSnapshot,
                DateTimeOffset.UtcNow,
                IsInsightContextAvailable());

        StartInsightGeneration(decision);
    }

    private void StartInsightGeneration(
        MachineInsightTriggerDecision decision)
    {
        if (decision.ShouldGenerate)
        {
            _ = GenerateInsightAsync(decision);
        }
    }

    private async Task GenerateInsightAsync(
        MachineInsightTriggerDecision decision)
    {
        var identity = _latestIdentity;
        var resources = _latestResourceSnapshot;
        var processSnapshots = _latestProcessSnapshots.ToArray();
        var storageSnapshot = _latestStorageSnapshot;
        var folderInspectionSnapshot =
            _latestFolderInspectionSnapshot;
        var softwareInventorySnapshot =
            _latestSoftwareInventorySnapshot;
        var packagedSoftwareInventorySnapshot =
            _latestPackagedSoftwareInventorySnapshot;
        var startupInventorySnapshot =
            _latestStartupInventorySnapshot;
        var findingsSnapshot = _latestFindingsSnapshot;
        var cancellationToken =
            _windowCancellationTokenSource.Token;

        if (identity is null ||
            resources is null ||
            processSnapshots.Length == 0 ||
            cancellationToken.IsCancellationRequested)
        {
            var followUp = _insightTriggerPolicy.CompleteRequest(
                decision,
                insightAccepted: false,
                DateTimeOffset.UtcNow,
                isOllamaOnline: false);
            UpdateExplainMachineStateButtonState();
            StartInsightGeneration(followUp);
            return;
        }

        _isExplanationRequestRunning = true;
        ApplyPresenceVisualMode();
        UpdateExplainMachineStateButtonState();
        ExplainMachineStateButton.Content = "Refreshing...";
        MachineExplanationProgressRing.Visibility =
            Visibility.Visible;
        MachineExplanationProgressRing.IsActive = true;
        MachineExplanationStatusText.Text =
            "Refreshing from verified local context...";

        var stopwatch = Stopwatch.StartNew();
        var insightAccepted = false;

        try
        {
            var request = new MachineStateExplanationRequest(
                Identity: identity,
                Resources: resources,
                TopProcesses: processSnapshots,
                Storage: CreateStorageExplanationContext(
                    storageSnapshot,
                    folderInspectionSnapshot),
                Software: CreateSoftwareExplanationContext(
                    softwareInventorySnapshot,
                    packagedSoftwareInventorySnapshot),
                Startup: CreateStartupExplanationContext(
                    startupInventorySnapshot),
                Findings: findingsSnapshot);
            var explanation =
                await _machineStateExplainer.ExplainAsync(
                    request,
                    cancellationToken);

            stopwatch.Stop();
            cancellationToken.ThrowIfCancellationRequested();

            var latestFingerprint =
                MachineInsightContextFingerprint.Create(
                    _latestFindingsSnapshot);

            if (_insightTriggerPolicy.IsCurrentContext(decision) &&
                string.Equals(
                    decision.ContextFingerprint,
                    latestFingerprint,
                    StringComparison.Ordinal))
            {
                MachineExplanationText.Text = explanation.Text;
                var elapsedSeconds =
                    stopwatch.Elapsed.TotalSeconds.ToString(
                        "F1",
                        CultureInfo.InvariantCulture);
                MachineExplanationMetadataText.Text =
                    explanation.Source ==
                        MachineExplanationSource.DeterministicFallback
                        ? "Verified summary · local safeguard"
                        : $"Generated locally · {explanation.Model} · " +
                            $"{elapsedSeconds}s";
                MachineExplanationMetadataText.Visibility =
                    Visibility.Visible;
                MachineExplanationStatusText.Text = string.Empty;
                _hasSuccessfulExplanation = true;
                insightAccepted = true;
                BeginNewInsightBloom();
            }
            else
            {
                MachineExplanationStatusText.Text =
                    "Watching the latest verified context.";
            }
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
                "Local insight is temporarily unavailable.";
        }
        finally
        {
            stopwatch.Stop();
            _isExplanationRequestRunning = false;
            ApplyPresenceVisualMode();

            var followUp = _insightTriggerPolicy.CompleteRequest(
                decision,
                insightAccepted,
                DateTimeOffset.UtcNow,
                IsInsightContextAvailable());

            if (!cancellationToken.IsCancellationRequested)
            {
                ExplainMachineStateButton.Content =
                    "Refresh insight";
                MachineExplanationProgressRing.IsActive = false;
                MachineExplanationProgressRing.Visibility =
                    Visibility.Collapsed;
                UpdateExplainMachineStateButtonState();
                StartInsightGeneration(followUp);
            }
        }
    }

    private bool IsInsightContextAvailable() =>
        _latestIdentity is not null &&
        _latestResourceSnapshot is not null &&
        _latestProcessSnapshots.Count > 0 &&
        _isOllamaServiceAvailable &&
        !_windowCancellationTokenSource.IsCancellationRequested;

    private void UpdateExplainMachineStateButtonState()
    {
        ExplainMachineStateButton.IsEnabled =
            IsInsightContextAvailable() &&
            !_insightTriggerPolicy.IsRequestInFlight &&
            !_isExplanationRequestRunning;
    }

    private static MachineStorageExplanationContext?
        CreateStorageExplanationContext(
            MachineStorageSnapshot? storageSnapshot,
            MachineFolderInspectionSnapshot?
                folderInspectionSnapshot)
    {
        var systemVolume = storageSnapshot?.Volumes
            .FirstOrDefault(volume => volume.IsSystemVolume);

        if (systemVolume is null)
        {
            return null;
        }

        MachineFolderScanExplanationContext? folderScan = null;

        if (folderInspectionSnapshot is not null &&
            StorageRootsMatch(
                systemVolume.RootPath,
                folderInspectionSnapshot.RootPath))
        {
            var folders = folderInspectionSnapshot.Folders
                .OrderByDescending(folder => folder.SizeBytes)
                .ThenByDescending(folder => folder.IsComplete)
                .ThenBy(
                    folder => folder.Path,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    folder => folder.Path,
                    StringComparer.Ordinal)
                .Take(ExplanationLargeFolderCount)
                .Select(folder =>
                    new MachineFolderMeasurementExplanationContext(
                        Name: GetFolderName(folder.Path),
                        MeasuredSizeBytes: folder.SizeBytes,
                        IsComplete: folder.IsComplete))
                .ToArray();

            folderScan = new MachineFolderScanExplanationContext(
                Folders: folders,
                IsComplete: folderInspectionSnapshot.IsComplete);
        }

        return new MachineStorageExplanationContext(
            SystemVolumeRoot: systemVolume.RootPath,
            TotalSizeBytes: systemVolume.TotalSizeBytes,
            AvailableSizeBytes:
                systemVolume.AvailableFreeSpaceBytes,
            LargeFolderScan: folderScan);
    }

    private static MachineSoftwareExplanationContext?
        CreateSoftwareExplanationContext(
            MachineSoftwareInventorySnapshot?
                softwareInventorySnapshot,
            MachinePackagedSoftwareInventorySnapshot?
                packagedSoftwareInventorySnapshot)
    {
        if (softwareInventorySnapshot is null &&
            packagedSoftwareInventorySnapshot is null)
        {
            return null;
        }

        return new MachineSoftwareExplanationContext(
            ClassicDesktop: softwareInventorySnapshot is null
                ? null
                : new MachineSoftwareInventoryExplanationSummary(
                    RegistrationCount:
                        softwareInventorySnapshot.Items.Count,
                    IsComplete:
                        softwareInventorySnapshot.IsComplete,
                    SkippedEntryCount:
                        softwareInventorySnapshot.SkippedEntryCount),
            PackagedApplications:
                packagedSoftwareInventorySnapshot is null
                    ? null
                    : new MachineSoftwareInventoryExplanationSummary(
                        RegistrationCount:
                            packagedSoftwareInventorySnapshot
                                .Items.Count,
                        IsComplete:
                            packagedSoftwareInventorySnapshot
                                .IsComplete,
                        SkippedEntryCount:
                            packagedSoftwareInventorySnapshot
                                .SkippedEntryCount));
    }

    private static MachineStartupExplanationContext?
        CreateStartupExplanationContext(
            MachineStartupInventorySnapshot?
                startupInventorySnapshot)
    {
        if (startupInventorySnapshot is null)
        {
            return null;
        }

        var items = startupInventorySnapshot.Items;
        var names = items
            .Select(item => item.Name.Trim())
            .Where(name => name.Length > 0)
            .OrderBy(
                name => name,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(name => name, StringComparer.Ordinal)
            .Take(ExplanationStartupNameCount)
            .ToArray();

        return new MachineStartupExplanationContext(
            RegistrationCount: items.Count,
            RegistryRunCount: items.Count(item =>
                item.Source ==
                    MachineStartupSource.RegistryRunKey),
            StartupFolderCount: items.Count(item =>
                item.Source ==
                    MachineStartupSource.StartupFolder),
            MachineCount: items.Count(item =>
                item.Scope == MachineStartupScope.AllUsers),
            CurrentUserCount: items.Count(item =>
                item.Scope == MachineStartupScope.CurrentUser),
            IsComplete: startupInventorySnapshot.IsComplete,
            Names: names);
    }

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
            ReevaluateFindings();
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

            _latestFolderInspectionSnapshot = snapshot;
            UpdateLargeFolderResults(
                snapshot,
                stopwatch.Elapsed >= LargeFolderScanTimeBudget);
            ReevaluateFindings();
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
            ReevaluateFindings();
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

    private async void OnRefreshPackagedSoftwareClicked(
        object sender,
        RoutedEventArgs e)
    {
        await LoadPackagedSoftwareInventoryAsync(
            isManualRefresh: true,
            cancellationToken:
                _windowCancellationTokenSource.Token);
    }

    private async Task LoadPackagedSoftwareInventoryAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken)
    {
        if (_isPackagedSoftwareInventoryRequestRunning)
        {
            return;
        }

        _isPackagedSoftwareInventoryRequestRunning = true;
        if (isManualRefresh)
        {
            RefreshPackagedSoftwareButton.Content =
                "Refreshing...";
        }

        UpdateRefreshPackagedSoftwareButtonState();

        if (isManualRefresh)
        {
            await Task.Yield();
        }

        try
        {
            var snapshot = await _packagedSoftwareInventoryProvider
                .GetAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            UpdatePackagedSoftwareInventory(snapshot);
            _latestPackagedSoftwareInventorySnapshot = snapshot;
            ReevaluateFindings();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            if (_latestPackagedSoftwareInventorySnapshot is null)
            {
                PackagedSoftwareList.ItemsSource =
                    Array.Empty<PackagedSoftwareDisplayItem>();
                PackagedSoftwareInventorySummaryText.Text =
                    "0 packages found\nShowing 0";
            }

            PackagedSoftwareInventoryStatusText.Text =
                "Packaged-software inventory is temporarily unavailable.";
        }
        finally
        {
            _isPackagedSoftwareInventoryRequestRunning = false;

            if (!cancellationToken.IsCancellationRequested)
            {
                RefreshPackagedSoftwareButton.Content =
                    "Refresh packaged applications";
                UpdateRefreshPackagedSoftwareButtonState();
            }
        }
    }

    private void UpdatePackagedSoftwareInventory(
        MachinePackagedSoftwareInventorySnapshot snapshot)
    {
        ApplyPackagedSoftwareInventoryFilter(snapshot);

        if (!snapshot.IsComplete)
        {
            var partialResultDetails = new List<string>(
                capacity: 2);

            if (snapshot.SkippedEntryCount > 0)
            {
                partialResultDetails.Add(
                    $"{snapshot.SkippedEntryCount} " +
                    (snapshot.SkippedEntryCount == 1
                        ? "package skipped"
                        : "packages skipped"));
            }

            if (snapshot.OptionalPropertyFailureCount > 0)
            {
                partialResultDetails.Add(
                    $"{snapshot.OptionalPropertyFailureCount} " +
                    (snapshot.OptionalPropertyFailureCount == 1
                        ? "optional property unavailable"
                        : "optional properties unavailable"));
            }

            PackagedSoftwareInventoryStatusText.Text =
                partialResultDetails.Count == 0
                    ? "Inventory is partial."
                    : $"Inventory is partial · " +
                        string.Join(" · ", partialResultDetails);
            return;
        }

        PackagedSoftwareInventoryStatusText.Text =
            snapshot.Items.Count == 0
                ? "No user-facing MSIX/AppX packages found."
                : string.Empty;
    }

    private void OnPackagedSoftwareSearchTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_latestPackagedSoftwareInventorySnapshot is not null)
        {
            ApplyPackagedSoftwareInventoryFilter(
                _latestPackagedSoftwareInventorySnapshot);
        }
    }

    private void ApplyPackagedSoftwareInventoryFilter(
        MachinePackagedSoftwareInventorySnapshot snapshot)
    {
        var searchText = PackagedSoftwareSearchBox.Text.Trim();
        var filteredItems = snapshot.Items
            .Where(item =>
                searchText.Length == 0 ||
                item.DisplayName.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(
                    item.PublisherDisplayName) &&
                    item.PublisherDisplayName.Contains(
                        searchText,
                        StringComparison.OrdinalIgnoreCase)) ||
                item.PackageFamilyName.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase))
            .Select(CreatePackagedSoftwareDisplayItem)
            .ToArray();

        PackagedSoftwareList.ItemsSource = filteredItems;
        PackagedSoftwareInventorySummaryText.Text =
            $"{snapshot.Items.Count} packages found\n" +
            $"Showing {filteredItems.Length}";
    }

    private static PackagedSoftwareDisplayItem
        CreatePackagedSoftwareDisplayItem(
            MachinePackagedSoftwareSnapshot software)
    {
        var publisher = string.IsNullOrWhiteSpace(
            software.PublisherDisplayName)
            ? "Publisher unavailable"
            : software.PublisherDisplayName.Trim();
        var flags = new List<string>(capacity: 2);

        if (software.IsDevelopmentMode == true)
        {
            flags.Add("Development package");
        }

        if (software.IsStub == true)
        {
            flags.Add("Stub package");
        }

        var installedLocation = string.IsNullOrWhiteSpace(
            software.InstalledLocation)
            ? string.Empty
            : $"Installed at {software.InstalledLocation.Trim()}";

        return new PackagedSoftwareDisplayItem(
            software.DisplayName,
            $"{publisher} · Version {software.Version} · " +
                FormatPackagedSoftwareArchitecture(
                    software.Architecture),
            $"Package family: {software.PackageFamilyName}",
            string.Join(" · ", flags),
            installedLocation);
    }

    private static string FormatPackagedSoftwareArchitecture(
        MachinePackagedSoftwareArchitecture architecture) =>
        architecture switch
        {
            MachinePackagedSoftwareArchitecture.Neutral => "Neutral",
            MachinePackagedSoftwareArchitecture.X86 => "x86",
            MachinePackagedSoftwareArchitecture.X64 => "x64",
            MachinePackagedSoftwareArchitecture.Arm => "ARM",
            MachinePackagedSoftwareArchitecture.Arm64 => "ARM64",
            MachinePackagedSoftwareArchitecture.X86OnArm64 =>
                "x86 on ARM64",
            _ => "Architecture unavailable",
        };

    private void UpdateRefreshPackagedSoftwareButtonState()
    {
        RefreshPackagedSoftwareButton.IsEnabled =
            !_isPackagedSoftwareInventoryRequestRunning &&
            !_windowCancellationTokenSource.IsCancellationRequested;
    }

    private async void OnRefreshStartupClicked(
        object sender,
        RoutedEventArgs e)
    {
        await LoadStartupInventoryAsync(
            isManualRefresh: true,
            cancellationToken:
                _windowCancellationTokenSource.Token);
    }

    private async Task LoadStartupInventoryAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken)
    {
        if (_isStartupInventoryRequestRunning)
        {
            return;
        }

        _isStartupInventoryRequestRunning = true;
        if (isManualRefresh)
        {
            RefreshStartupButton.Content = "Refreshing...";
        }

        UpdateRefreshStartupButtonState();

        if (isManualRefresh)
        {
            await Task.Yield();
        }

        try
        {
            var snapshot = await _startupInventoryProvider
                .GetAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            UpdateStartupInventory(snapshot);
            _latestStartupInventorySnapshot = snapshot;
            ReevaluateFindings();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            if (_latestStartupInventorySnapshot is null)
            {
                StartupApplicationsList.ItemsSource =
                    Array.Empty<StartupApplicationDisplayItem>();
                StartupInventorySummaryText.Text =
                    "0 entries found\nShowing 0";
            }

            StartupInventoryStatusText.Text =
                "Startup inventory is temporarily unavailable.";
        }
        finally
        {
            _isStartupInventoryRequestRunning = false;

            if (!cancellationToken.IsCancellationRequested)
            {
                RefreshStartupButton.Content =
                    "Refresh startup applications";
                UpdateRefreshStartupButtonState();
            }
        }
    }

    private void UpdateStartupInventory(
        MachineStartupInventorySnapshot snapshot)
    {
        ApplyStartupInventoryFilter(snapshot);

        StartupInventoryStatusText.Text = !snapshot.IsComplete
            ? $"Inventory is partial · " +
                $"{snapshot.ReadFailureCount} " +
                (snapshot.ReadFailureCount == 1
                    ? "read failure"
                    : "read failures")
            : snapshot.Items.Count == 0
                ? "No startup applications found in Run keys or Startup folders."
                : string.Empty;
    }

    private void OnStartupSearchTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_latestStartupInventorySnapshot is not null)
        {
            ApplyStartupInventoryFilter(
                _latestStartupInventorySnapshot);
        }
    }

    private void ApplyStartupInventoryFilter(
        MachineStartupInventorySnapshot snapshot)
    {
        var searchText = StartupSearchBox.Text.Trim();
        var filteredItems = snapshot.Items
            .Where(item =>
                searchText.Length == 0 ||
                item.Name.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase) ||
                item.CommandOrPath.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase))
            .Select(CreateStartupApplicationDisplayItem)
            .ToArray();

        StartupApplicationsList.ItemsSource = filteredItems;
        StartupInventorySummaryText.Text =
            $"{snapshot.Items.Count} entries found\n" +
            $"Showing {filteredItems.Length}";
    }

    private static StartupApplicationDisplayItem
        CreateStartupApplicationDisplayItem(
            MachineStartupApplicationSnapshot startupApplication)
    {
        var scope = startupApplication.Scope switch
        {
            MachineStartupScope.CurrentUser => "Current user",
            MachineStartupScope.AllUsers => "All users",
            _ => UnavailableValue,
        };

        return startupApplication.Source switch
        {
            MachineStartupSource.RegistryRunKey =>
                new StartupApplicationDisplayItem(
                    startupApplication.Name,
                    $"Command: {startupApplication.CommandOrPath}",
                    $"{scope} · " +
                    $"{FormatStartupRegistryView(startupApplication.RegistryView)} Run key"),
            MachineStartupSource.StartupFolder =>
                new StartupApplicationDisplayItem(
                    startupApplication.Name,
                    $"Path: {startupApplication.CommandOrPath}",
                    $"{scope} · Startup folder"),
            _ => new StartupApplicationDisplayItem(
                startupApplication.Name,
                startupApplication.CommandOrPath,
                $"{scope} · Source unavailable"),
        };
    }

    private static string FormatStartupRegistryView(
        MachineStartupRegistryView? registryView) =>
        registryView switch
        {
            MachineStartupRegistryView.Registry32 => "32-bit",
            MachineStartupRegistryView.Registry64 => "64-bit",
            MachineStartupRegistryView.Shared => "Shared",
            _ => "Unknown-view",
        };

    private void UpdateRefreshStartupButtonState()
    {
        RefreshStartupButton.IsEnabled =
            !_isStartupInventoryRequestRunning &&
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

    private void OnDashboardBackRequested(
        NavigationView sender,
        NavigationViewBackRequestedEventArgs args)
    {
        if (!_compactPresenceInteraction.CloseDashboard())
        {
            return;
        }

        SetDashboardExpanded(false);
    }

    private void SetDashboardExpanded(bool isExpanded)
    {
        _detailsExpanded = isExpanded;

        DetailsPanel.Visibility = _detailsExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyCompactPresentation();

        if (_detailsExpanded)
        {
            DetailsPanel.SelectedItem = OverviewNavigationItem;
            ShowDashboardPage("overview");
            MainContent.DispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Low,
                () =>
                {
                    if (_detailsExpanded &&
                        !_windowCancellationTokenSource
                            .IsCancellationRequested)
                    {
                        OverviewNavigationItem.Focus(
                            FocusState.Programmatic);
                    }
                });
            TryRequestDashboardInsight();
        }
    }

    private void ApplyCompactPresentation(bool force = false)
    {
        var presentation =
            _compactPresenceInteraction.Presentation;

        if (!force &&
            _appliedCompactPresentation == presentation)
        {
            return;
        }

        _appliedCompactPresentation = presentation;
        var isDashboardExpanded = presentation ==
            CompactPresencePresentation.Dashboard;
        UpdateWindowChrome(isDashboardExpanded);

        if (isDashboardExpanded)
        {
            _ambientOrbTimer.Stop();
            _ambientOrbWindow.Hide();
            AppWindow.Show();
            ResizeAndPositionWindow(
                ExpandedWindowWidth,
                ExpandedWindowHeight);
            return;
        }

        ShowAmbientOrb();
        AppWindow.Hide();
    }

    private void ShowAmbientOrb()
    {
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(
                AppWindow.Id,
                DisplayAreaFallback.Nearest);
            var workArea = displayArea.WorkArea;
            var position = CompactPresenceLayout.CalculateBottomRightPosition(
                new CompactPresenceWorkArea(
                    displayArea.OuterBounds.X + workArea.X,
                    displayArea.OuterBounds.Y + workArea.Y,
                    workArea.Width,
                    workArea.Height),
                CompactPresenceLayout.AmbientOrbSize,
                WorkAreaMargin);
            _ambientOrbWindow.Show(position.X, position.Y);
            UpdateAmbientOrbAnimationTimer();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private void OnAmbientOrbTimerTick(
        DispatcherQueueTimer sender,
        object args)
    {
        if (!_ambientOrbWindow.AdvanceFrame())
        {
            sender.Stop();
        }
    }

    private void OpenDashboardFromAmbientOrb()
    {
        if (_windowCancellationTokenSource.IsCancellationRequested ||
            !_compactPresenceInteraction.OpenDashboard())
        {
            return;
        }

        SetDashboardExpanded(true);
    }

    private void BeginNewInsightBloom()
    {
        if (_windowCancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        _showNewInsightBloom = true;
        ApplyPresenceVisualMode(force: true);
    }

    private void ApplyPresenceVisualMode(bool force = false)
    {
        if (_windowCancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        var mode = CompactPresenceLayout.SelectVisualMode(
            _latestOverallState,
            _isExplanationRequestRunning,
            _showNewInsightBloom);

        if (!force && _activePresenceVisualMode == mode)
        {
            return;
        }

        _activePresenceVisualMode = mode;
        _ambientOrbWindow.SetAnimationsEnabled(_uiSettings.AnimationsEnabled);
        _ambientOrbWindow.SetVisualMode(mode);

        if (!_uiSettings.AnimationsEnabled)
        {
            if (mode == CompactPresenceVisualMode.NewInsight)
            {
                _showNewInsightBloom = false;
                _activePresenceVisualMode = null;
                ApplyPresenceVisualMode(force: true);
            }

            return;
        }

        UpdateAmbientOrbAnimationTimer();
    }

    private void UpdateAmbientOrbAnimationTimer()
    {
        if (_ambientOrbWindow.ShouldAnimate)
        {
            _ambientOrbTimer.Start();
        }
        else
        {
            _ambientOrbTimer.Stop();
        }
    }

    private void OnNewInsightBloomCompleted(object? sender, EventArgs args)
    {
        if (_windowCancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        _showNewInsightBloom = false;
        _activePresenceVisualMode = null;
        ApplyPresenceVisualMode(force: true);
    }

    private void OnSystemAnimationsEnabledChanged(
        UISettings sender,
        object args)
    {
        MainContent.DispatcherQueue.TryEnqueue(() =>
            ApplyPresenceVisualMode(force: true));
    }

    private void UpdateWindowChrome(bool isDashboardExpanded)
    {
        try
        {
            var targetBackdrop = isDashboardExpanded
                ? _dashboardBackdrop
                : null;
            if (!ReferenceEquals(SystemBackdrop, targetBackdrop))
            {
                SystemBackdrop = targetBackdrop;
            }

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(
                    isDashboardExpanded,
                    isDashboardExpanded);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private void OnDashboardNavigationSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (OverviewPage is null)
        {
            return;
        }

        var tag = (args.SelectedItemContainer as NavigationViewItem)?
            .Tag?.ToString() ?? "overview";

        ShowDashboardPage(tag);
    }

    private void ShowDashboardPage(string tag)
    {
        OverviewPage.Visibility = tag == "overview"
            ? Visibility.Visible
            : Visibility.Collapsed;
        StoragePage.Visibility = tag == "storage"
            ? Visibility.Visible
            : Visibility.Collapsed;
        SoftwarePage.Visibility = tag == "software"
            ? Visibility.Visible
            : Visibility.Collapsed;
        StartupPage.Visibility = tag == "startup"
            ? Visibility.Visible
            : Visibility.Collapsed;
        RuntimePage.Visibility = tag == "runtime"
            ? Visibility.Visible
            : Visibility.Collapsed;
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

            var workAreaLeft =
                displayArea.OuterBounds.X + workArea.X;
            var workAreaTop =
                displayArea.OuterBounds.Y + workArea.Y;
            var targetCompactSize = new CompactPresenceSize(
                targetSize.Width,
                targetSize.Height);
            var position =
                CompactPresenceLayout.CalculateBottomRightPosition(
                    new CompactPresenceWorkArea(
                        workAreaLeft,
                        workAreaTop,
                        workArea.Width,
                        workArea.Height),
                    targetCompactSize,
                    WorkAreaMargin);

            AppWindow.MoveAndResize(new RectInt32(
                position.X,
                position.Y,
                targetSize.Width,
                targetSize.Height));
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
            var currentPosition = AppWindow.Position;
            var currentSize = AppWindow.Size;
            var right = currentPosition.X + currentSize.Width;
            var bottom = currentPosition.Y + currentSize.Height;

            AppWindow.MoveAndResize(new RectInt32(
                right - requestedSize.Width,
                bottom - requestedSize.Height,
                requestedSize.Width,
                requestedSize.Height));
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
        _ambientOrbTimer.Stop();
        _ambientOrbTimer.Tick -= OnAmbientOrbTimerTick;
        _ambientOrbWindow.NewInsightCompleted -= OnNewInsightBloomCompleted;
        _ambientOrbWindow.Dispose();
        if (_isAnimationSettingsChangeSubscribed &&
            OperatingSystem.IsWindowsVersionAtLeast(
                10,
                0,
                19041))
        {
            _uiSettings.AnimationsEnabledChanged -=
                OnSystemAnimationsEnabledChanged;
        }
        _folderScanCancellationTokenSource?.Cancel();
        _windowCancellationTokenSource.Cancel();
    }
}

public sealed record ProcessDisplayItem(
    string Name,
    string Details);

public sealed record MachineFindingDisplayItem(
    string Header,
    string Detail);

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

public sealed record PackagedSoftwareDisplayItem(
    string DisplayName,
    string PublisherVersionArchitecture,
    string PackageFamilyDetails,
    string PackageFlagsDetails,
    string InstalledLocationDetails);

public sealed record StartupApplicationDisplayItem(
    string Name,
    string CommandOrPathDetails,
    string SourceDetails);
