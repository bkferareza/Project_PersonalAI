using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

public sealed partial class NetworkView : UserControl
{
    public NetworkView()
    {
        InitializeComponent();
    }
}

public sealed record NetworkInterfaceDisplayItem(
    string Name,
    string StatusAndType,
    string Description,
    string LinkDetails,
    string ReceivedDetails,
    string SentDetails);
