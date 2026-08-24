using Machine.App.Features;
using Machine.Core;

namespace Machine.Tests;

public sealed class OverviewTodayStatusPresentationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 25, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CompleteTodayStatusShowsObservedEnergyAndEstimatedCost()
    {
        var presentation = Present(48.602d, 1, 0.72m, Rate());

        Assert.Equal("Running bill today", presentation.Title);
        Assert.Equal("~₱0.72 estimated", presentation.PrimaryText);
        Assert.Equal(
            "0.049 kWh observed PC energy",
            presentation.EnergyText);
        Assert.Contains("Meralco residential reference",
            presentation.EvidenceText);
        Assert.Contains("₱14.7833/kWh", presentation.EvidenceText);
        Assert.Contains("August 2026", presentation.EvidenceText);
    }

    [Fact]
    public void MissingRateNeverClaimsZeroCost()
    {
        var presentation = Present(48.602d, 1, null, null);

        Assert.Equal("Cost unavailable", presentation.PrimaryText);
        Assert.Equal(
            "0.049 kWh observed PC energy",
            presentation.EnergyText);
        Assert.DoesNotContain("₱0.00", presentation.PrimaryText);
        Assert.Contains("reference unavailable",
            presentation.EvidenceText,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoObservedEnergyUsesRestrainedInitializationSemantics()
    {
        var presentation = Present(0d, 0, null, Rate());

        Assert.Equal("Still observing", presentation.PrimaryText);
        Assert.DoesNotContain("0.000 kWh", presentation.EnergyText);
        Assert.DoesNotContain("₱0.00", presentation.PrimaryText);
    }

    [Fact]
    public void TodayStatusCoexistsWithCurrentMachineFinding()
    {
        var today = MachineTodayStatusProjector.Project(CreateToday());
        var finding = MachineInsightCandidateProjector.ProjectMachineFinding(
            new MachineFindingsSnapshot(
                MachineOverallState.Warning,
                [new(
                    "memory.usage.high",
                    MachineFindingSeverity.Warning,
                    "Memory usage is high",
                    "Current memory usage is 92.0%.")]),
            Now)!;
        var selected = new MachineInsightArbiter().Evaluate([finding], Now);

        Assert.True(today.HasObservedEnergy);
        Assert.Equal(finding, selected.CurrentInsight);
    }

    [Fact]
    public void OverviewKeepsTodayFindingsAndLocalInsightAsSiblingCards()
    {
        var path = Directory.GetFiles(
            Path.Combine(AppContext.BaseDirectory, "FeatureViews"),
            "OverviewView.xaml",
            SearchOption.AllDirectories).Single();
        var xaml = File.ReadAllText(path);
        var today = xaml.IndexOf("x:Name=\"TodayStatusCard\"",
            StringComparison.Ordinal);
        var findings = xaml.IndexOf("x:Name=\"CurrentFindingsCard\"",
            StringComparison.Ordinal);
        var insight = xaml.IndexOf("x:Name=\"LocalInsightCard\"",
            StringComparison.Ordinal);

        Assert.True(today >= 0);
        Assert.True(findings > today);
        Assert.True(insight > findings);
        Assert.DoesNotContain(
            "RunningBillInsightPanel",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TodayStatusUpdateCannotCreateNewInsightOrInferenceDependency()
    {
        var first = MachineTodayStatusProjector.Project(CreateToday(0.72m));
        var second = MachineTodayStatusProjector.Project(CreateToday(0.74m));
        var arbiter = new MachineInsightArbiter();
        var candidate = MachineInsightCandidateProjector.
            ProjectLearnedEnergyDeviation(
                CreateLearnedDeviation(),
                Now,
                TimeZoneInfo.Utc)!;
        arbiter.Evaluate([candidate], Now);
        arbiter.MarkCurrentViewed();

        Assert.NotEqual(
            first.EstimatedPcElectricityCost,
            second.EstimatedPcElectricityCost);
        var unchanged = arbiter.Evaluate(
            [candidate with
            {
                CreatedAt = Now.AddMinutes(1),
                ValidUntil = Now.AddMinutes(11)
            }],
            Now.AddMinutes(1));
        Assert.Equal(candidate.Id, unchanged.CurrentInsight?.Id);
        Assert.False(unchanged.HasNewUnseenInsight);
        Assert.DoesNotContain(
            typeof(MachineTodayStatusProjector).GetFields(
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public),
            field => typeof(IMachineStateExplainer).IsAssignableFrom(
                field.FieldType));
        Assert.False(typeof(MachineInsightCandidate).IsAssignableFrom(
            typeof(MachineTodayStatusProjection)));
    }

    private static MachineTodayLearnedEnergyComparison
        CreateLearnedDeviation() => new(
            new DateOnly(2026, 8, 25),
            0.620d,
            TimeSpan.FromMinutes(90),
            TimeSpan.FromMinutes(90),
            1d,
            0.500d,
            0.450d,
            0.550d,
            MachineTodayLearnedEnergyComparisonState.AboveLearnedRange,
            MachineLearningEvidenceMaturity.Established,
            0.120d,
            24d,
            9.16m,
            7.39m,
            6.65m,
            8.13m,
            Rate());

    private static OverviewTodayStatusPresentation Present(
        double wattHours,
        long contributionCount,
        decimal? cost,
        ElectricityRateSnapshot? rate) =>
        OverviewTodayStatusPresenter.Present(
            MachineTodayStatusProjector.Project(new(
                new DateOnly(2026, 8, 25),
                wattHours,
                cost,
                cost is null
                    ? MachineCostCoverage.Unavailable
                    : MachineCostCoverage.Complete,
                TimeSpan.FromHours(2),
                42d,
                50d,
                contributionCount,
                rate)));

    private static MachineTodayEnergyCostProjection CreateToday(
        decimal cost = 0.72m) => new(
        new DateOnly(2026, 8, 25),
        48.602d,
        cost,
        MachineCostCoverage.Complete,
        TimeSpan.FromHours(2),
        42d,
        50d,
        20,
        Rate());

    private static ElectricityRateSnapshot Rate() => new(
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
