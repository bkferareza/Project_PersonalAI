using Machine.Core;

namespace Machine.Tests;

public sealed class MachineUsageOutlookCachePolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AmbientOrHiddenOverviewCannotAuthorizeGeneration()
    {
        var policy = new MachineUsageOutlookCachePolicy();

        var decision = policy.Request(
            Request(),
            Now,
            isOverviewVisible: false);

        Assert.Equal(MachineUsageOutlookDecisionKind.None,
            decision.Kind);
        Assert.False(policy.IsRequestInFlight);
    }

    [Fact]
    public void FirstVisibleRequestGeneratesAndFreshMatchUsesCache()
    {
        var policy = new MachineUsageOutlookCachePolicy();
        var request = Request();
        var first = policy.Request(request, Now, isOverviewVisible: true);

        Assert.True(first.ShouldGenerate);
        Assert.True(policy.IsRequestInFlight);
        var outlook = Outlook(Now.AddSeconds(3));
        policy.Complete(first, outlook, Now.AddSeconds(3));

        var cached = policy.Request(
            request,
            Now.AddMinutes(30),
            isOverviewVisible: true);
        Assert.Equal(MachineUsageOutlookDecisionKind.UseCached,
            cached.Kind);
        Assert.Equal(outlook, cached.CachedOutlook);
        Assert.False(policy.IsRequestInFlight);
    }

    [Fact]
    public void TtlExpirationAuthorizesFreshGeneration()
    {
        var policy = SeedCache();

        var decision = policy.Request(
            Request(),
            Now + MachineUsageOutlookCachePolicy.CacheTimeToLive,
            isOverviewVisible: true);

        Assert.True(decision.ShouldGenerate);
    }

    [Fact]
    public void TinyNumericMovementKeepsMaterialFingerprintStable()
    {
        var original = Request();
        var changedForecast = original.Forecast with
        {
            TypicalPowerWatts = 151d,
            NextObservedHourEnergyKilowattHours = 0.151d,
            NextObservedHourEstimatedCost = 2.25m,
            Today = original.Forecast.Today with
            {
                ActualObservedEnergyKilowattHours = 0.502d,
                ActualEstimatedCost = 7.42m
            },
            ProjectedEndOfDayObservedEnergyKilowattHours = 0.802d,
            ProjectedEndOfDayEstimatedCost = 11.86m
        };

        Assert.Equal(
            MachineUsageOutlookCachePolicy.CreateFingerprint(original),
            MachineUsageOutlookCachePolicy.CreateFingerprint(
                original with { Forecast = changedForecast }));
    }

    [Fact]
    public void SemanticComparisonChangeInvalidatesCache()
    {
        var original = Request();
        var changed = original with
        {
            Forecast = original.Forecast with
            {
                Today = original.Forecast.Today with
                {
                    ComparisonState = MachineTodayLearnedEnergyComparisonState
                        .AboveLearnedRange
                }
            }
        };

        Assert.NotEqual(
            MachineUsageOutlookCachePolicy.CreateFingerprint(original),
            MachineUsageOutlookCachePolicy.CreateFingerprint(changed));
    }

    [Fact]
    public void PromptPolicyVersionChangesOnlyGeneratedProseFingerprint()
    {
        var request = Request();
        var forecast = request.Forecast;

        var legacyFingerprint =
            MachineUsageOutlookCachePolicy.CreateFingerprint(
                request,
                "taglish-v1");
        var currentFingerprint =
            MachineUsageOutlookCachePolicy.CreateFingerprint(
                request,
                MachineUsageOutlookPromptPolicy.CurrentVersion);

        Assert.NotEqual(legacyFingerprint, currentFingerprint);
        Assert.Same(forecast, request.Forecast);
        Assert.Equal(0.800d,
            request.Forecast.ProjectedEndOfDayObservedEnergyKilowattHours);
        Assert.Equal(MachineUsageForecastAvailabilityReason.Available,
            request.Forecast.AvailabilityReason);
    }

    [Fact]
    public void ConversationalEndOfDayRequiresCompleteAvailability()
    {
        var forecast = Request().Forecast;

        Assert.True(
            MachineUsageOutlookPromptPolicy.CanExposeEndOfDayProjection(
                forecast));
        Assert.False(
            MachineUsageOutlookPromptPolicy.CanExposeEndOfDayProjection(
                forecast with
                {
                    ForecastCoverage = 0.04d,
                    AvailabilityReason =
                        MachineUsageForecastAvailabilityReason
                            .PartialFutureCoverage
                }));
        Assert.True(forecast.HasEndOfDayForecast);
    }

    [Fact]
    public void ManualRefreshBypassesFreshCache()
    {
        var policy = SeedCache();

        var decision = policy.Request(
            Request(),
            Now.AddMinutes(5),
            isOverviewVisible: true,
            forceRefresh: true);

        Assert.True(decision.ShouldGenerate);
    }

    [Fact]
    public void FailureBackoffPreventsRepeatedVisibleRetries()
    {
        var policy = new MachineUsageOutlookCachePolicy();
        var request = Request();
        var first = policy.Request(request, Now, isOverviewVisible: true);
        policy.Complete(first, null, Now.AddSeconds(1));

        Assert.Equal(MachineUsageOutlookDecisionKind.None,
            policy.Request(
                request,
                Now.AddMinutes(4),
                isOverviewVisible: true).Kind);
        Assert.True(policy.Request(
            request,
            Now.AddMinutes(6),
            isOverviewVisible: true).ShouldGenerate);
    }

    [Fact]
    public void OutlookHasNoPostureInsightLearningOrActionAuthority()
    {
        Assert.False(typeof(MachineInsightCandidate).IsAssignableFrom(
            typeof(MachineUsageOutlook)));
        Assert.DoesNotContain(
            typeof(MachineActionCoordinator).GetConstructors()
                .SelectMany(constructor => constructor.GetParameters()),
            parameter => parameter.ParameterType ==
                typeof(IMachineUsageOutlookGenerator));
        Assert.DoesNotContain(
            typeof(MachineLearningService).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic),
            field => field.FieldType ==
                typeof(IMachineUsageOutlookGenerator));
        Assert.DoesNotContain(
            typeof(MachineFindingsEvaluator).GetFields(
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic),
            field => field.FieldType ==
                typeof(IMachineUsageOutlookGenerator));
    }

    private static MachineUsageOutlookCachePolicy SeedCache()
    {
        var policy = new MachineUsageOutlookCachePolicy();
        var decision = policy.Request(
            Request(),
            Now,
            isOverviewVisible: true);
        policy.Complete(decision, Outlook(Now), Now);
        return policy;
    }

    private static MachineUsageOutlookRequest Request()
    {
        var rate = new ElectricityRateSnapshot(
            1,
            "Meralco",
            "PHP",
            14.7833m,
            new DateOnly(2026, 8, 1),
            Now,
            Now.AddDays(30),
            "official-test",
            MachinePowerEstimateConfidence.HighEstimate,
            MachinePowerEstimateConfidence.HighEstimate);
        var today = new MachineTodayLearnedEnergyComparison(
            new DateOnly(2026, 8, 28),
            0.500d,
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(4),
            1d,
            0.520d,
            0.450d,
            0.600d,
            MachineTodayLearnedEnergyComparisonState.WithinLearnedRange,
            MachineLearningEvidenceMaturity.Provisional,
            -0.020d,
            -3.85d,
            7.39m,
            7.69m,
            6.65m,
            8.87m,
            rate);
        var usage = new MachineLearnedHourlyUsageProfile(
            22,
            0.7d,
            0.3d,
            TimeSpan.FromMinutes(42),
            TimeSpan.FromMinutes(18),
            TimeSpan.FromHours(1),
            7,
            4,
            1d,
            MachineLearningEvidenceMaturity.Provisional);
        var forecast = new MachineUsageForecast(
            CapturedAt: Now,
            CurrentContext: new(22, MachineUserActivityState.Active),
            CurrentContextMaturity: MachineLearningConfidence.Provisional,
            CurrentPowerMaturity: MachineLearningEvidenceMaturity.Provisional,
            CurrentHourUsage: usage,
            TypicalPowerWatts: 150d,
            TypicalPowerLowerWatts: 140d,
            TypicalPowerUpperWatts: 160d,
            NextObservedHourEnergyKilowattHours: 0.150d,
            NextObservedHourEnergyLowerKilowattHours: 0.140d,
            NextObservedHourEnergyUpperKilowattHours: 0.160d,
            NextObservedHourEstimatedCost: 2.22m,
            NextObservedHourEstimatedCostLower: 2.07m,
            NextObservedHourEstimatedCostUpper: 2.37m,
            Today: today,
            RemainingDayExpectedObservedDuration: TimeSpan.FromHours(2),
            RemainingDayExpectedEnergyKilowattHours: 0.300d,
            RemainingDayLowerKilowattHours: 0.280d,
            RemainingDayUpperKilowattHours: 0.320d,
            ProjectedEndOfDayObservedEnergyKilowattHours: 0.800d,
            ProjectedEndOfDayLowerKilowattHours: 0.780d,
            ProjectedEndOfDayUpperKilowattHours: 0.820d,
            ProjectedEndOfDayEstimatedCost: 11.83m,
            ProjectedEndOfDayCostLower: 11.53m,
            ProjectedEndOfDayCostUpper: 12.12m,
            ForecastMaturity: MachineLearningEvidenceMaturity.Provisional,
            ForecastCoverage: 1d,
            AvailabilityReason:
                MachineUsageForecastAvailabilityReason.Available,
            RateReference: rate);
        return new(
            forecast,
            MachineLearningMemoryState.Active,
            240,
            4,
            33,
            0,
            []);
    }

    private static MachineUsageOutlook Outlook(DateTimeOffset at) => new(
        "The next observed hour is projected at 0.150 kWh.",
        "qwen3.5:4b",
        at,
        MachineExplanationSource.LocalModel);
}
