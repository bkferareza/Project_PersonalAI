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
        MachineLearnedPowerCostProjection? currentPower,
        MachineTodayLearnedEnergyComparison todayComparison,
        MachineLearnedUsageSnapshot learnedUsage,
        MachineUsageForecast forecast,
        MachineHealthHistorySnapshot healthHistory,
        OllamaStatusSnapshot? ollamaStatus,
        OverviewView overview)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(todayComparison);
        ArgumentNullException.ThrowIfNull(learnedUsage);
        ArgumentNullException.ThrowIfNull(forecast);
        ArgumentNullException.ThrowIfNull(healthHistory);
        ArgumentNullException.ThrowIfNull(overview);
        var current = snapshot.CurrentObservation;
        var baseline = snapshot.CurrentBaseline;
        var confidence = baseline?.Confidence ??
            MachineLearningConfidence.Calibrating;
        var readiness = snapshot.Readiness.PatternReadiness;
        var memoryState = FormatMemoryState(snapshot.Readiness.MemoryState);

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
            $"{readiness.TotalProfileCount:N0} learned " +
            (readiness.TotalProfileCount == 1 ? "profile" : "profiles") +
            $" · {readiness.EstablishedProfileCount:N0} established · " +
            $"{readiness.PatternsProduced:N0} recognized " +
            (readiness.PatternsProduced == 1 ? "pattern" : "patterns");

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
            : $"{current.Timestamp.ToLocalTime():h tt} · " +
                $"{FormatActivity(current.ActivityState)} · " +
                FormatContextMaturity(confidence);

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
        LearningActivityStatusText.Text = FormatLearningActivityStatus(
            activity.Status);
        LearningActivityEventsList.ItemsSource = activity.RecentEvents
            .Select(item => new LearningActivityDisplayItem(
                $"{item.OccurredAt.ToLocalTime():MMM d HH:mm:ss} · {FormatActivityKind(item.Kind)}",
                FormatActivityDetail(item)))
            .ToArray();
        UpdateRuntimeStatus(ollamaStatus);
    }

    private void UpdateCurrentPowerProjection(
        MachineLearnedPowerCostProjection? projection)
    {
        if (projection is null)
        {
            LearningCurrentPowerContextText.Text =
                "Waiting for a learned context";
            LearningCurrentPowerTypicalText.Text = "Still learning";
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
            LearningCurrentPowerTypicalText.Text = "Still learning";
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
                "Still learning today's normal",
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
                "Current power behavior is still learning";
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
                : "Still learning";
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
            _ => "Still learning"
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
            _ => "Still learning · Insufficient"
        };

    private static string FormatMemoryState(
        MachineLearningMemoryState state) => state switch
        {
            MachineLearningMemoryState.Active => "Active",
            MachineLearningMemoryState.PersistenceAtRisk =>
                "Persistence at risk",
            _ => "Calibrating"
        };

    private static string FormatContextMaturity(
        MachineLearningConfidence confidence) => confidence switch
        {
            MachineLearningConfidence.Established => "Established",
            MachineLearningConfidence.Provisional => "Provisional",
            _ => "Calibrating"
        };

    private static string FormatCurrentContextMaturity(
        MachineLearningBaseline? baseline)
    {
        if (baseline is null)
        {
            return "Provisional needs 12 samples. Established needs 168 " +
                "samples across 7 distinct observed days.";
        }

        if (baseline.Confidence == MachineLearningConfidence.Established)
        {
            return $"Established from {baseline.SampleCount:N0} samples across " +
                $"{baseline.ObservedDayCount:N0} distinct observed days. " +
                "Freshness is tracked separately.";
        }

        var samplesForEstablished = Math.Max(
            0,
            MachineLearningService.EstablishedSampleCount -
                baseline.SampleCount);
        var daysForEstablished = Math.Max(
            0,
            MachineLearningService.EstablishedObservedDayCount -
                baseline.ObservedDayCount);
        if (baseline.Confidence == MachineLearningConfidence.Calibrating)
        {
            var samplesForProvisional = Math.Max(
                0,
                MachineLearningService.ProvisionalSampleCount -
                    baseline.SampleCount);
            return $"{samplesForProvisional:N0} more " +
                (samplesForProvisional == 1 ? "sample" : "samples") +
                " for Provisional. " +
                FormatEstablishedEvidenceRemaining(
                    samplesForEstablished,
                    daysForEstablished);
        }

        return FormatEstablishedEvidenceRemaining(
            samplesForEstablished,
            daysForEstablished);
    }

    private static string FormatEstablishedEvidenceRemaining(
        long samplesRemaining,
        int daysRemaining)
    {
        if (samplesRemaining == 0 && daysRemaining == 0)
        {
            return "Established evidence thresholds are met; the next " +
                "accepted observation will refresh maturity.";
        }

        var requirements = new List<string>();
        if (samplesRemaining > 0)
        {
            requirements.Add($"{samplesRemaining:N0} more " +
                (samplesRemaining == 1 ? "sample" : "samples"));
        }
        if (daysRemaining > 0)
        {
            requirements.Add($"{daysRemaining:N0} more distinct observed " +
                (daysRemaining == 1 ? "day" : "days"));
        }
        return $"Established needs {string.Join(" and ", requirements)}. " +
            "Both thresholds must be met; freshness is tracked separately.";
    }

    private static string FormatPatternReadinessHeadline(
        MachineLearningPatternReadiness readiness) =>
        readiness.PatternsProduced > 0
            ? $"{readiness.PatternsProduced:N0} broader " +
                (readiness.PatternsProduced == 1 ? "pattern" : "patterns") +
                " recognized"
            : "Still learning broader patterns";

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

    private static string FormatActivityDetail(MachineLearningActivityEvent item)
    {
        var details = new List<string>();
        if (item.ObservationCount is not null)
        {
            details.Add($"{item.ObservationCount:N0} observations");
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

    internal void UpdateRuntimeStatus(
        OllamaStatusSnapshot? snapshot)
    {
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
}
