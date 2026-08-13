using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

public sealed partial class SoftwareView : UserControl
{
    public SoftwareView()
    {
        InitializeComponent();
        SoftwareSearchBox.TextChanged += OnSoftwareSearchTextChanged;
        RefreshSoftwareButton.Click += OnRefreshSoftwareClicked;
        PackagedSoftwareSearchBox.TextChanged +=
            OnPackagedSoftwareSearchTextChanged;
        RefreshPackagedSoftwareButton.Click +=
            OnRefreshPackagedSoftwareClicked;
    }
}

public sealed record InstalledSoftwareDisplayItem(
    string Name,
    string PublisherAndVersion,
    string RegistrationDetails,
    string InstallLocationDetails);

public sealed record PackagedSoftwareDisplayItem(
    string DisplayName,
    string PublisherVersionArchitecture,
    string PackageFamilyDetails,
    string PackageFlagsDetails,
    string InstalledLocationDetails);
