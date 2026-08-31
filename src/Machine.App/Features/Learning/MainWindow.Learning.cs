using Machine.App.Features;
using Machine.Core;

namespace Machine.App;

public sealed partial class MainWindow
{
    private void UpdateLearningDashboard()
    {
        var now = DateTimeOffset.UtcNow;
        var learning = _learningService.GetDashboardSnapshot(now);
        var history = _historyService.GetSnapshot(
            MachineHistoryRange.Last30Days,
            now);
        // The comparison deliberately omits the sub-cadence pending energy.
        // Accepted History energy and duration therefore cover the same
        // persisted/restart-safe observation intervals exactly once.
        var acceptedToday = MachineTodayEnergyCostProjector.Project(
            history.Rollups,
            _cachedElectricityRates,
            now);
        var currentPower = MachineLearnedPowerCostProjector.Project(
            learning.CurrentBaseline,
            acceptedToday.Rate);
        var todayComparison = CreateTodayLearnedEnergyComparison(
            now,
            history,
            learning,
            acceptedToday);
        var learnedUsage = MachineLearnedUsageProjector.Project(
            history.Rollups,
            now);
        var forecast = MachineUsageForecastProjector.Project(
            now,
            learning.CurrentBaseline,
            learning.ContextProfiles,
            learnedUsage,
            currentPower,
            todayComparison);
        _latestUsageForecast = forecast;

        LearningPage.Update(
            learning,
            _learningService.ActivityLog.GetSnapshot(learning, now),
            currentPower,
            todayComparison,
            learnedUsage,
            forecast,
            _healthHistoryService.GetSnapshot(),
            _latestInferenceStatus,
            OverviewPage);

        if (_detailsExpanded &&
            OverviewPage.Visibility == Microsoft.UI.Xaml.Visibility.Visible)
        {
            _ = EnsureUsageOutlookAsync(forceRefresh: false);
        }
    }

    private MachineTodayLearnedEnergyComparison
        CreateTodayLearnedEnergyComparison(DateTimeOffset now)
    {
        var history = _historyService.GetSnapshot(
            MachineHistoryRange.Last7Days,
            now);
        var learning = _learningService.GetDashboardSnapshot(now);
        var acceptedToday = MachineTodayEnergyCostProjector.Project(
            history.Rollups,
            _cachedElectricityRates,
            now);
        return CreateTodayLearnedEnergyComparison(
            now,
            history,
            learning,
            acceptedToday);
    }

    private static MachineTodayLearnedEnergyComparison
        CreateTodayLearnedEnergyComparison(
            DateTimeOffset now,
            MachineHistorySnapshot history,
            MachineLearningDashboardSnapshot learning,
            MachineTodayEnergyCostProjection acceptedToday) =>
        MachineTodayLearnedEnergyProjector.Project(
            history.Rollups,
            learning.ContextProfiles,
            acceptedToday,
            now);
}
