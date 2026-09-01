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

public sealed partial class LearningView
{
    internal void Update(
        MachineLearningDashboardSnapshot snapshot,
        MachineLearningActivitySnapshot activity,
        MachineLearningLabSnapshot lab,
        MachineSituationSnapshot situation,
        MachineLearnedPowerCostProjection? currentPower,
        MachineTodayLearnedEnergyComparison todayComparison,
        MachineLearnedUsageSnapshot learnedUsage,
        MachineUsageForecast forecast,
        MachineHealthHistorySnapshot healthHistory,
        LocalInferenceStatus? inferenceStatus,
        OverviewView overview,
        MachineBrief? brief)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(lab);
        ArgumentNullException.ThrowIfNull(situation);
        ArgumentNullException.ThrowIfNull(todayComparison);
        ArgumentNullException.ThrowIfNull(learnedUsage);
        ArgumentNullException.ThrowIfNull(forecast);
        ArgumentNullException.ThrowIfNull(healthHistory);
        ArgumentNullException.ThrowIfNull(overview);
        var current = snapshot.CurrentObservation;
        var baseline = snapshot.CurrentBaseline;
        var confidence = baseline?.Confidence ??
            MachineLearningConfidence.Calibrating;
        var readiness = lab.PatternReadiness;
        var memoryState = snapshot.Readiness.MemoryState ==
                MachineLearningMemoryState.PersistenceAtRisk
            ? "Persistence at risk"
            : lab.Live.LifetimeObservationCount > 0
                ? "Active"
                : "Waiting for first evidence";

        UpdateLiveLearning(lab);

        overview.LearningConfidenceText.Text =
            $"Behavior memory · {memoryState}";
        overview.LearningObservedDurationText.Text = baseline is null
            ? "Current context · Waiting for verified telemetry"
            : $"Current context · {FormatLearningHour(baseline.LocalHour)} · " +
                $"{FormatActivity(baseline.ActivityState)} · " +
                FormatContextMaturity(confidence);
        overview.LearningObservationText.Text = baseline is null
            ? $"{FormatSampleCount(snapshot.ObservationCount)} observed"
            : $"{FormatSampleCount(baseline.SampleCount)} across " +
                $"{baseline.ObservedDayCount:N0} " +
                (baseline.ObservedDayCount == 1 ? "day" : "days") +
                $" · {readiness.EstablishedProfileCount:N0} established " +
                (readiness.EstablishedProfileCount == 1
                    ? "profile"
                    : "profiles") +
                $" · {FormatPatternReadinessCompact(readiness)}";

        LearningPageMemoryStateText.Text =
            $"Behavior memory · {memoryState}";
        LearningPageMemoryEvidenceText.Text =
            $"{lab.Memory.BaselineCount:N0} baselines · " +
            $"{lab.Memory.ProfileCount:N0} compact profiles · " +
            $"{readiness.EstablishedProfileCount:N0} established · " +
            $"{lab.Memory.PatternCount:N0} recurring patterns";

        var sessionCount = snapshot.Metadata.LifetimeMachineSessionCount;
        LearningPageObservedText.Text =
            $"{FormatDuration(snapshot.ObservedDuration)} across " +
            $"{sessionCount:N0} Matasuri " +
            (sessionCount == 1 ? "session" : "sessions");
        LearningPageLifetimeObservationsText.Text =
            $"{snapshot.Metadata.LifetimeAcceptedObservationCount:N0} lifetime";
        LearningPageContextCountText.Text =
            $"{lab.Memory.BaselineCount:N0} / " +
            $"{MachineLearningService.MaximumContextProfileCount:N0}";
        LearningPageEstablishedProfilesText.Text =
            $"{lab.Memory.ProfileCount:N0} / " +
            $"{lab.Memory.ProfileCapacity:N0}";
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
            $"{lab.Memory.RawObservationCount:N0} / " +
            $"{lab.Memory.RawObservationCapacity:N0} · " +
            $"{FormatDuration(lab.Memory.RawObservationRetention)} window";
        LearningPageRecentEpisodesText.Text =
            $"{snapshot.RecentEpisodeCount:N0} / " +
            $"{MachineLearningService.MaximumEpisodeCount:N0}";
        LearningPageCurrentContextText.Text = current is null
            ? "Waiting for verified telemetry"
            : $"{current.Timestamp.ToLocalTime():h tt} · " +
                $"{FormatActivity(current.ActivityState)} · " +
                FormatContextMaturity(confidence);
        LearningPageMemoryPersistenceText.Text =
            $"Schema v{lab.Memory.SchemaVersion} · " +
            $"{FormatLearningDataHealth(lab.Memory.DataHealth)} · " +
            (lab.Memory.LastPersistedAt is { } lastPersisted
                ? $"last save {FormatLearningDateTime(
                    lastPersisted,
                    "unknown")}"
                : "not yet saved") +
            (lab.Memory.HasPendingChanges
                ? " · changes pending"
                : " · fully persisted");

        LearningPageCurrentBucketText.Text = baseline is null
            ? "Waiting"
            : $"{FormatLearningHour(baseline.LocalHour)} · " +
                FormatActivity(baseline.ActivityState);
        LearningPageCurrentSamplesText.Text =
            $"{baseline?.SampleCount ?? 0:N0}";
        LearningPageObservedDaysText.Text =
            $"{baseline?.ObservedDayCount ?? 0:N0} / " +
            $"{MachineLearningService.EstablishedObservedDayCount:N0}";
        LearningPageConfidenceText.Text = baseline is null
            ? "No evidence"
            : FormatContextMaturity(confidence);
        LearningPageConfidenceRulesText.Text =
            FormatCurrentContextMaturity(baseline);

        LearningPatternReadinessHeadlineText.Text =
            FormatPatternReadinessHeadline(readiness);
        LearningPatternProfileReadinessText.Text =
            $"{readiness.TotalProfileCount:N0} profiles · " +
            $"{readiness.ProfilesWithSufficientSamples:N0} meet " +
            $"{MachineLearningService.EstablishedSampleCount:N0} samples · " +
            $"{readiness.ProfilesWithSufficientDistinctDays:N0} meet " +
            $"{MachineLearningService.EstablishedObservedDayCount:N0} days · " +
            $"{readiness.EstablishedProfileCount:N0} established";
        LearningPatternPairReadinessText.Text =
            $"{readiness.AdjacentCandidatePairCount:N0} same-activity adjacent pairs · " +
            $"{readiness.PairsMeetingEvidenceThresholds:N0} meet evidence thresholds · " +
            $"{readiness.EstablishedPairCount:N0} established · " +
            $"{readiness.TemporallyEligiblePairCount:N0} current · " +
            $"{readiness.PairsReachingCompatibilityComparison:N0} compared · " +
            $"{readiness.CompatiblePairCount:N0} compatible";
        LearningPatternReadinessReasonText.Text =
            FormatPatternReadinessReason(readiness);
        UpdateCurrentPowerProjection(currentPower);
        UpdateTodayLearnedEnergyComparison(todayComparison);
        UpdateUsageForecast(learnedUsage, forecast, overview);

        var orderedProfiles = lab.LearnedContexts
            .Select(item => CreateLearningProfileDisplayItem(
                item,
                baseline is not null &&
                    item.LocalHour == baseline.LocalHour &&
                    item.ActivityState == baseline.ActivityState))
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
        LearningPatternsEmptyText.Text =
            FormatPatternReadinessReason(readiness);
        LearningPatternsEmptyText.Visibility = patterns.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var healthLearnedItems = MachineHealthLearnedItemProjector.Project(
            healthHistory);
        var learnedItems = snapshot.LearnedItems
            .Concat(healthLearnedItems)
            .Take(MachineLearnedItemProjector.DefaultMaximumItemCount)
            .Select(item => new LearnedItemDisplayItem(
                $"{FormatLearningLayer(item.Layer)} · " +
                    (item.IsEarlyObservation
                        ? "Early evidence"
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
        LearningActivityStatusText.Text = FormatLearningActivityStatus(
            activity.Status);
        var recentChanges = lab.RecentChanges
            .Select(item => new LearningActivityDisplayItem(
                FormatActivityHeader(item),
                FormatActivityDetail(item)))
            .ToArray();
        LearningActivityEventsList.ItemsSource = recentChanges;
        LearningActivityEmptyText.Visibility = recentChanges.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        LearningAiKnowledgeSummaryText.Text =
            $"Situation snapshot · {FormatAge(
                DateTimeOffset.UtcNow <= situation.CapturedAt
                    ? TimeSpan.Zero
                    : DateTimeOffset.UtcNow - situation.CapturedAt)} old · " +
            $"{situation.Evidence.Count:N0} selected / " +
            $"{situation.CandidateEvidenceCount:N0} candidates";
        LearningAiKnowledgeEvidenceText.Text = string.Join(
            " · ",
            situation.Evidence
                .GroupBy(item => item.Category)
                .OrderBy(group => group.Key)
                .Select(group => $"{FormatSituationCategory(group.Key)} " +
                    $"{group.Count():N0}"));
        LearningAiKnowledgePromptText.Text =
            $"Brief {MachineBriefPromptPolicy.CurrentVersion} · response " +
            $"schema v{MachineBriefPromptPolicy.ResponseSchemaVersion} · " +
            $"situation schema v{situation.SchemaVersion}";
        LearningAiKnowledgeContextText.Text =
            $"{situation.Evidence.Count:N0} selected evidence items · " +
            (brief is null || string.IsNullOrWhiteSpace(
                    brief.SituationFingerprint)
                ? "fingerprint pending first generation"
                : $"fingerprint {brief.SituationFingerprint}");
        UpdateBriefInspection(brief);
        LearningAiKnowledgeEvidenceList.ItemsSource = situation.Evidence
            .Select(item => new SituationEvidenceDisplayItem(
                $"{item.Id} · {FormatSituationCategory(item.Category)} · " +
                    $"{FormatSituationImportance(item.Importance)}",
                item.Summary,
                item.DisplayValues.Count == 0
                    ? "No display values"
                    : string.Join(" · ", item.DisplayValues)))
            .ToArray();
        UpdateRuntimeStatus(inferenceStatus);
    }

    private void UpdateLiveLearning(MachineLearningLabSnapshot lab)
    {
        var live = lab.Live;
        var observation = live.CurrentObservation;
        var context = live.CurrentContext;
        LearningLiveContextText.Text = context is null
            ? "Waiting for verified telemetry"
            : $"{FormatLearningHour(context.LocalHour)} · " +
                FormatActivity(context.ActivityState);
        LearningLiveLastObservationText.Text = live.LastIntakeAt is { } at
            ? $"Last intake · {at.ToLocalTime():MMM d HH:mm:ss} · " +
                $"{FormatAge(live.LastIntakeAge)} ago"
            : "No intake attempt yet";
        LearningLiveAcceptanceText.Text = live.LastIntakeOutcome switch
        {
            MachineLearningIntakeOutcome.Accepted => "Accepted",
            MachineLearningIntakeOutcome.Rejected => "Rejected",
            MachineLearningIntakeOutcome.Throttled => "Throttled",
            _ => "Waiting"
        };
        LearningLiveAcceptanceReasonText.Text = live.LastIntakeReason;
        LearningLiveLifetimeText.Text =
            $"{live.LifetimeObservationCount:N0}";
        LearningLiveSessionText.Text =
            $"{live.SessionObservationCount:N0}";
        LearningLiveContextEvidenceText.Text = context is null
            ? "0 samples · 0 days"
            : $"{context.SampleCount:N0} " +
                (context.SampleCount == 1 ? "sample" : "samples") +
                $" · {context.ObservedDayCount:N0} " +
                (context.ObservedDayCount == 1 ? "day" : "days");
        LearningLiveMaturityText.Text = context is null
            ? "No evidence"
            : $"{FormatContextMaturity(context.Confidence)} · " +
                FormatFreshness(context.Freshness);
        LearningLivePowerMaturityText.Text = context is null
            ? "No evidence"
            : $"{FormatPowerMaturity(context.EstimatedWallPowerMaturity)} · " +
                $"{context.EstimatedWallPowerSampleCount:N0} eligible " +
                (context.EstimatedWallPowerSampleCount == 1
                    ? "sample"
                    : "samples") +
                (context.EstimatedWallPowerFreshness is { } freshness
                    ? $" · {FormatFreshness(freshness)}"
                    : string.Empty);

        var signals = observation is null || context is null
            ? []
            : new LearningSignalDisplayItem[]
            {
                new(
                    "Activity",
                    $"Current · {FormatActivity(observation.ActivityState)}",
                    $"Context key · {FormatLearningHour(context.LocalHour)} · " +
                        FormatActivity(context.ActivityState)),
                new(
                    "CPU usage",
                    $"Current · {observation.CpuUsagePercent:F1}%",
                    FormatLearnedMetric(
                        context.CpuMean,
                        context.CpuStandardDeviation,
                        context.AdaptiveCpuMean,
                        context.CpuTypicalRange,
                        "%")),
                new(
                    "Memory usage",
                    $"Current · {observation.MemoryUsagePercent:F1}%",
                    FormatLearnedMetric(
                        context.MemoryMean,
                        context.MemoryStandardDeviation,
                        context.AdaptiveMemoryMean,
                        context.MemoryTypicalRange,
                        "%")),
                new(
                    "Network behavior",
                    $"Current · {FormatNetworkActivity(
                        observation.NetworkActivityClass)}",
                    FormatLearnedNetwork(context)),
                new(
                    "Estimated wall power",
                    observation.EstimatedWallPowerWatts is { } watts
                        ? $"Current eligible estimate · {watts:F1} W"
                        : "Current · no eligible power estimate",
                    FormatLearnedPower(context))
            };
        LearningLiveSignalsList.ItemsSource = signals;
        LearningLiveSignalsEmptyText.Visibility = signals.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static string FormatAge(TimeSpan? age)
    {
        if (age is null)
        {
            return "unknown";
        }
        if (age.Value.TotalHours >= 1d)
        {
            return $"{(int)age.Value.TotalHours}h {age.Value.Minutes}m";
        }
        if (age.Value.TotalMinutes >= 1d)
        {
            return $"{(int)age.Value.TotalMinutes}m {age.Value.Seconds}s";
        }
        return $"{Math.Max(0, (int)age.Value.TotalSeconds)}s";
    }

    private static string FormatLearnedMetric(
        double historicalMean,
        double historicalStandardDeviation,
        double adaptiveMean,
        MachineLearningRange? adaptiveRange,
        string unit) =>
        $"Historical mean {historicalMean:F1}{unit} · σ " +
        $"{historicalStandardDeviation:F1}{unit}\n" +
        $"Adaptive mean {adaptiveMean:F1}{unit} · " +
        (adaptiveRange is null
            ? "typical range not yet available"
            : $"range {adaptiveRange.Low:F1}–{adaptiveRange.High:F1}{unit}");

    private static string FormatLearnedNetwork(
        MachineLearningBaseline context) =>
        context.DominantNetworkActivityClass is { } dominant
            ? $"Dominant {FormatNetworkActivity(dominant)} · " +
                $"{context.DominantNetworkActivityCount:N0} / " +
                $"{context.NetworkObservationCount:N0} classified samples\n" +
                $"Quiet {context.NetworkQuietSampleCount:N0} · " +
                $"Light {context.NetworkLightSampleCount:N0} · " +
                $"Active {context.NetworkActiveSampleCount:N0} · " +
                $"Unavailable {context.NetworkUnavailableSampleCount:N0}"
            : "No classified network evidence yet";

    private static string FormatLearnedPower(
        MachineLearningBaseline context)
    {
        if (context.EstimatedWallPowerSampleCount == 0 ||
            context.EstimatedWallPowerMeanWatts is not { } historicalMean)
        {
            return "No eligible power evidence stored for this context";
        }

        var historicalDeviation =
            context.EstimatedWallPowerStandardDeviationWatts ?? 0d;
        var adaptive = context.AdaptiveEstimatedWallPowerMeanWatts is
                { } adaptiveMean
            ? $"Adaptive mean {adaptiveMean:F1} W"
            : "Adaptive mean unavailable";
        var range = context.EstimatedWallPowerTypicalRange is { } typical
            ? $" · range {typical.Low:F1}–{typical.High:F1} W"
            : " · typical range not yet available";
        return $"Historical mean {historicalMean:F1} W · " +
            $"σ {historicalDeviation:F1} W\n{adaptive}{range}";
    }

    private static string FormatNetworkActivity(
        MachineNetworkActivityClass activityClass) => activityClass switch
        {
            MachineNetworkActivityClass.Quiet => "Quiet",
            MachineNetworkActivityClass.Light => "Light",
            MachineNetworkActivityClass.Active => "Active",
            _ => "Unavailable"
        };

    private static string FormatFreshness(
        MachineLearningFreshness freshness) => freshness switch
        {
            MachineLearningFreshness.Fresh => "Fresh",
            MachineLearningFreshness.Aging => "Aging",
            MachineLearningFreshness.Stale => "Stale",
            _ => "Unknown freshness"
        };

    private static string FormatSituationCategory(
        MachineSituationCategory category) => category switch
        {
            MachineSituationCategory.Now => "Now",
            MachineSituationCategory.Recently => "Recently",
            MachineSituationCategory.LearnedNormal => "Learned normal",
            MachineSituationCategory.Today => "Today",
            MachineSituationCategory.Forward => "Forward",
            MachineSituationCategory.ActionOutcome => "Actions",
            MachineSituationCategory.LearningConfidence => "Learning",
            MachineSituationCategory.SelfHealth => "Self-health",
            _ => "Other"
        };

    private static string FormatSituationImportance(
        MachineSituationImportance importance) => importance switch
        {
            MachineSituationImportance.Critical => "Critical",
            MachineSituationImportance.Important => "Important",
            MachineSituationImportance.Notable => "Notable",
            MachineSituationImportance.Context => "Context",
            _ => "Routine"
        };

    private void UpdateCurrentPowerProjection(
        MachineLearnedPowerCostProjection? projection)
    {
        if (projection is null)
        {
            LearningCurrentPowerContextText.Text =
                "Waiting for a learned context";
            LearningCurrentPowerTypicalText.Text =
                "No current context evidence";
            LearningCurrentPowerRangeText.Text = "Unavailable";
            LearningCurrentPowerEvidenceText.Text =
                "No eligible power evidence yet";
            LearningCurrentPowerCostText.Text = "Unavailable";
            LearningCurrentPowerCostRangeText.Text =
                "Projected range unavailable";
            LearningCurrentPowerRateText.Text =
                "Published residential reference rate unavailable";
            return;
        }

        LearningCurrentPowerContextText.Text =
            $"{FormatLearningHour(projection.LocalHour)} · " +
            FormatActivity(projection.ActivityState);
        LearningCurrentPowerEvidenceText.Text =
            $"{FormatPowerMaturity(projection.PowerMaturity)} · " +
            $"{FormatSampleCount(projection.PowerEvidenceCount)} · " +
            $"{projection.ObservedPowerEvidenceDays:N0} observed " +
            (projection.ObservedPowerEvidenceDays == 1 ? "day" : "days");

        if (!projection.HasUsablePower ||
            projection.TypicalEstimatedWallPowerWatts is not { } watts ||
            projection.TypicalEstimatedWallPowerRange is not { } range)
        {
            LearningCurrentPowerTypicalText.Text = "Early power evidence";
            LearningCurrentPowerRangeText.Text =
                "Unavailable until enough power evidence";
            LearningCurrentPowerCostText.Text =
                "Unavailable until enough power evidence";
            LearningCurrentPowerCostRangeText.Text =
                "Matasuri does not project cost from insufficient power evidence.";
        }
        else
        {
            LearningCurrentPowerTypicalText.Text = $"~{watts:F0} W";
            LearningCurrentPowerRangeText.Text =
                $"{range.Low:F0}–{range.High:F0} W";
            LearningCurrentPowerCostText.Text =
                projection.ProjectedCostPerObservedHour is { } cost &&
                projection.Rate is { } costRate
                    ? $"~{FormatCurrency(costRate.CurrencyCode)}{cost:F2} / observed hour"
                    : "Cost unavailable";
            LearningCurrentPowerCostRangeText.Text =
                projection.ProjectedLowerCostPerObservedHour is { } lowCost &&
                projection.ProjectedUpperCostPerObservedHour is { } highCost &&
                projection.Rate is { } rangeRate
                    ? $"Learned range · ~{FormatCurrency(rangeRate.CurrencyCode)}{lowCost:F2}–" +
                        $"{FormatCurrency(rangeRate.CurrencyCode)}{highCost:F2} / observed hour"
                    : "Learned watts remain available without a matching rate.";
        }

        LearningCurrentPowerRateText.Text = projection.Rate is { } rate
            ? $"Published residential reference · {rate.ProviderName} · " +
                $"{FormatCurrency(rate.CurrencyCode)}{rate.RatePerKWh:F4}/kWh · " +
                rate.EffectiveMonth.ToString(
                    "MMMM yyyy",
                    CultureInfo.CurrentCulture)
            : "Published residential reference rate unavailable";
    }

    private void UpdateTodayLearnedEnergyComparison(
        MachineTodayLearnedEnergyComparison comparison)
    {
        LearningTodayComparisonStatusText.Text = comparison.ComparisonState switch
        {
            MachineTodayLearnedEnergyComparisonState.WithinLearnedRange =>
                "Within learned range",
            MachineTodayLearnedEnergyComparisonState.AboveLearnedRange =>
                "Above learned range",
            MachineTodayLearnedEnergyComparisonState.BelowLearnedRange =>
                "Below learned range",
            MachineTodayLearnedEnergyComparisonState.StillLearning =>
                "Learned-normal comparison unavailable",
            _ => "Today comparison unavailable"
        };
        LearningTodayComparisonDetailText.Text =
            FormatTodayComparisonDetail(comparison);
        LearningTodayObservedEnergyText.Text =
            comparison.ActualObservedEnergyKilowattHours > 0d
                ? $"{comparison.ActualObservedEnergyKilowattHours:F3} kWh"
                : "Beginning now";
        LearningTodayObservedDurationText.Text =
            FormatProjectionDuration(comparison.ObservedDuration);
        LearningTodayCoverageText.Text =
            $"{FormatProjectionDuration(comparison.LearnedCoveredDuration)} " +
            $"of {FormatProjectionDuration(comparison.ObservedDuration)} · " +
            $"{comparison.LearnedCoverage:P1}";
        LearningTodayActualCostText.Text =
            comparison.ActualEstimatedCost is { } actualCost &&
            comparison.Rate is { } actualRate
                ? $"~{FormatCurrency(actualRate.CurrencyCode)}{actualCost:F2}"
                : "Cost unavailable";

        if (comparison.ExpectedObservedEnergyKilowattHours is { } expected &&
            comparison.ExpectedLowerEnergyKilowattHours is { } lower &&
            comparison.ExpectedUpperEnergyKilowattHours is { } upper)
        {
            LearningTodayExpectedEnergyText.Text =
                $"{expected:F3} kWh\n{lower:F3}–{upper:F3} kWh range";
            LearningTodayExpectedCostText.Text =
                comparison.ExpectedEstimatedCost is { } expectedCost &&
                comparison.ExpectedLowerCost is { } lowerCost &&
                comparison.ExpectedUpperCost is { } upperCost &&
                comparison.Rate is { } expectedRate
                    ? $"~{FormatCurrency(expectedRate.CurrencyCode)}{expectedCost:F2}\n" +
                        $"{FormatCurrency(expectedRate.CurrencyCode)}{lowerCost:F2}–" +
                        $"{FormatCurrency(expectedRate.CurrencyCode)}{upperCost:F2} range"
                    : "Published rate unavailable";
        }
        else
        {
            LearningTodayExpectedEnergyText.Text =
                "Unavailable until coverage is complete";
            LearningTodayExpectedCostText.Text =
                "Unavailable until coverage is complete";
        }
    }

    private void UpdateUsageForecast(
        MachineLearnedUsageSnapshot learnedUsage,
        MachineUsageForecast forecast,
        OverviewView overview)
    {
        var usage = forecast.CurrentHourUsage;
        if (usage is null || !usage.HasUsableEvidence)
        {
            LearningCurrentHourUsageText.Text =
                $"{FormatLearningHour(forecast.CapturedAt.ToLocalTime().Hour)} · " +
                "Still gathering repeated activity evidence";
            LearningCurrentHourUsageEvidenceText.Text =
                learnedUsage.HistoricalDayCount > 0
                    ? $"{learnedUsage.HistoricalDayCount:N0}-day History window · " +
                        "two observed days are needed for an early usage profile"
                    : "No completed historical day is available yet";
        }
        else
        {
            LearningCurrentHourUsageText.Text =
                $"{FormatLearningHour(usage.LocalHour)} · " +
                $"Active {usage.ActiveFraction:P0} · " +
                $"Idle {usage.IdleFraction:P0}";
            LearningCurrentHourUsageEvidenceText.Text =
                $"{FormatUsageMaturity(usage.Maturity)} · " +
                $"{usage.ObservedDayCount:N0} observed " +
                (usage.ObservedDayCount == 1 ? "day" : "days") +
                $" in a {usage.HistoricalDayCount:N0}-day window · " +
                $"{FormatProjectionDuration(usage.TypicalObservedDuration)} " +
                "typical observed time";
        }

        if (forecast.HasNextObservedHourForecast)
        {
            LearningNextHourEnergyText.Text =
                $"~{forecast.NextObservedHourEnergyKilowattHours!.Value:F3} kWh";
            LearningNextHourEnergyRangeText.Text =
                forecast.NextObservedHourEnergyLowerKilowattHours is { } low &&
                forecast.NextObservedHourEnergyUpperKilowattHours is { } high
                    ? $"{low:F3}–{high:F3} kWh · " +
                        FormatForecastMaturity(forecast.CurrentPowerMaturity)
                    : FormatForecastMaturity(forecast.CurrentPowerMaturity);
            LearningNextHourCostText.Text =
                forecast.NextObservedHourEstimatedCost is { } cost &&
                forecast.RateReference is { } rate
                    ? $"~{FormatCurrency(rate.CurrencyCode)}{cost:F2}"
                    : "Rate unavailable";
            LearningNextHourCostRangeText.Text =
                forecast.NextObservedHourEstimatedCostLower is { } lowCost &&
                forecast.NextObservedHourEstimatedCostUpper is { } highCost &&
                forecast.RateReference is { } rangeRate
                    ? $"{FormatCurrency(rangeRate.CurrencyCode)}{lowCost:F2}–" +
                        $"{FormatCurrency(rangeRate.CurrencyCode)}{highCost:F2}"
                    : "Estimated cost range unavailable";
        }
        else
        {
            LearningNextHourEnergyText.Text = "Unavailable";
            LearningNextHourEnergyRangeText.Text =
                "Current power evidence is insufficient for a forecast";
            LearningNextHourCostText.Text = "Unavailable";
            LearningNextHourCostRangeText.Text =
                "No monetary projection without learned energy";
        }

        if (forecast.HasEndOfDayForecast)
        {
            LearningEndOfDayEnergyText.Text =
                $"~{forecast.ProjectedEndOfDayObservedEnergyKilowattHours!.Value:F3} kWh";
            LearningEndOfDayEnergyRangeText.Text =
                forecast.ProjectedEndOfDayLowerKilowattHours is { } low &&
                forecast.ProjectedEndOfDayUpperKilowattHours is { } high
                    ? $"{low:F3}–{high:F3} kWh"
                    : "Projected range unavailable";
            LearningEndOfDayCostText.Text =
                forecast.ProjectedEndOfDayEstimatedCost is { } cost &&
                forecast.RateReference is { } rate
                    ? $"~{FormatCurrency(rate.CurrencyCode)}{cost:F2}"
                    : "Rate unavailable";
            LearningEndOfDayCostRangeText.Text =
                forecast.ProjectedEndOfDayCostLower is { } lowCost &&
                forecast.ProjectedEndOfDayCostUpper is { } highCost &&
                forecast.RateReference is { } rangeRate
                    ? $"{FormatCurrency(rangeRate.CurrencyCode)}{lowCost:F2}–" +
                        $"{FormatCurrency(rangeRate.CurrencyCode)}{highCost:F2}"
                    : "Estimated cost range unavailable";
        }
        else
        {
            LearningEndOfDayEnergyText.Text = "Unavailable";
            LearningEndOfDayEnergyRangeText.Text =
                "No trustworthy remaining-day energy range";
            LearningEndOfDayCostText.Text = "Unavailable";
            LearningEndOfDayCostRangeText.Text =
                "No monetary projection without learned energy";
        }

        LearningForecastEvidenceText.Text =
            FormatForecastEvidence(forecast);
        overview.OverviewNextHourEnergyText.Text =
            forecast.HasNextObservedHourForecast
                ? $"~{forecast.NextObservedHourEnergyKilowattHours!.Value:F3} kWh"
                : "No authoritative comparison";
        overview.OverviewNextHourCostText.Text =
            forecast.NextObservedHourEstimatedCost is { } nextCost &&
            forecast.RateReference is { } nextRate
                ? $"~{FormatCurrency(nextRate.CurrencyCode)}{nextCost:F2} estimated"
                : forecast.HasNextObservedHourForecast
                    ? "Published rate unavailable"
                    : "Current power evidence unavailable";
        overview.OverviewEndOfDayForecastText.Text =
            forecast.HasEndOfDayForecast
                ? $"End of day · ~{forecast.ProjectedEndOfDayObservedEnergyKilowattHours!.Value:F3} kWh" +
                    (forecast.ProjectedEndOfDayEstimatedCost is { } endCost &&
                        forecast.RateReference is { } endRate
                            ? $" · ~{FormatCurrency(endRate.CurrencyCode)}{endCost:F2}"
                            : string.Empty)
                : "End-of-day projection unavailable";
        overview.OverviewForecastEvidenceText.Text =
            FormatForecastEvidence(forecast);
    }

    private static string FormatUsageMaturity(
        MachineLearningEvidenceMaturity maturity) => maturity switch
        {
            MachineLearningEvidenceMaturity.Established =>
                "Established usage behavior",
            MachineLearningEvidenceMaturity.Provisional =>
                "Early usage behavior",
            _ => "Still gathering usage evidence"
        };

    private static string FormatForecastMaturity(
        MachineLearningEvidenceMaturity maturity) => maturity switch
        {
            MachineLearningEvidenceMaturity.Established =>
                "Established learned forecast",
            MachineLearningEvidenceMaturity.Provisional =>
                "Early projection",
            _ => "Insufficient repeated evidence"
        };

    private static string FormatForecastEvidence(
        MachineUsageForecast forecast)
    {
        var coverage = $"{forecast.ForecastCoverage:P0} future-hour coverage";
        return forecast.AvailabilityReason switch
        {
            MachineUsageForecastAvailabilityReason.Available =>
                $"{FormatForecastMaturity(forecast.ForecastMaturity)} · " +
                    $"{coverage} · {FormatProjectionDuration(forecast.RemainingDayExpectedObservedDuration)} " +
                    "expected observed time. Based on previously observed " +
                    "remaining-day behavior.",
            MachineUsageForecastAvailabilityReason.PartialFutureCoverage =>
                $"Partial early projection · {coverage} · " +
                    $"{FormatProjectionDuration(forecast.RemainingDayExpectedObservedDuration)} " +
                    "expected observed time. Missing hours are not extrapolated.",
            MachineUsageForecastAvailabilityReason.MissingFuturePowerEvidence =>
                "End-of-day projection unavailable: learned activity exists, " +
                    "but matching current power evidence is missing.",
            _ =>
                $"End-of-day projection unavailable: fewer than " +
                    $"{MachineLearnedUsageProjector.ProvisionalObservedDayCount:N0} " +
                    "observed days of future-hour activity evidence."
        };
    }

    private static string FormatTodayComparisonDetail(
        MachineTodayLearnedEnergyComparison comparison)
    {
        if (comparison.ComparisonState ==
            MachineTodayLearnedEnergyComparisonState.Unavailable)
        {
            return "Waiting for accepted Today energy and duration evidence.";
        }
        if (comparison.ComparisonState ==
            MachineTodayLearnedEnergyComparisonState.StillLearning)
        {
            return "Power behavior is not yet available for every observed " +
                "context. No above/below comparison was made.";
        }

        var maturity = comparison.ComparisonMaturity ==
                MachineLearningEvidenceMaturity.Established
            ? "Established learned comparison"
            : "Early learned estimate · Provisional power evidence";
        return comparison.DifferenceKilowattHours is { } difference &&
            comparison.DifferencePercent is { } differencePercent
                ? $"{maturity} · Difference {difference:+0.000;-0.000;0.000} kWh " +
                    $"({differencePercent:+0.0;-0.0;0.0}%)."
                : maturity + ".";
    }

    private static string FormatPowerMaturity(
        MachineLearningEvidenceMaturity maturity) => maturity switch
        {
            MachineLearningEvidenceMaturity.Established => "Established",
            MachineLearningEvidenceMaturity.Provisional =>
                "Early estimate · Provisional",
            _ => "Early evidence · insufficient for projection"
        };

    private static string FormatContextMaturity(
        MachineLearningConfidence confidence) => confidence switch
        {
            MachineLearningConfidence.Established => "Established",
            MachineLearningConfidence.Provisional => "Provisional",
            _ => "Early evidence"
        };

    private static string FormatCurrentContextMaturity(
        MachineLearningBaseline? baseline)
    {
        if (baseline is null)
        {
            return "Evidence appears here with the first accepted sample. " +
                "Maturity controls authority, not visibility.";
        }

        if (baseline.Confidence == MachineLearningConfidence.Established)
        {
            return $"Established from {baseline.SampleCount:N0} samples across " +
                $"{baseline.ObservedDayCount:N0} distinct observed days. " +
                "Freshness is tracked separately.";
        }

        if (baseline.Confidence == MachineLearningConfidence.Calibrating)
        {
            return $"Early evidence · {baseline.SampleCount:N0} accepted " +
                (baseline.SampleCount == 1 ? "sample" : "samples") +
                $" across {baseline.ObservedDayCount:N0} observed " +
                (baseline.ObservedDayCount == 1 ? "day" : "days") +
                ". Provisional and Established authority require repeated evidence.";
        }

        return $"Provisional evidence · {baseline.SampleCount:N0} accepted " +
            $"samples across {baseline.ObservedDayCount:N0} observed days. " +
            "Established authority requires both sustained samples and " +
            "distinct-day coverage.";
    }

    private static string FormatPatternReadinessHeadline(
        MachineLearningPatternReadiness readiness) =>
        readiness.PatternsProduced > 0
            ? $"{readiness.PatternsProduced:N0} broader " +
                (readiness.PatternsProduced == 1 ? "pattern" : "patterns") +
                " recognized"
            : "No recurring pattern established";

    private static string FormatPatternReadinessCompact(
        MachineLearningPatternReadiness readiness) =>
        readiness.PrimaryBlocker switch
        {
            MachineLearningPatternReadinessBlocker.None =>
                $"{readiness.PatternsProduced:N0} broader " +
                    (readiness.PatternsProduced == 1 ? "pattern" : "patterns"),
            MachineLearningPatternReadinessBlocker.InsufficientDistinctDays =>
                $"patterns need {MachineLearningService.EstablishedObservedDayCount:N0} distinct days",
            MachineLearningPatternReadinessBlocker.InsufficientSamples =>
                $"patterns need {MachineLearningService.EstablishedSampleCount:N0} samples per context",
            MachineLearningPatternReadinessBlocker.NoAdjacentContexts =>
                "patterns need adjacent same-activity contexts",
            MachineLearningPatternReadinessBlocker.StaleEvidence =>
                "pattern evidence is stale",
            _ => "pattern readiness is still building"
        };

    private static string FormatPatternReadinessReason(
        MachineLearningPatternReadiness readiness) =>
        readiness.PrimaryBlocker switch
        {
            MachineLearningPatternReadinessBlocker.None =>
                $"{readiness.PatternsProduced:N0} pattern " +
                    (readiness.PatternsProduced == 1 ? "is" : "are") +
                    " recognized from current compatible evidence.",
            MachineLearningPatternReadinessBlocker.InsufficientProfiles =>
                "At least two learned contexts are needed before adjacent " +
                    "behavior can be compared.",
            MachineLearningPatternReadinessBlocker.NoAdjacentContexts =>
                "Learned contexts exist, but no same-activity neighboring " +
                    "hours are available yet.",
            MachineLearningPatternReadinessBlocker.InsufficientSamples =>
                $"No adjacent pair has reached {MachineLearningService.EstablishedSampleCount:N0} " +
                    "samples in both contexts yet.",
            MachineLearningPatternReadinessBlocker.InsufficientDistinctDays =>
                $"Adjacent contexts have enough samples, but no pair spans " +
                    $"{MachineLearningService.EstablishedObservedDayCount:N0} distinct observed days yet.",
            MachineLearningPatternReadinessBlocker.NoEstablishedAdjacentContexts =>
                "No adjacent pair has become Established in both contexts yet.",
            MachineLearningPatternReadinessBlocker.StaleEvidence =>
                "Established adjacent contexts exist, but their evidence is stale.",
            MachineLearningPatternReadinessBlocker.MissingTypicalRanges =>
                "Current established pairs are still waiting for comparable " +
                    "CPU and memory ranges.",
            MachineLearningPatternReadinessBlocker.IncompatibleCpuBehavior =>
                "Current adjacent contexts differ too much in CPU behavior " +
                    "to form one broader pattern.",
            MachineLearningPatternReadinessBlocker.IncompatibleMemoryBehavior =>
                "Current adjacent contexts differ too much in memory behavior " +
                    "to form one broader pattern.",
            MachineLearningPatternReadinessBlocker.IncompatibleNetworkBehavior =>
                "Current adjacent contexts have incompatible dominant network behavior.",
            MachineLearningPatternReadinessBlocker.FullDayRunExcluded =>
                "A full-day run is intentionally excluded because it is not " +
                    "a bounded recurring window.",
            _ => "Pattern readiness is still being evaluated from verified evidence."
        };

    private static string FormatActivity(
        MachineUserActivityState activityState) => activityState switch
        {
            MachineUserActivityState.Active => "Active",
            MachineUserActivityState.Idle => "Idle",
            _ => "Unknown activity"
        };

    private static string FormatCurrency(string currencyCode) =>
        string.Equals(currencyCode, "PHP", StringComparison.OrdinalIgnoreCase)
            ? "₱"
            : $"{currencyCode} ";

    private static string FormatProjectionDuration(TimeSpan duration)
    {
        var bounded = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        if (bounded.TotalHours >= 1d)
        {
            return $"{(int)bounded.TotalHours}h {bounded.Minutes}m";
        }
        if (bounded.TotalMinutes >= 1d)
        {
            return $"{bounded.Minutes}m";
        }
        return bounded > TimeSpan.Zero ? "<1m" : "0m";
    }

    private static string FormatLearningActivityStatus(
        MachineLearningActivityStatus status) => status switch
        {
            MachineLearningActivityStatus.Active => "Active",
            MachineLearningActivityStatus.Waiting => "Waiting for a verified observation",
            MachineLearningActivityStatus.PersistenceDelayed => "Persistence delayed",
            MachineLearningActivityStatus.Unavailable => "Unavailable",
            _ => "Starting"
        };

    private static string FormatActivityKind(MachineLearningActivityKind kind) =>
        string.Concat(kind.ToString().Select((character, index) =>
            index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));

    private static string FormatActivityHeader(
        MachineLearningActivityEvent item)
    {
        var context = item.ContextChange;
        return context is null
            ? $"{item.OccurredAt.ToLocalTime():MMM d HH:mm:ss} · " +
                FormatActivityKind(item.Kind)
            : $"{item.OccurredAt.ToLocalTime():MMM d HH:mm:ss} · " +
                $"{FormatLearningHour(context.LocalHour)} · " +
                $"{FormatActivity(context.ActivityState)} · " +
                FormatActivityKind(item.Kind);
    }

    private static string FormatActivityDetail(MachineLearningActivityEvent item)
    {
        var details = new List<string>();
        if (item.ContextChange is { } change)
        {
            details.Add($"Samples {change.PreviousSampleCount:N0} → " +
                $"{change.SampleCount:N0}");
            if (change.PreviousObservedDayCount != change.ObservedDayCount)
            {
                details.Add($"Days {change.PreviousObservedDayCount:N0} → " +
                    $"{change.ObservedDayCount:N0}");
            }
            if (change.PreviousMaturity != change.Maturity)
            {
                details.Add($"Maturity " +
                    $"{FormatOptionalMaturity(change.PreviousMaturity)} → " +
                    FormatContextMaturity(change.Maturity));
            }
            var previousCpu = change.PreviousAdaptiveCpuMean;
            if (previousCpu is null ||
                Math.Abs(previousCpu.Value - change.AdaptiveCpuMean) >= 0.05d)
            {
                details.Add(previousCpu is null
                    ? $"CPU adaptive mean {change.AdaptiveCpuMean:F1}%"
                    : $"CPU adaptive mean {previousCpu.Value:F1}% → " +
                        $"{change.AdaptiveCpuMean:F1}%");
            }
            var previousMemory = change.PreviousAdaptiveMemoryMean;
            if (previousMemory is null ||
                Math.Abs(previousMemory.Value -
                    change.AdaptiveMemoryMean) >= 0.05d)
            {
                details.Add(previousMemory is null
                    ? $"Memory adaptive mean " +
                        $"{change.AdaptiveMemoryMean:F1}%"
                    : $"Memory adaptive mean {previousMemory.Value:F1}% → " +
                        $"{change.AdaptiveMemoryMean:F1}%");
            }
            if (change.PreviousPowerEvidenceCount !=
                change.PowerEvidenceCount)
            {
                details.Add($"Power samples " +
                    $"{change.PreviousPowerEvidenceCount:N0} → " +
                    $"{change.PowerEvidenceCount:N0}");
            }
            var previousPower = change.PreviousPowerMeanWatts;
            if (change.PowerMeanWatts is { } powerMean &&
                (previousPower is null ||
                    Math.Abs(previousPower.Value - powerMean) >= 0.05d))
            {
                details.Add(previousPower is null
                    ? $"Power mean {powerMean:F1} W"
                    : $"Power mean {previousPower.Value:F1} → " +
                        $"{powerMean:F1} W");
            }
            if (change.PreviousPowerMaturity != change.PowerMaturity)
            {
                details.Add($"Power maturity " +
                    $"{FormatOptionalPowerMaturity(
                        change.PreviousPowerMaturity)} → " +
                    FormatPowerMaturity(change.PowerMaturity));
            }
        }
        if (item.ObservationCount is not null)
        {
            details.Add($"{item.ObservationCount:N0} lifetime observations");
        }
        if (item.ProfileCount is not null)
        {
            details.Add($"{item.ProfileCount:N0} profiles");
        }
        if (item.EpisodeCount is not null)
        {
            details.Add($"{item.EpisodeCount:N0} episodes");
        }
        if (item.Count > 1)
        {
            details.Add($"{item.Count:N0} coalesced");
        }
        if (!string.IsNullOrWhiteSpace(item.Detail))
        {
            details.Add(item.Detail);
        }
        if (item.ByteCount is not null)
        {
            details.Add($"{item.ByteCount:N0} bytes");
        }
        if (item.DurationMilliseconds is not null)
        {
            details.Add($"{item.DurationMilliseconds:N0} ms");
        }
        if (item.PowerEvidenceAccepted is { } accepted)
        {
            details.Add(accepted
                ? "power evidence accepted"
                : "power evidence unavailable");
        }
        if (item.PowerEvidenceCount is { } powerEvidenceCount)
        {
            details.Add($"{powerEvidenceCount:N0} power samples");
        }
        return details.Count == 0 ? "Lifecycle event" : string.Join(" · ", details);
    }

    private static string FormatOptionalMaturity(
        MachineLearningConfidence? maturity) => maturity is null
            ? "none"
            : FormatContextMaturity(maturity.Value);

    private static string FormatOptionalPowerMaturity(
        MachineLearningEvidenceMaturity? maturity) => maturity is null
            ? "none"
            : FormatPowerMaturity(maturity.Value);

    private static LearningProfileDisplayItem
        CreateLearningProfileDisplayItem(
            MachineLearningBaseline context,
            bool isCurrent)
    {
        var first = context.FirstObservedAt.ToLocalTime();
        var last = context.LastObservedAt.ToLocalTime();
        var observedSpan = first.Date == last.Date
            ? $"Observed {first:MMM d, yyyy}"
            : $"Observed {first:MMM d, yyyy} to {last:MMM d, yyyy}";

        return new LearningProfileDisplayItem(
            (isCurrent ? "NOW · " : string.Empty) +
                $"{FormatLearningHour(context.LocalHour)} · " +
                FormatActivity(context.ActivityState),
            $"{FormatContextMaturity(context.Confidence)} · " +
                FormatFreshness(context.Freshness),
            FormatLearnedMetric(
                context.CpuMean,
                context.CpuStandardDeviation,
                context.AdaptiveCpuMean,
                context.CpuTypicalRange,
                "%"),
            FormatLearnedMetric(
                context.MemoryMean,
                context.MemoryStandardDeviation,
                context.AdaptiveMemoryMean,
                context.MemoryTypicalRange,
                "%"),
            FormatLearnedNetwork(context),
            FormatLearnedPower(context),
            $"Evidence · {FormatSampleCount(context.SampleCount)} · " +
                $"{context.ObservedDayCount:N0} observed " +
                (context.ObservedDayCount == 1 ? "day" : "days") +
                $"\n{observedSpan} · Updated " +
                $"{FormatLearningDateTime(context.LastObservedAt, "Unknown")}",
            context.Freshness == MachineLearningFreshness.Stale ? 0.64 : 1d);
    }

    private static LearningPatternDisplayItem
        CreateLearningPatternDisplayItem(
            MachineLearningRecurringPattern pattern)
    {
        var network = pattern.DominantNetworkActivityClass is { } dominant
            ? $"Network mostly {FormatNetworkActivity(dominant)}"
            : "Network evidence is incomplete across this window";
        return new LearningPatternDisplayItem(
            $"{FormatLearningHour(pattern.StartHour)}–" +
                $"{FormatLearningHour(pattern.EndHourExclusive)} · " +
                FormatActivity(pattern.ActivityState),
            $"{FormatContextMaturity(pattern.Confidence)} pattern · " +
                FormatFreshness(pattern.Freshness) +
                (pattern.CrossesMidnight ? " · crosses midnight" : string.Empty),
            FormatLearningRange("Typical", pattern.CpuTypicalRange, null),
            FormatLearningRange("Typical", pattern.MemoryTypicalRange, null),
            network,
            $"Built from {pattern.MemberContexts.Count:N0} established hourly " +
                (pattern.MemberContexts.Count == 1 ? "profile" : "profiles") +
                $" · {pattern.CombinedSampleCount:N0} observations · " +
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
                : $"Observed adaptive mean {adaptiveMean.Value:F1}%\nRange not yet available"
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

    internal void UpdateRuntimeStatus(
        LocalInferenceStatus? snapshot)
    {
        if (snapshot is null)
        {
            LearningAiRuntimeText.Text = "Status unavailable";
            LearningAiModelText.Text = "Loaded-model status unavailable";
            LearningAiKnowledgeRuntimeText.Text = "Status unavailable";
            LearningAiKnowledgeModelText.Text = "Status unavailable";
            return;
        }

        var runtimeState = snapshot.IsRuntimeAvailable
            ? snapshot.ModelState switch
            {
                LocalInferenceModelState.Asleep => "Asleep",
                LocalInferenceModelState.Loading => "Loading Qwen",
                LocalInferenceModelState.Ready => "Ready",
                LocalInferenceModelState.Generating => "Generating",
                LocalInferenceModelState.Faulted => "Faulted",
                _ => "Status unavailable"
            }
            : "Faulted";
        LearningAiRuntimeText.Text = runtimeState;
        LearningAiModelText.Text = !snapshot.IsRuntimeAvailable
                ? "Loaded-model status unavailable"
                : snapshot.LoadedModels.Count == 0
                    ? "Qwen unloaded"
                    : snapshot.LoadedModels.Count == 1
                        ? $"{snapshot.LoadedModels[0].Name} loaded"
                        : $"{snapshot.LoadedModels.Count:N0} models loaded";
        var runtimeDetails = new List<string>
        {
            $"{snapshot.RuntimeName} " +
            $"{snapshot.RuntimeVersion ?? "version unavailable"}",
            snapshot.Backend ?? "backend unavailable",
            runtimeState,
            snapshot.IsProcessOwned ? "Job-owned child" : "no child loaded"
        };
        if (!string.IsNullOrWhiteSpace(snapshot.RuntimeSha))
        {
            runtimeDetails.Add(
                $"runtime SHA {AbbreviateHash(snapshot.RuntimeSha)}");
        }
        if (snapshot.GpuLayerCount is { } gpuLayers)
        {
            runtimeDetails.Add($"{gpuLayers:N0} configured GPU layers");
        }
        if (snapshot.ProcessId is { } processId)
        {
            runtimeDetails.Add($"PID {processId:N0}");
        }
        if (snapshot.LastLoadDuration is { } loadDuration)
        {
            runtimeDetails.Add($"last load {loadDuration.TotalSeconds:F1}s");
        }
        if (snapshot.LastGenerationDuration is { } generationDuration)
        {
            runtimeDetails.Add(
                $"last generation {generationDuration.TotalSeconds:F1}s");
        }
        if (snapshot.ResidencyRemaining is { } residency)
        {
            runtimeDetails.Add($"residency {FormatDuration(residency)} remaining");
        }
        LearningAiKnowledgeRuntimeText.Text = string.Join(" · ",
            runtimeDetails);
        var configuredModel = snapshot.ConfiguredModelName ??
            snapshot.LoadedModels.FirstOrDefault()?.Name ??
            "Model identity unavailable";
        var quantization = snapshot.ConfiguredQuantization ??
            snapshot.LoadedModels.FirstOrDefault()?.Quantization ??
            "quantization unavailable";
        var contextLength = snapshot.ContextLength ??
            snapshot.LoadedModels.FirstOrDefault()?.ContextLength;
        var modelDetails = new List<string>
        {
            configuredModel,
            quantization
        };
        if (contextLength is { } context)
        {
            modelDetails.Add($"{context:N0}-token context");
        }
        if (snapshot.ConfiguredModelSizeBytes is { } size)
        {
            modelDetails.Add($"{size / (1024d * 1024d * 1024d):F2} GiB");
        }
        if (!string.IsNullOrWhiteSpace(snapshot.ModelSha256))
        {
            modelDetails.Add($"SHA-256 {AbbreviateHash(snapshot.ModelSha256)}");
        }
        var residentBytes = snapshot.LoadedModels.Sum(model =>
            Math.Max(0L, model.ResidentBytes));
        if (residentBytes > 0)
        {
            modelDetails.Add(
                $"{residentBytes / (1024d * 1024d * 1024d):F2} GiB GPU model buffer");
        }
        else if (snapshot.LoadedModels.Count == 0)
        {
            modelDetails.Add("GPU model buffer not resident");
        }
        else
        {
            modelDetails.Add("GPU model buffer measurement pending");
        }
        modelDetails.Add(runtimeState);
        LearningAiKnowledgeModelText.Text = string.Join(" · ", modelDetails);
    }

    internal void UpdateBriefInspection(MachineBrief? brief)
    {
        if (brief is null)
        {
            LearningAiKnowledgeValidationText.Text =
                "No local Brief generation validated in this session";
            LearningAiKnowledgeGenerationText.Text = "No generation yet";
            return;
        }

        LearningAiKnowledgeValidationText.Text =
            brief.Diagnostics.ValidationState switch
            {
                MachineBriefValidationState.Valid =>
                    "Last generation · Valid",
                MachineBriefValidationState.Repaired =>
                    "Last generation · Valid after one bounded repair",
                _ => "Last generation · " +
                    brief.Diagnostics.ValidationReason
            };
        if (!string.IsNullOrWhiteSpace(brief.SituationFingerprint))
        {
            var context = LearningAiKnowledgeContextText.Text;
            var fingerprintMarker = context.IndexOf(
                " · fingerprint", StringComparison.Ordinal);
            if (fingerprintMarker >= 0)
            {
                context = context[..fingerprintMarker];
            }
            LearningAiKnowledgeContextText.Text =
                $"{context} · fingerprint {brief.SituationFingerprint}";
        }
        var generation = new List<string>
        {
            brief.Source == MachineExplanationSource.LocalModel
                ? "Local Qwen"
                : "Deterministic fallback",
            $"{brief.Diagnostics.RequestCount:N0} " +
                (brief.Diagnostics.RequestCount == 1 ? "request" : "requests"),
            $"~{brief.Diagnostics.EstimatedInputTokenCount:N0} estimated input tokens"
        };
        if (brief.Diagnostics.PromptTokenCount is { } promptTokens)
        {
            generation.Add($"{promptTokens:N0} runtime prompt tokens");
        }
        if (brief.Diagnostics.OutputTokenCount is { } outputTokens)
        {
            generation.Add($"{outputTokens:N0} output tokens");
        }
        if (brief.Diagnostics.LoadDuration is { } load)
        {
            generation.Add($"load {load.TotalSeconds:F1}s");
        }
        if (brief.Diagnostics.GenerationDuration is { } elapsed)
        {
            generation.Add($"generation {elapsed.TotalSeconds:F1}s");
        }
        if (brief.Diagnostics.RepairAttempted)
        {
            generation.Add("one bounded repair attempted");
        }
        LearningAiKnowledgeGenerationText.Text = string.Join(" · ",
            generation);
    }

    private static string AbbreviateHash(string value) =>
        value.Length <= 12 ? value : value[..12];

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1d
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{Math.Max(0, duration.Minutes)}m";
}
