using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Machine.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    private const int MaximumNetworkInterfaceCount = 12;
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
    private readonly IMachineUserActivityProvider _userActivityProvider;
    private readonly IMachineNetworkProvider _networkProvider;
    private readonly IMachineSessionProvider _sessionProvider;
    private readonly MachineLearningService _learningService;
    private readonly IMachineLearningStore _learningStore;
    private readonly MachineInsightTriggerPolicy
        _insightTriggerPolicy = new();
    private readonly CompactPresenceInteraction
        _compactPresenceInteraction = new();
    private readonly CancellationTokenSource
        _windowCancellationTokenSource = new();
    private readonly SystemBackdrop _dashboardBackdrop;
    private readonly UISettings _uiSettings = new();
    private readonly NativeAmbientOrbWindow _ambientOrbWindow;
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
    private MachineOverallState _latestOverallState =
        MachineOverallState.Unknown;
    private CompactPresencePresentation?
        _appliedCompactPresentation;
    private CompactPresenceVisualMode?
        _activePresenceVisualMode;
    private bool _showNewInsightBloom;
    private bool _isAnimationSettingsChangeSubscribed;
    private bool _isXamlRootChangeSubscribed;

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
        MachineLearningService learningService,
        IMachineLearningStore learningStore)
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
        ArgumentNullException.ThrowIfNull(learningService);
        ArgumentNullException.ThrowIfNull(learningStore);

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
        _learningService = learningService;
        _learningStore = learningStore;

        InitializeComponent();
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
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }

        ApplyCompactPresentation(force: true);
        UpdateDashboardDragRegion();
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
            var resourceTask = _resourceProvider.GetAsync(cancellationToken);
            var networkTask = TryCaptureNetworkAsync(cancellationToken);
            var sessionTask = TryCaptureSessionAsync(cancellationToken);
            await Task.WhenAll(resourceTask, networkTask, sessionTask);

            var snapshot = await resourceTask;
            var networkSnapshot = await networkTask;
            var sessionSnapshot = await sessionTask;

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

            ReevaluateFindings();
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
        var observation = new MachineLearningObservation(
            resources.CapturedAt,
            resources.CpuUsagePercent,
            memoryPercent,
            activityState.Value,
            _latestFindingsSnapshot.OverallState,
            _latestFindingsSnapshot.Findings.Select(finding =>
                $"{finding.Code}:{finding.Severity}").ToArray(),
            freePercent,
            MachineInsightContextFingerprint.Create(_latestFindingsSnapshot),
            networkActivityClass,
            receiveBytesPerSecond,
            sendBytesPerSecond);

        return _learningService.Observe(observation);
    }

    private static double? GetVerifiedRate(double? value) =>
        value is not null && double.IsFinite(value.Value) && value.Value >= 0d
            ? value
            : null;

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
            $"Machine running {FormatUptime(snapshot.MachineUptime)}";
        OverviewSessionActivityText.Text =
            $"{snapshot.CurrentUserInputState} · " +
            $"last input {FormatInputAge(snapshot.CurrentUserIdleDuration)} ago";
        SessionSystemUptimeText.Text = FormatUptime(snapshot.SystemUptime);
        SessionMachineUptimeText.Text = FormatUptime(snapshot.MachineUptime);
        SessionInputStateText.Text = snapshot.CurrentUserInputState.ToString();
        SessionIdleDurationText.Text =
            FormatInputAge(snapshot.CurrentUserIdleDuration);
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
            $"{sessionCount:N0} Machine " +
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

        var learnedItems = snapshot.LearnedItems.Select(item =>
            new LearnedItemDisplayItem(
                $"{FormatLearningLayer(item.Layer)} · " +
                    (item.IsEarlyObservation
                        ? $"Early · {item.Confidence}"
                        : item.Confidence ==
                            MachineLearningConfidence.Established
                            ? "Established"
                            : "Recorded"),
                item.Text,
                $"Evidence · {FormatSampleCount(item.EvidenceCount)}"))
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
                Session: CreateSessionInsightContext(sessionSnapshot));
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

    private void OnDashboardBackRequested(
        NavigationView sender,
        NavigationViewBackRequestedEventArgs args)
    {
        ReturnToAmbientPresence();
    }

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
        LearningPage.Visibility = tag == "learning"
            ? Visibility.Visible
            : Visibility.Collapsed;
        NetworkPage.Visibility = tag == "network"
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

        if (tag == "learning")
        {
            UpdateLearningDashboard();
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
