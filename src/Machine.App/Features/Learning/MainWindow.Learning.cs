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
            MachineHistoryRange.Last7Days,
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
        var todayComparison = MachineTodayLearnedEnergyProjector.Project(
            history.Rollups,
            learning.ContextProfiles,
            acceptedToday,
            now);

        LearningPage.Update(
            learning,
            _learningService.ActivityLog.GetSnapshot(learning, now),
            currentPower,
            todayComparison,
            _healthHistoryService.GetSnapshot(),
            _latestOllamaStatusSnapshot,
            OverviewPage);
    }
}
