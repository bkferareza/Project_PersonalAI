using Machine.App.Features;

namespace Machine.App;

public sealed partial class MainWindow
{
    private void UpdateLearningDashboard() => LearningPage.Update(
        _learningService.GetDashboardSnapshot(DateTimeOffset.UtcNow),
        _healthHistoryService.GetSnapshot(),
        _latestOllamaStatusSnapshot,
        OverviewPage);
}
