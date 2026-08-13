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

public sealed partial class StartupView
{
    private const string UnavailableValue = "Unavailable";
    private IMachineStartupInventoryProvider? _provider;
    private CancellationToken _lifetimeCancellationToken;
    private Action? _onSnapshotChanged;
    private MachineStartupInventorySnapshot? _latestSnapshot;
    private bool _isRequestRunning;

    internal MachineStartupInventorySnapshot? LatestSnapshot =>
        _latestSnapshot;

    internal void Initialize(
        IMachineStartupInventoryProvider provider,
        CancellationToken lifetimeCancellationToken,
        Action onSnapshotChanged)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(onSnapshotChanged);
        _provider = provider;
        _lifetimeCancellationToken = lifetimeCancellationToken;
        _onSnapshotChanged = onSnapshotChanged;
    }

    private async void OnRefreshStartupClicked(
        object sender,
        RoutedEventArgs e)
    {
        await LoadAsync(
            isManualRefresh: true,
            cancellationToken: _lifetimeCancellationToken);
    }

    internal async Task LoadAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken)
    {
        if (_isRequestRunning || _provider is null)
        {
            return;
        }

        _isRequestRunning = true;
        if (isManualRefresh)
        {
            RefreshStartupButton.Content = "Refreshing...";
        }

        UpdateRefreshStartupButtonState();

        if (isManualRefresh)
        {
            await Task.Yield();
        }

        try
        {
            var snapshot = await _provider
                .GetAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            UpdateStartupInventory(snapshot);
            _latestSnapshot = snapshot;
            _onSnapshotChanged?.Invoke();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            if (_latestSnapshot is null)
            {
                StartupApplicationsList.ItemsSource =
                    Array.Empty<StartupApplicationDisplayItem>();
                StartupInventorySummaryText.Text =
                    "0 entries found\nShowing 0";
            }

            StartupInventoryStatusText.Text =
                "Startup inventory is temporarily unavailable.";
        }
        finally
        {
            _isRequestRunning = false;

            if (!cancellationToken.IsCancellationRequested)
            {
                RefreshStartupButton.Content =
                    "Refresh startup applications";
                UpdateRefreshStartupButtonState();
            }
        }
    }

    private void UpdateStartupInventory(
        MachineStartupInventorySnapshot snapshot)
    {
        ApplyStartupInventoryFilter(snapshot);

        StartupInventoryStatusText.Text = !snapshot.IsComplete
            ? $"Inventory is partial · " +
                $"{snapshot.ReadFailureCount} " +
                (snapshot.ReadFailureCount == 1
                    ? "read failure"
                    : "read failures")
            : snapshot.Items.Count == 0
                ? "No startup applications found in Run keys or Startup folders."
                : string.Empty;
    }

    private void OnStartupSearchTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_latestSnapshot is not null)
        {
            ApplyStartupInventoryFilter(
                _latestSnapshot);
        }
    }

    private void ApplyStartupInventoryFilter(
        MachineStartupInventorySnapshot snapshot)
    {
        var searchText = StartupSearchBox.Text.Trim();
        var filteredItems = snapshot.Items
            .Where(item =>
                searchText.Length == 0 ||
                item.Name.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase) ||
                item.CommandOrPath.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase))
            .Select(CreateStartupApplicationDisplayItem)
            .ToArray();

        StartupApplicationsList.ItemsSource = filteredItems;
        StartupInventorySummaryText.Text =
            $"{snapshot.Items.Count} entries found\n" +
            $"Showing {filteredItems.Length}";
    }

    private static StartupApplicationDisplayItem
        CreateStartupApplicationDisplayItem(
            MachineStartupApplicationSnapshot startupApplication)
    {
        var scope = startupApplication.Scope switch
        {
            MachineStartupScope.CurrentUser => "Current user",
            MachineStartupScope.AllUsers => "All users",
            _ => UnavailableValue,
        };

        return startupApplication.Source switch
        {
            MachineStartupSource.RegistryRunKey =>
                new StartupApplicationDisplayItem(
                    startupApplication.Name,
                    $"Command: {startupApplication.CommandOrPath}",
                    $"{scope} · " +
                    $"{FormatStartupRegistryView(startupApplication.RegistryView)} Run key"),
            MachineStartupSource.StartupFolder =>
                new StartupApplicationDisplayItem(
                    startupApplication.Name,
                    $"Path: {startupApplication.CommandOrPath}",
                    $"{scope} · Startup folder"),
            _ => new StartupApplicationDisplayItem(
                startupApplication.Name,
                startupApplication.CommandOrPath,
                $"{scope} · Source unavailable"),
        };
    }

    private static string FormatStartupRegistryView(
        MachineStartupRegistryView? registryView) =>
        registryView switch
        {
            MachineStartupRegistryView.Registry32 => "32-bit",
            MachineStartupRegistryView.Registry64 => "64-bit",
            MachineStartupRegistryView.Shared => "Shared",
            _ => "Unknown-view",
        };

    private void UpdateRefreshStartupButtonState()
    {
        RefreshStartupButton.IsEnabled =
            !_isRequestRunning &&
            !_lifetimeCancellationToken.IsCancellationRequested;
    }
}
