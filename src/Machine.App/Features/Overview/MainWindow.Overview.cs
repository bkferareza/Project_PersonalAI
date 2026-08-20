using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Machine.Core;
using Machine.Windows;
using Machine.App.Features;
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
    private void UpdateCurrentFindings(
        MachineFindingsSnapshot snapshot)
    {
        var presentationState = GetPresentationState();
        OverviewPage.FindingsOverallStateText.Text = presentationState.ToString();
        OverviewPage.FindingsOverallStateText.Foreground =
            GetStateBrush(presentationState);
        OverviewPage.OverviewStatePostureText.Text = presentationState switch
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
                Header: finding.PostureImpact ==
                    MachineFindingPostureImpact.Local
                    ? $"{finding.Severity} - localized issue - {finding.Title}"
                    : $"{finding.Severity} - {finding.Title}",
                Detail: finding.Detail))
            .ToArray();

        OverviewPage.CurrentFindingsList.ItemsSource = displayItems;
        OverviewPage.FindingsSummaryText.Text = snapshot.OverallState ==
            MachineOverallState.Unknown
                ? "Resource telemetry and readable " +
                    "system-volume data are unavailable."
                : displayItems.Length == 0
                    ? "No deterministic issues currently detected."
                    : snapshot.OverallState == MachineOverallState.Stable &&
                      snapshot.Findings.Any(finding =>
                          finding.PostureImpact ==
                              MachineFindingPostureImpact.Local)
                        ? $"{snapshot.Findings.Count(finding =>
                            finding.PostureImpact ==
                                MachineFindingPostureImpact.Local)} localized " +
                          "reliability issue remains visible."
                        : string.Empty;
        OverviewPage.FindingsSummaryText.Visibility =
            OverviewPage.FindingsSummaryText.Text.Length == 0
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

            RuntimePage.TopProcessesList.ItemsSource = verifiedSnapshots
                .Select(snapshot => new ProcessDisplayItem(
                    snapshot.Name,
                    $"PID {snapshot.ProcessId} · " +
                    $"{snapshot.CpuUsagePercent:F1}% CPU · " +
                    FormatBytes(snapshot.WorkingSetBytes)))
                .ToArray();
            RuntimePage.ProcessStatusText.Text = string.Empty;
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

            RuntimePage.ProcessStatusText.Text =
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
        LearningPage.UpdateRuntimeStatus(_latestOllamaStatusSnapshot);
        if (!snapshot.IsServiceAvailable)
        {
            ShowOllamaOffline();
            return;
        }

        _isOllamaServiceAvailable = true;
        RuntimePage.OllamaServiceStatusText.Text = "Online";
        RuntimePage.OllamaVersionText.Text = string.IsNullOrWhiteSpace(
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

        RuntimePage.OllamaRunningModelsList.ItemsSource = displayItems;

        if (displayItems.Length == 0)
        {
            RuntimePage.OllamaLoadedModelsStatusText.Text =
                "No models currently loaded.";
            UpdateExplainMachineStateButtonState();
            TryRequestDashboardInsight();
            return;
        }

        RuntimePage.OllamaLoadedModelsStatusText.Text = string.Empty;
        UpdateExplainMachineStateButtonState();
        TryRequestDashboardInsight();
    }

    private void ShowOllamaOffline()
    {
        _isOllamaServiceAvailable = false;
        RuntimePage.OllamaServiceStatusText.Text = "Offline";
        RuntimePage.OllamaVersionText.Text = UnavailableValue;
        ClearOllamaModels(
            "Loaded-model status is unavailable.");
        LearningPage.UpdateRuntimeStatus(_latestOllamaStatusSnapshot);
        UpdateExplainMachineStateButtonState();
    }

    private void ClearOllamaModels(string status)
    {
        RuntimePage.OllamaRunningModelsList.ItemsSource =
            Array.Empty<OllamaModelDisplayItem>();
        RuntimePage.OllamaLoadedModelsStatusText.Text = status;
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
        var storageSnapshot = StoragePage.LatestStorageSnapshot;
        var folderInspectionSnapshot =
            StoragePage.LatestFolderInspectionSnapshot;
        var softwareInventorySnapshot =
            SoftwarePage.LatestClassicSnapshot;
        var packagedSoftwareInventorySnapshot =
            SoftwarePage.LatestPackagedSnapshot;
        var startupInventorySnapshot =
            StartupPage.LatestSnapshot;
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
        OverviewPage.ExplainMachineStateButton.Content = "Refreshing...";
        OverviewPage.MachineExplanationProgressRing.Visibility =
            Visibility.Visible;
        OverviewPage.MachineExplanationProgressRing.IsActive = true;
        OverviewPage.MachineExplanationStatusText.Text =
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
                OverviewPage.MachineExplanationText.Text = explanation.Text;
                var elapsedSeconds =
                    stopwatch.Elapsed.TotalSeconds.ToString(
                        "F1",
                        CultureInfo.InvariantCulture);
                OverviewPage.MachineExplanationMetadataText.Text =
                    explanation.Source ==
                        MachineExplanationSource.DeterministicFallback
                        ? "Verified summary · local safeguard"
                        : $"Generated locally · {explanation.Model} · " +
                            $"{elapsedSeconds}s";
                OverviewPage.MachineExplanationMetadataText.Visibility =
                    Visibility.Visible;
                OverviewPage.MachineExplanationStatusText.Text = string.Empty;
                _hasSuccessfulExplanation = true;
                insightAccepted = true;
                BeginNewInsightBloom();
            }
            else
            {
                OverviewPage.MachineExplanationStatusText.Text =
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
                OverviewPage.MachineExplanationMetadataText.Text = string.Empty;
                OverviewPage.MachineExplanationMetadataText.Visibility =
                    Visibility.Collapsed;
            }

            OverviewPage.MachineExplanationStatusText.Text =
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
                OverviewPage.ExplainMachineStateButton.Content =
                    "Refresh insight";
                OverviewPage.MachineExplanationProgressRing.IsActive = false;
                OverviewPage.MachineExplanationProgressRing.Visibility =
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
        OverviewPage.ExplainMachineStateButton.IsEnabled =
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
