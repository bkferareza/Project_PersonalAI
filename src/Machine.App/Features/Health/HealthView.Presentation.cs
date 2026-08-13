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

public sealed partial class HealthView
{
    private const int MaximumUpdateHistoryDisplayCount = 12;
    private const int MaximumReliabilityIncidentDisplayCount = 16;
    private const int MaximumRecurringFailureDisplayCount = 4;
    private const string UnavailableValue = "Unavailable";

    internal void Update(
        MachineWindowsUpdateSnapshot? windowsUpdate,
        MachineRebootPendingSnapshot? rebootPending,
        MachineReliabilitySnapshot? reliability,
        OverviewView overview)
    {
        ArgumentNullException.ThrowIfNull(overview);
        UpdateWindowsUpdateDashboard(windowsUpdate);
        UpdateRestartDashboard(rebootPending, overview);
        UpdateReliabilityDashboard(reliability, overview);

        var statusMessages = new List<string>();
        if (windowsUpdate is { } update &&
            update.DataStatus != MachineHealthDataStatus.Complete)
        {
            statusMessages.Add(update.VerifiedAt is null
                ? "Windows Update status unavailable"
                : update.RefreshStatus ==
                    MachineWindowsUpdateRefreshStatus.CachedAfterFailure
                    ? "Windows Update is showing its last verified state"
                    : "some Windows Update details are unavailable");
        }
        if (rebootPending?.IsPartial == true)
        {
            statusMessages.Add("restart evidence partial");
        }
        if (reliability is { } reliabilitySnapshot &&
            reliabilitySnapshot.DataStatus != MachineHealthDataStatus.Complete)
        {
            statusMessages.Add(reliability.VerifiedAt is null
                ? "reliability history unavailable"
                : "reliability history partial");
        }

        HealthStatusText.Text = string.Join(" · ", statusMessages);
        HealthStatusText.Visibility = statusMessages.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateWindowsUpdateDashboard(
        MachineWindowsUpdateSnapshot? snapshot)
    {
        if (snapshot?.VerifiedAt is null)
        {
            WindowsUpdateStateText.Text = "Status unavailable";
            WindowsUpdateFreshnessText.Text = snapshot is null
                ? "Waiting for local Windows Update state"
                : "No verified state is available";
            WindowsUpdatePendingText.Text = UnavailableValue;
            WindowsUpdateImportantText.Text = UnavailableValue;
            WindowsUpdateLastScanText.Text = UnavailableValue;
            WindowsUpdateLastInstallText.Text = UnavailableValue;
            WindowsUpdateHistoryList.ItemsSource =
                Array.Empty<UpdateHistoryDisplayItem>();
            WindowsUpdateHistoryEmptyText.Visibility = Visibility.Visible;
            return;
        }

        WindowsUpdateStateText.Text = FormatWindowsUpdateState(snapshot);
        var age = DateTimeOffset.UtcNow - snapshot.VerifiedAt.Value;
        WindowsUpdateFreshnessText.Text = snapshot.RefreshStatus ==
                MachineWindowsUpdateRefreshStatus.CachedAfterFailure
            ? $"Last verified {FormatRelativeAge(age)} ago · latest refresh failed"
            : $"Verified {FormatRelativeAge(age)} ago";
        WindowsUpdatePendingText.Text = snapshot.PendingUpdateCount is { } pending
            ? $"{pending:N0}"
            : UnavailableValue;
        WindowsUpdateImportantText.Text =
            snapshot.PendingImportantUpdateCount is { } important
                ? $"{important:N0}"
                : UnavailableValue;
        WindowsUpdateLastScanText.Text = FormatHealthDateTime(
            snapshot.LastSuccessfulUpdateScan,
            UnavailableValue);
        WindowsUpdateLastInstallText.Text = FormatHealthDateTime(
            snapshot.LastSuccessfulUpdateInstall,
            UnavailableValue);

        var history = snapshot.RecentUpdateHistory
            .Take(MaximumUpdateHistoryDisplayCount)
            .Select(entry => new UpdateHistoryDisplayItem(
                Header: $"{entry.OccurredAt.ToLocalTime():MMM d · h:mm tt} · " +
                    FormatUpdateHistoryResult(entry.Result),
                Title: entry.Title,
                Details: string.Join(
                    " · ",
                    new[] { entry.KnowledgeBaseId, entry.Category }
                        .Where(value => !string.IsNullOrWhiteSpace(value)))))
            .ToArray();
        WindowsUpdateHistoryList.ItemsSource = history;
        WindowsUpdateHistoryEmptyText.Visibility = history.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateRestartDashboard(
        MachineRebootPendingSnapshot? snapshot,
        OverviewView overview)
    {
        if (snapshot is null || snapshot.IsPending is null)
        {
            RestartStateText.Text = "Restart status unknown";
            RestartReasonsText.Text = snapshot?.IsPartial == true
                ? "Available restart indicators were inconclusive."
                : "Waiting for local restart indicators.";
            RestartDataStatusText.Text = snapshot is null
                ? string.Empty
                : $"Checked {FormatRelativeAge(DateTimeOffset.UtcNow - snapshot.CapturedAt)} ago";
            overview.OverviewHealthPrimaryText.Text = "Restart status unknown";
            return;
        }

        RestartStateText.Text = snapshot.IsPending == true
            ? "Restart pending"
            : "No restart pending";
        RestartReasonsText.Text = snapshot.IsPending == true
            ? string.Join(
                " · ",
                snapshot.Reasons.Select(FormatRebootReason))
            : "No verified restart indicator is currently set.";
        RestartDataStatusText.Text =
            $"Checked {FormatRelativeAge(DateTimeOffset.UtcNow - snapshot.CapturedAt)} ago" +
            (snapshot.IsPartial ? " · partial evidence" : string.Empty);
        overview.OverviewHealthPrimaryText.Text = snapshot.IsPending == true
            ? "Restart pending"
            : "No restart pending";
    }

    private void UpdateReliabilityDashboard(
        MachineReliabilitySnapshot? snapshot,
        OverviewView overview)
    {
        if (snapshot?.VerifiedAt is null)
        {
            SetReliabilityCounts(null);
            ReliabilityFreshnessText.Text = snapshot is null
                ? "Waiting for Windows reliability history"
                : "Reliability history unavailable";
            ReliabilityIncidentsList.ItemsSource =
                Array.Empty<ReliabilityIncidentDisplayItem>();
            ReliabilityIncidentsEmptyText.Visibility = Visibility.Visible;
            RecurringFailuresList.ItemsSource =
                Array.Empty<RecurringFailureDisplayItem>();
            RecurringFailuresEmptyText.Visibility = Visibility.Visible;
            overview.OverviewHealthSecondaryText.Text =
                "Reliability history unavailable";
            return;
        }

        var sevenDays = snapshot.Summary.Last7Days;
        SetReliabilityCounts(sevenDays);
        ReliabilityFreshnessText.Text =
            $"Last 7 days · verified " +
            $"{FormatRelativeAge(DateTimeOffset.UtcNow - snapshot.VerifiedAt.Value)} ago" +
            (snapshot.DataStatus == MachineHealthDataStatus.Complete
                ? string.Empty
                : " · partial");
        var incidents = snapshot.Incidents
            .Take(MaximumReliabilityIncidentDisplayCount)
            .Select(incident => new ReliabilityIncidentDisplayItem(
                Header: $"{incident.OccurredAt.ToLocalTime():MMM d · h:mm tt}",
                Category: FormatReliabilityCategory(incident.Category),
                Details: CreateReliabilityIncidentDetails(incident)))
            .ToArray();
        ReliabilityIncidentsList.ItemsSource = incidents;
        ReliabilityIncidentsEmptyText.Visibility = incidents.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var recurring = snapshot.Summary.RecurringApplications
            .Take(MaximumRecurringFailureDisplayCount)
            .Select(item =>
            {
                var thirtyDayNoun = item.IncidentCountLast30Days == 1
                    ? "incident"
                    : "incidents";
                var sevenDayNoun = item.IncidentCountLast7Days == 1
                    ? "incident"
                    : "incidents";
                return new RecurringFailureDisplayItem(
                    ApplicationName: item.ApplicationName,
                    Details:
                        $"{item.IncidentCountLast30Days:N0} " +
                        $"{thirtyDayNoun} in 30 days · " +
                        $"{item.IncidentCountLast7Days:N0} " +
                        $"{sevenDayNoun} in 7 days");
            })
            .ToArray();
        RecurringFailuresList.ItemsSource = recurring;
        RecurringFailuresEmptyText.Visibility = recurring.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var appFailures = sevenDays.ApplicationCrashCount +
            sevenDays.ApplicationHangCount;
        overview.OverviewHealthSecondaryText.Text = appFailures > 0
            ? $"{appFailures:N0} app " +
                (appFailures == 1 ? "failure" : "failures") +
                " recorded in 7 days"
            : sevenDays.UnexpectedShutdownCount > 0
                ? $"{sevenDays.UnexpectedShutdownCount:N0} unexpected " +
                    (sevenDays.UnexpectedShutdownCount == 1
                        ? "shutdown"
                        : "shutdowns") + " recorded in 7 days"
                : sevenDays.TotalIncidentCount > 0
                    ? $"{sevenDays.TotalIncidentCount:N0} reliability " +
                        (sevenDays.TotalIncidentCount == 1
                            ? "incident"
                            : "incidents") + " recorded in 7 days"
                : snapshot.DataStatus == MachineHealthDataStatus.Complete
                    ? "No reliability incidents recorded in the verified 7-day window"
                    : "Reliability history is partially available";
    }

    private void SetReliabilityCounts(
        MachineReliabilityWindowSummary? summary)
    {
        ReliabilityCrashCountText.Text = summary is null
            ? UnavailableValue
            : $"{summary.ApplicationCrashCount:N0}";
        ReliabilityHangCountText.Text = summary is null
            ? UnavailableValue
            : $"{summary.ApplicationHangCount:N0}";
        ReliabilityShutdownCountText.Text = summary is null
            ? UnavailableValue
            : $"{summary.UnexpectedShutdownCount:N0}";
        ReliabilityUpdateFailureCountText.Text = summary is null
            ? UnavailableValue
            : $"{summary.UpdateFailureCount:N0}";
        ReliabilityHardwareFailureCountText.Text = summary is null
            ? UnavailableValue
            : $"{summary.HardwareFailureCount:N0}";
    }

    private static string FormatWindowsUpdateState(
        MachineWindowsUpdateSnapshot snapshot) => snapshot.UpdateState switch
    {
        MachineWindowsUpdateState.UpToDate => "Up to date",
        MachineWindowsUpdateState.UpdatesAvailable =>
            snapshot.PendingUpdateCount is { } pending
                ? $"{pending:N0} " +
                    (pending == 1 ? "update available" : "updates available")
                : "Updates available",
        MachineWindowsUpdateState.InstallPending => "Installation pending",
        MachineWindowsUpdateState.RestartRequired => "Restart required",
        _ => "Status unavailable"
    };

    private static string FormatUpdateHistoryResult(
        MachineWindowsUpdateHistoryResult result) => result switch
    {
        MachineWindowsUpdateHistoryResult.Succeeded => "Installed",
        MachineWindowsUpdateHistoryResult.SucceededWithErrors =>
            "Installed with errors",
        MachineWindowsUpdateHistoryResult.Failed => "Failed",
        MachineWindowsUpdateHistoryResult.Cancelled => "Cancelled",
        MachineWindowsUpdateHistoryResult.InProgress => "In progress",
        _ => "Result unavailable"
    };

    private static string FormatRebootReason(
        MachineRebootPendingReason reason) => reason switch
    {
        MachineRebootPendingReason.WindowsUpdate => "Windows Update",
        MachineRebootPendingReason.ComponentServicing =>
            "Component servicing",
        MachineRebootPendingReason.PendingFileRename =>
            "Pending file rename",
        MachineRebootPendingReason.ComputerRename => "Computer rename",
        _ => "Other Windows indicator"
    };

    private static string FormatReliabilityCategory(
        MachineReliabilityIncidentCategory category) => category switch
    {
        MachineReliabilityIncidentCategory.ApplicationCrash =>
            "Application crash",
        MachineReliabilityIncidentCategory.ApplicationHang =>
            "Application hang",
        MachineReliabilityIncidentCategory.UnexpectedShutdown =>
            "Unexpected shutdown",
        MachineReliabilityIncidentCategory.WindowsFailure =>
            "Windows failure",
        MachineReliabilityIncidentCategory.HardwareFailure =>
            "Hardware-error record",
        MachineReliabilityIncidentCategory.UpdateFailure =>
            "Update failure",
        MachineReliabilityIncidentCategory.InstallFailure =>
            "Install failure",
        _ => "Reliability incident"
    };

    private static string CreateReliabilityIncidentDetails(
        MachineReliabilityIncident incident)
    {
        var details = new[]
        {
            incident.ApplicationName,
            incident.UpdateIdentifier,
            incident.FailureCode,
            incident.EventId is { } eventId ? $"Event {eventId}" : null
        }.Where(value => !string.IsNullOrWhiteSpace(value));
        return string.Join(" · ", details);
    }

    private static string FormatHealthDateTime(
        DateTimeOffset? value,
        string unavailable) => value is null
        ? unavailable
        : value.Value.ToLocalTime().ToString(
            "MMM d · h:mm tt",
            CultureInfo.CurrentCulture);

    private static string FormatRelativeAge(TimeSpan age)
    {
        var bounded = age < TimeSpan.Zero ? TimeSpan.Zero : age;
        if (bounded.TotalDays >= 1d)
        {
            return $"{(int)bounded.TotalDays}d";
        }
        if (bounded.TotalHours >= 1d)
        {
            return $"{(int)bounded.TotalHours}h";
        }
        if (bounded.TotalMinutes >= 1d)
        {
            return $"{Math.Max(1, (int)bounded.TotalMinutes)}m";
        }
        return "under a minute";
    }

}
