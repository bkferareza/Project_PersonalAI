using Machine.Core;

namespace Machine.Tests;

public sealed class MachineInsightArbiterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 25, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProvisionalLearnedEvidenceProducesNoCandidate()
    {
        var comparison = CreateComparison(
            MachineTodayLearnedEnergyComparisonState.AboveLearnedRange,
            maturity: MachineLearningEvidenceMaturity.Provisional);

        Assert.Null(Project(comparison));
    }

    [Fact]
    public void PartialLearnedCoverageProducesNoCandidate()
    {
        var comparison = CreateComparison(
            MachineTodayLearnedEnergyComparisonState.AboveLearnedRange,
            coveredDuration: TimeSpan.FromMinutes(75));

        Assert.Null(Project(comparison));
    }

    [Fact]
    public void TinyDeviationBeyondRangeProducesNoCandidate()
    {
        var comparison = CreateComparison(
            MachineTodayLearnedEnergyComparisonState.AboveLearnedRange,
            actual: 0.556d);

        Assert.Null(Project(comparison));
    }

    [Fact]
    public void EstablishedCompleteAboveComparisonProducesCandidate()
    {
        var candidate = Project(CreateComparison(
            MachineTodayLearnedEnergyComparisonState.AboveLearnedRange));

        Assert.NotNull(candidate);
        Assert.Equal(
            MachineInsightCandidateProjector.LearnedEnergyAboveId,
            candidate.Id);
        Assert.Equal("Running heavier than usual", candidate.Title);
        Assert.Equal(MachineInsightImportance.Notable, candidate.Importance);
        Assert.Equal(
            MachineLearningEvidenceMaturity.Established,
            candidate.EvidenceMaturity);
        Assert.Contains("0.620 kWh", candidate.PrimaryText);
        Assert.Contains("0.450–0.550 kWh", candidate.SecondaryText);
    }

    [Fact]
    public void EstablishedCompleteBelowComparisonProducesCandidate()
    {
        var candidate = Project(CreateComparison(
            MachineTodayLearnedEnergyComparisonState.BelowLearnedRange,
            actual: 0.370d));

        Assert.NotNull(candidate);
        Assert.Equal(
            MachineInsightCandidateProjector.LearnedEnergyBelowId,
            candidate.Id);
        Assert.Equal("Running lighter than usual", candidate.Title);
    }

    [Fact]
    public void WithinLearnedRangeProducesNoDeviationCandidate()
    {
        Assert.Null(Project(CreateComparison(
            MachineTodayLearnedEnergyComparisonState.WithinLearnedRange,
            actual: 0.500d)));
    }

    [Fact]
    public void SameCandidateUpdatesValuesWithoutBecomingNewAgain()
    {
        var arbiter = new MachineInsightArbiter();
        var first = Project(CreateComparison(
            MachineTodayLearnedEnergyComparisonState.AboveLearnedRange))!;
        var initial = arbiter.Evaluate([first], Now);
        arbiter.MarkCurrentViewed();
        var updated = Project(CreateComparison(
            MachineTodayLearnedEnergyComparisonState.AboveLearnedRange,
            actual: 0.650d), Now.AddMinutes(1))!;

        var second = arbiter.Evaluate([updated], Now.AddMinutes(1));

        Assert.True(initial.HasNewUnseenInsight);
        Assert.Equal(first.Id, second.CurrentInsight?.Id);
        Assert.Contains("0.650 kWh", second.CurrentInsight?.PrimaryText);
        Assert.False(second.HasNewUnseenInsight);
    }

    [Fact]
    public void CooldownSuppressesSameDirectionAfterEligibilityGap()
    {
        var arbiter = new MachineInsightArbiter();
        var above = Project(CreateComparison(
            MachineTodayLearnedEnergyComparisonState.AboveLearnedRange))!;
        arbiter.Evaluate([above], Now);
        arbiter.MarkCurrentViewed();
        arbiter.Evaluate([], Now.AddMinutes(1));

        var repeated = arbiter.Evaluate(
            [above with
            {
                CreatedAt = Now.AddHours(1),
                ValidUntil = Now.AddHours(1) +
                    MachineInsightCandidateProjector.CandidateFreshness
            }],
            Now.AddHours(1));

        Assert.Equal(above.Id, repeated.CurrentInsight?.Id);
        Assert.False(repeated.HasNewUnseenInsight);
    }

    [Fact]
    public void MeaningfulDirectionTransitionCanBecomeNew()
    {
        var arbiter = new MachineInsightArbiter();
        var above = Project(CreateComparison(
            MachineTodayLearnedEnergyComparisonState.AboveLearnedRange))!;
        arbiter.Evaluate([above], Now);
        arbiter.MarkCurrentViewed();
        arbiter.Evaluate([], Now.AddMinutes(1));
        var below = Project(CreateComparison(
            MachineTodayLearnedEnergyComparisonState.BelowLearnedRange,
            actual: 0.370d), Now.AddHours(1))!;

        var transitioned = arbiter.Evaluate([below], Now.AddHours(1));

        Assert.Equal(
            MachineInsightCandidateProjector.LearnedEnergyBelowId,
            transitioned.CurrentInsight?.Id);
        Assert.True(transitioned.HasNewUnseenInsight);
    }

    [Fact]
    public void RunningBillUpdatesNeverBecomeNew()
    {
        var arbiter = new MachineInsightArbiter();
        var first = CreateRunningBill(Now, 1.25m);
        var second = CreateRunningBill(Now.AddMinutes(1), 1.27m);

        var initial = arbiter.Evaluate([first], Now);
        var updated = arbiter.Evaluate([second], Now.AddMinutes(1));

        Assert.Equal(
            MachineInsightCandidateProjector.RunningBillId,
            updated.CurrentInsight?.Id);
        Assert.False(initial.HasNewUnseenInsight);
        Assert.False(updated.HasNewUnseenInsight);
    }

    [Fact]
    public void CurrentMachineFindingOutranksLearnedDeviationAndRunningBill()
    {
        var arbiter = new MachineInsightArbiter();
        var finding = MachineInsightCandidateProjector.ProjectMachineFinding(
            new MachineFindingsSnapshot(
                MachineOverallState.Warning,
                [new(
                    "memory.usage.high",
                    MachineFindingSeverity.Warning,
                    "Memory usage is high",
                    "Current memory usage is 92.0%.")]),
            Now)!;

        var selected = arbiter.Evaluate(
            [
                CreateRunningBill(Now, 1.25m),
                Project(CreateComparison(
                    MachineTodayLearnedEnergyComparisonState.
                        AboveLearnedRange))!,
                finding
            ],
            Now);

        Assert.Equal(MachineInsightKind.MachineFinding,
            selected.CurrentInsight?.Kind);
        Assert.Equal("Memory usage is high", selected.CurrentInsight?.Title);
    }

    [Fact]
    public void CandidateFirstSurfacedAfterHigherPriorityClearsIsNewOnce()
    {
        var arbiter = new MachineInsightArbiter();
        var deviation = Project(CreateComparison(
            MachineTodayLearnedEnergyComparisonState.AboveLearnedRange))!;
        var finding = MachineInsightCandidateProjector.ProjectMachineFinding(
            new MachineFindingsSnapshot(
                MachineOverallState.Warning,
                [new(
                    "memory.usage.high",
                    MachineFindingSeverity.Warning,
                    "Memory usage is high",
                    "Current memory usage is 92.0%.")]),
            Now)!;

        var initial = arbiter.Evaluate([deviation, finding], Now);
        arbiter.MarkCurrentViewed();
        var surfaced = arbiter.Evaluate(
            [deviation with
            {
                CreatedAt = Now.AddMinutes(1),
                ValidUntil = Now.AddMinutes(11)
            }],
            Now.AddMinutes(1));
        arbiter.MarkCurrentViewed();
        var update = arbiter.Evaluate(
            [deviation with
            {
                CreatedAt = Now.AddMinutes(2),
                ValidUntil = Now.AddMinutes(12)
            }],
            Now.AddMinutes(2));

        Assert.Equal(finding.Id, initial.CurrentInsight?.Id);
        Assert.Equal(deviation.Id, surfaced.CurrentInsight?.Id);
        Assert.True(surfaced.HasNewUnseenInsight);
        Assert.False(update.HasNewUnseenInsight);
        Assert.Equal(deviation.Id, update.CurrentInsight?.Id);
    }

    [Fact]
    public void ViewingCurrentInsightClearsUnseenState()
    {
        var arbiter = new MachineInsightArbiter();
        var candidate = Project(CreateComparison(
            MachineTodayLearnedEnergyComparisonState.AboveLearnedRange))!;
        Assert.True(arbiter.Evaluate([candidate], Now).HasNewUnseenInsight);

        var viewed = arbiter.MarkCurrentViewed();

        Assert.False(viewed.HasNewUnseenInsight);
        Assert.Equal(candidate.Id, viewed.CurrentInsight?.Id);
    }

    [Fact]
    public void ArbitrationHasNoInferenceDependency()
    {
        Assert.DoesNotContain(
            typeof(MachineInsightArbiter).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic),
            field => typeof(IMachineStateExplainer).IsAssignableFrom(
                field.FieldType));
    }

    private static MachineInsightCandidate? Project(
        MachineTodayLearnedEnergyComparison comparison,
        DateTimeOffset? now = null) =>
        MachineInsightCandidateProjector.ProjectLearnedEnergyDeviation(
            comparison,
            now ?? Now,
            TimeZoneInfo.Utc);

    private static MachineTodayLearnedEnergyComparison CreateComparison(
        MachineTodayLearnedEnergyComparisonState state,
        MachineLearningEvidenceMaturity maturity =
            MachineLearningEvidenceMaturity.Established,
        TimeSpan? coveredDuration = null,
        double actual = 0.620d)
    {
        var duration = TimeSpan.FromMinutes(90);
        var covered = coveredDuration ?? duration;
        return new(
            new DateOnly(2026, 8, 25),
            actual,
            duration,
            covered,
            covered.TotalSeconds / duration.TotalSeconds,
            0.500d,
            0.450d,
            0.550d,
            state,
            maturity,
            actual - 0.500d,
            (actual - 0.500d) / 0.500d * 100d,
            9.16m,
            7.39m,
            6.65m,
            8.13m,
            CreateRate());
    }

    private static MachineInsightCandidate CreateRunningBill(
        DateTimeOffset now,
        decimal cost)
    {
        var today = new MachineTodayEnergyCostProjection(
            new DateOnly(2026, 8, 25),
            84.55d,
            cost,
            MachineCostCoverage.Complete,
            TimeSpan.FromHours(2),
            42d,
            50d,
            20,
            CreateRate());
        return MachineInsightCandidateProjector.ProjectRunningBill(
            today,
            now)!;
    }

    private static ElectricityRateSnapshot CreateRate() => new(
        1,
        "Meralco",
        "PHP",
        14.7833m,
        new DateOnly(2026, 8, 1),
        Now,
        Now.AddDays(30),
        "test",
        MachinePowerEstimateConfidence.ModerateEstimate,
        MachinePowerEstimateConfidence.ModerateEstimate);
}
