using System.Diagnostics;
using Machine.Core;
using Machine.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

public sealed partial class StartupView
{
    private const string DisableActionPrefix = "disable:";
    private const string RestoreActionPrefix = "restore:";

    private async void OnStartupActionClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (_isActionRunning ||
            _actionService is null ||
            sender is not Button button ||
            button.Tag is not string actionToken)
        {
            return;
        }

        _isActionRunning = true;
        button.IsEnabled = false;
        UpdateRefreshStartupButtonState();
        try
        {
            if (actionToken.StartsWith(
                DisableActionPrefix,
                StringComparison.Ordinal))
            {
                await ReviewAndDisableAsync(
                    actionToken[DisableActionPrefix.Length..],
                    _lifetimeCancellationToken);
            }
            else if (actionToken.StartsWith(
                    RestoreActionPrefix,
                    StringComparison.Ordinal) &&
                Guid.TryParseExact(
                    actionToken[RestoreActionPrefix.Length..],
                    "N",
                    out var actionId))
            {
                await ReviewAndRestoreAsync(
                    actionId,
                    _lifetimeCancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            ShowActionResult(new(
                "Not changed",
                "Matasuri could not safely record and verify this action. " +
                "No unrecorded change was started."));
        }
        finally
        {
            _isActionRunning = false;
            UpdateRefreshStartupButtonState();
            if (_latestSnapshot is not null)
            {
                ApplyStartupInventoryFilter(_latestSnapshot);
            }
        }
    }

    private async Task ReviewAndDisableAsync(
        string stableIdentity,
        CancellationToken cancellationToken)
    {
        if (_actionService is null ||
            !_startupItemsByIdentity.TryGetValue(
                stableIdentity,
                out var startupItem))
        {
            ShowActionResult(new(
                "Not changed",
                "The startup item is no longer present. Refresh and review it again."));
            return;
        }

        var planned = await _actionService.CreateDisablePlanAsync(
            startupItem,
            cancellationToken);
        if (planned.Plan is null)
        {
            ShowActionResult(PresentUnavailablePlan(planned.Status));
            await RefreshAfterActionAsync(cancellationToken);
            return;
        }

        var plan = planned.Plan;
        var review = StartupActionPresenter.PresentDisable(plan);
        if (!await ShowReviewAsync(review))
        {
            return;
        }

        var approval = MachineActionApproval.ForExecution(plan);
        var result = await _actionService.ExecuteAsync(
            plan,
            approval,
            cancellationToken);
        ShowActionResult(
            StartupActionPresenter.PresentExecutionResult(result));
        await RefreshAfterActionAsync(cancellationToken);
    }

    private async Task ReviewAndRestoreAsync(
        Guid actionId,
        CancellationToken cancellationToken)
    {
        if (_actionService is null ||
            !_startupOutcomesById.TryGetValue(actionId, out var outcome))
        {
            ShowActionResult(new(
                "Not changed",
                "The preserved recovery record is no longer available."));
            return;
        }

        MachineActionUndoPlan plan;
        try
        {
            plan = WindowsStartupActionService.CreateUndoPlan(outcome);
        }
        catch (InvalidOperationException)
        {
            ShowActionResult(new(
                "Not changed",
                "The preserved startup state cannot currently be restored safely."));
            return;
        }

        var review = StartupActionPresenter.PresentRestore(plan);
        if (!await ShowReviewAsync(review))
        {
            return;
        }

        var approval = MachineActionApproval.ForUndo(plan);
        var result = await _actionService.UndoAsync(
            plan,
            approval,
            cancellationToken);
        ShowActionResult(
            StartupActionPresenter.PresentUndoResult(result));
        await RefreshAfterActionAsync(cancellationToken);
    }

    private async Task RefreshAfterActionAsync(
        CancellationToken cancellationToken)
    {
        if (_provider is null || _actionService is null)
        {
            return;
        }

        var snapshot = await _provider.GetAsync(cancellationToken);
        _latestActionOutcomes = await _actionService
            .GetOutcomesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        UpdateStartupInventory(snapshot);
        _latestSnapshot = snapshot;
        _onSnapshotChanged?.Invoke();
    }

    private async Task<bool> ShowReviewAsync(
        StartupActionReviewPresentation presentation)
    {
        var content = new StackPanel
        {
            MaxWidth = 440,
            Spacing = 9
        };
        AddReviewField(content, "TARGET", presentation.Target);
        AddReviewField(content, "CURRENT STATE", presentation.CurrentState);
        AddReviewField(content, "CHANGE", presentation.Change);
        AddReviewField(content, "EFFECT", presentation.Effect);
        AddReviewField(content, "NOT AFFECTED", presentation.NotAffected);
        AddReviewField(content, "REVERSIBLE", presentation.Reversibility);
        AddReviewField(
            content,
            "ADMINISTRATOR PERMISSION",
            presentation.AdministratorPermission);
        AddReviewField(content, "VERIFICATION", presentation.Verification);
        if (!string.IsNullOrWhiteSpace(presentation.Limitations))
        {
            AddReviewField(content, "LIMITATIONS", presentation.Limitations);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = presentation.Title,
            Content = content,
            CloseButtonText = "Cancel",
            PrimaryButtonText = presentation.PrimaryButtonText,
            DefaultButton = ContentDialogButton.Close
        };
        AutomationProperties.SetAutomationId(
            dialog,
            "StartupActionReviewDialog");

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static void AddReviewField(
        StackPanel content,
        string label,
        string value)
    {
        var field = new StackPanel { Spacing = 2 };
        field.Children.Add(new TextBlock
        {
            Text = label,
            CharacterSpacing = 80,
            FontSize = 10,
            Opacity = 0.72
        });
        field.Children.Add(new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(field);
    }

    private void ShowActionResult(
        StartupActionResultPresentation presentation)
    {
        StartupActionResultTitleText.Text = presentation.Title;
        StartupActionResultDetailText.Text = presentation.Detail;
        StartupActionResultBorder.Visibility = Visibility.Visible;
    }

    private static StartupActionResultPresentation PresentUnavailablePlan(
        WindowsStartupActionPlanStatus status) =>
        status switch
        {
            WindowsStartupActionPlanStatus.PermissionRequired => new(
                "Not changed",
                "Administrator permission is required; this version keeps the item read-only."),
            WindowsStartupActionPlanStatus.Protected => new(
                "Not changed",
                "Matasuri's own startup presence is protected from the generic action path."),
            WindowsStartupActionPlanStatus.TargetChanged => new(
                "Not changed",
                "The startup registration changed since it was shown. Refresh and review it again."),
            _ => new(
                "Not changed",
                "This startup provider cannot be changed safely in this version.")
        };
}
