using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

public sealed partial class StartupView : UserControl
{
    public StartupView()
    {
        InitializeComponent();
        StartupSearchBox.TextChanged += OnStartupSearchTextChanged;
        RefreshStartupButton.Click += OnRefreshStartupClicked;
    }

}

public sealed record StartupApplicationDisplayItem(
    string StableIdentity,
    string Name,
    string CommandOrPathDetails,
    string SourceDetails,
    string StateDetails,
    string ManageabilityDetails,
    string ManageabilityReason,
    Visibility ManageabilityReasonVisibility,
    string ActionLabel,
    string ActionAutomationId,
    string ActionAccessibleName,
    string ActionToken,
    bool IsActionEnabled,
    Visibility ActionVisibility);
