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
