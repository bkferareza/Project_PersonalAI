using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

public sealed partial class HealthView : UserControl
{
    public HealthView()
    {
        InitializeComponent();
    }
}

public sealed record UpdateHistoryDisplayItem(
    string Header,
    string Title,
    string Details);

public sealed record ReliabilityIncidentDisplayItem(
    string Header,
    string Category,
    string Details);

public sealed record RecurringFailureDisplayItem(
    string ApplicationName,
    string Details);
