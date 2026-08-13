using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

public sealed partial class ServicesView : UserControl
{
    public ServicesView()
    {
        InitializeComponent();
        RefreshServicesButton.Click += OnRefreshServicesClicked;
        ServiceSearchBox.TextChanged += OnServiceFilterChanged;
        ServiceStateFilter.SelectionChanged += OnServiceFilterChanged;
        ServiceStartTypeFilter.SelectionChanged += OnServiceFilterChanged;
    }
}

public sealed record ServiceDisplayItem(
    string DisplayName,
    string Identity,
    string State,
    string StartDetails);
