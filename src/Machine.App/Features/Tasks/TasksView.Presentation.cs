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

public sealed partial class TasksView
{
    private const int MaximumInventoryDisplayCount = 1_000;
    private IMachineScheduledTaskInventoryProvider? _provider;
    private CancellationToken _lifetimeCancellationToken;
    private MachineScheduledTaskInventorySnapshot? _latestSnapshot;
    private bool _isRequestRunning;

    internal void Initialize(
        IMachineScheduledTaskInventoryProvider provider,
        CancellationToken lifetimeCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _lifetimeCancellationToken = lifetimeCancellationToken;
    }

    private async void OnRefreshTasksClicked(
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
        RefreshTasksButton.IsEnabled = false;
        if (isManualRefresh)
        {
            RefreshTasksButton.Content = "Refreshing...";
            await Task.Yield();
        }
        try
        {
            var snapshot = await _provider.GetAsync(
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _latestSnapshot = snapshot;
            ApplyTaskFilter(snapshot);
            TasksStatusText.Text = InventoryPresentation.CreateStatus(
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
            TasksStatusText.Text =
                "Scheduled-task inventory is temporarily unavailable.";
        }
        finally
        {
            _isRequestRunning = false;
            if (!cancellationToken.IsCancellationRequested)
            {
                RefreshTasksButton.Content = "Refresh";
                RefreshTasksButton.IsEnabled = true;
            }
        }
    }

    private void OnTaskFilterChanged(object sender, object args)
    {
        if (_latestSnapshot is { } snapshot)
        {
            ApplyTaskFilter(snapshot);
        }
    }

    private void ApplyTaskFilter(
        MachineScheduledTaskInventorySnapshot snapshot)
    {
        var search = TaskSearchBox.Text.Trim();
        var enabled = InventoryPresentation.GetSelectedTag(TaskEnabledFilter);
        var state = InventoryPresentation.GetSelectedTag(TaskStateFilter);
        var result = InventoryPresentation.GetSelectedTag(TaskResultFilter);
        var filtered = snapshot.Items.Where(item =>
                (search.Length == 0 ||
                 item.Name.Contains(search,
                     StringComparison.OrdinalIgnoreCase) ||
                 item.Path.Contains(search,
                     StringComparison.OrdinalIgnoreCase) ||
                 item.Author?.Contains(search,
                     StringComparison.OrdinalIgnoreCase) == true ||
                 item.ExecutableName?.Contains(search,
                     StringComparison.OrdinalIgnoreCase) == true) &&
                (enabled == "all" ||
                 enabled == "enabled" && item.Enabled ||
                 enabled == "disabled" && !item.Enabled) &&
                (state == "all" ||
                 string.Equals(item.State.ToString(), state,
                     StringComparison.Ordinal)) &&
                (result != "failed" || item.LastRunFailed))
            .Take(MaximumInventoryDisplayCount)
            .Select(CreateTaskDisplayItem)
            .ToArray();
        TasksList.ItemsSource = filtered;
        TasksSummaryText.Text =
            $"{snapshot.Items.Count:N0} tasks · showing {filtered.Length:N0}";
    }

    private static ScheduledTaskDisplayItem CreateTaskDisplayItem(
        MachineScheduledTaskSnapshot item)
    {
        var triggers = item.TriggerCategories.Count == 0
            ? "Triggers unavailable"
            : string.Join(", ", item.TriggerCategories);
        var next = item.NextRunAt is { } nextRun
            ? $"Next {nextRun.ToLocalTime():MMM d, HH:mm}"
            : "No next run reported";
        var last = item.LastRunAt is { } lastRun
            ? $"Last {lastRun.ToLocalTime():MMM d, HH:mm}"
            : "No last run reported";
        var executable = item.ExecutableName is null
            ? string.Empty
            : $" · {item.ExecutableName}";
        return new(
            item.Name,
            item.Path,
            item.Enabled ? item.State.ToString() : "Disabled",
            $"{triggers} · {next}",
            $"{last} · Result " +
                (item.LastResult?.ToString("X8") ?? "unavailable") +
                executable);
    }
}
