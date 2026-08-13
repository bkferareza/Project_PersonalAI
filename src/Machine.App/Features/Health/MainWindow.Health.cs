using Machine.App.Features;

namespace Machine.App;

public sealed partial class MainWindow
{
    private void UpdateHealthDashboard() => HealthPage.Update(
        _latestWindowsUpdateSnapshot,
        _latestRebootPendingSnapshot,
        _latestReliabilitySnapshot,
        OverviewPage);
}
