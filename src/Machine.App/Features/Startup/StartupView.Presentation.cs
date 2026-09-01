using System.Diagnostics;
using Machine.Core;
using Machine.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

public sealed partial class StartupView
{
    private const string UnavailableValue = "Unavailable";
    private IMachineStartupInventoryProvider? _provider;
    private WindowsStartupActionService? _actionService;
    private CancellationToken _lifetimeCancellationToken;
    private Action? _onSnapshotChanged;
    private MachineStartupInventorySnapshot? _latestSnapshot;
    private IReadOnlyList<MachineActionOutcome> _latestActionOutcomes = [];
    private IReadOnlyDictionary<string, MachineStartupApplicationSnapshot>
        _startupItemsByIdentity =
            new Dictionary<string, MachineStartupApplicationSnapshot>();
    private IReadOnlyDictionary<Guid, MachineActionOutcome>
        _startupOutcomesById =
            new Dictionary<Guid, MachineActionOutcome>();
    private bool _isRequestRunning;
    private bool _isActionRunning;
    private bool _hasReconciledActions;

    internal MachineStartupInventorySnapshot? LatestSnapshot =>
        _latestSnapshot;

    internal IReadOnlyList<MachineActionOutcome> LatestActionOutcomes =>
        _latestActionOutcomes;

    internal void Initialize(
        IMachineStartupInventoryProvider provider,
        WindowsStartupActionService actionService,
        CancellationToken lifetimeCancellationToken,
        Action onSnapshotChanged)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(actionService);
        ArgumentNullException.ThrowIfNull(onSnapshotChanged);
        _provider = provider;
        _actionService = actionService;
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

            if (_actionService is not null)
            {
                if (!_hasReconciledActions)
                {
                    await _actionService.ReconcileInProgressAsync(
                        cancellationToken);
                    _hasReconciledActions = true;
                }

                _latestActionOutcomes = await _actionService
                    .GetOutcomesAsync(cancellationToken);
            }

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
                    "0 entries found\n" +
                    "0 manageable without administrator access\n" +
                    "Showing 0";
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
            : snapshot.Items.Count == 0 &&
                !_latestActionOutcomes.Any(IsRestorableStartupOutcome)
                ? "No startup applications found in Run keys or Startup folders."
                : string.Empty;
    }

    private void OnStartupSearchTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_latestSnapshot is not null)
        {
            ApplyStartupInventoryFilter(_latestSnapshot);
        }
    }

    private void ApplyStartupInventoryFilter(
        MachineStartupInventorySnapshot snapshot)
    {
        var searchText = StartupSearchBox.Text.Trim();
        var activeIdentities = snapshot.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.StableIdentity))
            .Select(item => item.StableIdentity!)
            .ToHashSet(StringComparer.Ordinal);
        var restorableOutcomes = _latestActionOutcomes
            .Where(IsRestorableStartupOutcome)
            .Where(outcome => !activeIdentities.Contains(
                outcome.Target.StableIdentity))
            .OrderBy(outcome => outcome.Target.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unresolvedByTarget = _latestActionOutcomes
            .Where(IsRestorableStartupOutcome)
            .Select(outcome => outcome.Target.StableIdentity)
            .ToHashSet(StringComparer.Ordinal);
        var displayItems = snapshot.Items
            .Select(item => CreateStartupApplicationDisplayItem(
                item,
                item.StableIdentity is not null &&
                    unresolvedByTarget.Contains(item.StableIdentity)))
            .Concat(restorableOutcomes.Select(
                CreateRestorableStartupDisplayItem))
            .ToArray();
        var filteredItems = displayItems
            .Where(item => searchText.Length == 0 ||
                item.Name.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase) ||
                item.CommandOrPathDetails.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase) ||
                item.SourceDetails.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase) ||
                item.ManageabilityDetails.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        _startupItemsByIdentity = snapshot.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.StableIdentity))
            .GroupBy(item => item.StableIdentity!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        _startupOutcomesById = restorableOutcomes
            .ToDictionary(outcome => outcome.ActionId);
        StartupApplicationsList.ItemsSource = filteredItems;
        var manageableCount = displayItems.Count(item =>
            item.ActionVisibility == Visibility.Visible &&
            item.IsActionEnabled);
        StartupInventorySummaryText.Text =
            $"{snapshot.Items.Count} entries found\n" +
            $"{manageableCount} manageable without administrator access\n" +
            $"Showing {filteredItems.Length}";

        var recentActions = _latestActionOutcomes
            .Where(outcome => outcome.Capability ==
                MachineActionCapability.SetStartupEnabled)
            .OrderByDescending(outcome =>
                outcome.UndoCompletedAt ??
                outcome.CompletedAt ??
                outcome.StartedAt)
            .Take(20)
            .Select(StartupActionPresenter.PresentHistory)
            .ToArray();
        StartupRecentActionsList.ItemsSource = recentActions;
        StartupRecentActionsEmptyText.Visibility =
            recentActions.Length == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private static StartupApplicationDisplayItem
        CreateStartupApplicationDisplayItem(
            MachineStartupApplicationSnapshot startupApplication,
            bool hasUnresolvedRecovery)
    {
        var scope = startupApplication.Scope switch
        {
            MachineStartupScope.CurrentUser => "Current user",
            MachineStartupScope.AllUsers => "All users",
            _ => UnavailableValue,
        };
        var sourceDetails = startupApplication.Source switch
        {
            MachineStartupSource.RegistryRunKey =>
                $"{scope} · " +
                $"{FormatStartupRegistryView(startupApplication.RegistryView)} Run key",
            MachineStartupSource.StartupFolder =>
                $"{scope} · Startup folder",
            _ => $"{scope} · Source unavailable",
        };
        var commandOrPathDetails = startupApplication.Source ==
            MachineStartupSource.RegistryRunKey
                ? $"Command: {startupApplication.CommandOrPath}"
                : $"Path: {startupApplication.CommandOrPath}";
        var actionAvailable = startupApplication.ActionAvailability ==
                MachineStartupActionAvailability.Supported &&
            !string.IsNullOrWhiteSpace(startupApplication.StableIdentity) &&
            !hasUnresolvedRecovery;
        var (manageability, reason) = hasUnresolvedRecovery
            ? (
                "Read-only while recovery is unresolved",
                "A prior controlled change for this registration still has recoverable state.")
            : FormatManageability(startupApplication);

        return new(
            StableIdentity: startupApplication.StableIdentity ?? string.Empty,
            Name: startupApplication.Name,
            CommandOrPathDetails: commandOrPathDetails,
            SourceDetails: sourceDetails,
            StateDetails: "Enabled at startup",
            ManageabilityDetails: manageability,
            ManageabilityReason: reason,
            ManageabilityReasonVisibility:
                string.IsNullOrWhiteSpace(reason)
                    ? Visibility.Collapsed
                    : Visibility.Visible,
            ActionLabel: actionAvailable
                ? "Disable at startup"
                : string.Empty,
            ActionAutomationId: actionAvailable
                ? $"DisableStartupAction_{startupApplication.StableIdentity}"
                : string.Empty,
            ActionAccessibleName: actionAvailable
                ? $"Disable {startupApplication.Name} at startup"
                : string.Empty,
            ActionToken: actionAvailable
                ? $"disable:{startupApplication.StableIdentity}"
                : string.Empty,
            IsActionEnabled: actionAvailable,
            ActionVisibility: actionAvailable
                ? Visibility.Visible
                : Visibility.Collapsed);
    }

    private static StartupApplicationDisplayItem
        CreateRestorableStartupDisplayItem(
            MachineActionOutcome outcome) => new(
        StableIdentity: outcome.Target.StableIdentity,
        Name: outcome.Target.DisplayName,
        CommandOrPathDetails:
            "Exact recovery data is preserved locally and is not displayed.",
        SourceDetails: outcome.Target.Kind switch
        {
            MachineActionTargetKind.StartupRegistryRunEntry =>
                "Current user · Run key",
            MachineActionTargetKind.StartupFolderEntry =>
                "Current user · Startup folder",
            _ => "Current user · Startup registration"
        },
        StateDetails: "Disabled at startup by Matasuri",
        ManageabilityDetails: "Manageable · Reversible",
        ManageabilityReason:
            "Restore re-creates only the exact registration Matasuri preserved.",
        ManageabilityReasonVisibility: Visibility.Visible,
        ActionLabel: "Restore at startup",
        ActionAutomationId:
            $"RestoreStartupAction_{outcome.ActionId:N}",
        ActionAccessibleName:
            $"Restore {outcome.Target.DisplayName} at startup",
        ActionToken: $"restore:{outcome.ActionId:N}",
        IsActionEnabled: true,
        ActionVisibility: Visibility.Visible);

    private static (string Label, string Reason) FormatManageability(
        MachineStartupApplicationSnapshot startupApplication) =>
        startupApplication.ActionAvailability switch
        {
            MachineStartupActionAvailability.Supported =>
                ("Manageable · Reversible", string.Empty),
            MachineStartupActionAvailability.PermissionRequired =>
                ("Requires administrator access · Read-only",
                    "System-scoped startup entries are not changed in this version."),
            MachineStartupActionAvailability.Protected =>
                ("Matasuri startup · Protected",
                    "Matasuri's own persistent presence is not changed through the generic startup action path."),
            _ =>
                ("Unsupported safely · Read-only",
                    startupApplication.RegistryValueKind is not null and
                        not MachineStartupRegistryValueKind.String and
                        not MachineStartupRegistryValueKind.ExpandString
                        ? "This registry value type is preserved read-only."
                        : "This startup provider cannot be reversed safely in this version.")
        };

    private static bool IsRestorableStartupOutcome(
        MachineActionOutcome outcome) =>
        outcome.Capability == MachineActionCapability.SetStartupEnabled &&
        outcome.Reversible &&
        outcome.UndoState is MachineActionUndoStatus.Available or
            MachineActionUndoStatus.Failed or
            MachineActionUndoStatus.ChangedButVerificationFailed or
            MachineActionUndoStatus.TargetChanged or
            MachineActionUndoStatus.RecoveryUnknown;

    private static string FormatStartupRegistryView(
        MachineStartupRegistryView? registryView) =>
        registryView switch
        {
            MachineStartupRegistryView.Registry32 => "32-bit",
            MachineStartupRegistryView.Registry64 => "64-bit",
            MachineStartupRegistryView.Shared => "Shared",
            _ => "Unknown view",
        };

    private void UpdateRefreshStartupButtonState()
    {
        RefreshStartupButton.IsEnabled =
            !_isRequestRunning &&
            !_isActionRunning &&
            !_lifetimeCancellationToken.IsCancellationRequested;
    }
}
