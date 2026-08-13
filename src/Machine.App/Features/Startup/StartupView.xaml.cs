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
    string Name,
    string CommandOrPathDetails,
    string SourceDetails);
