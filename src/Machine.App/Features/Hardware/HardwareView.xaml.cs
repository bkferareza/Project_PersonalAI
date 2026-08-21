using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

public sealed partial class HardwareView : UserControl
{
    public HardwareView()
    {
        InitializeComponent();
    }
}

public sealed record StorageDeviceDisplayItem(string Name, string Identity, string Health);
