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

namespace Machine.App;

public sealed partial class MainWindow : Window
{
    private const int ExpandedWindowWidth = 650;
    private const int ExpandedWindowHeight = 820;
    private const int WorkAreaMargin = 24;
    private const int TopProcessCount = 5;
    private const int LargeFolderResultCount = 10;
    private const int ExplanationLargeFolderCount = 3;
    private const int ExplanationStartupNameCount = 5;
    private const int FindingsDisplayCount = 8;
    private const int MaximumNetworkInterfaceCount = 12;
    private const int MaximumUpdateHistoryDisplayCount = 12;
    private const int MaximumReliabilityIncidentDisplayCount = 16;
    private const int MaximumRecurringFailureDisplayCount = 4;
    private const int MaximumInventoryDisplayCount = 1_000;
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
    private static readonly TimeSpan HealthRefreshInterval =
        TimeSpan.FromMinutes(10);
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
    private readonly IMachineUserActivityProvider _userActivityProvider;
    private readonly IMachineNetworkProvider _networkProvider;
    private readonly IMachineSessionProvider _sessionProvider;
    private readonly IMachineWindowsUpdateProvider _windowsUpdateProvider;
    private readonly IMachineRebootPendingProvider _rebootPendingProvider;
    private readonly IMachineReliabilityProvider _reliabilityProvider;
    private readonly MachineLearningService _learningService;
    private readonly IMachineLearningStore _learningStore;
    private readonly MachineHealthHistoryService _healthHistoryService;
    private readonly IMachineHealthHistoryStore _healthHistoryStore;
    private readonly MachineHistoryService _historyService;
    private readonly IMachineHistoryStore _historyStore;
    private readonly IMachineServiceInventoryProvider
        _serviceInventoryProvider;
    private readonly IMachineScheduledTaskInventoryProvider
        _taskInventoryProvider;
    private readonly IMachineDeviceInventoryProvider
        _deviceInventoryProvider;
    private readonly IMachineGpuTelemetryProvider _gpuTelemetryProvider;
    private readonly MachineInsightTriggerPolicy
        _insightTriggerPolicy = new();
    private readonly CompactPresenceInteraction
        _compactPresenceInteraction = new();
    private readonly CancellationTokenSource
        _windowCancellationTokenSource = new();
    private readonly SystemBackdrop _dashboardBackdrop;
    private readonly UISettings _uiSettings = new();
    private readonly NativeAmbientOrbWindow _ambientOrbWindow;
    private WindowsPowerBroadcastMonitor? _powerBroadcastMonitor;
    private InputNonClientPointerSource? _nonClientPointerSource;
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
    private MachineNetworkSnapshot? _latestNetworkSnapshot;
    private MachineSessionSnapshot? _latestSessionSnapshot;
    private MachineWindowsUpdateSnapshot? _latestWindowsUpdateSnapshot;
    private MachineRebootPendingSnapshot? _latestRebootPendingSnapshot;
    private MachineReliabilitySnapshot? _latestReliabilitySnapshot;
    private MachineServiceInventorySnapshot? _latestServiceInventorySnapshot;
    private MachineScheduledTaskInventorySnapshot?
        _latestTaskInventorySnapshot;
    private MachineDeviceInventorySnapshot? _latestDeviceInventorySnapshot;
    private MachineGpuTelemetrySnapshot? _latestGpuTelemetrySnapshot;
    private OllamaStatusSnapshot? _latestOllamaStatusSnapshot;
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
    private bool _isHealthRequestRunning;
    private bool _isServiceInventoryRequestRunning;
    private bool _isTaskInventoryRequestRunning;
    private bool _isDeviceInventoryRequestRunning;
    private MachineHistoryRange _selectedHistoryRange =
        MachineHistoryRange.Last24Hours;
    private MachineOverallState _latestOverallState =
        MachineOverallState.Unknown;
    private CompactPresencePresentation?
        _appliedCompactPresentation;
    private CompactPresenceVisualMode?
        _activePresenceVisualMode;
    private bool _showNewInsightBloom;
    private bool _isAnimationSettingsChangeSubscribed;
    private bool _isXamlRootChangeSubscribed;
    private Storyboard? _shellAtmosphereStoryboard;
    private Storyboard? _generatingAtmosphereStoryboard;
    private MatasuriShellAtmosphere? _appliedShellAtmosphere;
#if DEBUG
    private readonly MatasuriPresentationValidationOptions
        _presentationValidationOptions;
#endif

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
        IMachineStartupInventoryProvider startupInventoryProvider,
        IMachineUserActivityProvider userActivityProvider,
        IMachineNetworkProvider networkProvider,
        IMachineSessionProvider sessionProvider,
        IMachineWindowsUpdateProvider windowsUpdateProvider,
        IMachineRebootPendingProvider rebootPendingProvider,
        IMachineReliabilityProvider reliabilityProvider,
        MachineLearningService learningService,
        IMachineLearningStore learningStore,
        MachineHealthHistoryService healthHistoryService,
        IMachineHealthHistoryStore healthHistoryStore,
        MachineHistoryService historyService,
        IMachineHistoryStore historyStore,
        IMachineServiceInventoryProvider serviceInventoryProvider,
        IMachineScheduledTaskInventoryProvider taskInventoryProvider,
        IMachineDeviceInventoryProvider deviceInventoryProvider,
        IMachineGpuTelemetryProvider gpuTelemetryProvider,
        string? presentationValidationArguments = null)
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
        ArgumentNullException.ThrowIfNull(userActivityProvider);
        ArgumentNullException.ThrowIfNull(networkProvider);
        ArgumentNullException.ThrowIfNull(sessionProvider);
        ArgumentNullException.ThrowIfNull(windowsUpdateProvider);
        ArgumentNullException.ThrowIfNull(rebootPendingProvider);
        ArgumentNullException.ThrowIfNull(reliabilityProvider);
        ArgumentNullException.ThrowIfNull(learningService);
        ArgumentNullException.ThrowIfNull(learningStore);
        ArgumentNullException.ThrowIfNull(healthHistoryService);
        ArgumentNullException.ThrowIfNull(healthHistoryStore);
        ArgumentNullException.ThrowIfNull(historyService);
        ArgumentNullException.ThrowIfNull(historyStore);
        ArgumentNullException.ThrowIfNull(serviceInventoryProvider);
        ArgumentNullException.ThrowIfNull(taskInventoryProvider);
        ArgumentNullException.ThrowIfNull(deviceInventoryProvider);
        ArgumentNullException.ThrowIfNull(gpuTelemetryProvider);

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
        _userActivityProvider = userActivityProvider;
        _networkProvider = networkProvider;
        _sessionProvider = sessionProvider;
        _windowsUpdateProvider = windowsUpdateProvider;
        _rebootPendingProvider = rebootPendingProvider;
        _reliabilityProvider = reliabilityProvider;
        _learningService = learningService;
        _learningStore = learningStore;
        _healthHistoryService = healthHistoryService;
        _healthHistoryStore = healthHistoryStore;
        _historyService = historyService;
        _historyStore = historyStore;
        _serviceInventoryProvider = serviceInventoryProvider;
        _taskInventoryProvider = taskInventoryProvider;
        _deviceInventoryProvider = deviceInventoryProvider;
        _gpuTelemetryProvider = gpuTelemetryProvider;
#if DEBUG
        _presentationValidationOptions =
            MatasuriPresentationValidationOptions.Parse(
                presentationValidationArguments);
#endif

        InitializeComponent();
#if DEBUG
        MainContent.RequestedTheme =
            _presentationValidationOptions.Theme switch
            {
                MatasuriPresentationTheme.Light => ElementTheme.Light,
                MatasuriPresentationTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
#endif
        ApplyShellAtmosphere();
        _dashboardBackdrop = SystemBackdrop!;
        _ambientOrbWindow = new NativeAmbientOrbWindow(
            OpenDashboardFromAmbientOrb);
        _ambientOrbWindow.NewInsightCompleted += OnNewInsightBloomCompleted;
        ApplyPresenceVisualMode(force: true);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            _uiSettings.AnimationsEnabledChanged +=
                OnSystemAnimationsEnabledChanged;
            _isAnimationSettingsChangeSubscribed = true;
        }
        Activated += OnWindowActivated;
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
                presenter.SetBorderAndTitleBar(
                    DashboardChromeLayout.HasBorder,
                    DashboardChromeLayout.HasTitleBar);
            }

            _nonClientPointerSource =
                InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
            _powerBroadcastMonitor ??= new(
                WinRT.Interop.WindowNative.GetWindowHandle(this),
                OnPowerTransition);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }

        ApplyCompactPresentation(force: true);
        UpdateDashboardDragRegion();
    }

    private void OnPowerTransition(MachinePowerTransition transition)
    {
        var historyKind = transition.Kind switch
        {
            MachinePowerTransitionKind.Suspend =>
                MachineHistoryEventKind.SystemSuspend,
            MachinePowerTransitionKind.ResumeAutomatic =>
                MachineHistoryEventKind.SystemResumeAutomatic,
            MachinePowerTransitionKind.ResumeSuspend =>
                MachineHistoryEventKind.SystemResumeSuspend,
            _ => throw new ArgumentOutOfRangeException()
        };
        _historyService.RecordPowerTransition(
            historyKind,
            transition.OccurredAt);
    }

    private void ApplyDashboardCornerPreference()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var cornerPreference =
            DashboardChromeLayout.DwmRoundSmallCornerPreference;
        var result = DwmSetWindowAttribute(
            WinRT.Interop.WindowNative.GetWindowHandle(this),
            DashboardChromeLayout.DwmWindowCornerPreferenceAttribute,
            ref cornerPreference,
            Marshal.SizeOf<int>());
        if (result != 0)
        {
            Debug.WriteLine(
                $"DwmSetWindowAttribute failed with HRESULT 0x{result:X8}.");
        }
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

        if (MainContent.XamlRoot is not null)
        {
            MainContent.XamlRoot.Changed += OnDashboardXamlRootChanged;
            _isXamlRootChangeSubscribed = true;
        }

        await LoadIdentityAsync();
        await LoadLearningAsync();
        await LoadHealthHistoryAsync();
        await LoadHistoryAsync();

        var cancellationToken =
            _windowCancellationTokenSource.Token;

        var telemetryLoop =
            RunTelemetryLoopAsync(cancellationToken);
        var processLoop =
            RunProcessLoopAsync(cancellationToken);
        var ollamaStatusLoop =
            RunOllamaStatusLoopAsync(cancellationToken);
        var healthLoop = RunHealthLoopAsync(cancellationToken);

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
                cancellationToken: cancellationToken),
            LoadServiceInventoryAsync(
                isManualRefresh: false,
                cancellationToken),
            LoadTaskInventoryAsync(
                isManualRefresh: false,
                cancellationToken),
            LoadDeviceInventoryAsync(
                isManualRefresh: false,
                cancellationToken));

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
            ollamaStatusLoop,
            healthLoop);
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
            LoadStatusText.Text = "Local identity could not be loaded.";
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
            var resourceTask = _resourceProvider.GetAsync(cancellationToken);
            var networkTask = TryCaptureNetworkAsync(cancellationToken);
            var sessionTask = TryCaptureSessionAsync(cancellationToken);
            var gpuTask = TryCaptureGpuAsync(cancellationToken);
            await Task.WhenAll(
                resourceTask,
                networkTask,
                sessionTask,
                gpuTask);

            var snapshot = await resourceTask;
            var networkSnapshot = await networkTask;
            var sessionSnapshot = await sessionTask;
            var gpuSnapshot = await gpuTask;

            cancellationToken.ThrowIfCancellationRequested();

            _latestResourceSnapshot = snapshot;
            if (networkSnapshot is not null)
            {
                _latestNetworkSnapshot = networkSnapshot;
            }
            if (sessionSnapshot is not null)
            {
                _latestSessionSnapshot = sessionSnapshot;
            }
            if (gpuSnapshot is not null)
            {
                _latestGpuTelemetrySnapshot = gpuSnapshot;
            }

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
            UpdateNetworkTelemetry(networkSnapshot);
            UpdateSessionTelemetry(sessionSnapshot);
            UpdateGpuDashboard(gpuSnapshot);

            ReevaluateFindings();
            var historyChanged = CaptureHistoryObservation(
                snapshot,
                networkSnapshot,
                sessionSnapshot,
                gpuSnapshot);
            var learningChanged = await CaptureLearningObservationAsync(
                snapshot,
                networkSnapshot,
                sessionSnapshot,
                cancellationToken);
            var previousLearningHealth = _learningService.DataHealth;
            var previousLastPersistence = _learningService.LastPersistedAt;
            var persisted = await _learningService.SaveIfDueAsync(
                _learningStore,
                DateTimeOffset.UtcNow,
                cancellationToken: cancellationToken);
            if (learningChanged || persisted ||
                previousLearningHealth != _learningService.DataHealth ||
                previousLastPersistence != _learningService.LastPersistedAt)
            {
                UpdateLearningDashboard();
            }
            if (learningChanged)
            {
                _historyService.ObserveLearningMilestones(
                    _learningService.GetDashboardSnapshot(
                        DateTimeOffset.UtcNow));
            }
            if (historyChanged)
            {
                await _historyService.SaveIfDueAsync(
                    _historyStore,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                UpdateHistoryDashboard();
            }
            ObserveInsightTriggers();
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

            var learningChanged = _learningService.TryBeginObservationAttempt(
                DateTimeOffset.UtcNow);
            if (learningChanged)
            {
                _learningService.RecordMissingPrerequisite();
                UpdateLearningDashboard();
            }

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

    private async Task<MachineNetworkSnapshot?> TryCaptureNetworkAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _networkProvider.GetAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return null;
        }
    }

    private async Task<MachineSessionSnapshot?> TryCaptureSessionAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _sessionProvider.GetAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return null;
        }
    }

    private async Task<MachineGpuTelemetrySnapshot?> TryCaptureGpuAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _gpuTelemetryProvider.GetAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return null;
        }
    }

    private void UpdatePresenceState(
        MachineOverallState overallState)
    {
        _latestOverallState = overallState;
        ApplyShellAtmosphere();
        ApplyPresenceVisualMode();
    }

    private Brush GetStateBrush(
        MachineOverallState overallState)
    {
        return (Brush)MainContent.Resources[
            "MatasuriStateAccentBrush"];
    }

    private MachineOverallState GetPresentationState()
    {
#if DEBUG
        return _presentationValidationOptions.State ??
            _latestOverallState;
#else
        return _latestOverallState;
#endif
    }

    private bool IsGeneratingPresentation()
    {
#if DEBUG
        return _presentationValidationOptions.IsGenerating ||
            _isExplanationRequestRunning;
#else
        return _isExplanationRequestRunning;
#endif
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
                Startup: _latestStartupInventorySnapshot,
                WindowsUpdate: _latestWindowsUpdateSnapshot,
                RebootPending: _latestRebootPendingSnapshot,
                Reliability: _latestReliabilitySnapshot));

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

    private void ObserveInsightTriggers()
    {
        var decision = _insightTriggerPolicy.ObserveTelemetry(
            _latestFindingsSnapshot,
            DateTimeOffset.UtcNow,
            IsInsightContextAvailable(),
            allowAutomaticGeneration: _initialContextHydrationCompleted);
        StartInsightGeneration(decision);
    }

    private async Task LoadLearningAsync()
    {
        try
        {
            await _learningService.LoadAsync(
                _learningStore,
                _windowCancellationTokenSource.Token);
            UpdateLearningDashboard();
        }
        catch (OperationCanceledException)
            when (_windowCancellationTokenSource.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            UpdateLearningDashboard();
        }
    }

    private async Task LoadHealthHistoryAsync()
    {
        try
        {
            await _healthHistoryService.LoadAsync(
                _healthHistoryStore,
                _windowCancellationTokenSource.Token);
            UpdateHealthDashboard();
            UpdateLearningDashboard();
        }
        catch (OperationCanceledException)
            when (_windowCancellationTokenSource.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            UpdateHealthDashboard();
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            await _historyService.LoadAsync(
                _historyStore,
                _windowCancellationTokenSource.Token);
            _historyService.BeginSession(DateTimeOffset.UtcNow);
            _historyService.ObserveLearningMilestones(
                _learningService.GetDashboardSnapshot(DateTimeOffset.UtcNow));
            UpdateHistoryDashboard();
        }
        catch (OperationCanceledException)
            when (_windowCancellationTokenSource.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            _historyService.BeginSession(DateTimeOffset.UtcNow);
            UpdateHistoryDashboard();
        }
    }

    private async Task RunHealthLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await RefreshHealthAsync(
                    isManualRefresh: false,
                    cancellationToken);
                await Task.Delay(
                    HealthRefreshInterval,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshHealthAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken)
    {
        if (_isHealthRequestRunning)
        {
            return;
        }

        _isHealthRequestRunning = true;
        UpdateRefreshHealthButtonState();
        if (isManualRefresh)
        {
            RefreshHealthButton.Content = "Refreshing...";
            await Task.Yield();
        }

        try
        {
            var updateTask = TryCaptureWindowsUpdateAsync(
                cancellationToken);
            var rebootTask = TryCaptureRebootPendingAsync(
                cancellationToken);
            var reliabilityTask = TryCaptureReliabilityAsync(
                cancellationToken);
            await Task.WhenAll(updateTask, rebootTask, reliabilityTask);
            cancellationToken.ThrowIfCancellationRequested();

            var update = await updateTask;
            var reboot = await rebootTask;
            var reliability = await reliabilityTask;
            if (update is not null)
            {
                _latestWindowsUpdateSnapshot = update;
            }
            if (reboot is not null)
            {
                _latestRebootPendingSnapshot = reboot;
            }
            if (reliability is not null)
            {
                _latestReliabilitySnapshot = reliability;
            }

            var observedAt = DateTimeOffset.UtcNow;
            _healthHistoryService.Observe(
                update,
                reboot,
                reliability,
                observedAt);
            _historyService.ObserveHealth(
                update,
                reboot,
                reliability,
                observedAt);
            await _healthHistoryService.SaveIfDueAsync(
                _healthHistoryStore,
                observedAt,
                cancellationToken);

            UpdateHealthDashboard();
            UpdateLearningDashboard();
            UpdateHistoryDashboard();
            ReevaluateFindings();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            HealthStatusText.Text =
                "Health context is temporarily unavailable.";
            HealthStatusText.Visibility = Visibility.Visible;
        }
        finally
        {
            _isHealthRequestRunning = false;
            if (!cancellationToken.IsCancellationRequested)
            {
                RefreshHealthButton.Content = "Refresh health";
                UpdateRefreshHealthButtonState();
            }
        }
    }

    private async Task<MachineWindowsUpdateSnapshot?>
        TryCaptureWindowsUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _windowsUpdateProvider.GetAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return null;
        }
    }

    private async Task<MachineRebootPendingSnapshot?>
        TryCaptureRebootPendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _rebootPendingProvider.GetAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return null;
        }
    }

    private async Task<MachineReliabilitySnapshot?>
        TryCaptureReliabilityAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _reliabilityProvider.GetAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return null;
        }
    }

    private async void OnRefreshHealthClicked(
        object sender,
        RoutedEventArgs e)
    {
        await RefreshHealthAsync(
            isManualRefresh: true,
            _windowCancellationTokenSource.Token);
    }

    private void UpdateRefreshHealthButtonState()
    {
        RefreshHealthButton.IsEnabled =
            !_isHealthRequestRunning &&
            !_windowCancellationTokenSource.IsCancellationRequested;
    }

    private async Task<bool> CaptureLearningObservationAsync(
        MachineResourceSnapshot resources,
        MachineNetworkSnapshot? network,
        MachineSessionSnapshot? session,
        CancellationToken cancellationToken)
    {
        if (!_learningService.TryBeginObservationAttempt(
            resources.CapturedAt))
        {
            return false;
        }

        if (!double.IsFinite(resources.CpuUsagePercent) ||
            resources.TotalMemoryBytes <= 0 ||
            resources.UsedMemoryBytes < 0 ||
            resources.UsedMemoryBytes > resources.TotalMemoryBytes)
        {
            _learningService.RecordMissingPrerequisite();
            return true;
        }

        var activityState = session?.CurrentUserInputState;
        if (activityState is null)
        {
            try
            {
                var activity = await _userActivityProvider.GetAsync(
                    cancellationToken);
                activityState = activity.State;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                _learningService.RecordMissingPrerequisite();
                return true;
            }
        }

        if (activityState is null || !Enum.IsDefined(activityState.Value))
        {
            _learningService.RecordMissingPrerequisite();
            return true;
        }

        var memoryPercent = resources.TotalMemoryBytes == 0
            ? 0d
            : resources.UsedMemoryBytes / (double)resources.TotalMemoryBytes * 100d;
        var systemVolume = _latestStorageSnapshot?.Volumes
            .FirstOrDefault(volume => volume.IsSystemVolume);
        double? freePercent = systemVolume is null ||
            systemVolume.TotalSizeBytes <= 0
                ? null
                : systemVolume.AvailableFreeSpaceBytes /
                    (double)systemVolume.TotalSizeBytes * 100d;
        var networkActivityClass = network?.Aggregate.ActivityClass ??
            MachineNetworkActivityClass.Unavailable;
        var receiveBytesPerSecond = networkActivityClass ==
                MachineNetworkActivityClass.Unavailable
            ? null
            : GetVerifiedRate(
                network?.Aggregate.ReceiveBytesPerSecond);
        var sendBytesPerSecond = networkActivityClass ==
                MachineNetworkActivityClass.Unavailable
            ? null
            : GetVerifiedRate(
                network?.Aggregate.SendBytesPerSecond);
        var behavioralFindings = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: _latestResourceSnapshot,
                Storage: _latestStorageSnapshot,
                FolderInspection: _latestFolderInspectionSnapshot,
                ClassicSoftware: _latestSoftwareInventorySnapshot,
                PackagedSoftware:
                    _latestPackagedSoftwareInventorySnapshot,
                Startup: _latestStartupInventorySnapshot));
        var observation = new MachineLearningObservation(
            resources.CapturedAt,
            resources.CpuUsagePercent,
            memoryPercent,
            activityState.Value,
            behavioralFindings.OverallState,
            behavioralFindings.Findings.Select(finding =>
                $"{finding.Code}:{finding.Severity}").ToArray(),
            freePercent,
            MachineInsightContextFingerprint.Create(behavioralFindings),
            networkActivityClass,
            receiveBytesPerSecond,
            sendBytesPerSecond);

        return _learningService.Observe(observation);
    }

    private bool CaptureHistoryObservation(
        MachineResourceSnapshot resources,
        MachineNetworkSnapshot? network,
        MachineSessionSnapshot? session,
        MachineGpuTelemetrySnapshot? gpu)
    {
        double? memoryPercent = resources.TotalMemoryBytes == 0
            ? null
            : resources.UsedMemoryBytes /
                (double)resources.TotalMemoryBytes * 100d;
        var systemVolume = _latestStorageSnapshot?.Volumes
            .FirstOrDefault(volume => volume.IsSystemVolume);
        double? freePercent = systemVolume is null ||
            systemVolume.TotalSizeBytes <= 0
                ? null
                : systemVolume.AvailableFreeSpaceBytes /
                    (double)systemVolume.TotalSizeBytes * 100d;
        var primaryGpu = gpu?.Adapters.FirstOrDefault();
        return _historyService.Observe(new MachineHistoryObservation(
            resources.CapturedAt,
            resources.CpuUsagePercent,
            memoryPercent,
            GetVerifiedRate(network?.Aggregate.ReceiveBytesPerSecond),
            GetVerifiedRate(network?.Aggregate.SendBytesPerSecond),
            session?.CurrentUserInputState,
            _latestOverallState,
            freePercent,
            primaryGpu?.GpuUtilizationPercent,
            primaryGpu?.MemoryUtilizationPercent,
            primaryGpu?.TemperatureCelsius,
            primaryGpu?.BoardPowerWatts));
    }

    private static double? GetVerifiedRate(double? value) =>
        value is not null && double.IsFinite(value.Value) && value.Value >= 0d
            ? value
            : null;

    private void UpdateGpuDashboard(MachineGpuTelemetrySnapshot? snapshot)
    {
        var adapter = snapshot?.Adapters.FirstOrDefault();
        if (adapter is null)
        {
            GpuAdapterNameText.Text = "Graphics telemetry unavailable";
            GpuProviderStatusText.Text = snapshot?.FailureCode ==
                    "nvml.no-device"
                ? "No accessible NVIDIA adapter was reported. Device inventory remains available."
                : "Detailed GPU telemetry unavailable for this adapter.";
            GpuUtilizationText.Text = "—";
            GpuMemoryText.Text = "—";
            GpuTemperatureText.Text = "—";
            GpuPowerText.Text = "—";
            GpuGraphicsClockText.Text = "—";
            GpuMemoryClockText.Text = "—";
            GpuFanText.Text = "Unavailable";
            return;
        }

        GpuAdapterNameText.Text = adapter.AdapterName ??
            "NVIDIA graphics adapter";
        GpuProviderStatusText.Text = snapshot!.Availability ==
                MachineGpuTelemetryAvailability.Available
            ? "Verified through the installed NVIDIA NVML driver interface"
            : "Partial telemetry from the installed NVIDIA NVML driver interface";
        GpuUtilizationText.Text = FormatPercent(
            adapter.GpuUtilizationPercent);
        GpuMemoryText.Text = adapter.MemoryUsedBytes is { } used &&
            adapter.MemoryTotalBytes is { } total
                ? $"{used / BytesPerGibibyte:F1} / " +
                    $"{total / BytesPerGibibyte:F1} GB"
                : "—";
        GpuTemperatureText.Text = adapter.TemperatureCelsius is { } temperature
            ? $"{temperature:F0} °C"
            : "—";
        GpuPowerText.Text = adapter.BoardPowerWatts is { } power
            ? $"{power:F0} W"
            : "—";
        GpuGraphicsClockText.Text = adapter.GraphicsClockMHz is { } graphics
            ? $"{graphics:N0} MHz"
            : "—";
        GpuMemoryClockText.Text = adapter.MemoryClockMHz is { } memory
            ? $"{memory:N0} MHz"
            : "—";
        GpuFanText.Text = adapter.FanPercent is { } fan
            ? $"{fan:F0}% of reported maximum"
            : "Fan telemetry unavailable";
    }

    private static string FormatPercent(double? value) =>
        value is { } percentage ? $"{percentage:F0}%" : "—";

    private void OnHistoryRangeClicked(
        object sender,
        RoutedEventArgs args)
    {
        _selectedHistoryRange = (sender as Button)?.Tag?.ToString() switch
        {
            "7d" => MachineHistoryRange.Last7Days,
            "30d" => MachineHistoryRange.Last30Days,
            "all" => MachineHistoryRange.All,
            _ => MachineHistoryRange.Last24Hours
        };
        UpdateHistoryDashboard();
    }

    private void UpdateHistoryDashboard()
    {
        if (HistoryPage is null)
        {
            return;
        }
        var snapshot = _historyService.GetSnapshot(
            _selectedHistoryRange,
            DateTimeOffset.UtcNow);
        HistoryObservedDurationText.Text = snapshot.TotalObservedDuration >
                TimeSpan.Zero
            ? $"{FormatDuration(snapshot.TotalObservedDuration)} observed"
            : "Beginning now";
        HistoryResolutionText.Text =
            $"{FormatHistoryResolution(snapshot.Resolution)} rollups · " +
            "offline and suspended time remain gaps";
        SetHistoryRangeButtonState();

        var cpu = AggregateHistoryMetric(
            snapshot.Rollups,
            static item => item.CpuUtilizationPercent);
        var memory = AggregateHistoryMetric(
            snapshot.Rollups,
            static item => item.MemoryUtilizationPercent);
        var gpu = AggregateHistoryMetric(
            snapshot.Rollups,
            static item => item.GpuUtilizationPercent);
        var summary = new List<string>();
        if (cpu is not null)
        {
            summary.Add($"CPU {cpu.Mean:F0}% avg · {cpu.Maximum:F0}% peak");
        }
        if (memory is not null)
        {
            summary.Add($"Memory {memory.Mean:F0}% avg");
        }
        if (gpu is not null)
        {
            summary.Add($"GPU {gpu.Mean:F0}% avg · {gpu.Maximum:F0}% peak");
        }
        HistoryResourceSummaryText.Text = summary.Count == 0
            ? "Waiting for history"
            : string.Join("\n", summary);

        var activeTicks = snapshot.Rollups.Aggregate(
            0L,
            (total, item) => SaturatingAddTicks(
                total,
                item.ActivityDurations.ActiveTicks));
        var idleTicks = snapshot.Rollups.Aggregate(
            0L,
            (total, item) => SaturatingAddTicks(
                total,
                item.ActivityDurations.IdleTicks));
        SetDurationColumns(
            [HistoryActiveColumn, HistoryIdleColumn],
            [activeTicks, idleTicks]);
        HistoryActivityText.Text =
            $"Active {FormatDuration(TimeSpan.FromTicks(activeTicks))} · " +
            $"Idle {FormatDuration(TimeSpan.FromTicks(idleTicks))}";

        var stateTicks = new[]
        {
            snapshot.Rollups.Aggregate(0L, (total, item) =>
                SaturatingAddTicks(total, item.StateDurations.StableTicks)),
            snapshot.Rollups.Aggregate(0L, (total, item) =>
                SaturatingAddTicks(total, item.StateDurations.AttentionTicks)),
            snapshot.Rollups.Aggregate(0L, (total, item) =>
                SaturatingAddTicks(total, item.StateDurations.WarningTicks)),
            snapshot.Rollups.Aggregate(0L, (total, item) =>
                SaturatingAddTicks(total, item.StateDurations.CriticalTicks)),
            snapshot.Rollups.Aggregate(0L, (total, item) =>
                SaturatingAddTicks(total, item.StateDurations.UnknownTicks))
        };
        SetDurationColumns(
            [
                HistoryStableColumn,
                HistoryAttentionColumn,
                HistoryWarningColumn,
                HistoryCriticalColumn,
                HistoryUnknownColumn
            ],
            stateTicks);
        HistoryStateDurationText.Text = string.Join(
            " · ",
            new[]
            {
                ("Stable", stateTicks[0]),
                ("Attention", stateTicks[1]),
                ("Warning", stateTicks[2]),
                ("Critical", stateTicks[3]),
                ("Unknown", stateTicks[4])
            }.Where(item => item.Item2 > 0).Select(item =>
                $"{item.Item1} " +
                FormatDuration(TimeSpan.FromTicks(item.Item2)))) switch
        {
            "" => "No state-duration evidence yet",
            var text => text
        };

        var groupedEvents = MachineHistoryEventGrouper.GroupForDisplay(
            snapshot.Events)
            .Take(200)
            .Select(CreateHistoryEventDisplayItem)
            .ToArray();
        HistoryEventsList.ItemsSource = groupedEvents;
        HistoryEventsEmptyText.Visibility = groupedEvents.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        RenderHistoryTrends(snapshot.Rollups);
    }

    private void SetHistoryRangeButtonState()
    {
        var selected = _selectedHistoryRange switch
        {
            MachineHistoryRange.Last7Days => History7DayButton,
            MachineHistoryRange.Last30Days => History30DayButton,
            MachineHistoryRange.All => HistoryAllButton,
            _ => History24HourButton
        };
        foreach (var button in new[]
        {
            History24HourButton,
            History7DayButton,
            History30DayButton,
            HistoryAllButton
        })
        {
            button.Opacity = ReferenceEquals(button, selected) ? 1d : 0.55d;
            button.FontWeight = ReferenceEquals(button, selected)
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    private static string FormatHistoryResolution(
        MachineHistoryResolution resolution) => resolution switch
        {
            MachineHistoryResolution.FiveMinutes => "5-minute",
            MachineHistoryResolution.Hour => "Hourly",
            MachineHistoryResolution.Day => "Daily",
            MachineHistoryResolution.Month => "Monthly",
            _ => "Bounded"
        };

    private static HistoryEventDisplayItem CreateHistoryEventDisplayItem(
        MachineHistoryEvent item)
    {
        var title = item.Count > 1
            ? $"{item.Title} · {item.Count} occurrences"
            : item.Title;
        var time = item.Count > 1 && item.PeriodStart is { } start
            ? $"{start.ToLocalTime():HH:mm}–" +
                $"{(item.PeriodEnd ?? item.OccurredAt).ToLocalTime():HH:mm}"
            : item.OccurredAt.ToLocalTime().ToString("HH:mm");
        return new(
            time,
            title,
            item.Detail,
            string.IsNullOrWhiteSpace(item.Detail)
                ? Visibility.Collapsed
                : Visibility.Visible);
    }

    private void OnHistoryTrendSizeChanged(
        object sender,
        SizeChangedEventArgs args) => UpdateHistoryDashboard();

    private void RenderHistoryTrends(
        IReadOnlyList<MachineHistoryRollup> rollups)
    {
        var width = Math.Max(1d, HistoryTrendCanvas.ActualWidth);
        var height = Math.Max(1d, HistoryTrendCanvas.ActualHeight);
        SetHistoryPath(
            HistoryCpuPolyline,
            CreateHistorySegments(
                rollups,
                static item => item.CpuUtilizationPercent?.Mean,
                width,
                height));
        SetHistoryPath(
            HistoryMemoryPolyline,
            CreateHistorySegments(
                rollups,
                static item => item.MemoryUtilizationPercent?.Mean,
                width,
                height));
        var gpuSegments = CreateHistorySegments(
            rollups,
            static item => item.GpuUtilizationPercent?.Mean,
            width,
            height);
        SetHistoryPath(HistoryGpuPolyline, gpuSegments);
        var hasGpuSeries = gpuSegments.Any(segment => segment.Count > 1);
        HistoryGpuPolyline.Visibility = hasGpuSeries
            ? Visibility.Visible
            : Visibility.Collapsed;
        HistoryGpuLegendText.Visibility = hasGpuSeries
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static IReadOnlyList<IReadOnlyList<
        global::Windows.Foundation.Point>> CreateHistorySegments(
            IReadOnlyList<MachineHistoryRollup> rollups,
            Func<MachineHistoryRollup, double?> select,
            double width,
            double height)
    {
        if (rollups.Count == 0)
        {
            return [];
        }
        var start = rollups[0].BucketStart;
        var end = rollups[^1].BucketEnd;
        var durationTicks = Math.Max(1L, (end - start).Ticks);
        var segments = new List<List<global::Windows.Foundation.Point>>();
        List<global::Windows.Foundation.Point>? current = null;
        DateTimeOffset? previousEnd = null;
        foreach (var rollup in rollups)
        {
            var value = select(rollup);
            var isContinuous = previousEnd is null ||
                rollup.BucketStart <= previousEnd.Value;
            if (value is null || !double.IsFinite(value.Value))
            {
                current = null;
                previousEnd = rollup.BucketEnd;
                continue;
            }
            if (current is null || !isContinuous)
            {
                current = [];
                segments.Add(current);
            }
            var x = (rollup.BucketStart - start).Ticks /
                (double)durationTicks * width;
            var y = height - Math.Clamp(value.Value, 0d, 100d) /
                100d * height;
            current.Add(new(x, y));
            previousEnd = rollup.BucketEnd;
        }
        return segments;
    }

    private static void SetHistoryPath(
        Microsoft.UI.Xaml.Shapes.Path path,
        IReadOnlyList<IReadOnlyList<global::Windows.Foundation.Point>>
            segments)
    {
        var geometry = new Microsoft.UI.Xaml.Media.PathGeometry();
        foreach (var points in segments.Where(item => item.Count > 0))
        {
            var figure = new Microsoft.UI.Xaml.Media.PathFigure
            {
                StartPoint = points[0],
                IsClosed = false,
                IsFilled = false
            };
            foreach (var point in points.Skip(1))
            {
                figure.Segments.Add(
                    new Microsoft.UI.Xaml.Media.LineSegment
                    {
                        Point = point
                    });
            }
            geometry.Figures.Add(figure);
        }
        path.Data = geometry;
    }

    private static HistoryMetricAggregate? AggregateHistoryMetric(
        IEnumerable<MachineHistoryRollup> rollups,
        Func<MachineHistoryRollup, MachineHistoryNumericSummary?> select)
    {
        var values = rollups.Select(select)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
        if (values.Length == 0)
        {
            return null;
        }
        var count = values.Sum(item => (double)item.SampleCount);
        return new(
            values.Sum(item => item.Mean * item.SampleCount) / count,
            values.Max(item => item.Maximum));
    }

    private static void SetDurationColumns(
        IReadOnlyList<ColumnDefinition> columns,
        IReadOnlyList<long> values)
    {
        var any = values.Any(value => value > 0);
        for (var index = 0; index < columns.Count; index++)
        {
            columns[index].Width = new GridLength(
                any ? Math.Max(0, values[index]) : index == 0 ? 1 : 0,
                GridUnitType.Star);
        }
    }

    private static long SaturatingAddTicks(long left, long right) =>
        right > 0 && left > long.MaxValue - right
            ? long.MaxValue
            : left + right;

    private void UpdateNetworkTelemetry(MachineNetworkSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            NetworkStatusText.Text =
                "Network telemetry is temporarily unavailable.";
            NetworkStatusText.Visibility = Visibility.Visible;
            if (_latestNetworkSnapshot is null)
            {
                OverviewNetworkActivityText.Text = UnavailableValue;
                OverviewNetworkReceiveText.Text = UnavailableValue;
                OverviewNetworkSendText.Text = UnavailableValue;
                OverviewNetworkInterfaceText.Text =
                    "Interface status unavailable";
                NetworkReceiveRateText.Text = UnavailableValue;
                NetworkSendRateText.Text = UnavailableValue;
                NetworkActivityClassText.Text = UnavailableValue;
                NetworkInterfacesList.ItemsSource =
                    Array.Empty<NetworkInterfaceDisplayItem>();
                NetworkInterfacesEmptyText.Visibility = Visibility.Visible;
            }
            return;
        }

        var aggregate = snapshot.Aggregate;
        OverviewNetworkActivityText.Text = aggregate.ActivityClass.ToString();
        OverviewNetworkReceiveText.Text =
            $"Receive {FormatByteRate(aggregate.ReceiveBytesPerSecond)}";
        OverviewNetworkSendText.Text =
            $"Send {FormatByteRate(aggregate.SendBytesPerSecond)}";
        OverviewNetworkInterfaceText.Text =
            FormatOnlineInterfaceCount(aggregate.ActiveInterfaceCount);

        NetworkReceiveRateText.Text =
            FormatByteRate(aggregate.ReceiveBytesPerSecond);
        NetworkSendRateText.Text =
            FormatByteRate(aggregate.SendBytesPerSecond);
        NetworkActivityClassText.Text = aggregate.ActivityClass.ToString();
        var interfaceItems = snapshot.Interfaces
            .Take(MaximumNetworkInterfaceCount)
            .Select(CreateNetworkInterfaceDisplayItem)
            .ToArray();
        NetworkInterfacesList.ItemsSource = interfaceItems;
        NetworkInterfacesEmptyText.Visibility = interfaceItems.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        NetworkStatusText.Text = snapshot.Interfaces.Count >
                MaximumNetworkInterfaceCount
            ? $"Showing {MaximumNetworkInterfaceCount:N0} of " +
                $"{snapshot.Interfaces.Count:N0} active interfaces."
            : string.Empty;
        NetworkStatusText.Visibility = string.IsNullOrEmpty(
            NetworkStatusText.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    private void UpdateSessionTelemetry(MachineSessionSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            if (_latestSessionSnapshot is null)
            {
                OverviewSessionUptimeText.Text =
                    "Session uptime unavailable";
                OverviewSessionActivityText.Text =
                    "Input state unavailable";
                SessionSystemUptimeText.Text = UnavailableValue;
                SessionMachineUptimeText.Text = UnavailableValue;
                SessionInputStateText.Text = UnavailableValue;
                SessionIdleDurationText.Text = UnavailableValue;
            }
            return;
        }

        OverviewSessionUptimeText.Text =
            $"Windows up {FormatUptime(snapshot.SystemUptime)} · " +
            $"Matasuri running {FormatUptime(snapshot.MachineUptime)}";
        OverviewSessionActivityText.Text =
            $"{snapshot.CurrentUserInputState} · " +
            $"last input {FormatInputAge(snapshot.CurrentUserIdleDuration)} ago";
        SessionSystemUptimeText.Text = FormatUptime(snapshot.SystemUptime);
        SessionMachineUptimeText.Text = FormatUptime(snapshot.MachineUptime);
        SessionInputStateText.Text = snapshot.CurrentUserInputState.ToString();
        SessionIdleDurationText.Text =
            FormatInputAge(snapshot.CurrentUserIdleDuration);
    }

    private void UpdateHealthDashboard()
    {
        UpdateWindowsUpdateDashboard(_latestWindowsUpdateSnapshot);
        UpdateRestartDashboard(_latestRebootPendingSnapshot);
        UpdateReliabilityDashboard(_latestReliabilitySnapshot);

        var statusMessages = new List<string>();
        if (_latestWindowsUpdateSnapshot is { } update &&
            update.DataStatus != MachineHealthDataStatus.Complete)
        {
            statusMessages.Add(update.VerifiedAt is null
                ? "Windows Update status unavailable"
                : update.RefreshStatus ==
                    MachineWindowsUpdateRefreshStatus.CachedAfterFailure
                    ? "Windows Update is showing its last verified state"
                    : "some Windows Update details are unavailable");
        }
        if (_latestRebootPendingSnapshot?.IsPartial == true)
        {
            statusMessages.Add("restart evidence partial");
        }
        if (_latestReliabilitySnapshot is { } reliability &&
            reliability.DataStatus != MachineHealthDataStatus.Complete)
        {
            statusMessages.Add(reliability.VerifiedAt is null
                ? "reliability history unavailable"
                : "reliability history partial");
        }

        HealthStatusText.Text = string.Join(" · ", statusMessages);
        HealthStatusText.Visibility = statusMessages.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateWindowsUpdateDashboard(
        MachineWindowsUpdateSnapshot? snapshot)
    {
        if (snapshot?.VerifiedAt is null)
        {
            WindowsUpdateStateText.Text = "Status unavailable";
            WindowsUpdateFreshnessText.Text = snapshot is null
                ? "Waiting for local Windows Update state"
                : "No verified state is available";
            WindowsUpdatePendingText.Text = UnavailableValue;
            WindowsUpdateImportantText.Text = UnavailableValue;
            WindowsUpdateLastScanText.Text = UnavailableValue;
            WindowsUpdateLastInstallText.Text = UnavailableValue;
            WindowsUpdateHistoryList.ItemsSource =
                Array.Empty<UpdateHistoryDisplayItem>();
            WindowsUpdateHistoryEmptyText.Visibility = Visibility.Visible;
            return;
        }

        WindowsUpdateStateText.Text = FormatWindowsUpdateState(snapshot);
        var age = DateTimeOffset.UtcNow - snapshot.VerifiedAt.Value;
        WindowsUpdateFreshnessText.Text = snapshot.RefreshStatus ==
                MachineWindowsUpdateRefreshStatus.CachedAfterFailure
            ? $"Last verified {FormatRelativeAge(age)} ago · latest refresh failed"
            : $"Verified {FormatRelativeAge(age)} ago";
        WindowsUpdatePendingText.Text = snapshot.PendingUpdateCount is { } pending
            ? $"{pending:N0}"
            : UnavailableValue;
        WindowsUpdateImportantText.Text =
            snapshot.PendingImportantUpdateCount is { } important
                ? $"{important:N0}"
                : UnavailableValue;
        WindowsUpdateLastScanText.Text = FormatHealthDateTime(
            snapshot.LastSuccessfulUpdateScan,
            UnavailableValue);
        WindowsUpdateLastInstallText.Text = FormatHealthDateTime(
            snapshot.LastSuccessfulUpdateInstall,
            UnavailableValue);

        var history = snapshot.RecentUpdateHistory
            .Take(MaximumUpdateHistoryDisplayCount)
            .Select(entry => new UpdateHistoryDisplayItem(
                Header: $"{entry.OccurredAt.ToLocalTime():MMM d · h:mm tt} · " +
                    FormatUpdateHistoryResult(entry.Result),
                Title: entry.Title,
                Details: string.Join(
                    " · ",
                    new[] { entry.KnowledgeBaseId, entry.Category }
                        .Where(value => !string.IsNullOrWhiteSpace(value)))))
            .ToArray();
        WindowsUpdateHistoryList.ItemsSource = history;
        WindowsUpdateHistoryEmptyText.Visibility = history.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateRestartDashboard(
        MachineRebootPendingSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.IsPending is null)
        {
            RestartStateText.Text = "Restart status unknown";
            RestartReasonsText.Text = snapshot?.IsPartial == true
                ? "Available restart indicators were inconclusive."
                : "Waiting for local restart indicators.";
            RestartDataStatusText.Text = snapshot is null
                ? string.Empty
                : $"Checked {FormatRelativeAge(DateTimeOffset.UtcNow - snapshot.CapturedAt)} ago";
            OverviewHealthPrimaryText.Text = "Restart status unknown";
            return;
        }

        RestartStateText.Text = snapshot.IsPending == true
            ? "Restart pending"
            : "No restart pending";
        RestartReasonsText.Text = snapshot.IsPending == true
            ? string.Join(
                " · ",
                snapshot.Reasons.Select(FormatRebootReason))
            : "No verified restart indicator is currently set.";
        RestartDataStatusText.Text =
            $"Checked {FormatRelativeAge(DateTimeOffset.UtcNow - snapshot.CapturedAt)} ago" +
            (snapshot.IsPartial ? " · partial evidence" : string.Empty);
        OverviewHealthPrimaryText.Text = snapshot.IsPending == true
            ? "Restart pending"
            : "No restart pending";
    }

    private void UpdateReliabilityDashboard(
        MachineReliabilitySnapshot? snapshot)
    {
        if (snapshot?.VerifiedAt is null)
        {
            SetReliabilityCounts(null);
            ReliabilityFreshnessText.Text = snapshot is null
                ? "Waiting for Windows reliability history"
                : "Reliability history unavailable";
            ReliabilityIncidentsList.ItemsSource =
                Array.Empty<ReliabilityIncidentDisplayItem>();
            ReliabilityIncidentsEmptyText.Visibility = Visibility.Visible;
            RecurringFailuresList.ItemsSource =
                Array.Empty<RecurringFailureDisplayItem>();
            RecurringFailuresEmptyText.Visibility = Visibility.Visible;
            OverviewHealthSecondaryText.Text =
                "Reliability history unavailable";
            return;
        }

        var sevenDays = snapshot.Summary.Last7Days;
        SetReliabilityCounts(sevenDays);
        ReliabilityFreshnessText.Text =
            $"Last 7 days · verified " +
            $"{FormatRelativeAge(DateTimeOffset.UtcNow - snapshot.VerifiedAt.Value)} ago" +
            (snapshot.DataStatus == MachineHealthDataStatus.Complete
                ? string.Empty
                : " · partial");
        var incidents = snapshot.Incidents
            .Take(MaximumReliabilityIncidentDisplayCount)
            .Select(incident => new ReliabilityIncidentDisplayItem(
                Header: $"{incident.OccurredAt.ToLocalTime():MMM d · h:mm tt}",
                Category: FormatReliabilityCategory(incident.Category),
                Details: CreateReliabilityIncidentDetails(incident)))
            .ToArray();
        ReliabilityIncidentsList.ItemsSource = incidents;
        ReliabilityIncidentsEmptyText.Visibility = incidents.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var recurring = snapshot.Summary.RecurringApplications
            .Take(MaximumRecurringFailureDisplayCount)
            .Select(item =>
            {
                var thirtyDayNoun = item.IncidentCountLast30Days == 1
                    ? "incident"
                    : "incidents";
                var sevenDayNoun = item.IncidentCountLast7Days == 1
                    ? "incident"
                    : "incidents";
                return new RecurringFailureDisplayItem(
                    ApplicationName: item.ApplicationName,
                    Details:
                        $"{item.IncidentCountLast30Days:N0} " +
                        $"{thirtyDayNoun} in 30 days · " +
                        $"{item.IncidentCountLast7Days:N0} " +
                        $"{sevenDayNoun} in 7 days");
            })
            .ToArray();
        RecurringFailuresList.ItemsSource = recurring;
        RecurringFailuresEmptyText.Visibility = recurring.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var appFailures = sevenDays.ApplicationCrashCount +
            sevenDays.ApplicationHangCount;
        OverviewHealthSecondaryText.Text = appFailures > 0
            ? $"{appFailures:N0} app " +
                (appFailures == 1 ? "failure" : "failures") +
                " recorded in 7 days"
            : sevenDays.UnexpectedShutdownCount > 0
                ? $"{sevenDays.UnexpectedShutdownCount:N0} unexpected " +
                    (sevenDays.UnexpectedShutdownCount == 1
                        ? "shutdown"
                        : "shutdowns") + " recorded in 7 days"
                : sevenDays.TotalIncidentCount > 0
                    ? $"{sevenDays.TotalIncidentCount:N0} reliability " +
                        (sevenDays.TotalIncidentCount == 1
                            ? "incident"
                            : "incidents") + " recorded in 7 days"
                : snapshot.DataStatus == MachineHealthDataStatus.Complete
                    ? "No reliability incidents recorded in the verified 7-day window"
                    : "Reliability history is partially available";
    }

    private void SetReliabilityCounts(
        MachineReliabilityWindowSummary? summary)
    {
        ReliabilityCrashCountText.Text = summary is null
            ? UnavailableValue
            : $"{summary.ApplicationCrashCount:N0}";
        ReliabilityHangCountText.Text = summary is null
            ? UnavailableValue
            : $"{summary.ApplicationHangCount:N0}";
        ReliabilityShutdownCountText.Text = summary is null
            ? UnavailableValue
            : $"{summary.UnexpectedShutdownCount:N0}";
        ReliabilityUpdateFailureCountText.Text = summary is null
            ? UnavailableValue
            : $"{summary.UpdateFailureCount:N0}";
        ReliabilityHardwareFailureCountText.Text = summary is null
            ? UnavailableValue
            : $"{summary.HardwareFailureCount:N0}";
    }

    private static string FormatWindowsUpdateState(
        MachineWindowsUpdateSnapshot snapshot) => snapshot.UpdateState switch
    {
        MachineWindowsUpdateState.UpToDate => "Up to date",
        MachineWindowsUpdateState.UpdatesAvailable =>
            snapshot.PendingUpdateCount is { } pending
                ? $"{pending:N0} " +
                    (pending == 1 ? "update available" : "updates available")
                : "Updates available",
        MachineWindowsUpdateState.InstallPending => "Installation pending",
        MachineWindowsUpdateState.RestartRequired => "Restart required",
        _ => "Status unavailable"
    };

    private static string FormatUpdateHistoryResult(
        MachineWindowsUpdateHistoryResult result) => result switch
    {
        MachineWindowsUpdateHistoryResult.Succeeded => "Installed",
        MachineWindowsUpdateHistoryResult.SucceededWithErrors =>
            "Installed with errors",
        MachineWindowsUpdateHistoryResult.Failed => "Failed",
        MachineWindowsUpdateHistoryResult.Cancelled => "Cancelled",
        MachineWindowsUpdateHistoryResult.InProgress => "In progress",
        _ => "Result unavailable"
    };

    private static string FormatRebootReason(
        MachineRebootPendingReason reason) => reason switch
    {
        MachineRebootPendingReason.WindowsUpdate => "Windows Update",
        MachineRebootPendingReason.ComponentServicing =>
            "Component servicing",
        MachineRebootPendingReason.PendingFileRename =>
            "Pending file rename",
        MachineRebootPendingReason.ComputerRename => "Computer rename",
        _ => "Other Windows indicator"
    };

    private static string FormatReliabilityCategory(
        MachineReliabilityIncidentCategory category) => category switch
    {
        MachineReliabilityIncidentCategory.ApplicationCrash =>
            "Application crash",
        MachineReliabilityIncidentCategory.ApplicationHang =>
            "Application hang",
        MachineReliabilityIncidentCategory.UnexpectedShutdown =>
            "Unexpected shutdown",
        MachineReliabilityIncidentCategory.WindowsFailure =>
            "Windows failure",
        MachineReliabilityIncidentCategory.HardwareFailure =>
            "Hardware-error record",
        MachineReliabilityIncidentCategory.UpdateFailure =>
            "Update failure",
        MachineReliabilityIncidentCategory.InstallFailure =>
            "Install failure",
        _ => "Reliability incident"
    };

    private static string CreateReliabilityIncidentDetails(
        MachineReliabilityIncident incident)
    {
        var details = new[]
        {
            incident.ApplicationName,
            incident.UpdateIdentifier,
            incident.FailureCode,
            incident.EventId is { } eventId ? $"Event {eventId}" : null
        }.Where(value => !string.IsNullOrWhiteSpace(value));
        return string.Join(" · ", details);
    }

    private static string FormatHealthDateTime(
        DateTimeOffset? value,
        string unavailable) => value is null
        ? unavailable
        : value.Value.ToLocalTime().ToString(
            "MMM d · h:mm tt",
            CultureInfo.CurrentCulture);

    private static string FormatRelativeAge(TimeSpan age)
    {
        var bounded = age < TimeSpan.Zero ? TimeSpan.Zero : age;
        if (bounded.TotalDays >= 1d)
        {
            return $"{(int)bounded.TotalDays}d";
        }
        if (bounded.TotalHours >= 1d)
        {
            return $"{(int)bounded.TotalHours}h";
        }
        if (bounded.TotalMinutes >= 1d)
        {
            return $"{Math.Max(1, (int)bounded.TotalMinutes)}m";
        }
        return "under a minute";
    }

    private static NetworkInterfaceDisplayItem
        CreateNetworkInterfaceDisplayItem(
            MachineNetworkInterfaceSnapshot networkInterface) => new(
                networkInterface.Name,
                $"{networkInterface.OperationalStatus} · " +
                    networkInterface.InterfaceType,
                networkInterface.Description ?? string.Empty,
                FormatLinkSpeed(
                    networkInterface.ReceiveLinkSpeedBitsPerSecond,
                    networkInterface.TransmitLinkSpeedBitsPerSecond),
                networkInterface.BytesReceived is null
                    ? "Received unavailable"
                    : $"Received {FormatBytes(networkInterface.BytesReceived.Value)}",
                networkInterface.BytesSent is null
                    ? "Sent unavailable"
                    : $"Sent {FormatBytes(networkInterface.BytesSent.Value)}");

    private static string FormatOnlineInterfaceCount(int count) =>
        $"{Math.Max(0, count):N0} " +
        (count == 1 ? "interface" : "interfaces") + " online";

    private void UpdateLearningDashboard()
    {
        var snapshot = _learningService.GetDashboardSnapshot(
            DateTimeOffset.UtcNow);
        var current = snapshot.CurrentObservation;
        var baseline = snapshot.CurrentBaseline;
        var confidence = baseline?.Confidence ??
            MachineLearningConfidence.Calibrating;

        LearningConfidenceText.Text = confidence.ToString();
        LearningObservedDurationText.Text =
            $"{FormatDuration(snapshot.ObservedDuration)} observed";
        LearningObservationText.Text =
            FormatSampleCount(snapshot.ObservationCount);

        var sessionCount = snapshot.Metadata.LifetimeMachineSessionCount;
        LearningPageObservedText.Text =
            $"{FormatDuration(snapshot.ObservedDuration)} across " +
            $"{sessionCount:N0} Matasuri " +
            (sessionCount == 1 ? "session" : "sessions");
        LearningPageLifetimeObservationsText.Text =
            $"{snapshot.Metadata.LifetimeAcceptedObservationCount:N0} lifetime";
        LearningPageContextCountText.Text =
            $"{snapshot.ContextProfiles.Count:N0} / " +
            $"{MachineLearningService.MaximumContextProfileCount:N0}";
        LearningPageEstablishedProfilesText.Text =
            $"{snapshot.ContextProfiles.Count(profile => profile.Confidence == MachineLearningConfidence.Established):N0}";
        LearningPageBroaderPatternCountText.Text =
            $"{snapshot.BroaderPatterns.Count:N0}";
        LearningPageSessionCountText.Text = $"{sessionCount:N0}";
        LearningPageFirstLearnedText.Text = FormatLearningDateTime(
            snapshot.Metadata.FirstLearningAt,
            "Not yet observed");
        LearningPageLastLearnedText.Text = FormatLearningDateTime(
            snapshot.Metadata.LastLearningAt,
            "Not yet observed");
        LearningPageRawObservationsText.Text =
            $"{snapshot.RawObservationCount:N0} / " +
            $"{MachineLearningService.MaximumObservationCount:N0}";
        LearningPageRecentEpisodesText.Text =
            $"{snapshot.RecentEpisodeCount:N0} / " +
            $"{MachineLearningService.MaximumEpisodeCount:N0}";
        LearningPageCurrentContextText.Text = current is null
            ? "Waiting for verified telemetry"
            : $"{current.ActivityState} · " +
                $"{current.Timestamp.ToLocalTime():h tt}";

        LearningPageCurrentBucketText.Text = baseline is null
            ? "Waiting"
            : $"{FormatLearningHour(baseline.LocalHour)} · " +
                $"{baseline.ActivityState}";
        LearningPageCurrentSamplesText.Text =
            $"{baseline?.SampleCount ?? 0:N0}";
        LearningPageObservedDaysText.Text =
            $"{baseline?.ObservedDayCount ?? 0:N0} / " +
            $"{MachineLearningService.EstablishedObservedDayCount:N0}";
        LearningPageConfidenceText.Text = confidence.ToString();

        var orderedProfiles = snapshot.ContextProfiles
            .OrderBy(item => baseline is not null &&
                item.LocalHour == baseline.LocalHour &&
                item.ActivityState == baseline.ActivityState ? 0 : 1)
            .ThenBy(item => item.Freshness)
            .ThenByDescending(item => item.LastReinforcedAt)
            .ThenBy(item => item.LocalHour)
            .ThenBy(item => item.ActivityState)
            .Select(CreateLearningProfileDisplayItem)
            .ToArray();
        LearningProfilesList.ItemsSource = orderedProfiles;
        LearningProfilesEmptyText.Visibility = orderedProfiles.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var patterns = snapshot.BroaderPatterns
            .OrderByDescending(item =>
                item.Confidence == MachineLearningConfidence.Established)
            .ThenBy(item => item.Freshness)
            .ThenBy(item => item.StartHour)
            .ThenBy(item => item.ActivityState)
            .Select(CreateLearningPatternDisplayItem)
            .ToArray();
        LearningPatternsList.ItemsSource = patterns;
        LearningPatternsEmptyText.Visibility = patterns.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var healthLearnedItems = MachineHealthLearnedItemProjector.Project(
            _healthHistoryService.GetSnapshot());
        var learnedItems = snapshot.LearnedItems
            .Concat(healthLearnedItems)
            .Take(MachineLearnedItemProjector.DefaultMaximumItemCount)
            .Select(item => new LearnedItemDisplayItem(
                $"{FormatLearningLayer(item.Layer)} · " +
                    (item.IsEarlyObservation
                        ? $"Early · {item.Confidence}"
                        : item.Confidence ==
                            MachineLearningConfidence.Established
                            ? "Established"
                            : "Recorded"),
                item.Text,
                item.Layer == MachineLearningMemoryLayer.HealthHistory
                    ? $"Evidence · {item.EvidenceCount:N0} verified " +
                        (item.EvidenceCount == 1 ? "record" : "records")
                    : $"Evidence · {FormatSampleCount(item.EvidenceCount)}"))
            .ToArray();
        LearnedItemsList.ItemsSource = learnedItems;
        LearningItemsEmptyText.Visibility = learnedItems.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var episodes = MachineLearningEpisodeProjector
            .Project(snapshot.RecentEpisodes)
            .Select(CreateLearningEpisodeDisplayItem)
            .ToArray();
        RecentLearningEpisodesList.ItemsSource = episodes;
        LearningEpisodesEmptyText.Visibility = episodes.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        LearningDataHealthText.Text = FormatLearningDataHealth(
            snapshot.DataHealth);
        LearningAcceptedText.Text =
            $"{snapshot.Diagnostics.AcceptedObservationCount:N0}";
        LearningThrottledText.Text =
            $"{snapshot.Diagnostics.ThrottledObservationCount:N0}";
        LearningSkippedText.Text =
            $"{snapshot.Diagnostics.MissingPrerequisiteCount:N0} missing prerequisites";
        LearningLastAcceptedText.Text = FormatLearningTimestamp(
            snapshot.Diagnostics.LastAcceptedObservationAt,
            "Not yet observed");
        LearningDirtyStateText.Text = snapshot.IsDirty
            ? "Changes waiting for the next periodic save"
            : "No pending changes";
        LearningLastPersistedText.Text = FormatLearningDateTime(
            snapshot.LastPersistedAt,
            "Not yet persisted");
        LearningSchemaText.Text =
            $"v{snapshot.Metadata.PersistedSchemaVersion}";
        UpdateLearningRuntimeStatus();
    }

    private static LearningProfileDisplayItem
        CreateLearningProfileDisplayItem(
            MachineLearningContextProfile profile)
    {
        var valueLabel = profile.Confidence ==
                MachineLearningConfidence.Established
            ? profile.Freshness == MachineLearningFreshness.Stale
                ? "Historical learned range"
                : "Typical"
            : "Adaptive observed range";
        var first = profile.FirstObservedAt.ToLocalTime();
        var last = profile.LastObservedAt.ToLocalTime();
        var observedSpan = first.Date == last.Date
            ? $"Observed {first:MMM d, yyyy}"
            : $"Observed {first:MMM d, yyyy} to {last:MMM d, yyyy}";
        var networkValue = profile.DominantNetworkActivityClass is
                { } dominantClass
            ? $"Mostly {dominantClass}\n" +
                $"{profile.DominantNetworkActivityCount:N0} / " +
                $"{profile.NetworkObservationCount:N0} observations"
            : "Still calibrating";

        return new LearningProfileDisplayItem(
            $"{FormatLearningHour(profile.LocalHour)} - " +
                $"{profile.ActivityState}",
            $"{profile.Confidence} - {profile.Freshness}",
            FormatLearningRange(valueLabel, profile.Cpu.TypicalRange,
                profile.Cpu.AdaptiveMean),
            FormatLearningRange(valueLabel, profile.Memory.TypicalRange,
                profile.Memory.AdaptiveMean),
            networkValue,
            $"Evidence - {FormatSampleCount(profile.LifetimeSampleCount)} - " +
                $"{profile.DistinctObservedDayCount:N0} observed " +
                (profile.DistinctObservedDayCount == 1 ? "day" : "days") +
                $"\n{observedSpan} - Reinforced " +
                $"{FormatLearningDateTime(profile.LastReinforcedAt, "Unknown")}",
            profile.Freshness == MachineLearningFreshness.Stale ? 0.64 : 1d);
    }

    private static LearningPatternDisplayItem
        CreateLearningPatternDisplayItem(
            MachineLearningRecurringPattern pattern)
    {
        var network = pattern.DominantNetworkActivityClass is { } dominant
            ? $"Network mostly {dominant}"
            : "Network evidence is incomplete across this window";
        return new LearningPatternDisplayItem(
            $"{FormatLearningHour(pattern.StartHour)}-" +
                $"{FormatLearningHour(pattern.EndHourExclusive)} - " +
                $"{pattern.ActivityState}",
            $"{pattern.Confidence} pattern - {pattern.Freshness}" +
                (pattern.CrossesMidnight ? " - crosses midnight" : string.Empty),
            FormatLearningRange("Typical", pattern.CpuTypicalRange, null),
            FormatLearningRange("Typical", pattern.MemoryTypicalRange, null),
            network,
            $"Built from {pattern.MemberContexts.Count:N0} established hourly " +
                (pattern.MemberContexts.Count == 1 ? "profile" : "profiles") +
                $" - {pattern.CombinedSampleCount:N0} observations - " +
                $"minimum {pattern.MinimumDistinctObservedDayCount:N0} observed days");
    }

    private static string FormatLearningLayer(
        MachineLearningMemoryLayer layer) => layer switch
        {
            MachineLearningMemoryLayer.ContextBaseline => "Layer 1 baseline",
            MachineLearningMemoryLayer.CompactProfile => "Layer 2 profile",
            MachineLearningMemoryLayer.BroaderPattern => "Layer 3 pattern",
            MachineLearningMemoryLayer.AggregateEpisode => "Aggregate episode",
            MachineLearningMemoryLayer.HealthHistory => "Health history",
            _ => "Learned evidence"
        };

    private static string FormatLearningRange(
        string label,
        MachineLearningRange? range,
        double? adaptiveMean) => range is null
            ? adaptiveMean is null
                ? "Range unavailable"
                : $"Observed adaptive mean {adaptiveMean.Value:F1}%\nRange still calibrating"
            : $"{label} {range.Low:F1}-{range.High:F1}%";

    private static LearningEpisodeDisplayItem
        CreateLearningEpisodeDisplayItem(MachineLearningEpisode episode)
    {
        var start = episode.StartedAt.ToLocalTime();
        var end = episode.EndedAt.ToLocalTime();
        var timeRange = start.Date == end.Date
            ? $"{start:HH:mm} → {end:HH:mm}"
            : $"{start:MMM d HH:mm} → {end:MMM d HH:mm}";
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(episode.Outcome))
        {
            details.Add(episode.Outcome);
        }
        if (episode.FindingKeys.Count > 0)
        {
            details.Add("Finding codes · " + string.Join(", ",
                episode.FindingKeys.Take(3)));
        }
        if (details.Count == 0)
        {
            details.Add("No finding codes recorded");
        }

        return new LearningEpisodeDisplayItem(
            $"{timeRange} · {FormatDuration(episode.EndedAt - episode.StartedAt)}",
            $"{episode.ActivityState} · {episode.OverallState} · " +
                FormatSampleCount(episode.SampleCount),
            $"CPU avg {episode.AverageCpuUsagePercent:F1}% · " +
                $"peak {episode.PeakCpuUsagePercent:F1}%",
            $"Memory avg {episode.AverageMemoryUsagePercent:F1}%",
            string.Join(" · ", details));
    }

    private static string FormatLearningHour(int hour)
    {
        var boundedHour = Math.Clamp(hour, 0, 23);
        return new DateTime(2000, 1, 1, boundedHour, 0, 0)
            .ToString("h tt", CultureInfo.CurrentCulture);
    }

    private static string FormatSampleCount(long count) =>
        $"{count:N0} " + (count == 1 ? "sample" : "samples");

    private static string FormatLearningTimestamp(
        DateTimeOffset? timestamp,
        string fallback) => timestamp is null
            ? fallback
            : timestamp.Value.ToLocalTime().ToString(
                "HH:mm:ss",
                CultureInfo.CurrentCulture);

    private static string FormatLearningDataHealth(
        MachineLearningDataHealth health) => health switch
        {
            MachineLearningDataHealth.Healthy => "Healthy",
            MachineLearningDataHealth.NotYetPersisted => "Not yet persisted",
            MachineLearningDataHealth.RecoveredFromCorruptState =>
                "Recovered from corrupt state",
            MachineLearningDataHealth.PersistenceTemporarilyUnavailable =>
                "Persistence temporarily unavailable",
            _ => "Not yet persisted"
        };

    private static string FormatLearningDateTime(
        DateTimeOffset? timestamp,
        string fallback) => timestamp is null
            ? fallback
            : timestamp.Value.ToLocalTime().ToString(
                "MMM d, yyyy HH:mm",
                CultureInfo.CurrentCulture);

    private void UpdateLearningRuntimeStatus()
    {
        var snapshot = _latestOllamaStatusSnapshot;
        if (snapshot is null)
        {
            LearningAiRuntimeText.Text = "Status unavailable";
            LearningAiModelText.Text = "Loaded-model status unavailable";
            return;
        }

        LearningAiRuntimeText.Text = snapshot.IsServiceAvailable
            ? "Online"
            : "Offline";
        LearningAiModelText.Text = !snapshot.IsServiceAvailable ||
            !snapshot.IsRunningModelStatusAvailable
                ? "Loaded-model status unavailable"
                : snapshot.RunningModels.Count == 0
                    ? "No model loaded"
                    : snapshot.RunningModels.Count == 1
                        ? $"{snapshot.RunningModels[0].Name} loaded"
                        : $"{snapshot.RunningModels.Count:N0} models loaded";
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1d
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{Math.Max(0, duration.Minutes)}m";

    private void UpdateCurrentFindings(
        MachineFindingsSnapshot snapshot)
    {
        var presentationState = GetPresentationState();
        FindingsOverallStateText.Text = presentationState.ToString();
        FindingsOverallStateText.Foreground =
            GetStateBrush(presentationState);
        OverviewStatePostureText.Text = presentationState switch
        {
            MachineOverallState.Stable =>
                "Quiet right now. Verified signals remain within a calm posture.",
            MachineOverallState.Attention =>
                "A small change deserves attention, without immediate urgency.",
            MachineOverallState.Warning =>
                "Verified evidence shows a condition worth reviewing soon.",
            MachineOverallState.Critical =>
                "Verified evidence shows a serious condition requiring attention.",
            _ => "Waiting for enough verified local evidence."
        };

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
            _latestOllamaStatusSnapshot = null;
            ShowOllamaOffline();
        }
    }

    private void UpdateOllamaStatus(
        OllamaStatusSnapshot snapshot)
    {
        _latestOllamaStatusSnapshot = snapshot;
        UpdateLearningRuntimeStatus();
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
        UpdateLearningRuntimeStatus();
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
        var networkSnapshot = _latestNetworkSnapshot;
        var sessionSnapshot = _latestSessionSnapshot;
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
        ApplyShellAtmosphere();
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
                Findings: findingsSnapshot,
                LearnedContext: _learningService.GetLearnedContext(),
                Network: CreateNetworkInsightContext(networkSnapshot),
                Session: CreateSessionInsightContext(sessionSnapshot),
                Health: MachineHealthInsightProjector.Project(
                    _latestWindowsUpdateSnapshot,
                    _latestRebootPendingSnapshot,
                    _latestReliabilitySnapshot),
                History: MachineHistoryInsightProjector.Project(
                    _historyService.GetSnapshot(
                        MachineHistoryRange.Last7Days,
                        DateTimeOffset.UtcNow)),
                Gpu: CreateGpuInsightContext(_latestGpuTelemetrySnapshot));
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
            ApplyShellAtmosphere();
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

    private static MachineGpuInsightContext? CreateGpuInsightContext(
        MachineGpuTelemetrySnapshot? snapshot)
    {
        var adapter = snapshot?.Adapters.FirstOrDefault();
        return adapter is null
            ? null
            : new MachineGpuInsightContext(
                adapter.GpuUtilizationPercent,
                adapter.MemoryUtilizationPercent,
                adapter.TemperatureCelsius,
                adapter.BoardPowerWatts);
    }

    private static MachineNetworkInsightContext?
        CreateNetworkInsightContext(MachineNetworkSnapshot? snapshot) =>
            snapshot is null
                ? null
                : new MachineNetworkInsightContext(
                    snapshot.Aggregate.ActivityClass,
                    GetVerifiedRate(
                        snapshot.Aggregate.ReceiveBytesPerSecond),
                    GetVerifiedRate(
                        snapshot.Aggregate.SendBytesPerSecond));

    private static MachineSessionInsightContext?
        CreateSessionInsightContext(MachineSessionSnapshot? snapshot) =>
            snapshot is null
                ? null
                : new MachineSessionInsightContext(
                    snapshot.SystemUptime,
                    snapshot.MachineUptime);

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

    private async void OnRefreshServicesClicked(
        object sender,
        RoutedEventArgs args) => await LoadServiceInventoryAsync(
        isManualRefresh: true,
        _windowCancellationTokenSource.Token);

    private async Task LoadServiceInventoryAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken)
    {
        if (_isServiceInventoryRequestRunning)
        {
            return;
        }
        _isServiceInventoryRequestRunning = true;
        RefreshServicesButton.IsEnabled = false;
        if (isManualRefresh)
        {
            RefreshServicesButton.Content = "Refreshing...";
            await Task.Yield();
        }
        try
        {
            var snapshot = await _serviceInventoryProvider.GetAsync(
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _latestServiceInventorySnapshot = snapshot;
            ApplyServiceFilter(snapshot);
            ServicesStatusText.Text = CreateInventoryStatus(
                snapshot.IsComplete,
                snapshot.ReadFailureCount,
                snapshot.TruncatedItemCount);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            ServicesStatusText.Text =
                "Service inventory is temporarily unavailable.";
        }
        finally
        {
            _isServiceInventoryRequestRunning = false;
            if (!cancellationToken.IsCancellationRequested)
            {
                RefreshServicesButton.Content = "Refresh";
                RefreshServicesButton.IsEnabled = true;
            }
        }
    }

    private void OnServiceFilterChanged(object sender, object args)
    {
        if (_latestServiceInventorySnapshot is { } snapshot)
        {
            ApplyServiceFilter(snapshot);
        }
    }

    private void ApplyServiceFilter(MachineServiceInventorySnapshot snapshot)
    {
        var search = ServiceSearchBox.Text.Trim();
        var state = GetSelectedTag(ServiceStateFilter);
        var startType = GetSelectedTag(ServiceStartTypeFilter);
        var filtered = snapshot.Items.Where(item =>
                (search.Length == 0 ||
                 item.Name.Contains(search,
                     StringComparison.OrdinalIgnoreCase) ||
                 item.DisplayName.Contains(search,
                     StringComparison.OrdinalIgnoreCase)) &&
                ServiceStateMatches(item.State, state) &&
                ServiceStartTypeMatches(item.StartType, startType))
            .Take(MaximumInventoryDisplayCount)
            .Select(CreateServiceDisplayItem)
            .ToArray();
        ServicesList.ItemsSource = filtered;
        ServicesSummaryText.Text =
            $"{snapshot.Items.Count:N0} services · showing " +
            $"{filtered.Length:N0}";
    }

    private static bool ServiceStateMatches(
        MachineServiceState state,
        string filter) => filter switch
        {
            "Running" => state == MachineServiceState.Running,
            "Stopped" => state == MachineServiceState.Stopped,
            "Paused" => state == MachineServiceState.Paused,
            "pending" => state is
                MachineServiceState.StartPending or
                MachineServiceState.StopPending or
                MachineServiceState.ContinuePending or
                MachineServiceState.PausePending,
            _ => true
        };

    private static bool ServiceStartTypeMatches(
        MachineServiceStartType startType,
        string filter) => filter switch
        {
            "automatic" => startType is
                MachineServiceStartType.Automatic or
                MachineServiceStartType.AutomaticDelayed,
            "Manual" => startType == MachineServiceStartType.Manual,
            "Disabled" => startType == MachineServiceStartType.Disabled,
            "boot" => startType is
                MachineServiceStartType.Boot or
                MachineServiceStartType.System,
            _ => true
        };

    private static ServiceDisplayItem CreateServiceDisplayItem(
        MachineServiceSnapshot item) => new(
        item.DisplayName,
        item.Name == item.DisplayName
            ? item.Category.ToString()
            : $"{item.Name} · {item.Category}",
        item.State.ToString(),
        item.ProcessId is { } processId
            ? $"{FormatServiceStartType(item.StartType)} · PID {processId}"
            : FormatServiceStartType(item.StartType));

    private static string FormatServiceStartType(
        MachineServiceStartType value) => value switch
        {
            MachineServiceStartType.AutomaticDelayed =>
                "Automatic (delayed)",
            _ => value.ToString()
        };

    private async void OnRefreshTasksClicked(
        object sender,
        RoutedEventArgs args) => await LoadTaskInventoryAsync(
        isManualRefresh: true,
        _windowCancellationTokenSource.Token);

    private async Task LoadTaskInventoryAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken)
    {
        if (_isTaskInventoryRequestRunning)
        {
            return;
        }
        _isTaskInventoryRequestRunning = true;
        RefreshTasksButton.IsEnabled = false;
        if (isManualRefresh)
        {
            RefreshTasksButton.Content = "Refreshing...";
            await Task.Yield();
        }
        try
        {
            var snapshot = await _taskInventoryProvider.GetAsync(
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _latestTaskInventorySnapshot = snapshot;
            ApplyTaskFilter(snapshot);
            TasksStatusText.Text = CreateInventoryStatus(
                snapshot.IsComplete,
                snapshot.ReadFailureCount,
                snapshot.TruncatedItemCount);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            TasksStatusText.Text =
                "Scheduled-task inventory is temporarily unavailable.";
        }
        finally
        {
            _isTaskInventoryRequestRunning = false;
            if (!cancellationToken.IsCancellationRequested)
            {
                RefreshTasksButton.Content = "Refresh";
                RefreshTasksButton.IsEnabled = true;
            }
        }
    }

    private void OnTaskFilterChanged(object sender, object args)
    {
        if (_latestTaskInventorySnapshot is { } snapshot)
        {
            ApplyTaskFilter(snapshot);
        }
    }

    private void ApplyTaskFilter(
        MachineScheduledTaskInventorySnapshot snapshot)
    {
        var search = TaskSearchBox.Text.Trim();
        var enabled = GetSelectedTag(TaskEnabledFilter);
        var state = GetSelectedTag(TaskStateFilter);
        var result = GetSelectedTag(TaskResultFilter);
        var filtered = snapshot.Items.Where(item =>
                (search.Length == 0 ||
                 item.Name.Contains(search,
                     StringComparison.OrdinalIgnoreCase) ||
                 item.Path.Contains(search,
                     StringComparison.OrdinalIgnoreCase) ||
                 item.Author?.Contains(search,
                     StringComparison.OrdinalIgnoreCase) == true ||
                 item.ExecutableName?.Contains(search,
                     StringComparison.OrdinalIgnoreCase) == true) &&
                (enabled == "all" ||
                 enabled == "enabled" && item.Enabled ||
                 enabled == "disabled" && !item.Enabled) &&
                (state == "all" ||
                 string.Equals(item.State.ToString(), state,
                     StringComparison.Ordinal)) &&
                (result != "failed" || item.LastRunFailed))
            .Take(MaximumInventoryDisplayCount)
            .Select(CreateTaskDisplayItem)
            .ToArray();
        TasksList.ItemsSource = filtered;
        TasksSummaryText.Text =
            $"{snapshot.Items.Count:N0} tasks · showing {filtered.Length:N0}";
    }

    private static ScheduledTaskDisplayItem CreateTaskDisplayItem(
        MachineScheduledTaskSnapshot item)
    {
        var triggers = item.TriggerCategories.Count == 0
            ? "Triggers unavailable"
            : string.Join(", ", item.TriggerCategories);
        var next = item.NextRunAt is { } nextRun
            ? $"Next {nextRun.ToLocalTime():MMM d, HH:mm}"
            : "No next run reported";
        var last = item.LastRunAt is { } lastRun
            ? $"Last {lastRun.ToLocalTime():MMM d, HH:mm}"
            : "No last run reported";
        var executable = item.ExecutableName is null
            ? string.Empty
            : $" · {item.ExecutableName}";
        return new(
            item.Name,
            item.Path,
            item.Enabled ? item.State.ToString() : "Disabled",
            $"{triggers} · {next}",
            $"{last} · Result " +
                (item.LastResult?.ToString("X8") ?? "unavailable") +
                executable);
    }

    private async void OnRefreshDevicesClicked(
        object sender,
        RoutedEventArgs args) => await LoadDeviceInventoryAsync(
        isManualRefresh: true,
        _windowCancellationTokenSource.Token);

    private async Task LoadDeviceInventoryAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken)
    {
        if (_isDeviceInventoryRequestRunning)
        {
            return;
        }
        _isDeviceInventoryRequestRunning = true;
        RefreshDevicesButton.IsEnabled = false;
        if (isManualRefresh)
        {
            RefreshDevicesButton.Content = "Refreshing...";
            await Task.Yield();
        }
        try
        {
            var snapshot = await _deviceInventoryProvider.GetAsync(
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _latestDeviceInventorySnapshot = snapshot;
            var selectedClass = DeviceClassFilter.SelectedItem?.ToString();
            DeviceClassFilter.ItemsSource = new[] { "All classes" }
                .Concat(snapshot.Items.Select(item => item.DeviceClass)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item,
                        StringComparer.OrdinalIgnoreCase))
                .ToArray();
            DeviceClassFilter.SelectedItem = selectedClass is not null &&
                DeviceClassFilter.Items.Contains(selectedClass)
                    ? selectedClass
                    : "All classes";
            ApplyDeviceFilter(snapshot);
            DevicesStatusText.Text = CreateInventoryStatus(
                snapshot.IsComplete,
                snapshot.ReadFailureCount,
                snapshot.TruncatedItemCount);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            DevicesStatusText.Text =
                "Device inventory is temporarily unavailable.";
        }
        finally
        {
            _isDeviceInventoryRequestRunning = false;
            if (!cancellationToken.IsCancellationRequested)
            {
                RefreshDevicesButton.Content = "Refresh";
                RefreshDevicesButton.IsEnabled = true;
            }
        }
    }

    private void OnDeviceFilterChanged(object sender, object args)
    {
        if (_latestDeviceInventorySnapshot is { } snapshot)
        {
            ApplyDeviceFilter(snapshot);
        }
    }

    private void ApplyDeviceFilter(MachineDeviceInventorySnapshot snapshot)
    {
        var search = DeviceSearchBox.Text.Trim();
        var selectedClass = DeviceClassFilter.SelectedItem?.ToString();
        var problemFilter = GetSelectedTag(DeviceProblemFilter);
        var filtered = snapshot.Items.Where(item =>
                (search.Length == 0 ||
                 item.DisplayName.Contains(search,
                     StringComparison.OrdinalIgnoreCase) ||
                 item.DeviceClass.Contains(search,
                     StringComparison.OrdinalIgnoreCase) ||
                 item.Manufacturer?.Contains(search,
                     StringComparison.OrdinalIgnoreCase) == true ||
                 item.DriverProvider?.Contains(search,
                     StringComparison.OrdinalIgnoreCase) == true) &&
                (selectedClass is null or "All classes" ||
                 string.Equals(item.DeviceClass, selectedClass,
                     StringComparison.OrdinalIgnoreCase)) &&
                (problemFilter != "problem" ||
                 item.HasWindowsReportedProblem))
            .Take(MaximumInventoryDisplayCount)
            .Select(CreateDeviceDisplayItem)
            .ToArray();
        DevicesList.ItemsSource = filtered;
        var problemCount = snapshot.Items.Count(item =>
            item.HasWindowsReportedProblem);
        DevicesSummaryText.Text =
            $"{snapshot.Items.Count:N0} devices · {problemCount:N0} with " +
            $"a Windows-reported problem · showing {filtered.Length:N0}";
    }

    private static DeviceDisplayItem CreateDeviceDisplayItem(
        MachineDeviceSnapshot item)
    {
        var driverParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.DriverProvider))
        {
            driverParts.Add(item.DriverProvider);
        }
        if (!string.IsNullOrWhiteSpace(item.DriverVersion))
        {
            driverParts.Add($"Driver {item.DriverVersion}");
        }
        if (item.DriverDate is { } date)
        {
            driverParts.Add(date.ToString("MMM d, yyyy"));
        }
        return new(
            item.DisplayName,
            string.IsNullOrWhiteSpace(item.Manufacturer)
                ? item.DeviceClass
                : $"{item.DeviceClass} · {item.Manufacturer}",
            driverParts.Count == 0
                ? "Driver details unavailable"
                : string.Join(" · ", driverParts),
            item.HasWindowsReportedProblem
                ? $"Windows problem code {item.ProblemCode}"
                : "No Windows-reported problem");
    }

    private static string GetSelectedTag(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";

    private static string CreateInventoryStatus(
        bool isComplete,
        int readFailureCount,
        int truncatedItemCount)
    {
        if (isComplete)
        {
            return string.Empty;
        }
        var parts = new List<string> { "Inventory is partial" };
        if (readFailureCount > 0)
        {
            parts.Add($"{readFailureCount:N0} read " +
                (readFailureCount == 1 ? "failure" : "failures"));
        }
        if (truncatedItemCount > 0)
        {
            parts.Add($"{truncatedItemCount:N0} items beyond the bound");
        }
        return string.Join(" · ", parts);
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

    private static string FormatBytes(ulong bytes)
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

        if (bytes >= 1024UL)
        {
            return $"{bytes / 1024d:F1} KB";
        }

        return $"{bytes} B";
    }

    private static string FormatByteRate(double? bytesPerSecond)
    {
        if (bytesPerSecond is null ||
            !double.IsFinite(bytesPerSecond.Value) ||
            bytesPerSecond.Value < 0d)
        {
            return UnavailableValue;
        }

        var value = bytesPerSecond.Value;
        if (value >= BytesPerTebibyte)
        {
            return $"{value / BytesPerTebibyte:F1} TB/s";
        }

        if (value >= BytesPerGibibyte)
        {
            return $"{value / BytesPerGibibyte:F1} GB/s";
        }

        if (value >= BytesPerMebibyte)
        {
            return $"{value / BytesPerMebibyte:F1} MB/s";
        }

        if (value >= 1024d)
        {
            return $"{value / 1024d:F1} KB/s";
        }

        return $"{value:F0} B/s";
    }

    private static string FormatLinkSpeed(
        long? receiveBitsPerSecond,
        long? transmitBitsPerSecond)
    {
        if (receiveBitsPerSecond is null && transmitBitsPerSecond is null)
        {
            return "Link speed unavailable";
        }

        if (receiveBitsPerSecond == transmitBitsPerSecond)
        {
            return $"{FormatBitsPerSecond(receiveBitsPerSecond)} link";
        }

        return $"Receive {FormatBitsPerSecond(receiveBitsPerSecond)} · " +
            $"send {FormatBitsPerSecond(transmitBitsPerSecond)} link";
    }

    private static string FormatBitsPerSecond(long? bitsPerSecond)
    {
        if (bitsPerSecond is null || bitsPerSecond <= 0)
        {
            return UnavailableValue;
        }

        return bitsPerSecond >= 1_000_000_000L
            ? $"{bitsPerSecond / 1_000_000_000d:F1} Gbps"
            : bitsPerSecond >= 1_000_000L
                ? $"{bitsPerSecond / 1_000_000d:F1} Mbps"
                : $"{bitsPerSecond / 1_000d:F1} Kbps";
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        var bounded = uptime < TimeSpan.Zero ? TimeSpan.Zero : uptime;
        if (bounded.TotalDays >= 1d)
        {
            return $"{(int)bounded.TotalDays}d {bounded.Hours}h";
        }

        return bounded.TotalHours >= 1d
            ? $"{(int)bounded.TotalHours}h {bounded.Minutes}m"
            : $"{Math.Max(0, bounded.Minutes)}m";
    }

    private static string FormatInputAge(TimeSpan age)
    {
        var bounded = age < TimeSpan.Zero ? TimeSpan.Zero : age;
        if (bounded.TotalHours >= 1d)
        {
            return $"{(int)bounded.TotalHours}h {bounded.Minutes}m";
        }

        if (bounded.TotalMinutes >= 1d)
        {
            return $"{(int)bounded.TotalMinutes}m {bounded.Seconds}s";
        }

        return $"{Math.Max(0, bounded.Seconds)}s";
    }

    private void OnDashboardBackClicked(
        object sender,
        RoutedEventArgs args) => ReturnToAmbientPresence();

    private void OnDashboardCloseClicked(object sender, RoutedEventArgs args) =>
        DashboardChromeLayout.InvokeClose(Close);

    private void OnMainContentKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!_detailsExpanded ||
            !DashboardChromeLayout.IsReturnToAmbientKey(
                (uint)args.Key))
        {
            return;
        }

        args.Handled = ReturnToAmbientPresence();
    }

    private bool ReturnToAmbientPresence()
    {
        if (!_compactPresenceInteraction.CloseDashboard())
        {
            return false;
        }

        SetDashboardExpanded(false);
        return true;
    }

    private void SetDashboardExpanded(bool isExpanded)
    {
        _detailsExpanded = isExpanded;

        DetailsPanel.Visibility = _detailsExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardChrome.Visibility = _detailsExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyCompactPresentation();
        UpdateDashboardDragRegion();

        if (_detailsExpanded)
        {
            SelectNavigationButton(OverviewNavigationItem);
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
            _ambientOrbWindow.Hide();
            AppWindow.Show();
            ResizeAndPositionWindow(
                ExpandedWindowWidth,
                ExpandedWindowHeight);
            MainContent.DispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Low,
                UpdateDashboardDragRegion);
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
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
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

    private void ApplyShellAtmosphere()
    {
        if (MainContent is null)
        {
            return;
        }
        var atmosphere = MatasuriShellAtmospherePolicy.Select(
            GetPresentationState(),
            IsGeneratingPresentation(),
            _uiSettings.AnimationsEnabled);
        if (atmosphere == _appliedShellAtmosphere)
        {
            return;
        }
        _appliedShellAtmosphere = atmosphere;
        var atmosphereBrush = (SolidColorBrush)MainContent.Resources[
            "MatasuriAtmosphereBrush"];
        var accentBrush = (SolidColorBrush)MainContent.Resources[
            "MatasuriStateAccentBrush"];
        var targetAtmosphere = ToColor(atmosphere.Atmosphere);
        var targetAccent = ToColor(atmosphere.Accent);
        var currentAtmosphere = atmosphereBrush.Color;
        var currentAccent = accentBrush.Color;
        _shellAtmosphereStoryboard?.Stop();
        _shellAtmosphereStoryboard = null;
        atmosphereBrush.Color = currentAtmosphere;
        accentBrush.Color = currentAccent;
        if (atmosphere.TransitionDuration == TimeSpan.Zero)
        {
            atmosphereBrush.Color = targetAtmosphere;
            accentBrush.Color = targetAccent;
        }
        else
        {
            var easing = new CubicEase
            {
                EasingMode = EasingMode.EaseInOut
            };
            var atmosphereAnimation = new ColorAnimation
            {
                To = targetAtmosphere,
                Duration = atmosphere.TransitionDuration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(atmosphereAnimation, atmosphereBrush);
            Storyboard.SetTargetProperty(atmosphereAnimation, "Color");
            var accentAnimation = new ColorAnimation
            {
                To = targetAccent,
                Duration = atmosphere.TransitionDuration,
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseInOut
                }
            };
            Storyboard.SetTarget(accentAnimation, accentBrush);
            Storyboard.SetTargetProperty(accentAnimation, "Color");
            _shellAtmosphereStoryboard = new Storyboard();
            _shellAtmosphereStoryboard.Children.Add(atmosphereAnimation);
            _shellAtmosphereStoryboard.Children.Add(accentAnimation);
            _shellAtmosphereStoryboard.Completed += (_, _) =>
            {
                atmosphereBrush.Color = targetAtmosphere;
                accentBrush.Color = targetAccent;
            };
            _shellAtmosphereStoryboard.Begin();
        }

        _generatingAtmosphereStoryboard?.Stop();
        _generatingAtmosphereStoryboard = null;
        if (!atmosphere.IsGenerating)
        {
            GeneratingAtmosphereLayer.Opacity = 0d;
        }
        else if (!atmosphere.AnimateGeneratingOverlay)
        {
            GeneratingAtmosphereLayer.Opacity = 0.055d;
        }
        else
        {
            var animation = new DoubleAnimation
            {
                From = 0.035d,
                To = 0.10d,
                Duration = TimeSpan.FromMilliseconds(900),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase
                {
                    EasingMode = EasingMode.EaseInOut
                }
            };
            Storyboard.SetTarget(
                animation,
                GeneratingAtmosphereLayer);
            Storyboard.SetTargetProperty(animation, "Opacity");
            _generatingAtmosphereStoryboard = new Storyboard();
            _generatingAtmosphereStoryboard.Children.Add(animation);
            _generatingAtmosphereStoryboard.Begin();
        }
    }

    private static global::Windows.UI.Color ToColor(
        MatasuriColor color) => global::Windows.UI.Color.FromArgb(
        color.Alpha,
        color.Red,
        color.Green,
        color.Blue);

    private void ApplyPresenceVisualMode(bool force = false)
    {
        if (_windowCancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        var mode = CompactPresenceLayout.SelectVisualMode(
            GetPresentationState(),
            IsGeneratingPresentation(),
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
        {
            ApplyShellAtmosphere();
            ApplyPresenceVisualMode(force: true);
        });
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
                    DashboardChromeLayout.HasBorder,
                    DashboardChromeLayout.HasTitleBar);
            }

            ApplyDashboardCornerPreference();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private void OnDashboardDragRegionSizeChanged(
        object sender,
        SizeChangedEventArgs args) => UpdateDashboardDragRegion();

    private void OnDashboardXamlRootChanged(
        XamlRoot sender,
        XamlRootChangedEventArgs args) => UpdateDashboardDragRegion();

    private void UpdateDashboardDragRegion()
    {
        if (_nonClientPointerSource is null)
        {
            return;
        }

        try
        {
            if (!_detailsExpanded ||
                DashboardDragRegion.ActualWidth <= 0d ||
                DashboardDragRegion.ActualHeight <= 0d)
            {
                _nonClientPointerSource.ClearRegionRects(
                    NonClientRegionKind.Caption);
                return;
            }

            var offset = DashboardDragRegion
                .TransformToVisual(MainContent)
                .TransformPoint(new global::Windows.Foundation.Point(0d, 0d));
            var region = DashboardChromeLayout.CalculateCaptionRegion(
                offset.X,
                offset.Y,
                DashboardDragRegion.ActualWidth,
                DashboardDragRegion.ActualHeight,
                MainContent.XamlRoot?.RasterizationScale ?? 1d);
            _nonClientPointerSource.SetRegionRects(
                NonClientRegionKind.Caption,
                [new RectInt32(
                    region.X,
                    region.Y,
                    region.Width,
                    region.Height)]);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private void OnDashboardNavigationClicked(
        object sender,
        RoutedEventArgs args)
    {
        if (OverviewPage is null || sender is not Button button)
        {
            return;
        }

        var tag = button.Tag?.ToString() ?? "overview";

        SelectNavigationButton(button);
        ShowDashboardPage(tag);
    }

    private void SelectNavigationButton(Button selected)
    {
        var buttons = new[]
        {
            OverviewNavigationItem,
            HistoryNavigationItem,
            LearningNavigationItem,
            HealthNavigationItem,
            NetworkNavigationItem,
            HardwareNavigationItem,
            StorageNavigationItem,
            SoftwareNavigationItem,
            StartupNavigationItem,
            ServicesNavigationItem,
            TasksNavigationItem,
            DevicesNavigationItem,
            RuntimeNavigationItem
        };
        var selectedBrush = (Brush)MainContent.Resources[
            "MatasuriElevatedSurfaceBrush"];
        foreach (var button in buttons)
        {
            var isSelected = ReferenceEquals(button, selected);
            button.Background = isSelected
                ? selectedBrush
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.FontWeight = isSelected
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
            button.Opacity = isSelected ? 1d : 0.72d;
            AutomationProperties.SetName(
                button,
                $"{button.Content}{(isSelected ? ", selected" : string.Empty)}");
        }
    }

    private void ShowDashboardPage(string tag)
    {
        OverviewPage.Visibility = tag == "overview"
            ? Visibility.Visible
            : Visibility.Collapsed;
        HistoryPage.Visibility = tag == "history"
            ? Visibility.Visible
            : Visibility.Collapsed;
        LearningPage.Visibility = tag == "learning"
            ? Visibility.Visible
            : Visibility.Collapsed;
        NetworkPage.Visibility = tag == "network"
            ? Visibility.Visible
            : Visibility.Collapsed;
        HealthPage.Visibility = tag == "health"
            ? Visibility.Visible
            : Visibility.Collapsed;
        HardwarePage.Visibility = tag == "hardware"
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
        ServicesPage.Visibility = tag == "services"
            ? Visibility.Visible
            : Visibility.Collapsed;
        TasksPage.Visibility = tag == "tasks"
            ? Visibility.Visible
            : Visibility.Collapsed;
        DevicesPage.Visibility = tag == "devices"
            ? Visibility.Visible
            : Visibility.Collapsed;
        RuntimePage.Visibility = tag == "runtime"
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (tag == "history")
        {
            UpdateHistoryDashboard();
        }
        else if (tag == "learning")
        {
            UpdateLearningDashboard();
        }
        else if (tag == "health")
        {
            UpdateHealthDashboard();
            _ = RefreshHealthAsync(
                isManualRefresh: false,
                _windowCancellationTokenSource.Token);
        }
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    internal void StopForApplicationShutdown()
    {
        _shellAtmosphereStoryboard?.Stop();
        _shellAtmosphereStoryboard = null;
        _generatingAtmosphereStoryboard?.Stop();
        _generatingAtmosphereStoryboard = null;
        _powerBroadcastMonitor?.Dispose();
        _powerBroadcastMonitor = null;
        _ambientOrbWindow.NewInsightCompleted -= OnNewInsightBloomCompleted;
        _ambientOrbWindow.Dispose();
        if (_isXamlRootChangeSubscribed && MainContent.XamlRoot is not null)
        {
            MainContent.XamlRoot.Changed -= OnDashboardXamlRootChanged;
            _isXamlRootChangeSubscribed = false;
        }
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

public sealed record LearningProfileDisplayItem(
    string Header,
    string Evidence,
    string CpuValue,
    string MemoryValue,
    string NetworkValue,
    string Reinforcement,
    double Opacity);

public sealed record LearningPatternDisplayItem(
    string Header,
    string Status,
    string CpuValue,
    string MemoryValue,
    string NetworkValue,
    string Evidence);

public sealed record LearnedItemDisplayItem(
    string Label,
    string Text,
    string Evidence);

public sealed record LearningEpisodeDisplayItem(
    string Header,
    string Context,
    string CpuDetails,
    string MemoryDetails,
    string OutcomeAndFindings);

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

public sealed record NetworkInterfaceDisplayItem(
    string Name,
    string StatusAndType,
    string Description,
    string LinkDetails,
    string ReceivedDetails,
    string SentDetails);

public sealed record UpdateHistoryDisplayItem(
    string Header,
    string Title,
    string Details);

public sealed record ReliabilityIncidentDisplayItem(
    string Header,
    string Category,
    string Details);

public sealed record RecurringFailureDisplayItem(
    string ApplicationName,
    string Details);

public sealed record HistoryEventDisplayItem(
    string Time,
    string Title,
    string? Detail,
    Visibility DetailVisibility);

public sealed record HistoryMetricAggregate(
    double Mean,
    double Maximum);

public sealed record ServiceDisplayItem(
    string DisplayName,
    string Identity,
    string State,
    string StartDetails);

public sealed record ScheduledTaskDisplayItem(
    string Name,
    string Path,
    string State,
    string ScheduleDetails,
    string EvidenceDetails);

public sealed record DeviceDisplayItem(
    string DisplayName,
    string Identity,
    string DriverDetails,
    string StatusDetails);
