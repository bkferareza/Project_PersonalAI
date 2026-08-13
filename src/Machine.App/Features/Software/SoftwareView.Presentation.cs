using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Machine.Core;
using Machine.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace Machine.App.Features;

public sealed partial class SoftwareView
{
    private const string UnavailableValue = "Unavailable";
    private const double BytesPerMebibyte = 1024d * 1024d;
    private const double BytesPerGibibyte = 1024d * 1024d * 1024d;
    private const double BytesPerTebibyte =
        1024d * 1024d * 1024d * 1024d;

    private IMachineSoftwareInventoryProvider? _classicProvider;
    private IMachinePackagedSoftwareInventoryProvider? _packagedProvider;
    private CancellationToken _lifetimeCancellationToken;
    private Action? _onSnapshotChanged;
    private MachineSoftwareInventorySnapshot? _latestClassicSnapshot;
    private MachinePackagedSoftwareInventorySnapshot?
        _latestPackagedSnapshot;
    private bool _isClassicRequestRunning;
    private bool _isPackagedRequestRunning;

    internal MachineSoftwareInventorySnapshot? LatestClassicSnapshot =>
        _latestClassicSnapshot;

    internal MachinePackagedSoftwareInventorySnapshot?
        LatestPackagedSnapshot => _latestPackagedSnapshot;

    internal void Initialize(
        IMachineSoftwareInventoryProvider classicProvider,
        IMachinePackagedSoftwareInventoryProvider packagedProvider,
        CancellationToken lifetimeCancellationToken,
        Action onSnapshotChanged)
    {
        ArgumentNullException.ThrowIfNull(classicProvider);
        ArgumentNullException.ThrowIfNull(packagedProvider);
        ArgumentNullException.ThrowIfNull(onSnapshotChanged);
        _classicProvider = classicProvider;
        _packagedProvider = packagedProvider;
        _lifetimeCancellationToken = lifetimeCancellationToken;
        _onSnapshotChanged = onSnapshotChanged;
    }

    private async void OnRefreshSoftwareClicked(
        object sender,
        RoutedEventArgs e)
    {
        await LoadClassicAsync(
            isManualRefresh: true,
            cancellationToken: _lifetimeCancellationToken);
    }

    internal async Task LoadClassicAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken)
    {
        if (_isClassicRequestRunning || _classicProvider is null)
        {
            return;
        }

        _isClassicRequestRunning = true;
        if (isManualRefresh)
        {
            RefreshSoftwareButton.Content = "Refreshing...";
        }

        UpdateRefreshSoftwareButtonState();

        if (isManualRefresh)
        {
            await Task.Yield();
        }

        try
        {
            var snapshot = await _classicProvider
                .GetAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            UpdateSoftwareInventory(snapshot);
            _latestClassicSnapshot = snapshot;
            _onSnapshotChanged?.Invoke();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            if (_latestClassicSnapshot is null)
            {
                InstalledSoftwareList.ItemsSource =
                    Array.Empty<InstalledSoftwareDisplayItem>();
                SoftwareInventorySummaryText.Text =
                    "0 registrations found\nShowing 0";
            }

            SoftwareInventoryStatusText.Text =
                "Software inventory is temporarily unavailable.";
        }
        finally
        {
            _isClassicRequestRunning = false;

            if (!cancellationToken.IsCancellationRequested)
            {
                RefreshSoftwareButton.Content =
                    "Refresh software";
                UpdateRefreshSoftwareButtonState();
            }
        }
    }

    private void UpdateSoftwareInventory(
        MachineSoftwareInventorySnapshot snapshot)
    {
        ApplySoftwareInventoryFilter(snapshot);

        SoftwareInventoryStatusText.Text = snapshot.Items.Count == 0
            ? "No classic desktop software registrations found."
            : !snapshot.IsComplete
                ? $"Inventory is partial · " +
                    $"{snapshot.SkippedEntryCount} entries skipped"
                : string.Empty;
    }

    private void OnSoftwareSearchTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_latestClassicSnapshot is not null)
        {
            ApplySoftwareInventoryFilter(
                _latestClassicSnapshot);
        }
    }

    private void ApplySoftwareInventoryFilter(
        MachineSoftwareInventorySnapshot snapshot)
    {
        var searchText = SoftwareSearchBox.Text.Trim();
        var filteredItems = snapshot.Items
            .Where(item =>
                searchText.Length == 0 ||
                item.Name.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(item.Publisher) &&
                    item.Publisher.Contains(
                        searchText,
                        StringComparison.OrdinalIgnoreCase)))
            .Select(CreateInstalledSoftwareDisplayItem)
            .ToArray();

        InstalledSoftwareList.ItemsSource = filteredItems;
        SoftwareInventorySummaryText.Text =
            $"{snapshot.Items.Count} registrations found\n" +
            $"Showing {filteredItems.Length}";
    }

    private static InstalledSoftwareDisplayItem
        CreateInstalledSoftwareDisplayItem(
            MachineInstalledSoftwareSnapshot software)
    {
        var publisher = string.IsNullOrWhiteSpace(software.Publisher)
            ? "Publisher unavailable"
            : software.Publisher.Trim();
        var version = string.IsNullOrWhiteSpace(software.Version)
            ? "Version unavailable"
            : software.Version.Trim();
        var scope = software.Scope switch
        {
            MachineSoftwareScope.LocalMachine => "Machine",
            MachineSoftwareScope.CurrentUser => "Current user",
            _ => UnavailableValue,
        };
        var registryView = software.RegistryView switch
        {
            MachineSoftwareRegistryView.Registry32 =>
                "32-bit registration",
            MachineSoftwareRegistryView.Registry64 =>
                "64-bit registration",
            _ => "Registration view unavailable",
        };
        var estimatedSize =
            software.EstimatedSizeBytes is long bytes && bytes >= 0
                ? FormatBytes(bytes)
                : "Size unavailable";
        var installLocation =
            string.IsNullOrWhiteSpace(software.InstallLocation)
                ? string.Empty
                : $"Installed at {software.InstallLocation.Trim()}";

        return new InstalledSoftwareDisplayItem(
            software.Name,
            $"{publisher} · {version}",
            $"{scope} · {registryView} · {estimatedSize}",
            installLocation);
    }

    private void UpdateRefreshSoftwareButtonState()
    {
        RefreshSoftwareButton.IsEnabled =
            !_isClassicRequestRunning &&
            !_lifetimeCancellationToken.IsCancellationRequested;
    }

    private async void OnRefreshPackagedSoftwareClicked(
        object sender,
        RoutedEventArgs e)
    {
        await LoadPackagedAsync(
            isManualRefresh: true,
            cancellationToken: _lifetimeCancellationToken);
    }

    internal async Task LoadPackagedAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken)
    {
        if (_isPackagedRequestRunning || _packagedProvider is null)
        {
            return;
        }

        _isPackagedRequestRunning = true;
        if (isManualRefresh)
        {
            RefreshPackagedSoftwareButton.Content =
                "Refreshing...";
        }

        UpdateRefreshPackagedSoftwareButtonState();

        if (isManualRefresh)
        {
            await Task.Yield();
        }

        try
        {
            var snapshot = await _packagedProvider
                .GetAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            UpdatePackagedSoftwareInventory(snapshot);
            _latestPackagedSnapshot = snapshot;
            _onSnapshotChanged?.Invoke();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            if (_latestPackagedSnapshot is null)
            {
                PackagedSoftwareList.ItemsSource =
                    Array.Empty<PackagedSoftwareDisplayItem>();
                PackagedSoftwareInventorySummaryText.Text =
                    "0 packages found\nShowing 0";
            }

            PackagedSoftwareInventoryStatusText.Text =
                "Packaged-software inventory is temporarily unavailable.";
        }
        finally
        {
            _isPackagedRequestRunning = false;

            if (!cancellationToken.IsCancellationRequested)
            {
                RefreshPackagedSoftwareButton.Content =
                    "Refresh packaged applications";
                UpdateRefreshPackagedSoftwareButtonState();
            }
        }
    }

    private void UpdatePackagedSoftwareInventory(
        MachinePackagedSoftwareInventorySnapshot snapshot)
    {
        ApplyPackagedSoftwareInventoryFilter(snapshot);

        if (!snapshot.IsComplete)
        {
            var partialResultDetails = new List<string>(
                capacity: 2);

            if (snapshot.SkippedEntryCount > 0)
            {
                partialResultDetails.Add(
                    $"{snapshot.SkippedEntryCount} " +
                    (snapshot.SkippedEntryCount == 1
                        ? "package skipped"
                        : "packages skipped"));
            }

            if (snapshot.OptionalPropertyFailureCount > 0)
            {
                partialResultDetails.Add(
                    $"{snapshot.OptionalPropertyFailureCount} " +
                    (snapshot.OptionalPropertyFailureCount == 1
                        ? "optional property unavailable"
                        : "optional properties unavailable"));
            }

            PackagedSoftwareInventoryStatusText.Text =
                partialResultDetails.Count == 0
                    ? "Inventory is partial."
                    : $"Inventory is partial · " +
                        string.Join(" · ", partialResultDetails);
            return;
        }

        PackagedSoftwareInventoryStatusText.Text =
            snapshot.Items.Count == 0
                ? "No user-facing MSIX/AppX packages found."
                : string.Empty;
    }

    private void OnPackagedSoftwareSearchTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_latestPackagedSnapshot is not null)
        {
            ApplyPackagedSoftwareInventoryFilter(
                _latestPackagedSnapshot);
        }
    }

    private void ApplyPackagedSoftwareInventoryFilter(
        MachinePackagedSoftwareInventorySnapshot snapshot)
    {
        var searchText = PackagedSoftwareSearchBox.Text.Trim();
        var filteredItems = snapshot.Items
            .Where(item =>
                searchText.Length == 0 ||
                item.DisplayName.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(
                    item.PublisherDisplayName) &&
                    item.PublisherDisplayName.Contains(
                        searchText,
                        StringComparison.OrdinalIgnoreCase)) ||
                item.PackageFamilyName.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase))
            .Select(CreatePackagedSoftwareDisplayItem)
            .ToArray();

        PackagedSoftwareList.ItemsSource = filteredItems;
        PackagedSoftwareInventorySummaryText.Text =
            $"{snapshot.Items.Count} packages found\n" +
            $"Showing {filteredItems.Length}";
    }

    private static PackagedSoftwareDisplayItem
        CreatePackagedSoftwareDisplayItem(
            MachinePackagedSoftwareSnapshot software)
    {
        var publisher = string.IsNullOrWhiteSpace(
            software.PublisherDisplayName)
            ? "Publisher unavailable"
            : software.PublisherDisplayName.Trim();
        var flags = new List<string>(capacity: 2);

        if (software.IsDevelopmentMode == true)
        {
            flags.Add("Development package");
        }

        if (software.IsStub == true)
        {
            flags.Add("Stub package");
        }

        var installedLocation = string.IsNullOrWhiteSpace(
            software.InstalledLocation)
            ? string.Empty
            : $"Installed at {software.InstalledLocation.Trim()}";

        return new PackagedSoftwareDisplayItem(
            software.DisplayName,
            $"{publisher} · Version {software.Version} · " +
                FormatPackagedSoftwareArchitecture(
                    software.Architecture),
            $"Package family: {software.PackageFamilyName}",
            string.Join(" · ", flags),
            installedLocation);
    }

    private static string FormatPackagedSoftwareArchitecture(
        MachinePackagedSoftwareArchitecture architecture) =>
        architecture switch
        {
            MachinePackagedSoftwareArchitecture.Neutral => "Neutral",
            MachinePackagedSoftwareArchitecture.X86 => "x86",
            MachinePackagedSoftwareArchitecture.X64 => "x64",
            MachinePackagedSoftwareArchitecture.Arm => "ARM",
            MachinePackagedSoftwareArchitecture.Arm64 => "ARM64",
            MachinePackagedSoftwareArchitecture.X86OnArm64 =>
                "x86 on ARM64",
            _ => "Architecture unavailable",
        };

    private void UpdateRefreshPackagedSoftwareButtonState()
    {
        RefreshPackagedSoftwareButton.IsEnabled =
            !_isPackagedRequestRunning &&
            !_lifetimeCancellationToken.IsCancellationRequested;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= BytesPerTebibyte)
        {
            return $"{bytes / BytesPerTebibyte:F1} TB";
        }
        if (bytes >= BytesPerGibibyte)
        {
            return $"{bytes / BytesPerGibibyte:F1} GB";
        }
        if (bytes >= BytesPerMebibyte)
        {
            return $"{bytes / BytesPerMebibyte:F1} MB";
        }
        return $"{bytes} B";
    }
}
