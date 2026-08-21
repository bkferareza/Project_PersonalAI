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
    private static readonly TimeSpan HealthRefreshInterval =
        TimeSpan.FromMinutes(10);
    private readonly IMachineIdentityProvider _identityProvider;
    private readonly IMachineResourceProvider _resourceProvider;
    private readonly IMachineProcessProvider _processProvider;
    private readonly IOllamaStatusProvider _ollamaStatusProvider;
    private readonly IMachineStateExplainer _machineStateExplainer;
    private readonly IMachineUserActivityProvider _userActivityProvider;
    private readonly IMachineNetworkProvider _networkProvider;
    private readonly IMachineSessionProvider _sessionProvider;
    private readonly IMachineWindowsUpdateProvider _windowsUpdateProvider;
    private readonly IMachineRebootPendingProvider _rebootPendingProvider;
    private readonly IMachineReliabilityProvider _reliabilityProvider;
    private readonly MachineLearningService _learningService;
    private readonly IMachineLearningStore _learningStore;
    private readonly IMachineLearningActivityStore _learningActivityStore;
    private readonly MachineHealthHistoryService _healthHistoryService;
    private readonly IMachineHealthHistoryStore _healthHistoryStore;
    private readonly MachineHistoryService _historyService;
    private readonly IMachineHistoryStore _historyStore;
    private readonly IMachineGpuTelemetryProvider _gpuTelemetryProvider;
    private readonly IMachineCpuHardwareProvider _cpuHardwareProvider;
    private readonly IMachineStorageDeviceHealthProvider
        _storageDeviceHealthProvider;
    private readonly MachineInsightTriggerPolicy
        _insightTriggerPolicy = new();
    private readonly MachineEnergyAccumulator _energyAccumulator = new(
        Stopwatch.Frequency);
    private readonly CompactPresenceInteraction
        _compactPresenceInteraction = new();
    private readonly CancellationTokenSource
        _windowCancellationTokenSource = new();
    private readonly TaskCompletionSource _runtimeInitializationCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SystemBackdrop _dashboardBackdrop;
    private readonly UISettings _uiSettings = new();
    private readonly NativeAmbientOrbWindow _ambientOrbWindow;
    private WindowsPowerBroadcastMonitor? _powerBroadcastMonitor;
    private InputNonClientPointerSource? _nonClientPointerSource;
    private MachineIdentity? _latestIdentity;
    private MachineResourceSnapshot? _latestResourceSnapshot;
    private IReadOnlyList<MachineProcessSnapshot>
        _latestProcessSnapshots =
            Array.Empty<MachineProcessSnapshot>();
    private MachineNetworkSnapshot? _latestNetworkSnapshot;
    private MachineSessionSnapshot? _latestSessionSnapshot;
    private MachineWindowsUpdateSnapshot? _latestWindowsUpdateSnapshot;
    private MachineRebootPendingSnapshot? _latestRebootPendingSnapshot;
    private MachineReliabilitySnapshot? _latestReliabilitySnapshot;
    private MachineGpuTelemetrySnapshot? _latestGpuTelemetrySnapshot;
    private MachineCpuHardwareSnapshot? _latestCpuHardwareSnapshot;
    private MachineStorageDeviceHealthCollection? _latestStorageHealthSnapshot;
    private DateTimeOffset? _lastStorageHealthRefreshAt;
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
    private bool _isHealthRequestRunning;
    private MachineOverallState _latestOverallState =
        MachineOverallState.Unknown;
    private CompactPresencePresentation?
        _appliedCompactPresentation;
    private CompactPresenceVisualMode?
        _activePresenceVisualMode;
    private bool _showNewInsightBloom;
    private bool _isAnimationSettingsChangeSubscribed;
    private bool _isXamlRootChangeSubscribed;
    private bool _isApplicationShutdownRequested;
    private DispatcherQueueTimer? _focusLossCollapseTimer;
    private Storyboard? _shellAtmosphereStoryboard;
    private Storyboard? _generatingAtmosphereStoryboard;
    private MatasuriShellAtmosphere? _appliedShellAtmosphere;
#if DEBUG
    private readonly MatasuriPresentationValidationOptions
        _presentationValidationOptions;
#endif

    internal Task RuntimeInitialization =>
        _runtimeInitializationCompletion.Task;

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
        IMachineLearningActivityStore learningActivityStore,
        MachineHealthHistoryService healthHistoryService,
        IMachineHealthHistoryStore healthHistoryStore,
        MachineHistoryService historyService,
        IMachineHistoryStore historyStore,
        IMachineServiceInventoryProvider serviceInventoryProvider,
        IMachineScheduledTaskInventoryProvider taskInventoryProvider,
        IMachineDeviceInventoryProvider deviceInventoryProvider,
        IMachineGpuTelemetryProvider gpuTelemetryProvider,
        IMachineCpuHardwareProvider cpuHardwareProvider,
        IMachineStorageDeviceHealthProvider storageDeviceHealthProvider,
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
        ArgumentNullException.ThrowIfNull(learningActivityStore);
        ArgumentNullException.ThrowIfNull(healthHistoryService);
        ArgumentNullException.ThrowIfNull(healthHistoryStore);
        ArgumentNullException.ThrowIfNull(historyService);
        ArgumentNullException.ThrowIfNull(historyStore);
        ArgumentNullException.ThrowIfNull(serviceInventoryProvider);
        ArgumentNullException.ThrowIfNull(taskInventoryProvider);
        ArgumentNullException.ThrowIfNull(deviceInventoryProvider);
        ArgumentNullException.ThrowIfNull(gpuTelemetryProvider);
        ArgumentNullException.ThrowIfNull(cpuHardwareProvider);
        ArgumentNullException.ThrowIfNull(storageDeviceHealthProvider);

        _identityProvider = identityProvider;
        _resourceProvider = resourceProvider;
        _processProvider = processProvider;
        _ollamaStatusProvider = ollamaStatusProvider;
        _machineStateExplainer = machineStateExplainer;
        _userActivityProvider = userActivityProvider;
        _networkProvider = networkProvider;
        _sessionProvider = sessionProvider;
        _windowsUpdateProvider = windowsUpdateProvider;
        _rebootPendingProvider = rebootPendingProvider;
        _reliabilityProvider = reliabilityProvider;
        _learningService = learningService;
        _learningStore = learningStore;
        _learningActivityStore = learningActivityStore;
        _healthHistoryService = healthHistoryService;
        _healthHistoryStore = healthHistoryStore;
        _historyService = historyService;
        _historyStore = historyStore;
        _gpuTelemetryProvider = gpuTelemetryProvider;
        _cpuHardwareProvider = cpuHardwareProvider;
        _storageDeviceHealthProvider = storageDeviceHealthProvider;
#if DEBUG
        _presentationValidationOptions =
            MatasuriPresentationValidationOptions.Parse(
                presentationValidationArguments);
#endif

        InitializeComponent();
        HistoryPage.Initialize(_historyService);
        StoragePage.Initialize(
            storageProvider,
            folderInspectionProvider,
            _windowCancellationTokenSource.Token,
            () => ReevaluateFindings());
        SoftwarePage.Initialize(
            softwareInventoryProvider,
            packagedSoftwareInventoryProvider,
            _windowCancellationTokenSource.Token,
            () => ReevaluateFindings());
        StartupPage.Initialize(
            startupInventoryProvider,
            _windowCancellationTokenSource.Token,
            () => ReevaluateFindings());
        ServicesPage.Initialize(
            serviceInventoryProvider,
            _windowCancellationTokenSource.Token);
        TasksPage.Initialize(
            taskInventoryProvider,
            _windowCancellationTokenSource.Token);
        DevicesPage.Initialize(
            deviceInventoryProvider,
            _windowCancellationTokenSource.Token);
        WireFeatureViewEvents();
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

    private void WireFeatureViewEvents()
    {
        OverviewPage.ExplainMachineStateButton.Click +=
            OnExplainMachineStateClicked;

        HealthPage.RefreshHealthButton.Click += OnRefreshHealthClicked;

    }
}
