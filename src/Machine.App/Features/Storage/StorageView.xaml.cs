using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

public sealed partial class StorageView : UserControl
{
    public StorageView()
    {
        InitializeComponent();
        RefreshStorageButton.Click += OnRefreshStorageClicked;
        ScanLargeFoldersButton.Click += OnScanLargeFoldersClicked;
        CancelLargeFolderScanButton.Click +=
            OnCancelLargeFolderScanClicked;
    }
}

public sealed record StorageVolumeDisplayItem(
    string Header,
    string VolumeDetails,
    string CapacityDetails);

public sealed record LargeFolderDisplayItem(
    string Path,
    string Details);
