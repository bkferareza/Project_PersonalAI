using Machine.App.Features;
using Machine.Core;

namespace Machine.App;

public sealed partial class MainWindow
{
    private void UpdateNetworkTelemetry(MachineNetworkSnapshot? snapshot) =>
        NetworkPage.UpdateNetwork(
            snapshot,
            _latestNetworkSnapshot is not null,
            OverviewPage);

    private void UpdateSessionTelemetry(MachineSessionSnapshot? snapshot) =>
        NetworkPage.UpdateSession(
            snapshot,
            _latestSessionSnapshot is not null,
            OverviewPage);
}
