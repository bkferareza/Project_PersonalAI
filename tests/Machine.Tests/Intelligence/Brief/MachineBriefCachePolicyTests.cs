using Machine.Core;

namespace Machine.Tests;

public sealed class MachineBriefCachePolicyTests
{
    [Fact]
    public void SameMaterialSituationReusesCachedBrief()
    {
        var policy = new MachineBriefCachePolicy();
        var request = Request();
        var decision = policy.Request(request, MachineBriefTestData.Now,
            isOverviewVisible: true);
        var brief = Brief(decision.Fingerprint);
        policy.Complete(decision, brief, MachineBriefTestData.Now);

        var cached = policy.Request(request,
            MachineBriefTestData.Now.AddMinutes(10),
            isOverviewVisible: true);

        Assert.Equal(MachineBriefDecisionKind.UseCached, cached.Kind);
        Assert.Same(brief, cached.CachedBrief);
    }

    [Fact]
    public void TinyTelemetryMovementKeepsFingerprint()
    {
        var original = Request();
        var changedEvidence = MachineBriefTestData.Evidence()
            .Select(item => item.Id == "now.resources"
                ? item with
                {
                    Summary =
                        "Current resource use: CPU 13.6%; memory 43.4%.",
                    DisplayValues = ["13.6%", "43.4%"]
                }
                : item)
            .ToArray();
        var changed = original with
        {
            Situation = MachineBriefTestData.Situation(changedEvidence)
        };

        Assert.Equal(
            MachineBriefCachePolicy.CreateFingerprint(original),
            MachineBriefCachePolicy.CreateFingerprint(changed));
    }

    [Fact]
    public void MaterialFindingMaturityModelAndRuntimeInvalidate()
    {
        var original = Request();
        var materialEvidence = MachineBriefTestData.Evidence()
            .Select(item => item.Id == "recent.reliability"
                ? item with { Importance = MachineSituationImportance.Important }
                : item)
            .ToArray();
        var findingChanged = original with
        {
            Situation = MachineBriefTestData.Situation(materialEvidence)
        };
        var maturityChanged = original with
        {
            Situation = MachineBriefTestData.Situation(
                maturity: MachineLearningConfidence.Established)
        };

        var fingerprint = MachineBriefCachePolicy.CreateFingerprint(original);
        Assert.NotEqual(fingerprint,
            MachineBriefCachePolicy.CreateFingerprint(findingChanged));
        Assert.NotEqual(fingerprint,
            MachineBriefCachePolicy.CreateFingerprint(maturityChanged));
        Assert.NotEqual(fingerprint,
            MachineBriefCachePolicy.CreateFingerprint(
                original with { ModelIdentity = "qwen-next" }));
        Assert.NotEqual(fingerprint,
            MachineBriefCachePolicy.CreateFingerprint(
                original with { RuntimeVersion = "runtime-next" }));
        Assert.NotEqual(fingerprint,
            MachineBriefCachePolicy.CreateFingerprint(
                original, "brief-prompt-next", 1));
        Assert.NotEqual(fingerprint,
            MachineBriefCachePolicy.CreateFingerprint(
                original, MachineBriefPromptPolicy.CurrentVersion, 2));
    }

    [Fact]
    public void HiddenOverviewAndActiveRequestDoNotGenerate()
    {
        var policy = new MachineBriefCachePolicy();
        var request = Request();

        Assert.Equal(MachineBriefDecisionKind.None,
            policy.Request(request, MachineBriefTestData.Now,
                isOverviewVisible: false).Kind);
        var active = policy.Request(request, MachineBriefTestData.Now,
            isOverviewVisible: true);
        Assert.True(active.ShouldGenerate);
        Assert.Equal(MachineBriefDecisionKind.None,
            policy.Request(request, MachineBriefTestData.Now,
                isOverviewVisible: true).Kind);
    }

    private static MachineBriefRequest Request() => new(
        MachineBriefTestData.Situation(), "qwen3.5-4b", "b10724");

    private static MachineBrief Brief(string fingerprint)
    {
        var content = MachineBriefFallbackComposer.Compose(
            MachineBriefTestData.Situation());
        return new(
            content.Overall,
            content.OverallEvidenceIds,
            content.Points,
            content.Outlook,
            content.OutlookEvidenceIds,
            "qwen3.5-4b",
            MachineBriefTestData.Now,
            MachineExplanationSource.DeterministicFallback,
            new(
                MachineBriefValidationState.RejectedFallback,
                "test",
                false,
                1,
                100),
            fingerprint);
    }
}
