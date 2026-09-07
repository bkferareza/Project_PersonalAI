using Machine.Core;

namespace Machine.Tests;

public sealed class MachineBriefValidatorTests
{
    [Fact]
    public void ValidStructuredBriefPreservesEvidenceLinks()
    {
        var result = MachineBriefValidator.Validate(
            MachineBriefTestData.ValidDraft(),
            MachineBriefTestData.Situation());

        Assert.True(result.IsValid, result.SafeReason);
        Assert.NotNull(result.Content);
        Assert.Equal(2, result.Content.Points.Count);
        Assert.Equal("recent.reliability",
            result.Content.Points[0].EvidenceIds.Single());
        Assert.Equal("forward.next_observed_hour",
            result.Content.OutlookEvidenceIds.Single());
    }

    [Fact]
    public void InvalidEvidenceIdentityIsRejected()
    {
        var draft = MachineBriefTestData.ValidDraft() with
        {
            OverallEvidenceIds = ["invented.evidence"]
        };

        var result = MachineBriefValidator.Validate(
            draft, MachineBriefTestData.Situation());

        Assert.False(result.IsValid);
        Assert.Equal(MachineBriefValidationFailure.EvidenceIdentity,
            result.Failure);
    }

    [Fact]
    public void UnsupportedNumericClaimIsRejected()
    {
        var draft = MachineBriefTestData.ValidDraft() with
        {
            Outlook = "The next observed hour is projected at 9.999 kWh."
        };

        var result = MachineBriefValidator.Validate(
            draft, MachineBriefTestData.Situation());

        Assert.False(result.IsValid);
        Assert.Equal(MachineBriefValidationFailure.NumericGrounding,
            result.Failure);
    }

    [Fact]
    public void UnsupportedEntityIsRejected()
    {
        var draft = MachineBriefTestData.ValidDraft() with
        {
            Points =
            [
                new("Photoshop remains worth watching.",
                    ["recent.reliability"])
            ]
        };

        var result = MachineBriefValidator.Validate(
            draft, MachineBriefTestData.Situation());

        Assert.False(result.IsValid);
        Assert.Equal(MachineBriefValidationFailure.EntityGrounding,
            result.Failure);
        Assert.Contains("Photoshop", result.SafeReason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EntityLinkedToDifferentEvidenceIdentifiesRequiredCitation()
    {
        var draft = MachineBriefTestData.ValidDraft() with
        {
            Points =
            [
                new("GbtCloudMatrix.exe remains worth watching.",
                    ["now.posture"])
            ]
        };

        var result = MachineBriefValidator.Validate(
            draft, MachineBriefTestData.Situation());

        Assert.False(result.IsValid);
        Assert.Equal(MachineBriefValidationFailure.EntityGrounding,
            result.Failure);
        Assert.Contains("GbtCloudMatrix.exe", result.SafeReason,
            StringComparison.Ordinal);
        Assert.Contains("recent.reliability", result.SafeReason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EntityMayCiteOneOfSeveralEvidenceItemsThatCarryItsName()
    {
        var evidence = MachineBriefTestData.Evidence().Append(new(
            "recent.incident.latest",
            MachineSituationCategory.Recently,
            MachineSituationTimeScope.Recent,
            MachineSituationImportance.Notable,
            MachineSituationFreshness.Recent,
            MachineSituationEvidenceMaturity.Verified,
            "GbtCloudMatrix.exe was the latest reliability incident.",
            ["Latest incident"],
            ["GbtCloudMatrix.exe"])).ToArray();

        var result = MachineBriefValidator.Validate(
            MachineBriefTestData.ValidDraft(),
            MachineBriefTestData.Situation(evidence));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UnsupportedCausalPhraseIsRejected()
    {
        var draft = MachineBriefTestData.ValidDraft() with
        {
            Points =
            [
                new("GbtCloudMatrix.exe caused the machine state.",
                    ["recent.reliability"])
            ]
        };

        var result = MachineBriefValidator.Validate(
            draft, MachineBriefTestData.Situation());

        Assert.False(result.IsValid);
        Assert.Equal(MachineBriefValidationFailure.Causality,
            result.Failure);
    }

    [Theory]
    [InlineData("System resources are within learned ranges.")]
    [InlineData("System resources are normal.")]
    [InlineData("System resources remain within learned ranges despite high memory usage at 43.0%.")]
    [InlineData("Current CPU usage is normal.")]
    [InlineData("Current memory usage is above its learned baseline.")]
    public void RawResourcesAndLearnedStatisticsDoNotAuthorizeComparison(
        string text)
    {
        // Established evidence also does not authorize model-side arithmetic.
        var evidence = MachineBriefTestData.Evidence().Select(item =>
            item.Id == "learned.current_context"
                ? item with { Maturity = MachineSituationEvidenceMaturity.Established }
                : item).ToArray();
        var result = ValidatePoint(text,
            ["now.resources", "learned.current_context"], evidence);

        Assert.Equal(MachineBriefValidationFailure.ClaimGrounding,
            result.Failure);
    }

    [Fact]
    public void SuppliedDeterministicComparisonCanBeCopiedButNotReversed()
    {
        const string summary = "Current CPU 13.0% is below the learned typical range.";
        var evidence = MachineBriefTestData.Evidence().Append(new(
            "finding.cpu-comparison", MachineSituationCategory.Now,
            MachineSituationTimeScope.Current,
            MachineSituationImportance.Context,
            MachineSituationFreshness.Current,
            MachineSituationEvidenceMaturity.Verified,
            summary, ["13.0%"], [])).ToArray();

        Assert.True(ValidatePoint(summary,
            ["finding.cpu-comparison"], evidence).IsValid);
        Assert.Equal(MachineBriefValidationFailure.ClaimGrounding,
            ValidatePoint(summary.Replace("below", "above",
                StringComparison.Ordinal),
                ["finding.cpu-comparison"], evidence).Failure);
    }

    [Theory]
    [InlineData("GbtCloudMatrix.exe has recorded 2 failures and 2 unexpected shutdowns.")]
    [InlineData("GbtCloudMatrix.exe has 2 failures alongside 2 unexpected shutdowns.")]
    public void ApplicationAndSystemEventCountsCannotBeMerged(string text)
    {
        var evidence = EvidenceWithSystemShutdowns();
        var result = ValidatePoint(text,
            ["recent.reliability", "recent.reliability.7d"], evidence);

        Assert.Equal(MachineBriefValidationFailure.ClaimGrounding,
            result.Failure);
    }

    [Fact]
    public void ApplicationAndSystemEventCountsRemainAvailableSeparately()
    {
        var evidence = EvidenceWithSystemShutdowns();
        var draft = MachineBriefTestData.ValidDraft() with
        {
            Points =
            [
                new("GbtCloudMatrix.exe recorded 2 recent application failures.",
                    ["recent.reliability"]),
                new("Windows recorded 2 unexpected shutdowns in 7 days.",
                    ["recent.reliability.7d"])
            ]
        };

        Assert.True(MachineBriefValidator.Validate(draft,
            MachineBriefTestData.Situation(evidence)).IsValid);
    }

    [Theory]
    [InlineData("A pending file rename operation is currently blocking a system restart.", MachineBriefValidationFailure.Causality)]
    [InlineData("A pending file rename operation prevents a system restart.", MachineBriefValidationFailure.Causality)]
    [InlineData("A pending file rename operation requires a restart.", MachineBriefValidationFailure.Causality)]
    [InlineData("A system restart is currently queued.", MachineBriefValidationFailure.ClaimGrounding)]
    [InlineData("A system restart is scheduled.", MachineBriefValidationFailure.ClaimGrounding)]
    public void PendingRestartDoesNotAuthorizeCauseOrSchedule(
        string text, MachineBriefValidationFailure failure)
    {
        var evidence = EvidenceWithPendingRestart();

        Assert.Equal(failure,
            ValidatePoint(text, ["now.reboot"], evidence).Failure);
    }

    [Fact]
    public void PendingRestartStatusAndReasonRemainAvailable()
    {
        Assert.True(ValidatePoint(
            "Pending restart evidence includes pending file rename operations.",
            ["now.reboot"], EvidenceWithPendingRestart()).IsValid);
    }

    [Fact]
    public void CausalPermissionDoesNotAuthorizeAnUnrelatedRelationship()
    {
        const string summary = "History is unavailable because observation is paused.";
        var evidence = MachineBriefTestData.Evidence().Append(new(
            "recent.paused", MachineSituationCategory.Recently,
            MachineSituationTimeScope.Recent,
            MachineSituationImportance.Context,
            MachineSituationFreshness.Recent,
            MachineSituationEvidenceMaturity.Verified,
            summary, [], [], AllowsCausalLanguage: true)).ToArray();

        Assert.True(ValidatePoint(summary, ["recent.paused"], evidence).IsValid);
        Assert.Equal(MachineBriefValidationFailure.Causality,
            ValidatePoint("GbtCloudMatrix.exe caused the machine state.",
                ["recent.reliability", "recent.paused"], evidence).Failure);
    }

    private static MachineBriefValidationResult ValidatePoint(
        string text,
        IReadOnlyList<string> ids,
        IReadOnlyList<MachineSituationEvidenceItem> evidence) =>
        MachineBriefValidator.Validate(
            MachineBriefTestData.ValidDraft() with
            {
                Points = [new(text, ids)]
            }, MachineBriefTestData.Situation(evidence));

    private static IReadOnlyList<MachineSituationEvidenceItem>
        EvidenceWithSystemShutdowns() => MachineBriefTestData.Evidence().Append(new(
            "recent.reliability.7d", MachineSituationCategory.Recently,
            MachineSituationTimeScope.Last7Days,
            MachineSituationImportance.Notable,
            MachineSituationFreshness.Recent,
            MachineSituationEvidenceMaturity.Verified,
            "Windows recorded 2 unexpected shutdowns in 7 days.",
            ["2 unexpected shutdowns", "7 days"], [])).ToArray();

    private static IReadOnlyList<MachineSituationEvidenceItem>
        EvidenceWithPendingRestart() => MachineBriefTestData.Evidence().Append(new(
            "now.reboot", MachineSituationCategory.Now,
            MachineSituationTimeScope.Current,
            MachineSituationImportance.Notable,
            MachineSituationFreshness.Current,
            MachineSituationEvidenceMaturity.Verified,
            "Restart pending: Pending file rename operations.",
            ["Restart pending", "Pending file rename operations"], [])).ToArray();

    [Fact]
    public void MutationInstructionIsRejected()
    {
        var draft = MachineBriefTestData.ValidDraft() with
        {
            Points =
            [
                new("Disable GbtCloudMatrix.exe now.",
                    ["recent.reliability"])
            ]
        };

        var result = MachineBriefValidator.Validate(
            draft, MachineBriefTestData.Situation());

        Assert.False(result.IsValid);
        Assert.Equal(MachineBriefValidationFailure.ActionBoundary,
            result.Failure);
    }

    [Fact]
    public void UnsupportedEndOfDayProjectionIsRejected()
    {
        var draft = MachineBriefTestData.ValidDraft() with
        {
            Outlook = "End-of-day use is projected at 0.150 kWh.",
            OutlookEvidenceIds = ["forward.next_observed_hour"]
        };

        var result = MachineBriefValidator.Validate(
            draft, MachineBriefTestData.Situation());

        Assert.False(result.IsValid);
        Assert.Equal(MachineBriefValidationFailure.ForecastBoundary,
            result.Failure);
    }

    [Fact]
    public void LearningObservedDaysAreNotForecastCoverage()
    {
        var result = ValidatePoint(
            "Forecast availability is available with 4 days of coverage.",
            ["learning.awareness"], MachineBriefTestData.Evidence());

        Assert.Equal(MachineBriefValidationFailure.ForecastBoundary,
            result.Failure);
    }

    [Fact]
    public void MoreThanThreePointsIsRejectedBySchema()
    {
        var draft = MachineBriefTestData.ValidDraft() with
        {
            Points = Enumerable.Range(0, 4)
                .Select(_ => new MachineBriefDraftPoint(
                    "Everything remains normal.", ["now.posture"]))
                .ToArray()
        };

        var result = MachineBriefValidator.Validate(
            draft, MachineBriefTestData.Situation());

        Assert.False(result.IsValid);
        Assert.Equal(MachineBriefValidationFailure.Schema, result.Failure);
    }

    [Fact]
    public void DeterministicFallbackStaysBoundedAndEvidenceLinked()
    {
        var situation = MachineBriefTestData.Situation();

        var fallback = MachineBriefFallbackComposer.Compose(situation);

        Assert.Equal("Everything looks normal overall.", fallback.Overall);
        Assert.InRange(fallback.Points.Count, 1,
            MachineBriefPromptPolicy.MaximumPointCount);
        var validIds = situation.Evidence.Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(fallback.OverallEvidenceIds,
            id => Assert.Contains(id, validIds));
        Assert.All(fallback.Points.SelectMany(point => point.EvidenceIds),
            id => Assert.Contains(id, validIds));
        Assert.All(fallback.OutlookEvidenceIds,
            id => Assert.Contains(id, validIds));
        Assert.Contains(fallback.Points,
            point => point.EvidenceIds.Contains("recent.reliability",
                StringComparer.Ordinal));
    }

    [Fact]
    public void BriefOutputHasNoMachineAuthorityFields()
    {
        var propertyTypes = typeof(MachineBrief).GetProperties()
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.DoesNotContain(typeof(MachineOverallState), propertyTypes);
        Assert.DoesNotContain(typeof(MachineFindingsSnapshot), propertyTypes);
        Assert.DoesNotContain(typeof(MachineLearningDashboardSnapshot),
            propertyTypes);
        Assert.DoesNotContain(typeof(MachineUsageForecast), propertyTypes);
        Assert.DoesNotContain(typeof(MachineActionPlan), propertyTypes);
        Assert.DoesNotContain(typeof(MachineActionApproval), propertyTypes);
    }
}
