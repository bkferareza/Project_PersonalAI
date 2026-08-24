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

public sealed partial class MainWindow
{
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

        _ = RefreshElectricityRateAsync(_windowCancellationTokenSource.Token);

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
            StoragePage.LoadAsync(
                isManualRefresh: false,
                cancellationToken: cancellationToken),
            SoftwarePage.LoadClassicAsync(
                isManualRefresh: false,
                cancellationToken: cancellationToken),
            SoftwarePage.LoadPackagedAsync(
                isManualRefresh: false,
                cancellationToken: cancellationToken),
            StartupPage.LoadAsync(
                isManualRefresh: false,
                cancellationToken: cancellationToken),
            ServicesPage.LoadAsync(
                isManualRefresh: false,
                cancellationToken),
            TasksPage.LoadAsync(
                isManualRefresh: false,
                cancellationToken),
            DevicesPage.LoadAsync(
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
        _runtimeInitializationCompletion.TrySetResult();

        await Task.WhenAll(
            telemetryLoop,
            processLoop,
            ollamaStatusLoop,
            healthLoop);
    }

    private async Task RefreshElectricityRateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _electricityRateEnrichment.GetCurrentAsync(
                DateTimeOffset.Now, cancellationToken);
            var cache = await _electricityRateEnrichment.LoadCachedAsync(
                cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                _latestElectricityRate = result;
                _cachedElectricityRates = cache.Rates;
                UpdateLearningDashboard();
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private async Task LoadIdentityAsync()
    {
        try
        {
            var identity = await _identityProvider.GetAsync();

            _latestIdentity = identity;

            OverviewPage.DeviceNameText.Text = identity.DeviceName;
            OverviewPage.OperatingSystemText.Text = identity.OperatingSystem;
            OverviewPage.ArchitectureText.Text = identity.Architecture;
            OverviewPage.LoadStatusText.Text = string.Empty;
            UpdateExplainMachineStateButtonState();
            TryRequestDashboardInsight();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            OverviewPage.DeviceNameText.Text = UnavailableValue;
            OverviewPage.OperatingSystemText.Text = UnavailableValue;
            OverviewPage.ArchitectureText.Text = UnavailableValue;
            OverviewPage.LoadStatusText.Text = "Local identity could not be loaded.";
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
            var storageHealthTask = ShouldRefreshStorageHealth()
                ? TryCaptureStorageHealthAsync(cancellationToken)
                : Task.FromResult(_latestStorageHealthSnapshot);
            await Task.WhenAll(
                resourceTask,
                networkTask,
                sessionTask,
                gpuTask,
                storageHealthTask);

            var snapshot = await resourceTask;
            var networkSnapshot = await networkTask;
            var sessionSnapshot = await sessionTask;
            var gpuSnapshot = await gpuTask;
            var storageHealth = await storageHealthTask;
            var cpuHardware = await TryCaptureCpuHardwareAsync(
                snapshot, cancellationToken);
            var powerEstimate = MachinePowerEstimator.Estimate(
                snapshot.CapturedAt,
                cpuHardware,
                gpuSnapshot?.Adapters.FirstOrDefault(),
                snapshot.TotalMemoryBytes,
                storageHealth?.Devices.Count ?? 0);
            var energyDeltaWh = _energyAccumulator.Sample(
                powerEstimate.EstimatedWallWatts,
                Stopwatch.GetTimestamp(),
                snapshot.CapturedAt);
            _pendingHistoryEnergyWattHours += energyDeltaWh;

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
            if (cpuHardware is not null)
            {
                _latestCpuHardwareSnapshot = cpuHardware;
            }
            if (storageHealth is not null)
            {
                _latestStorageHealthSnapshot = storageHealth;
                _lastStorageHealthRefreshAt = snapshot.CapturedAt;
            }

            OverviewPage.CpuUsageText.Text =
                $"{snapshot.CpuUsagePercent:F1}%";

            var usedMemory =
                snapshot.UsedMemoryBytes / BytesPerGibibyte;
            var totalMemory =
                snapshot.TotalMemoryBytes / BytesPerGibibyte;
            OverviewPage.MemoryUsageText.Text =
                $"{usedMemory:F1} GB / {totalMemory:F1} GB";
            OverviewPage.TelemetryStatusText.Text = string.Empty;
            OverviewPage.TelemetryStatusText.Visibility = Visibility.Collapsed;
            UpdateNetworkTelemetry(networkSnapshot);
            UpdateSessionTelemetry(sessionSnapshot);
            _latestPowerEstimate = powerEstimate;
            _latestEnergySnapshot = _energyAccumulator.GetSnapshot();

            ReevaluateFindings();
            var historyChanged = CaptureHistoryObservation(
                snapshot,
                networkSnapshot,
                sessionSnapshot,
                gpuSnapshot,
                cpuHardware,
                storageHealth,
                powerEstimate,
                _pendingHistoryEnergyWattHours);
            if (historyChanged)
            {
                _pendingHistoryEnergyWattHours = 0d;
            }
            var historyEnergy = MachineHistoryEnergyCostProjector.Project(
                _historyService.GetSnapshot(MachineHistoryRange.Last30Days,
                    snapshot.CapturedAt).Rollups, _cachedElectricityRates);
            var todayHistoryEnergy =
                MachineTodayEnergyCostProjector.Project(
                    _historyService.GetSnapshot(
                        MachineHistoryRange.Last24Hours,
                        snapshot.CapturedAt).Rollups,
                    _cachedElectricityRates,
                    snapshot.CapturedAt,
                    _pendingHistoryEnergyWattHours);
            _latestTodayEnergyCost = todayHistoryEnergy;
            _latestThirtyDayEnergyCost = historyEnergy;
            HardwarePage.Update(gpuSnapshot, cpuHardware, storageHealth,
                powerEstimate, _latestEnergySnapshot, historyEnergy,
                todayHistoryEnergy.Rate, todayHistoryEnergy);
            HistoryPage.UpdateTodaySummary();
            UpdateRunningBillInsight();
            var learningChanged = await CaptureLearningObservationAsync(
                snapshot,
                networkSnapshot,
                sessionSnapshot,
                powerEstimate,
                cancellationToken);
            var previousLearningHealth = _learningService.DataHealth;
            var previousLastPersistence = _learningService.LastPersistedAt;
            var persisted = await _learningService.SaveIfDueAsync(
                _learningStore,
                DateTimeOffset.UtcNow,
                cancellationToken: cancellationToken);
            await _learningService.ActivityLog.SaveIfDueAsync(
                _learningActivityStore,
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
                HistoryPage.UpdateDashboard();
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
                OverviewPage.CpuUsageText.Text = UnavailableValue;
                OverviewPage.MemoryUsageText.Text = UnavailableValue;
            }

            OverviewPage.TelemetryStatusText.Text =
                "Resource telemetry could not be loaded.";
            OverviewPage.TelemetryStatusText.Visibility = Visibility.Visible;
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

    private bool ShouldRefreshStorageHealth() =>
        _lastStorageHealthRefreshAt is null ||
        DateTimeOffset.UtcNow - _lastStorageHealthRefreshAt.Value >=
            TimeSpan.FromMinutes(5);

    private async Task<MachineCpuHardwareSnapshot?> TryCaptureCpuHardwareAsync(
        MachineResourceSnapshot resources,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _cpuHardwareProvider.GetAsync(resources,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return null;
        }
    }

    private async Task<MachineStorageDeviceHealthCollection?>
        TryCaptureStorageHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _storageDeviceHealthProvider.GetAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
        return (Brush)Application.Current.Resources[
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
                Storage: StoragePage.LatestStorageSnapshot,
                FolderInspection: StoragePage.LatestFolderInspectionSnapshot,
                ClassicSoftware: SoftwarePage.LatestClassicSnapshot,
                PackagedSoftware:
                    SoftwarePage.LatestPackagedSnapshot,
                Startup: StartupPage.LatestSnapshot,
                WindowsUpdate: _latestWindowsUpdateSnapshot,
                RebootPending: _latestRebootPendingSnapshot,
                Reliability: _latestReliabilitySnapshot,
                ResidentApplicationIdentity: Path.GetFileName(
                    Environment.ProcessPath)));

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
            await _learningService.ActivityLog.LoadAsync(
                _learningActivityStore,
                _windowCancellationTokenSource.Token);
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
            HistoryPage.UpdateDashboard();
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
            _historyService.BeginSession(DateTimeOffset.UtcNow);
            HistoryPage.UpdateDashboard();
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
            HealthPage.RefreshHealthButton.Content = "Refreshing...";
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
            HistoryPage.UpdateDashboard();
            ReevaluateFindings();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            HealthPage.HealthStatusText.Text =
                "Health context is temporarily unavailable.";
            HealthPage.HealthStatusText.Visibility = Visibility.Visible;
        }
        finally
        {
            _isHealthRequestRunning = false;
            if (!cancellationToken.IsCancellationRequested)
            {
                HealthPage.RefreshHealthButton.Content = "Refresh health";
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
        HealthPage.RefreshHealthButton.IsEnabled =
            !_isHealthRequestRunning &&
            !_windowCancellationTokenSource.IsCancellationRequested;
    }

    private async Task<bool> CaptureLearningObservationAsync(
        MachineResourceSnapshot resources,
        MachineNetworkSnapshot? network,
        MachineSessionSnapshot? session,
        MachinePowerEstimate powerEstimate,
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
        var systemVolume = StoragePage.LatestStorageSnapshot?.Volumes
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
                Storage: StoragePage.LatestStorageSnapshot,
                FolderInspection: StoragePage.LatestFolderInspectionSnapshot,
                ClassicSoftware: SoftwarePage.LatestClassicSnapshot,
                PackagedSoftware:
                    SoftwarePage.LatestPackagedSnapshot,
                Startup: StartupPage.LatestSnapshot));
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
            sendBytesPerSecond,
            EstimatedWallPowerWatts:
                MachineLearningPowerEvidencePolicy.
                    SelectEligibleEstimatedWallPowerWatts(
                        powerEstimate,
                        resources.CapturedAt));

        return _learningService.Observe(observation);
    }

    private bool CaptureHistoryObservation(
        MachineResourceSnapshot resources,
        MachineNetworkSnapshot? network,
        MachineSessionSnapshot? session,
        MachineGpuTelemetrySnapshot? gpu,
        MachineCpuHardwareSnapshot? cpu,
        MachineStorageDeviceHealthCollection? storageHealth,
        MachinePowerEstimate powerEstimate,
        double observedEnergyWattHours)
    {
        double? memoryPercent = resources.TotalMemoryBytes == 0
            ? null
            : resources.UsedMemoryBytes /
                (double)resources.TotalMemoryBytes * 100d;
        var systemVolume = StoragePage.LatestStorageSnapshot?.Volumes
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
            primaryGpu?.BoardPowerWatts,
            cpu?.TemperatureCelsius,
            cpu?.EstimatedPackagePowerWatts,
            storageHealth?.Devices.Select(item => item.TemperatureCelsius)
                .Where(item => item is not null).Select(item => item!.Value)
                .DefaultIfEmpty().Max(),
            powerEstimate.EstimatedWallWatts,
            ObservedEnergyWattHours: observedEnergyWattHours));
    }

    private static double? GetVerifiedRate(double? value) =>
        value is not null && double.IsFinite(value.Value) && value.Value >= 0d
            ? value
            : null;
}
