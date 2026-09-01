using Machine.Core;

namespace Machine.Tests;

public sealed class MachineSituationProjectorTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        9,
        1,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void FullProjectionIsBoundedCategorizedAndSanitized()
    {
        var rate = CreateRate();
        var today = CreateToday(rate);
        var input = new MachineSituationInput
        {
            Findings = new(MachineOverallState.Attention,
            [
                new("health.application.recurring",
                    MachineFindingSeverity.Attention,
                    "Recurring application failures",
                    "GbtCloudMatrix.exe failed repeatedly.")
            ]),
            Resources = new(12.5d, 32UL * GiB, 14UL * GiB, Now),
            Gpu = new(Now, MachineGpuTelemetryAvailability.Available,
            [
                new(0, "NVIDIA GeForce RTX 3070", "NVIDIA", 22d,
                    2UL * GiB, 8UL * GiB, 25d, 53d, 78d,
                    1800, 7000, 30d)
            ]),
            Storage = new([
                new("C:\\", "Windows", "NTFS", 1L << 40,
                    400L << 30, IsSystemVolume: true)
            ], Now),
            Network = new([
                new("Ethernet 10.20.30.40", null, "Ethernet", "Up",
                    null, null, null, null)
            ], new(1, 100, 200, 1200d, 800d), Now),
            Session = new(TimeSpan.FromHours(4), TimeSpan.FromHours(4),
                MachineUserActivityState.Active, TimeSpan.FromSeconds(3), Now),
            Power = new(Now, 145d, 130d, 160d, 55d, 62d, 28d,
                MachinePowerEstimateConfidence.ModerateEstimate),
            WindowsUpdate = new(Now, Now, true, Now.AddDays(-1),
                Now.AddDays(-2), 0, 0, MachineWindowsUpdateState.UpToDate,
                [], MachineHealthDataStatus.Complete,
                MachineWindowsUpdateRefreshStatus.Verified),
            RebootPending = new(Now, false,
                MachineRebootPendingConfidence.Verified, [], [], false),
            Reliability = CreateReliability(
                applicationName: "GbtCloudMatrix.exe"),
            Learning = CreateLearning(),
            History = CreateHistoryWithPrivateDetail(),
            Today = today,
            TodayComparison = CreateTodayComparison(rate),
            Forecast = CreateForecast(today, rate, available: true),
            Startup = new([
                new("Private startup", "secret --token 123",
                    MachineStartupSource.RegistryRunKey,
                    MachineStartupScope.CurrentUser,
                    MachineStartupRegistryView.Registry64,
                    ActionAvailability:
                        MachineStartupActionAvailability.Supported)
            ], true, 0, Now),
            InferenceStatus = new(false, "llama.cpp", "b10724",
                LocalInferenceModelState.Faulted, [], null, false, Now,
                new(LocalInferenceFailureKind.RuntimeUnavailable,
                    "Pinned runtime unavailable"))
        };

        var snapshot = MachineSituationProjector.Project(input, Now);

        Assert.True(snapshot.Evidence.Count <=
            MachineSituationEvidenceSelector.MaximumEvidenceItemCount);
        Assert.Equal(snapshot.Evidence.Count,
            snapshot.Evidence.Select(item => item.Id).Distinct().Count());
        Assert.All(Enum.GetValues<MachineSituationCategory>(), category =>
            Assert.Contains(snapshot.Evidence,
                item => item.Category == category));
        Assert.Equal(MachineOverallState.Attention,
            snapshot.GlobalPosture);
        Assert.Contains(snapshot.Evidence,
            item => item.Id == "finding.health.application.recurring" &&
                item.EntityNames.Contains("GbtCloudMatrix.exe"));
        var normalized = string.Join('\n', snapshot.Evidence.SelectMany(item =>
            item.DisplayValues.Prepend(item.Summary)
                .Concat(item.EntityNames)));
        Assert.DoesNotContain("10.20.30.40", normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain("secret", normalized,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", normalized,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, snapshot.LearningAwareness.LearnedContextCount);
        Assert.Equal(12,
            snapshot.LearningAwareness.CurrentContextSampleCount);
        Assert.Equal(MachineLearningConfidence.Provisional,
            snapshot.LearningAwareness.CurrentContextMaturity);
    }

    [Fact]
    public void StableMachineStillSuppliesEnoughNormalContext()
    {
        var learning = CreateLearning(sampleCount: 1);
        var snapshot = MachineSituationProjector.Project(new()
        {
            Findings = new(MachineOverallState.Stable, []),
            Resources = new(8d, 32UL * GiB, 12UL * GiB, Now),
            Learning = learning,
            WindowsUpdate = new(Now, Now, true, Now, Now, 0, 0,
                MachineWindowsUpdateState.UpToDate, [],
                MachineHealthDataStatus.Complete,
                MachineWindowsUpdateRefreshStatus.Verified)
        }, Now);

        Assert.Contains(snapshot.Evidence,
            item => item.Id == "now.posture" &&
                item.Summary.Contains("Stable", StringComparison.Ordinal));
        Assert.Contains(snapshot.Evidence,
            item => item.Id == "now.resources");
        Assert.Contains(snapshot.Evidence,
            item => item.Id == "learned.current_context");
        Assert.Contains(snapshot.Evidence,
            item => item.Id == "learning.awareness");
        Assert.DoesNotContain(snapshot.Evidence,
            item => item.Importance >= MachineSituationImportance.Important);
    }

    [Fact]
    public void SelectorPrioritizesProblemsBeforeRoutineCoverage()
    {
        var candidates = Enum.GetValues<MachineSituationCategory>()
            .Select((category, index) => Evidence(
                $"routine.{index}",
                category,
                MachineSituationImportance.Routine))
            .Append(Evidence(
                "finding.critical",
                MachineSituationCategory.Now,
                MachineSituationImportance.Critical))
            .Concat(Enumerable.Range(0, 30).Select(index => Evidence(
                $"extra.{index}",
                MachineSituationCategory.Now,
                MachineSituationImportance.Context)))
            .ToArray();

        var selected = MachineSituationEvidenceSelector.Select(
            candidates,
            maximumItemCount: 4);

        Assert.Equal(4, selected.Count);
        Assert.Equal("finding.critical", selected[0].Id);
        Assert.Contains(selected, item =>
            item.Importance == MachineSituationImportance.Critical);
    }

    [Fact]
    public void OwnedRuntimeIncidentRemainsSelfHealthOnly()
    {
        var snapshot = MachineSituationProjector.Project(new()
        {
            Findings = new(MachineOverallState.Stable, []),
            Learning = CreateLearning(),
            Reliability = CreateReliability(
                MatasuriRuntimeIdentityPolicy.ExecutableName)
        }, Now);

        Assert.Equal(MachineOverallState.Stable, snapshot.GlobalPosture);
        Assert.Contains(snapshot.Evidence, item =>
            item.Id == "self_health.latest_runtime_incident" &&
            item.Category == MachineSituationCategory.SelfHealth);
        Assert.DoesNotContain(snapshot.Evidence,
            item => item.Id == "recent.incident.latest");
        Assert.DoesNotContain(snapshot.Evidence,
            item => item.Category != MachineSituationCategory.SelfHealth &&
                item.EntityNames.Contains(
                MatasuriRuntimeIdentityPolicy.ExecutableName));
    }

    [Fact]
    public void UnavailableForecastIsExplainedButNotFabricated()
    {
        var rate = CreateRate();
        var today = CreateToday(rate);
        var snapshot = MachineSituationProjector.Project(new()
        {
            Findings = new(MachineOverallState.Stable, []),
            Learning = CreateLearning(),
            Today = today,
            Forecast = CreateForecast(today, rate, available: false)
        }, Now);

        var unavailable = Assert.Single(snapshot.Evidence,
            item => item.Id == "forward.unavailable");
        Assert.Equal(MachineSituationCategory.Forward,
            unavailable.Category);
        Assert.True(unavailable.AllowsCausalLanguage);
        Assert.Contains("Missing Future Power Evidence",
            unavailable.Summary,
            StringComparison.Ordinal);
        Assert.Equal(
            MachineUsageForecastAvailabilityReason.MissingFuturePowerEvidence,
            snapshot.LearningAwareness.ForecastAvailability);
        var awareness = Assert.Single(snapshot.Evidence,
            item => item.Id == "learning.awareness");
        Assert.Contains("Missing Future Power Evidence",
            awareness.Summary,
            StringComparison.Ordinal);
    }

    private static MachineSituationEvidenceItem Evidence(
        string id,
        MachineSituationCategory category,
        MachineSituationImportance importance) => new(
        id,
        category,
        MachineSituationTimeScope.Current,
        importance,
        MachineSituationFreshness.Current,
        MachineSituationEvidenceMaturity.Verified,
        id,
        [id],
        []);

    private static MachineLearningDashboardSnapshot CreateLearning(
        int sampleCount = MachineLearningService.ProvisionalSampleCount)
    {
        var start = Now.AddMinutes(-10);
        var service = new MachineLearningService(start);
        for (var index = 0; index < sampleCount; index++)
        {
            Assert.True(service.Observe(new(
                start.AddSeconds(index * 30),
                10d + index / 10d,
                42d,
                MachineUserActivityState.Active,
                MachineOverallState.Stable,
                [],
                40d,
                "Active:Stable",
                MachineNetworkActivityClass.Quiet,
                EstimatedWallPowerWatts: 145d)));
        }
        return service.GetDashboardSnapshot(Now);
    }

    private static ElectricityRateSnapshot CreateRate() => new(
        1,
        "Meralco",
        "PHP",
        14.7833m,
        new DateOnly(2026, 9, 1),
        Now,
        Now.AddDays(30),
        "official",
        MachinePowerEstimateConfidence.HighEstimate,
        MachinePowerEstimateConfidence.HighEstimate);

    private static MachineTodayEnergyCostProjection CreateToday(
        ElectricityRateSnapshot rate) => new(
        new DateOnly(2026, 9, 1),
        1004d,
        14.84m,
        MachineCostCoverage.Complete,
        TimeSpan.FromHours(8),
        125.5d,
        180d,
        960,
        rate);

    private static MachineTodayLearnedEnergyComparison CreateTodayComparison(
        ElectricityRateSnapshot rate) => new(
        new DateOnly(2026, 9, 1),
        1.004d,
        TimeSpan.FromHours(8),
        TimeSpan.FromHours(8),
        1d,
        0.98d,
        0.90d,
        1.08d,
        MachineTodayLearnedEnergyComparisonState.WithinLearnedRange,
        MachineLearningEvidenceMaturity.Provisional,
        0.024d,
        2.45d,
        14.84m,
        14.49m,
        13.30m,
        15.97m,
        rate);

    private static MachineUsageForecast CreateForecast(
        MachineTodayEnergyCostProjection today,
        ElectricityRateSnapshot rate,
        bool available)
    {
        var comparison = CreateTodayComparison(rate);
        return new(
            CapturedAt: Now,
            CurrentContext: new(12, MachineUserActivityState.Active),
            CurrentContextMaturity: MachineLearningConfidence.Provisional,
            CurrentPowerMaturity: MachineLearningEvidenceMaturity.Provisional,
            CurrentHourUsage: null,
            TypicalPowerWatts: available ? 145d : null,
            TypicalPowerLowerWatts: available ? 135d : null,
            TypicalPowerUpperWatts: available ? 155d : null,
            NextObservedHourEnergyKilowattHours: available ? 0.145d : null,
            NextObservedHourEnergyLowerKilowattHours:
                available ? 0.135d : null,
            NextObservedHourEnergyUpperKilowattHours:
                available ? 0.155d : null,
            NextObservedHourEstimatedCost: available ? 2.14m : null,
            NextObservedHourEstimatedCostLower: available ? 2.00m : null,
            NextObservedHourEstimatedCostUpper: available ? 2.29m : null,
            Today: comparison,
            RemainingDayExpectedObservedDuration: available
                ? TimeSpan.FromHours(2)
                : TimeSpan.Zero,
            RemainingDayExpectedEnergyKilowattHours:
                available ? 0.29d : null,
            RemainingDayLowerKilowattHours: available ? 0.27d : null,
            RemainingDayUpperKilowattHours: available ? 0.31d : null,
            ProjectedEndOfDayObservedEnergyKilowattHours:
                available ? 1.294d : null,
            ProjectedEndOfDayLowerKilowattHours:
                available ? 1.274d : null,
            ProjectedEndOfDayUpperKilowattHours:
                available ? 1.314d : null,
            ProjectedEndOfDayEstimatedCost: available ? 19.13m : null,
            ProjectedEndOfDayCostLower: available ? 18.83m : null,
            ProjectedEndOfDayCostUpper: available ? 19.43m : null,
            ForecastMaturity: MachineLearningEvidenceMaturity.Provisional,
            ForecastCoverage: available ? 1d : 0.25d,
            AvailabilityReason: available
                ? MachineUsageForecastAvailabilityReason.Available
                : MachineUsageForecastAvailabilityReason.
                    MissingFuturePowerEvidence,
            RateReference: rate);
    }

    private static MachineHistorySnapshot CreateHistoryWithPrivateDetail()
    {
        var rollup = new MachineHistoryRollup(
            Now.AddHours(-1),
            Now,
            TimeSpan.FromHours(1).Ticks,
            null,
            null,
            null,
            null,
            null,
            new(0, 0, 0, 0, 0),
            new(TimeSpan.FromMinutes(45).Ticks,
                TimeSpan.FromMinutes(15).Ticks));
        return new(
            MachineHistoryRange.Last24Hours,
            MachineHistoryResolution.Hour,
            [rollup],
            [new(Now, MachineHistoryEventKind.ReliabilityIncidentRecorded,
                "Private", "10.20.30.40 secret token", "test", "private")],
            rollup.ObservedDurationTicks,
            rollup.BucketStart,
            rollup.BucketEnd,
            Now,
            false,
            MachineHistoryDataStatus.Healthy);
    }

    private static MachineReliabilitySnapshot CreateReliability(
        string applicationName)
    {
        var incident = new MachineReliabilityIncident(
            Now.AddHours(-1),
            MachineReliabilityIncidentCategory.ApplicationCrash,
            MachineReliabilityIncidentSeverity.Significant,
            "Application Error",
            applicationName,
            null,
            null,
            1000,
            "app-crash");
        var sevenDays = new MachineReliabilityWindowSummary(
            1, 0, 0, 0, 0, 0);
        var summary = new MachineReliabilitySummary(
            sevenDays,
            sevenDays,
            sevenDays,
            incident,
            [new(applicationName, 1, 1, incident.OccurredAt)]);
        return new(
            Now,
            Now,
            Now.AddDays(-30),
            MachineHealthDataStatus.Complete,
            0,
            [incident],
            summary,
            null,
            null);
    }

    private const ulong GiB = 1024UL * 1024UL * 1024UL;
}
