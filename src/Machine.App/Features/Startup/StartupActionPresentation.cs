using System.Globalization;
using Machine.Core;

namespace Machine.App.Features;

internal sealed record StartupActionReviewPresentation(
    string Title,
    string Target,
    string CurrentState,
    string Change,
    string Effect,
    string NotAffected,
    string Reversibility,
    string AdministratorPermission,
    string Verification,
    string Limitations,
    string PrimaryButtonText);

internal sealed record StartupActionResultPresentation(
    string Title,
    string Detail);

internal sealed record StartupActionHistoryPresentation(
    string Name,
    string ActionDetails,
    string VerificationAndTimeDetails);

internal static class StartupActionPresenter
{
    internal static StartupActionReviewPresentation PresentDisable(
        MachineActionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new(
            Title: "Disable at startup",
            Target: plan.Target.DisplayName,
            CurrentState: plan.CurrentState,
            Change: plan.RequestedState,
            Effect: plan.ExpectedEffect,
            NotAffected: plan.NotAffected,
            Reversibility: plan.Reversible
                ? "Yes — Matasuri preserves the exact prior registration."
                : "No",
            AdministratorPermission: plan.RequiresElevation
                ? "Required — this version will not make the change."
                : "Not required",
            Verification: plan.Verification,
            Limitations: plan.Limitations,
            PrimaryButtonText: "Disable at startup");
    }

    internal static StartupActionReviewPresentation PresentRestore(
        MachineActionUndoPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new(
            Title: "Restore at startup",
            Target: plan.Target.DisplayName,
            CurrentState: "Disabled at startup by Matasuri",
            Change: "Restore the exact startup registration Matasuri preserved.",
            Effect: "The app can start automatically at a future sign-in.",
            NotAffected: "This does not launch the app now.",
            Reversibility: "Yes — future changes still require review and approval.",
            AdministratorPermission: "Not required",
            Verification:
                "Matasuri will re-query the same startup registration after the restore.",
            Limitations: "Affects future sign-ins only.",
            PrimaryButtonText: "Restore at startup");
    }

    internal static StartupActionResultPresentation PresentExecutionResult(
        MachineActionCoordinatorResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Status switch
        {
            MachineActionResultStatus.SucceededVerified => new(
                "Disabled at startup",
                "Verified against current Windows startup state. " +
                "The running app, if any, was not closed."),
            MachineActionResultStatus.TargetChanged => new(
                "Not changed",
                "The startup registration changed since review. " +
                "Refresh and review a new plan."),
            MachineActionResultStatus.PermissionRequired => new(
                "Not changed",
                "Administrator permission is required; this version keeps the item read-only."),
            MachineActionResultStatus.Unsupported => new(
                "Not changed",
                "This startup provider is not safely manageable in this version."),
            MachineActionResultStatus.NotApproved => new(
                "Not changed",
                "The reviewed action was not explicitly approved."),
            MachineActionResultStatus.ChangedButVerificationFailed => new(
                "Change not verified",
                "Windows state could not be proven after the change. " +
                "Recovery information was retained."),
            MachineActionResultStatus.RecoveryUnknown => new(
                "Result needs review",
                "Matasuri found an interrupted action with uncertain Windows state. " +
                "Recovery information was retained."),
            _ => new(
                "Not changed",
                "Windows did not reach the reviewed startup state.")
        };
    }

    internal static StartupActionResultPresentation PresentUndoResult(
        MachineActionUndoCoordinatorResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Status switch
        {
            MachineActionUndoStatus.SucceededVerified => new(
                "Restored at startup",
                "The exact preserved registration was restored and verified. " +
                "The app was not launched."),
            MachineActionUndoStatus.TargetChanged => new(
                "Not changed",
                "The restore destination changed since review. " +
                "Matasuri did not overwrite it."),
            MachineActionUndoStatus.PermissionRequired => new(
                "Not changed",
                "Administrator permission is required; this version did not restore the item."),
            MachineActionUndoStatus.Unsupported => new(
                "Not changed",
                "The preserved startup provider is no longer safely supported."),
            MachineActionUndoStatus.NotApproved => new(
                "Not changed",
                "The restore was not explicitly approved."),
            MachineActionUndoStatus.ChangedButVerificationFailed => new(
                "Restore not verified",
                "Windows state could not be proven after the restore. " +
                "Recovery information was retained."),
            MachineActionUndoStatus.RecoveryUnknown => new(
                "Restore needs review",
                "Matasuri found uncertain Windows state and retained the recovery information."),
            _ => new(
                "Not changed",
                "Windows did not reach the reviewed restored state.")
        };
    }

    internal static StartupActionHistoryPresentation PresentHistory(
        MachineActionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var wasRestored = outcome.UndoState ==
            MachineActionUndoStatus.SucceededVerified;
        var action = wasRestored
            ? "Restored at startup"
            : outcome.Result == MachineActionResultStatus.SucceededVerified
                ? "Disabled at startup"
                : outcome.Result ==
                    MachineActionResultStatus.ChangedButVerificationFailed
                    ? "Change not verified"
                    : "Not changed";
        var verified = wasRestored ||
            outcome.VerificationResult ==
                MachineActionVerificationStatus.Verified;
        var completedAt = wasRestored
            ? outcome.UndoCompletedAt
            : outcome.CompletedAt;
        var time = completedAt?.ToLocalTime().ToString(
            "g", CultureInfo.CurrentCulture) ?? "Pending review";

        return new(
            outcome.Target.DisplayName,
            action,
            $"{(verified ? "Verified" : "Not verified")} · {time}");
    }
}
