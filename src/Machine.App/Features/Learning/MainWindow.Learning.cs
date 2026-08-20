using Machine.App.Features;

namespace Machine.App;

public sealed partial class MainWindow
{
    private void UpdateLearningDashboard() => LearningPage.Update(
        _learningService.GetDashboardSnapshot(DateTimeOffset.UtcNow),
        _learningService.ActivityLog.GetSnapshot(
            _learningService.GetDashboardSnapshot(DateTimeOffset.UtcNow),
            DateTimeOffset.UtcNow),
        _healthHistoryService.GetSnapshot(),
        _latestOllamaStatusSnapshot,
        OverviewPage);
}
