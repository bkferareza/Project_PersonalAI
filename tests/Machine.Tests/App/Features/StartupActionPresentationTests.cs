using Machine.App.Features;
using Machine.Core;

namespace Machine.Tests.App.Features;

public sealed class StartupActionPresentationTests
{
    [Fact]
    public void DisableReviewShowsExactEffectSafetyAndVerification()
    {
        var plan = CreatePlan();

        var presentation = StartupActionPresenter.PresentDisable(plan);

        Assert.Equal("Disable at startup", presentation.Title);
        Assert.Equal("Development fixture", presentation.Target);
        Assert.Equal("Enabled at startup", presentation.CurrentState);
        Assert.Contains("future sign-ins", presentation.Effect);
        Assert.Contains("does not close", presentation.NotAffected);
        Assert.StartsWith("Yes", presentation.Reversibility);
        Assert.Equal("Not required", presentation.AdministratorPermission);
        Assert.Contains("re-query", presentation.Verification);
        Assert.Equal("Disable at startup", presentation.PrimaryButtonText);
    }

    [Fact]
    public void RestoreReviewExplainsConflictSafeFutureEffect()
    {
        var outcome = CreateOutcome(CreatePlan());
        var undo = MachineActionUndoPlan.Create(outcome);

        var presentation = StartupActionPresenter.PresentRestore(undo);

        Assert.Equal("Restore at startup", presentation.Title);
        Assert.Contains("exact startup registration", presentation.Change);
        Assert.Contains("future sign-in", presentation.Effect);
        Assert.Contains("does not launch", presentation.NotAffected);
        Assert.Equal("Restore at startup", presentation.PrimaryButtonText);
    }

    [Theory]
    [InlineData(MachineActionResultStatus.SucceededVerified,
        "Disabled at startup")]
    [InlineData(MachineActionResultStatus.TargetChanged, "Not changed")]
    [InlineData(MachineActionResultStatus.PermissionRequired, "Not changed")]
    [InlineData(MachineActionResultStatus.ChangedButVerificationFailed,
        "Change not verified")]
    public void ExecutionResultUsesBoundedProductLanguage(
        MachineActionResultStatus status,
        string expectedTitle)
    {
        var result = new MachineActionCoordinatorResult(status);

        var presentation =
            StartupActionPresenter.PresentExecutionResult(result);

        Assert.Equal(expectedTitle, presentation.Title);
        Assert.DoesNotContain(status.ToString(), presentation.Detail);
        Assert.DoesNotContain("Exception", presentation.Detail);
    }

    [Fact]
    public void RecentHistoryShowsVerifiedRestoreWithoutProviderPayload()
    {
        var outcome = CreateOutcome(CreatePlan()) with
        {
            UndoState = MachineActionUndoStatus.SucceededVerified,
            UndoStartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UndoCompletedAt = DateTimeOffset.UtcNow,
            UndoVerificationResult =
                MachineActionVerificationStatus.Verified,
            UndoUserApproved = true
        };

        var presentation = StartupActionPresenter.PresentHistory(outcome);

        Assert.Equal("Development fixture", presentation.Name);
        Assert.Equal("Restored at startup", presentation.ActionDetails);
        Assert.StartsWith("Verified", presentation.VerificationAndTimeDetails);
        Assert.DoesNotContain(
            outcome.RecoveryPayload!.ProviderData,
            presentation.VerificationAndTimeDetails);
    }

    [Fact]
    public void StartupXamlRequiresExplicitActionAndExposesResultHistory()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "FeatureViews",
            "Startup",
            "StartupView.xaml");
        var xaml = File.ReadAllText(path);

        Assert.Contains("Content=\"{x:Bind ActionLabel}\"", xaml);
        Assert.Contains("Click=\"OnStartupActionClicked\"", xaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"StartupActionResultBorder\"",
            xaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"StartupRecentActionsList\"",
            xaml);
        Assert.Contains("never closes a running app", xaml);
        Assert.DoesNotContain("Optimize", xaml);
        Assert.DoesNotContain("Apply recommended", xaml);
    }

    private static MachineActionPlan CreatePlan()
    {
        var target = new MachineActionTarget(
            MachineActionTargetKind.StartupRegistryRunEntry,
            "startup:v1:test",
            "Development fixture");
        var current = MachineActionTargetState.Supported(
            target,
            "enabled:exact",
            "fixed-hkcu-run-provider-v1");

        return MachineActionPlan.Create(
            MachineActionCapability.SetStartupEnabled,
            target,
            currentState: "Enabled at startup",
            currentNormalizedState: current.NormalizedState,
            requestedState: "Remove this current-user startup registration",
            requestedNormalizedState: "disabled",
            changeCategory: "Current-user startup registration",
            expectedEffect:
                "The app will no longer start automatically at future sign-ins.",
            notAffected:
                "This affects future sign-ins only and does not close a running app.",
            reversible: true,
            requiresElevation: false,
            verification:
                "Matasuri will re-query the same startup registration.",
            limitations: "Affects future sign-ins only.",
            current.PreconditionFingerprint,
            new MachineActionRecoveryPayload(1, "opaque-provider-data"));
    }

    private static MachineActionOutcome CreateOutcome(
        MachineActionPlan plan) => new(
        plan.ActionId,
        plan.PlanFingerprint,
        plan.PreconditionFingerprint,
        plan.Capability,
        plan.Target,
        plan.ExpectedEffect,
        plan.RequestedNormalizedState,
        plan.CreatedAt,
        plan.CreatedAt.AddSeconds(1),
        MachineActionResultStatus.SucceededVerified,
        MachineActionVerificationStatus.Verified,
        Reversible: true,
        MachineActionUndoStatus.Available,
        UndoStartedAt: null,
        UndoCompletedAt: null,
        MachineActionVerificationStatus.NotAttempted,
        plan.CurrentNormalizedState,
        plan.RequestedNormalizedState,
        MachineActionFingerprint.CreatePrecondition(
            plan.Target,
            plan.RequestedNormalizedState,
            "fixed-hkcu-run-provider-v1"),
        UserApproved: true,
        UndoUserApproved: false,
        plan.RecoveryPayload,
        MachineActionRecoveryClassification.NotRequired,
        MachineActionRecoveryClassification.NotRequired,
        FailureCode: null);
}
