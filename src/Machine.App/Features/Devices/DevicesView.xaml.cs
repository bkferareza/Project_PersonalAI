using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

public sealed partial class DevicesView : UserControl
{
    public DevicesView()
    {
        InitializeComponent();
        RefreshDevicesButton.Click += OnRefreshDevicesClicked;
        DeviceSearchBox.TextChanged += OnDeviceFilterChanged;
        DeviceClassFilter.SelectionChanged += OnDeviceFilterChanged;
        DeviceProblemFilter.SelectionChanged += OnDeviceFilterChanged;
    }
}

public sealed record DeviceDisplayItem(
    string DisplayName,
    string Identity,
    string DriverDetails,
    string StatusDetails);
