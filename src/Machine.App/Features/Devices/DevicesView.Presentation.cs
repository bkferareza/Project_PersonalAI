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

public sealed partial class DevicesView
{
    private const int MaximumInventoryDisplayCount = 1_000;
    private IMachineDeviceInventoryProvider? _provider;
    private CancellationToken _lifetimeCancellationToken;
    private MachineDeviceInventorySnapshot? _latestSnapshot;
    private bool _isRequestRunning;

    internal void Initialize(
        IMachineDeviceInventoryProvider provider,
        CancellationToken lifetimeCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _lifetimeCancellationToken = lifetimeCancellationToken;
    }

    private async void OnRefreshDevicesClicked(
        object sender,
        RoutedEventArgs args) => await LoadAsync(
        isManualRefresh: true,
        _lifetimeCancellationToken);

    internal async Task LoadAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken)
    {
        if (_isRequestRunning || _provider is null)
        {
            return;
        }
        _isRequestRunning = true;
        RefreshDevicesButton.IsEnabled = false;
        if (isManualRefresh)
        {
            RefreshDevicesButton.Content = "Refreshing...";
            await Task.Yield();
        }
        try
        {
            var snapshot = await _provider.GetAsync(
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _latestSnapshot = snapshot;
            var selectedClass = DeviceClassFilter.SelectedItem?.ToString();
            DeviceClassFilter.ItemsSource = new[] { "All classes" }
                .Concat(snapshot.Items.Select(item => item.DeviceClass)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item,
                        StringComparer.OrdinalIgnoreCase))
                .ToArray();
            DeviceClassFilter.SelectedItem = selectedClass is not null &&
                DeviceClassFilter.Items.Contains(selectedClass)
                    ? selectedClass
                    : "All classes";
            ApplyDeviceFilter(snapshot);
            DevicesStatusText.Text = InventoryPresentation.CreateStatus(
                snapshot.IsComplete,
                snapshot.ReadFailureCount,
                snapshot.TruncatedItemCount);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            DevicesStatusText.Text =
                "Device inventory is temporarily unavailable.";
        }
        finally
        {
            _isRequestRunning = false;
            if (!cancellationToken.IsCancellationRequested)
            {
                RefreshDevicesButton.Content = "Refresh";
                RefreshDevicesButton.IsEnabled = true;
            }
        }
    }

    private void OnDeviceFilterChanged(object sender, object args)
    {
        if (_latestSnapshot is { } snapshot)
        {
            ApplyDeviceFilter(snapshot);
        }
    }

    private void ApplyDeviceFilter(MachineDeviceInventorySnapshot snapshot)
    {
        var search = DeviceSearchBox.Text.Trim();
        var selectedClass = DeviceClassFilter.SelectedItem?.ToString();
        var problemFilter = InventoryPresentation.GetSelectedTag(DeviceProblemFilter);
        var filtered = snapshot.Items.Where(item =>
                (search.Length == 0 ||
                 item.DisplayName.Contains(search,
                     StringComparison.OrdinalIgnoreCase) ||
                 item.DeviceClass.Contains(search,
                     StringComparison.OrdinalIgnoreCase) ||
                 item.Manufacturer?.Contains(search,
                     StringComparison.OrdinalIgnoreCase) == true ||
                 item.DriverProvider?.Contains(search,
                     StringComparison.OrdinalIgnoreCase) == true) &&
                (selectedClass is null or "All classes" ||
                 string.Equals(item.DeviceClass, selectedClass,
                     StringComparison.OrdinalIgnoreCase)) &&
                (problemFilter != "problem" ||
                 item.HasWindowsReportedProblem))
            .Take(MaximumInventoryDisplayCount)
            .Select(CreateDeviceDisplayItem)
            .ToArray();
        DevicesList.ItemsSource = filtered;
        var problemCount = snapshot.Items.Count(item =>
            item.HasWindowsReportedProblem);
        DevicesSummaryText.Text =
            $"{snapshot.Items.Count:N0} devices · {problemCount:N0} with " +
            $"a Windows-reported problem · showing {filtered.Length:N0}";
    }

    private static DeviceDisplayItem CreateDeviceDisplayItem(
        MachineDeviceSnapshot item)
    {
        var driverParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.DriverProvider))
        {
            driverParts.Add(item.DriverProvider);
        }
        if (!string.IsNullOrWhiteSpace(item.DriverVersion))
        {
            driverParts.Add($"Driver {item.DriverVersion}");
        }
        if (item.DriverDate is { } date)
        {
            driverParts.Add(date.ToString("MMM d, yyyy"));
        }
        return new(
            item.DisplayName,
            string.IsNullOrWhiteSpace(item.Manufacturer)
                ? item.DeviceClass
                : $"{item.DeviceClass} · {item.Manufacturer}",
            driverParts.Count == 0
                ? "Driver details unavailable"
                : string.Join(" · ", driverParts),
            item.HasWindowsReportedProblem
                ? $"Windows problem code {item.ProblemCode}"
                : "No Windows-reported problem");
    }

}
