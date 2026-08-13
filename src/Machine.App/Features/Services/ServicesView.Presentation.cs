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

public sealed partial class ServicesView
{
    private const int MaximumInventoryDisplayCount = 1_000;
    private IMachineServiceInventoryProvider? _provider;
    private CancellationToken _lifetimeCancellationToken;
    private MachineServiceInventorySnapshot? _latestSnapshot;
    private bool _isRequestRunning;

    internal void Initialize(
        IMachineServiceInventoryProvider provider,
        CancellationToken lifetimeCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _lifetimeCancellationToken = lifetimeCancellationToken;
    }

    private async void OnRefreshServicesClicked(
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
        RefreshServicesButton.IsEnabled = false;
        if (isManualRefresh)
        {
            RefreshServicesButton.Content = "Refreshing...";
            await Task.Yield();
        }
        try
        {
            var snapshot = await _provider.GetAsync(
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _latestSnapshot = snapshot;
            ApplyServiceFilter(snapshot);
            ServicesStatusText.Text = InventoryPresentation.CreateStatus(
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
            ServicesStatusText.Text =
                "Service inventory is temporarily unavailable.";
        }
        finally
        {
            _isRequestRunning = false;
            if (!cancellationToken.IsCancellationRequested)
            {
                RefreshServicesButton.Content = "Refresh";
                RefreshServicesButton.IsEnabled = true;
            }
        }
    }

    private void OnServiceFilterChanged(object sender, object args)
    {
        if (_latestSnapshot is { } snapshot)
        {
            ApplyServiceFilter(snapshot);
        }
    }

    private void ApplyServiceFilter(MachineServiceInventorySnapshot snapshot)
    {
        var search = ServiceSearchBox.Text.Trim();
        var state = InventoryPresentation.GetSelectedTag(ServiceStateFilter);
        var startType = InventoryPresentation.GetSelectedTag(ServiceStartTypeFilter);
        var filtered = snapshot.Items.Where(item =>
                (search.Length == 0 ||
                 item.Name.Contains(search,
                     StringComparison.OrdinalIgnoreCase) ||
                 item.DisplayName.Contains(search,
                     StringComparison.OrdinalIgnoreCase)) &&
                ServiceStateMatches(item.State, state) &&
                ServiceStartTypeMatches(item.StartType, startType))
            .Take(MaximumInventoryDisplayCount)
            .Select(CreateServiceDisplayItem)
            .ToArray();
        ServicesList.ItemsSource = filtered;
        ServicesSummaryText.Text =
            $"{snapshot.Items.Count:N0} services · showing " +
            $"{filtered.Length:N0}";
    }

    private static bool ServiceStateMatches(
        MachineServiceState state,
        string filter) => filter switch
        {
            "Running" => state == MachineServiceState.Running,
            "Stopped" => state == MachineServiceState.Stopped,
            "Paused" => state == MachineServiceState.Paused,
            "pending" => state is
                MachineServiceState.StartPending or
                MachineServiceState.StopPending or
                MachineServiceState.ContinuePending or
                MachineServiceState.PausePending,
            _ => true
        };

    private static bool ServiceStartTypeMatches(
        MachineServiceStartType startType,
        string filter) => filter switch
        {
            "automatic" => startType is
                MachineServiceStartType.Automatic or
                MachineServiceStartType.AutomaticDelayed,
            "Manual" => startType == MachineServiceStartType.Manual,
            "Disabled" => startType == MachineServiceStartType.Disabled,
            "boot" => startType is
                MachineServiceStartType.Boot or
                MachineServiceStartType.System,
            _ => true
        };

    private static ServiceDisplayItem CreateServiceDisplayItem(
        MachineServiceSnapshot item) => new(
        item.DisplayName,
        item.Name == item.DisplayName
            ? item.Category.ToString()
            : $"{item.Name} · {item.Category}",
        item.State.ToString(),
        item.ProcessId is { } processId
            ? $"{FormatServiceStartType(item.StartType)} · PID {processId}"
            : FormatServiceStartType(item.StartType));

    private static string FormatServiceStartType(
        MachineServiceStartType value) => value switch
        {
            MachineServiceStartType.AutomaticDelayed =>
                "Automatic (delayed)",
            _ => value.ToString()
        };
}
