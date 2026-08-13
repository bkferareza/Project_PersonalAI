using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

public sealed partial class OverviewView : UserControl
{
    public OverviewView()
    {
        InitializeComponent();
    }
}

public sealed record MachineFindingDisplayItem(
    string Header,
    string Detail);
