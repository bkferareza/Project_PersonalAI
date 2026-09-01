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

    private async Task RunInferenceStatusLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await RefreshInferenceStatusAsync(cancellationToken);
                await Task.Delay(
                    InferenceRefreshInterval,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshInferenceStatusAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _inferenceRuntime.GetStatusAsync(
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            UpdateInferenceStatus(snapshot);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            _latestInferenceStatus = null;
            ShowInferenceUnavailable();
        }
    }

    private void UpdateInferenceStatus(
        LocalInferenceStatus snapshot)
    {
        _latestInferenceStatus = snapshot;
        LearningPage.UpdateRuntimeStatus(_latestInferenceStatus);
        if (!snapshot.IsRuntimeAvailable)
        {
            ShowInferenceUnavailable();
            return;
        }

        _isInferenceRuntimeAvailable = true;
        RuntimePage.LocalAiStateText.Text =
            FormatLocalInferenceState(snapshot.ModelState);
        RuntimePage.LocalAiRuntimeText.Text =
            FormatLocalInferenceRuntime(snapshot);
        UpdateUsageOutlookButtonState();
        if (_detailsExpanded &&
            OverviewPage.Visibility == Visibility.Visible)
        {
            _ = EnsureUsageOutlookAsync(forceRefresh: false);
        }

        var displayItems = snapshot.LoadedModels
            .Select(CreateInferenceModelDisplayItem)
            .ToArray();

        RuntimePage.LocalAiRunningModelsList.ItemsSource = displayItems;

        if (displayItems.Length == 0)
        {
            RuntimePage.LocalAiLoadedModelsStatusText.Text =
                snapshot.ModelState == LocalInferenceModelState.Asleep
                    ? "Qwen is asleep until local interpretation is requested."
                    : "No model is currently loaded.";
            UpdateExplainMachineStateButtonState();
            return;
        }

        RuntimePage.LocalAiLoadedModelsStatusText.Text = string.Empty;
        UpdateExplainMachineStateButtonState();
    }

    private void ShowInferenceUnavailable()
    {
        _isInferenceRuntimeAvailable = false;
        RuntimePage.LocalAiStateText.Text = "Faulted";
        RuntimePage.LocalAiRuntimeText.Text = UnavailableValue;
        ClearInferenceModels(
            "Loaded-model status is unavailable.");
        LearningPage.UpdateRuntimeStatus(_latestInferenceStatus);
        if (_latestUsageOutlook is null)
        {
            OverviewPage.AiOutlookStatusText.Text =
                "AI outlook unavailable · local runtime offline.";
        }
        UpdateExplainMachineStateButtonState();
        UpdateUsageOutlookButtonState();
    }

    private void ClearInferenceModels(string status)
    {
        RuntimePage.LocalAiRunningModelsList.ItemsSource =
            Array.Empty<LocalInferenceModelDisplayItem>();
        RuntimePage.LocalAiLoadedModelsStatusText.Text = status;
    }

    private static LocalInferenceModelDisplayItem
        CreateInferenceModelDisplayItem(
            LocalInferenceLoadedModel model)
    {
        var parameterSize = string.IsNullOrWhiteSpace(
            model.ParameterSize)
            ? UnavailableValue
            : model.ParameterSize;
        var quantizationLevel = string.IsNullOrWhiteSpace(
            model.Quantization)
            ? UnavailableValue
            : model.Quantization;

        return new LocalInferenceModelDisplayItem(
            model.Name,
            $"{parameterSize} · {quantizationLevel}",
            $"{FormatBytes(model.SizeBytes)} model · " +
            $"{model.ContextLength.ToString("N0", CultureInfo.InvariantCulture)} context");
    }

    private static string FormatLocalInferenceState(
        LocalInferenceModelState state) => state switch
        {
            LocalInferenceModelState.Asleep => "Asleep",
            LocalInferenceModelState.Loading => "Loading Qwen",
            LocalInferenceModelState.Ready => "Ready",
            LocalInferenceModelState.Generating => "Generating",
            LocalInferenceModelState.Faulted => "Faulted",
            _ => UnavailableValue
        };

    private static string FormatLocalInferenceRuntime(
        LocalInferenceStatus snapshot)
    {
        var version = string.IsNullOrWhiteSpace(snapshot.RuntimeVersion)
            ? UnavailableValue
            : snapshot.RuntimeVersion;
        var backend = string.IsNullOrWhiteSpace(snapshot.Backend)
            ? null
            : snapshot.Backend;
        return string.IsNullOrWhiteSpace(backend)
            ? $"{snapshot.RuntimeName} {version}"
            : $"{snapshot.RuntimeName} {version} · {backend}";
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

    private async void OnRefreshUsageOutlookClicked(
        object sender,
        RoutedEventArgs e) =>
        await EnsureUsageOutlookAsync(forceRefresh: true);

    private async Task EnsureUsageOutlookAsync(bool forceRefresh)
    {
        var request = CreateUsageOutlookRequest();
        var isOverviewVisible = _detailsExpanded &&
            OverviewPage.Visibility == Visibility.Visible;
        if (request is null ||
            !_isInferenceRuntimeAvailable ||
            _windowCancellationTokenSource.IsCancellationRequested)
        {
            UpdateUsageOutlookButtonState();
            return;
        }

        var decision = _usageOutlookCachePolicy.Request(
            request,
            DateTimeOffset.UtcNow,
            isOverviewVisible,
            forceRefresh);
        if (decision.Kind == MachineUsageOutlookDecisionKind.UseCached &&
            decision.CachedOutlook is { } cached)
        {
            PresentUsageOutlook(cached, elapsed: null, fromCache: true);
            UpdateUsageOutlookButtonState();
            return;
        }
        if (!decision.ShouldGenerate)
        {
            UpdateUsageOutlookButtonState();
            return;
        }

        _isUsageOutlookRequestRunning = true;
        ApplyShellAtmosphere();
        ApplyPresenceVisualMode();
        UpdateUsageOutlookButtonState();
        OverviewPage.RefreshUsageOutlookButton.Content = "Refreshing...";
        OverviewPage.AiOutlookProgressRing.Visibility = Visibility.Visible;
        OverviewPage.AiOutlookProgressRing.IsActive = true;
        OverviewPage.AiOutlookStatusText.Text =
            "Generating from precomputed local forecast evidence...";
        var stopwatch = Stopwatch.StartNew();
        MachineUsageOutlook? generated = null;

        try
        {
            generated = await _usageOutlookGenerator.GenerateAsync(
                request,
                _windowCancellationTokenSource.Token);
            stopwatch.Stop();
            _windowCancellationTokenSource.Token
                .ThrowIfCancellationRequested();

            var currentRequest = CreateUsageOutlookRequest();
            if (currentRequest is not null &&
                string.Equals(
                    decision.Fingerprint,
                    _usageOutlookCachePolicy.CreateRequestFingerprint(
                        currentRequest),
                    StringComparison.Ordinal))
            {
                PresentUsageOutlook(
                    generated,
                    stopwatch.Elapsed,
                    fromCache: false);
            }
        }
        catch (OperationCanceledException)
            when (_windowCancellationTokenSource.IsCancellationRequested)
        {
            stopwatch.Stop();
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            Debug.WriteLine(exception);
            OverviewPage.AiOutlookStatusText.Text =
                "AI outlook unavailable · deterministic forecast remains active.";
        }
        finally
        {
            stopwatch.Stop();
            _usageOutlookCachePolicy.Complete(
                decision,
                generated,
                DateTimeOffset.UtcNow);
            _isUsageOutlookRequestRunning = false;
            ApplyShellAtmosphere();
            ApplyPresenceVisualMode();
            if (!_windowCancellationTokenSource.IsCancellationRequested)
            {
                OverviewPage.RefreshUsageOutlookButton.Content =
                    "Refresh outlook";
                OverviewPage.AiOutlookProgressRing.IsActive = false;
                OverviewPage.AiOutlookProgressRing.Visibility =
                    Visibility.Collapsed;
                UpdateUsageOutlookButtonState();
            }
        }
    }

    private MachineUsageOutlookRequest? CreateUsageOutlookRequest()
    {
        var forecast = _latestUsageForecast;
        if (forecast is null || !forecast.HasNextObservedHourForecast)
        {
            return null;
        }

        var learning = _learningService.GetDashboardSnapshot(
            DateTimeOffset.UtcNow);
        var baseline = learning.CurrentBaseline;
        var relevantPatterns = learning.BroaderPatterns
            .Where(pattern =>
                pattern.Confidence == MachineLearningConfidence.Established &&
                pattern.Freshness != MachineLearningFreshness.Stale)
            .OrderBy(pattern => forecast.CurrentContext is { } context &&
                pattern.ActivityState == context.ActivityState
                    ? 0
                    : 1)
            .ThenBy(pattern => pattern.StartHour)
            .Take(2)
            .ToArray();
        return new(
            forecast,
            learning.Readiness.MemoryState,
            baseline?.SampleCount ?? 0,
            baseline?.ObservedDayCount ?? 0,
            learning.Readiness.PatternReadiness.TotalProfileCount,
            learning.Readiness.PatternReadiness.EstablishedProfileCount,
            relevantPatterns);
    }

    private void PresentUsageOutlook(
        MachineUsageOutlook outlook,
        TimeSpan? elapsed,
        bool fromCache)
    {
        _latestUsageOutlook = outlook;
        OverviewPage.AiOutlookText.Text = outlook.Text;
        var latency = elapsed is { } duration
            ? $" · {duration.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s"
            : string.Empty;
        OverviewPage.AiOutlookMetadataText.Text = outlook.Source ==
                MachineExplanationSource.DeterministicFallback
            ? "Precomputed local safeguard"
            : fromCache
                ? $"Cached locally · {outlook.Model}"
                : $"Generated locally · {outlook.Model}{latency}";
        OverviewPage.AiOutlookStatusText.Text = string.Empty;
    }

    private void UpdateUsageOutlookButtonState()
    {
        OverviewPage.RefreshUsageOutlookButton.IsEnabled =
            _latestUsageForecast?.HasNextObservedHourForecast == true &&
            _isInferenceRuntimeAvailable &&
            !_usageOutlookCachePolicy.IsRequestInFlight &&
            !_isUsageOutlookRequestRunning &&
            !_windowCancellationTokenSource.IsCancellationRequested;
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
        var explainedInsight = _currentInsight;
        var cancellationToken =
            _windowCancellationTokenSource.Token;

        if (identity is null ||
            resources is null ||
            processSnapshots.Length == 0 ||
            cancellationToken.IsCancellationRequested)
        {
            _insightTriggerPolicy.CompleteRequest(
                decision,
                insightAccepted: false,
                DateTimeOffset.UtcNow,
                isLocalInferenceAvailable: false);
            UpdateExplainMachineStateButtonState();
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
                Gpu: CreateGpuInsightContext(_latestGpuTelemetrySnapshot),
                EnergyCost: CreateEnergyCostInsightSnapshot(),
                CurrentInsight: explainedInsight?.ExplainContext);
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
                    StringComparison.Ordinal) &&
                string.Equals(
                    explainedInsight?.Id,
                    _currentInsight?.Id,
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

            _insightTriggerPolicy.CompleteRequest(
                decision,
                insightAccepted,
                DateTimeOffset.UtcNow,
                IsInsightContextAvailable());

            if (!cancellationToken.IsCancellationRequested)
            {
                OverviewPage.ExplainMachineStateButton.Content = "Explain";
                OverviewPage.MachineExplanationProgressRing.IsActive = false;
                OverviewPage.MachineExplanationProgressRing.Visibility =
                    Visibility.Collapsed;
                UpdateExplainMachineStateButtonState();
            }
        }
    }

    private bool IsInsightContextAvailable() =>
        _currentInsight is not null &&
        _latestIdentity is not null &&
        _latestResourceSnapshot is not null &&
        _latestProcessSnapshots.Count > 0 &&
        _isInferenceRuntimeAvailable &&
        !_windowCancellationTokenSource.IsCancellationRequested;

    private void UpdateExplainMachineStateButtonState()
    {
        OverviewPage.ExplainMachineStateButton.IsEnabled =
            IsInsightContextAvailable() &&
            !_insightTriggerPolicy.IsRequestInFlight &&
            !_isExplanationRequestRunning;
    }

    private MachineEnergyCostInsightSnapshot? CreateEnergyCostInsightSnapshot()
    {
        var power = _latestPowerEstimate;
        var energy = _latestEnergySnapshot;
        if (power is null && energy is null && _latestElectricityRate?.Rate is null)
        {
            return null;
        }
        var today = _latestTodayEnergyCost;
        var thirtyDay = _latestThirtyDayEnergyCost;
        var rate = today?.Rate ?? _latestElectricityRate?.Rate;
        var sessionWh = energy?.SessionWattHours;
        return new(DateTimeOffset.UtcNow,
            power?.EstimatedWallWatts, power?.EstimatedWallLowerWatts,
            power?.EstimatedWallUpperWatts,
            power?.Confidence ?? MachinePowerEstimateConfidence.Unavailable,
            sessionWh is > 0d ? sessionWh.Value / 1000d : null,
            today?.HasObservedEnergy == true
                ? today.ObservedEnergyWattHours / 1000d
                : null,
            thirtyDay?.ObservedWattHours is > 0d ? thirtyDay.ObservedWattHours / 1000d : null,
            MachineElectricityCostCalculator.Calculate(sessionWh ?? -1d, rate),
            today?.EstimatedCost, thirtyDay?.EstimatedCost,
            thirtyDay?.MonthsWithRate == 0 ? MachineCostCoverage.Unavailable :
                thirtyDay?.MonthsWithoutRate > 0 ? MachineCostCoverage.Partial :
                MachineCostCoverage.Complete,
            rate?.ProviderName, rate?.CurrencyCode, rate?.RatePerKWh,
            rate?.EffectiveMonth,
            rate?.RateConfidence ?? MachinePowerEstimateConfidence.Unavailable);
    }

    private void UpdateLocalInsight()
    {
        if (_windowCancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        UpdateTodayStatus();

        var now = DateTimeOffset.UtcNow;
        var candidates = new MachineInsightCandidate?[]
        {
            MachineInsightCandidateProjector.ProjectMachineFinding(
                _latestFindingsSnapshot,
                now),
            MachineInsightCandidateProjector.ProjectLearnedEnergyDeviation(
                CreateTodayLearnedEnergyComparison(now),
                now)
        };
        var previousId = _currentInsight?.Id;
        var previousNewState = _hasNewUnseenInsight;
        var selection = _insightArbiter.Evaluate(
            candidates.OfType<MachineInsightCandidate>(),
            now);

        if (_detailsExpanded &&
            OverviewPage.Visibility == Visibility.Visible)
        {
            selection = _insightArbiter.MarkCurrentViewed();
        }

        _currentInsight = selection.CurrentInsight;
        _hasNewUnseenInsight = selection.HasNewUnseenInsight;
        if (_currentInsight is null)
        {
            OverviewPage.LocalInsightCandidatePanel.Visibility =
                Visibility.Collapsed;
            if (previousId is not null)
            {
                _hasSuccessfulExplanation = false;
                OverviewPage.MachineExplanationText.Text =
                    "Watching for a meaningful change.";
                OverviewPage.MachineExplanationMetadataText.Text =
                    string.Empty;
                OverviewPage.MachineExplanationMetadataText.Visibility =
                    Visibility.Collapsed;
            }
            UpdateExplainMachineStateButtonState();
            if (previousNewState != _hasNewUnseenInsight)
            {
                ApplyPresenceVisualMode(force: true);
            }
            return;
        }

        OverviewPage.LocalInsightTitleText.Text = _currentInsight.Title;
        OverviewPage.LocalInsightPrimaryText.Text =
            _currentInsight.PrimaryText;
        OverviewPage.LocalInsightSecondaryText.Text =
            _currentInsight.SecondaryText;
        OverviewPage.LocalInsightEvidenceText.Text =
            _currentInsight.EvidenceSummary;
        OverviewPage.LocalInsightCandidatePanel.Visibility =
            Visibility.Visible;

        if (!string.Equals(previousId, _currentInsight.Id,
            StringComparison.Ordinal))
        {
            _hasSuccessfulExplanation = false;
            OverviewPage.MachineExplanationText.Text =
                "Deterministic local evidence · explanation is optional.";
            OverviewPage.MachineExplanationMetadataText.Text = string.Empty;
            OverviewPage.MachineExplanationMetadataText.Visibility =
                Visibility.Collapsed;
        }

        UpdateExplainMachineStateButtonState();
        if (previousNewState != _hasNewUnseenInsight)
        {
            ApplyPresenceVisualMode(force: true);
        }
    }

    private void UpdateTodayStatus()
    {
        var presentation = OverviewTodayStatusPresenter.Present(
            MachineTodayStatusProjector.Project(_latestTodayEnergyCost));
        OverviewPage.TodayRunningBillTitleText.Text = presentation.Title;
        OverviewPage.TodayRunningBillCostText.Text =
            presentation.PrimaryText;
        OverviewPage.TodayRunningBillEnergyText.Text =
            presentation.EnergyText;
        OverviewPage.TodayRunningBillEvidenceText.Text =
            presentation.EvidenceText;
    }

    private void MarkCurrentInsightViewed()
    {
        if (!_insightArbiter.HasNewUnseenInsight)
        {
            return;
        }

        var selection = _insightArbiter.MarkCurrentViewed();
        _currentInsight = selection.CurrentInsight;
        _hasNewUnseenInsight = selection.HasNewUnseenInsight;
        ApplyPresenceVisualMode(force: true);
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
