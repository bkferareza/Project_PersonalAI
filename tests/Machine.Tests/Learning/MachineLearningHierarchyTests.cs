using System.Text.Json;
using Machine.Core;

namespace Machine.Tests;

public sealed class MachineLearningHierarchyTests
{
    [Fact]
    public void AdaptiveStatisticsInitializeAndUseTwentyOneDayHalfLife()
    {
        var service = new MachineLearningService();
        var start = CreateLocalTime(2026, 1, 1, 3);

        Assert.True(service.Observe(CreateObservation(
            start,
            cpu: 0,
            memory: 100)));
        var initial = Assert.Single(service.Baselines);
        Assert.Equal(0, initial.AdaptiveCpuMean);
        Assert.Equal(100, initial.AdaptiveMemoryMean);
        Assert.Equal(0, initial.AdaptiveCpuStandardDeviation);

        Assert.True(service.Observe(CreateObservation(
            start.Add(MachineLearningPolicy.AdaptiveHalfLife),
            cpu: 100,
            memory: 0)));
        var adapted = Assert.Single(service.Baselines);

        Assert.Equal(50, adapted.AdaptiveCpuMean, 6);
        Assert.Equal(50, adapted.AdaptiveCpuStandardDeviation, 6);
        Assert.Equal(50, adapted.AdaptiveMemoryMean, 6);
        Assert.Equal(50, adapted.AdaptiveMemoryStandardDeviation, 6);
        Assert.Equal(50, adapted.CpuMean, 6);
        Assert.Equal(Math.Sqrt(5_000), adapted.CpuStandardDeviation, 6);
    }

    [Fact]
    public void IdenticalAdaptiveValuesRemainStable()
    {
        var service = new MachineLearningService();
        var start = CreateLocalTime(2026, 1, 1, 3);

        for (var day = 0; day < 10; day++)
        {
            service.Observe(CreateObservation(
                start.AddDays(day),
                cpu: 23.5,
                memory: 61.25));
        }

        var baseline = Assert.Single(service.Baselines);
        Assert.Equal(23.5, baseline.AdaptiveCpuMean, 8);
        Assert.Equal(0, baseline.AdaptiveCpuStandardDeviation, 8);
        Assert.Equal(61.25, baseline.AdaptiveMemoryMean, 8);
        Assert.Equal(0, baseline.AdaptiveMemoryStandardDeviation, 8);
    }

    [Fact]
    public async Task RecentShiftMovesAdaptiveProfileWithoutDeletingLifetimeHistory()
    {
        var start = CreateLocalTime(2026, 1, 1, 3);
        var baselineState = CreateBaselineState(
            start,
            sampleCount: 1_000,
            observedDayCount: 30,
            cpuMean: 10,
            memoryMean: 40,
            adaptiveCpuMean: 10,
            adaptiveMemoryMean: 40);
        var state = CreateState(
            baselineState,
            observationCount: 1_000,
            sessionCount: 4);
        var service = new MachineLearningService(
            start.Add(MachineLearningPolicy.AdaptiveHalfLife));
        await service.LoadAsync(new RecordingStore(state));

        service.Observe(CreateObservation(
            start.Add(MachineLearningPolicy.AdaptiveHalfLife),
            cpu: 90,
            memory: 80));

        var baseline = Assert.Single(service.Baselines);
        Assert.InRange(baseline.CpuMean, 10.07, 10.09);
        Assert.Equal(50, baseline.AdaptiveCpuMean, 6);
        Assert.InRange(baseline.MemoryMean, 40.03, 40.05);
        Assert.Equal(60, baseline.AdaptiveMemoryMean, 6);
        Assert.Equal(1_001, baseline.SampleCount);
        Assert.Equal(MachineLearningConfidence.Established,
            baseline.Confidence);
    }

    [Fact]
    public void TypicalRangesRequireEvidenceAndClampPercentages()
    {
        Assert.Null(MachineLearningPolicy.CreateTypicalRange(50, 5, 1));

        var low = MachineLearningPolicy.CreateTypicalRange(5, 10, 2)!;
        Assert.Equal(0, low.Low);
        Assert.Equal(25, low.High);

        var high = MachineLearningPolicy.CreateTypicalRange(98, 5, 2)!;
        Assert.Equal(88, high.Low);
        Assert.Equal(100, high.High);

        var zeroVariance = MachineLearningPolicy.CreateTypicalRange(42, 0, 2)!;
        Assert.Equal(42, zeroVariance.Low);
        Assert.Equal(42, zeroVariance.High);
    }

    [Fact]
    public void FreshnessBoundariesAreExactAndIndependentOfConfidence()
    {
        var last = new DateTimeOffset(2026, 1, 1, 0, 0, 0,
            TimeSpan.Zero);

        Assert.Equal(MachineLearningFreshness.Fresh,
            MachineLearningPolicy.GetFreshness(
                last,
                last.AddDays(7)));
        Assert.Equal(MachineLearningFreshness.Aging,
            MachineLearningPolicy.GetFreshness(
                last,
                last.AddDays(7).AddTicks(1)));
        Assert.Equal(MachineLearningFreshness.Aging,
            MachineLearningPolicy.GetFreshness(
                last,
                last.AddDays(30)));
        Assert.Equal(MachineLearningFreshness.Stale,
            MachineLearningPolicy.GetFreshness(
                last,
                last.AddDays(30).AddTicks(1)));

        var provisional = CreateProfile(
            3,
            confidence: MachineLearningConfidence.Provisional,
            freshness: MachineLearningFreshness.Fresh);
        var establishedStale = CreateProfile(
            4,
            confidence: MachineLearningConfidence.Established,
            freshness: MachineLearningFreshness.Stale);
        Assert.Equal(MachineLearningConfidence.Provisional,
            provisional.Confidence);
        Assert.Equal(MachineLearningFreshness.Fresh,
            provisional.Freshness);
        Assert.Equal(MachineLearningConfidence.Established,
            establishedStale.Confidence);
        Assert.Equal(MachineLearningFreshness.Stale,
            establishedStale.Freshness);
    }

    [Fact]
    public async Task OfflineGapCreatesNoSamplesAndStaleProfileBecomesFresh()
    {
        var start = CreateLocalTime(2026, 1, 1, 3);
        var store = new RecordingStore(null);
        var first = new MachineLearningService(start);
        for (var sample = 0; sample < 12; sample++)
        {
            first.Observe(CreateObservation(
                start.AddSeconds(sample * 30),
                cpu: 20));
        }
        await first.SaveFinalSnapshotAsync(store, start.AddMinutes(6));

        var restartAt = start.AddDays(60);
        var restored = new MachineLearningService(restartAt);
        await restored.LoadAsync(new RecordingStore(store.SavedState));
        var stale = restored.GetDashboardSnapshot(restartAt);

        Assert.Equal(12, stale.ObservationCount);
        Assert.Equal(TimeSpan.FromMinutes(6), stale.ObservedDuration);
        Assert.Empty(restored.Journal);
        Assert.Equal(MachineLearningFreshness.Stale,
            Assert.Single(stale.ContextProfiles).Freshness);

        restored.Observe(CreateObservation(restartAt, cpu: 80));
        var reinforced = restored.GetDashboardSnapshot(restartAt);
        Assert.Equal(13, reinforced.ObservationCount);
        Assert.Equal(TimeSpan.FromMinutes(6.5), reinforced.ObservedDuration);
        Assert.Equal(MachineLearningFreshness.Fresh,
            Assert.Single(reinforced.ContextProfiles).Freshness);
        Assert.True(Assert.Single(reinforced.Baselines).AdaptiveCpuMean > 20);
    }

    [Fact]
    public void ProfilesMaterializeOnceAndOnlyAfterProvisionalEvidence()
    {
        var service = new MachineLearningService();
        var start = CreateLocalTime(2026, 1, 1, 3);

        for (var sample = 0; sample < 11; sample++)
        {
            service.Observe(CreateObservation(
                start.AddSeconds(sample * 30)));
        }
        Assert.Empty(service.ContextProfiles);

        service.Observe(CreateObservation(start.AddSeconds(11 * 30)));
        var provisional = Assert.Single(service.ContextProfiles);
        Assert.Equal(MachineLearningConfidence.Provisional,
            provisional.Confidence);

        var established = new MachineLearningService();
        for (var day = 0; day < 7; day++)
        {
            for (var sample = 0; sample < 24; sample++)
            {
                established.Observe(CreateObservation(
                    start.AddDays(day).AddSeconds(sample * 30)));
            }
        }
        var profile = Assert.Single(established.ContextProfiles);
        Assert.Equal(MachineLearningConfidence.Established,
            profile.Confidence);
        Assert.Equal(168, profile.LifetimeSampleCount);
        Assert.Equal(7, profile.DistinctObservedDayCount);
    }

    [Fact]
    public void ProfileIgnoresFloatingNoiseButUpdatesForMaterialAdaptiveShift()
    {
        var service = new MachineLearningService();
        var start = CreateLocalTime(2026, 1, 1, 3);
        for (var sample = 0; sample < 12; sample++)
        {
            service.Observe(CreateObservation(
                start.AddSeconds(sample * 30),
                cpu: 20,
                memory: 50));
        }

        var initial = Assert.Single(service.ContextProfiles);
        service.Observe(CreateObservation(
            start.AddMinutes(6),
            cpu: 20.001,
            memory: 50.001));
        var afterNoise = Assert.Single(service.ContextProfiles);
        Assert.Same(initial, afterNoise);

        service.Observe(CreateObservation(
            start.AddDays(21),
            cpu: 80,
            memory: 80));
        var shifted = Assert.Single(service.ContextProfiles);
        Assert.NotSame(initial, shifted);
        Assert.True(shifted.Cpu.AdaptiveMean > 40);
        Assert.Equal(start.AddDays(21), shifted.LastMateriallyChangedAt);
        Assert.Equal(14, Assert.Single(service.Baselines).SampleCount);
    }

    [Fact]
    public async Task ProfileReinforcementWaitsForThresholdAcrossFinalSave()
    {
        var start = CreateLocalTime(2026, 1, 1, 3);
        var store = new RecordingStore(null);
        var first = new MachineLearningService(start);
        for (var sample = 0; sample < 23; sample++)
        {
            first.Observe(CreateObservation(
                start.AddSeconds(sample * 30),
                cpu: 20,
                memory: 50));
        }

        var beforeShutdown = Assert.Single(first.ContextProfiles);
        Assert.Equal(12, beforeShutdown.LifetimeSampleCount);
        Assert.Equal(start.AddSeconds(11 * 30),
            beforeShutdown.LastReinforcedAt);

        await first.SaveFinalSnapshotAsync(
            store,
            start.AddMinutes(12));
        var persistedProfile = Assert.Single(
            store.SavedState!.ContextProfiles!);
        Assert.Equal(12, persistedProfile.LifetimeSampleCount);

        var restartAt = start.AddMinutes(15);
        var restored = new MachineLearningService(restartAt);
        await restored.LoadAsync(new RecordingStore(store.SavedState));
        Assert.Equal(12,
            Assert.Single(restored.ContextProfiles).LifetimeSampleCount);

        restored.Observe(CreateObservation(
            restartAt,
            cpu: 20,
            memory: 50));
        var reinforced = Assert.Single(restored.ContextProfiles);
        Assert.Equal(24, reinforced.LifetimeSampleCount);
        Assert.Equal(restartAt, reinforced.LastReinforcedAt);
        Assert.Single(restored.ContextProfiles);
    }

    [Fact]
    public void ProfileCarriesOnlyCompactAggregateNetworkEvidence()
    {
        var service = new MachineLearningService();
        var start = CreateLocalTime(2026, 1, 1, 3);
        for (var sample = 0; sample < 12; sample++)
        {
            service.Observe(CreateObservation(
                start.AddSeconds(sample * 30),
                network: sample < 9
                    ? MachineNetworkActivityClass.Light
                    : MachineNetworkActivityClass.Quiet));
        }

        var profile = Assert.Single(service.ContextProfiles);
        Assert.Equal(MachineNetworkActivityClass.Light,
            profile.DominantNetworkActivityClass);
        Assert.Equal(9, profile.DominantNetworkActivityCount);
        Assert.Equal(12, profile.NetworkObservationCount);

        var json = JsonSerializer.Serialize(profile);
        Assert.DoesNotContain("ContextFingerprint", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FindingKeys", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReceiveBytesPerSecond", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Interface", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LearnedItemProjectionUsesAllThreeLearningLayers()
    {
        var now = CreateLocalTime(2026, 1, 10, 3);
        var profile = CreateProfile(3);
        var baseline = new MachineLearningBaseline(
            3,
            MachineUserActivityState.Active,
            200,
            20,
            2,
            50,
            2,
            now.AddDays(-9),
            now,
            9,
            MachineLearningConfidence.Established,
            NetworkLightSampleCount: 150,
            NetworkQuietSampleCount: 50,
            AdaptiveCpuMean: 20,
            AdaptiveCpuStandardDeviation: 2,
            AdaptiveMemoryMean: 50,
            AdaptiveMemoryStandardDeviation: 2,
            AdaptiveSampleCount: 200,
            Freshness: MachineLearningFreshness.Fresh);
        var pattern = MachineRecurringPatternSynthesizer.Synthesize(
            [profile, CreateProfile(4), CreateProfile(5)],
            now).Single();

        var items = MachineLearnedItemProjector.Project(
            [baseline],
            [profile],
            [pattern],
            [],
            CreateObservation(now));

        Assert.Contains(items, item =>
            item.Layer == MachineLearningMemoryLayer.ContextBaseline);
        Assert.Contains(items, item =>
            item.Layer == MachineLearningMemoryLayer.CompactProfile);
        Assert.Contains(items, item =>
            item.Layer == MachineLearningMemoryLayer.BroaderPattern);
        Assert.All(items, item => Assert.DoesNotContain(
            "recommend",
            item.Text,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RangeSimilarityUsesCentralizedOverlapBoundary()
    {
        Assert.True(MachineLearningPolicy.AreRangesCompatible(
            new MachineLearningRange(0, 10),
            new MachineLearningRange(5, 15)));
        Assert.False(MachineLearningPolicy.AreRangesCompatible(
            new MachineLearningRange(0, 10),
            new MachineLearningRange(5.01, 15.01)));
        Assert.True(MachineLearningPolicy.AreRangesCompatible(
            new MachineLearningRange(20, 20),
            new MachineLearningRange(21, 21)));
        Assert.False(MachineLearningPolicy.AreRangesCompatible(
            new MachineLearningRange(20, 20),
            new MachineLearningRange(21.01, 21.01)));
    }

    [Fact]
    public void TwoAdjacentProfilesFormEarlyPatternAndThreeEstablishIt()
    {
        var now = DateTimeOffset.UnixEpoch;
        var two = MachineRecurringPatternSynthesizer.Synthesize(
            [CreateProfile(2), CreateProfile(3)],
            now);
        var early = Assert.Single(two);
        Assert.Equal(MachineLearningConfidence.Provisional,
            early.Confidence);
        Assert.Equal(2, early.MemberContexts.Count);

        var three = MachineRecurringPatternSynthesizer.Synthesize(
            [CreateProfile(2), CreateProfile(3), CreateProfile(4)],
            now);
        var established = Assert.Single(three);
        Assert.Equal(MachineLearningConfidence.Established,
            established.Confidence);
        Assert.Equal(2, established.StartHour);
        Assert.Equal(5, established.EndHourExclusive);
    }

    [Fact]
    public void CpuMemoryAndNetworkIncompatibilitySplitPatterns()
    {
        var compatible = CreateProfile(2);
        var cpuMismatch = CreateProfile(
            3,
            cpuRange: new MachineLearningRange(70, 80));
        Assert.Empty(MachineRecurringPatternSynthesizer.Synthesize(
            [compatible, cpuMismatch],
            DateTimeOffset.UnixEpoch));

        var memoryMismatch = CreateProfile(
            3,
            memoryRange: new MachineLearningRange(80, 90));
        Assert.Empty(MachineRecurringPatternSynthesizer.Synthesize(
            [compatible, memoryMismatch],
            DateTimeOffset.UnixEpoch));

        var networkMismatch = CreateProfile(
            3,
            network: MachineNetworkActivityClass.Active);
        Assert.Empty(MachineRecurringPatternSynthesizer.Synthesize(
            [compatible, networkMismatch],
            DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void MissingNetworkEvidenceIsCompatibleButImmatureOrStaleProfilesAreNot()
    {
        var established = CreateProfile(2);
        var missingNetwork = CreateProfile(3, network: null);
        Assert.Single(MachineRecurringPatternSynthesizer.Synthesize(
            [established, missingNetwork],
            DateTimeOffset.UnixEpoch));

        var provisional = CreateProfile(
            3,
            confidence: MachineLearningConfidence.Provisional);
        Assert.Empty(MachineRecurringPatternSynthesizer.Synthesize(
            [established, provisional],
            DateTimeOffset.UnixEpoch));

        var stale = CreateProfile(
            3,
            freshness: MachineLearningFreshness.Stale);
        Assert.Empty(MachineRecurringPatternSynthesizer.Synthesize(
            [established, stale],
            DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void MidnightRunIsCanonicalMaximalAndHasNoDuplicates()
    {
        var profiles = new[]
        {
            CreateProfile(0),
            CreateProfile(1),
            CreateProfile(22),
            CreateProfile(23),
            CreateProfile(23) with
            {
                LastReinforcedAt = DateTimeOffset.UnixEpoch.AddMinutes(1)
            }
        };

        var pattern = Assert.Single(
            MachineRecurringPatternSynthesizer.Synthesize(
                profiles,
                DateTimeOffset.UnixEpoch));

        Assert.Equal(22, pattern.StartHour);
        Assert.Equal(2, pattern.EndHourExclusive);
        Assert.True(pattern.CrossesMidnight);
        Assert.Equal([22, 23, 0, 1],
            pattern.MemberContexts.Select(context => context.LocalHour));
        Assert.Equal(4, pattern.MemberContexts.Distinct().Count());
    }

    [Fact]
    public void PatternSplitsAfterDriftAndMergesAfterConvergence()
    {
        var now = DateTimeOffset.UnixEpoch;
        var original = new[]
        {
            CreateProfile(0),
            CreateProfile(1),
            CreateProfile(2),
            CreateProfile(3)
        };
        Assert.Single(MachineRecurringPatternSynthesizer.Synthesize(
            original,
            now));

        var drifted = original.ToArray();
        drifted[2] = CreateProfile(
            2,
            cpuRange: new MachineLearningRange(70, 80));
        var split = MachineRecurringPatternSynthesizer.Synthesize(
            drifted,
            now.AddDays(1));
        var remaining = Assert.Single(split);
        Assert.Equal([0, 1],
            remaining.MemberContexts.Select(context => context.LocalHour));

        var merged = MachineRecurringPatternSynthesizer.Synthesize(
            original,
            now.AddDays(2));
        Assert.Equal(4, Assert.Single(merged).MemberContexts.Count);
    }

    [Fact]
    public void PatternCountIsHardBoundedAndFullDayRunIsExcluded()
    {
        var pairedProfiles = new List<MachineLearningContextProfile>();
        foreach (var activity in Enum.GetValues<MachineUserActivityState>())
        {
            for (var hour = 0; hour < 24; hour += 3)
            {
                pairedProfiles.Add(CreateProfile(hour, activity));
                pairedProfiles.Add(CreateProfile(hour + 1, activity));
            }
        }

        var patterns = MachineRecurringPatternSynthesizer.Synthesize(
            pairedProfiles,
            DateTimeOffset.UnixEpoch);
        Assert.Equal(16, patterns.Count);
        Assert.True(patterns.Count <= MachineLearningPolicy.MaximumPatternCount);

        var fullDay = Enumerable.Range(0, 24)
            .Select(hour => CreateProfile(hour))
            .ToArray();
        Assert.Empty(MachineRecurringPatternSynthesizer.Synthesize(
            fullDay,
            DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void ReadinessSeparatesActiveMemoryFromDistinctDayPatternGate()
    {
        var profiles = new[]
        {
            CreateProfile(2) with
            {
                Confidence = MachineLearningConfidence.Provisional,
                LifetimeSampleCount = 168,
                DistinctObservedDayCount = 4
            },
            CreateProfile(3) with
            {
                Confidence = MachineLearningConfidence.Provisional,
                LifetimeSampleCount = 168,
                DistinctObservedDayCount = 4
            }
        };

        var summary = MachineLearningReadinessProjector.Project(
            profiles,
            [],
            MachineLearningDataHealth.Healthy);

        Assert.Equal(MachineLearningMemoryState.Active,
            summary.MemoryState);
        Assert.Equal(2,
            summary.PatternReadiness.ProfilesWithSufficientSamples);
        Assert.Equal(0,
            summary.PatternReadiness.ProfilesWithSufficientDistinctDays);
        Assert.Equal(1,
            summary.PatternReadiness.AdjacentCandidatePairCount);
        Assert.Equal(1,
            summary.PatternReadiness.PairsWithSufficientSamples);
        Assert.Equal(0,
            summary.PatternReadiness.PairsWithSufficientDistinctDays);
        Assert.Equal(
            MachineLearningPatternReadinessBlocker.InsufficientDistinctDays,
            summary.PatternReadiness.PrimaryBlocker);
    }

    [Fact]
    public void ReadinessDiagnosesStaleEstablishedAdjacentEvidence()
    {
        var profiles = new[]
        {
            CreateProfile(2,
                freshness: MachineLearningFreshness.Stale),
            CreateProfile(3,
                freshness: MachineLearningFreshness.Stale)
        };

        var readiness = MachineLearningReadinessProjector.Project(
            profiles,
            [],
            MachineLearningDataHealth.Healthy).PatternReadiness;

        Assert.Equal(1, readiness.EstablishedPairCount);
        Assert.Equal(0, readiness.TemporallyEligiblePairCount);
        Assert.Equal(1, readiness.StaleRejectedPairCount);
        Assert.Equal(
            MachineLearningPatternReadinessBlocker.StaleEvidence,
            readiness.PrimaryBlocker);
    }

    [Fact]
    public void ReadinessUsesSameActivityAdjacencyAcrossMidnight()
    {
        var profiles = new[]
        {
            CreateProfile(23),
            CreateProfile(0)
        };
        var patterns = MachineRecurringPatternSynthesizer.Synthesize(
            profiles,
            DateTimeOffset.UnixEpoch);

        var readiness = MachineLearningReadinessProjector.Project(
            profiles,
            patterns,
            MachineLearningDataHealth.Healthy).PatternReadiness;

        Assert.Equal(1, readiness.AdjacentCandidatePairCount);
        Assert.Equal(1, readiness.CompatiblePairCount);
        Assert.Equal(1, readiness.CandidateRunCount);
        Assert.Single(patterns);
        Assert.Equal(MachineLearningPatternReadinessBlocker.None,
            readiness.PrimaryBlocker);
    }

    [Fact]
    public void ReadinessDoesNotJoinDifferentActivityStates()
    {
        var profiles = new[]
        {
            CreateProfile(2, MachineUserActivityState.Active),
            CreateProfile(3, MachineUserActivityState.Idle)
        };

        var readiness = MachineLearningReadinessProjector.Project(
            profiles,
            [],
            MachineLearningDataHealth.Healthy).PatternReadiness;

        Assert.Equal(0, readiness.AdjacentCandidatePairCount);
        Assert.Equal(
            MachineLearningPatternReadinessBlocker.NoAdjacentContexts,
            readiness.PrimaryBlocker);
    }

    [Fact]
    public void ReadinessReportsPersistenceRiskWithoutDiscardingMemory()
    {
        var summary = MachineLearningReadinessProjector.Project(
            [CreateProfile(2)],
            [],
            MachineLearningDataHealth.PersistenceTemporarilyUnavailable);

        Assert.Equal(MachineLearningMemoryState.PersistenceAtRisk,
            summary.MemoryState);
        Assert.Equal(1, summary.PatternReadiness.TotalProfileCount);
    }

    [Fact]
    public void PatternGeneratorRunsWhenAdjacentProfilesBecomeEstablished()
    {
        var service = new MachineLearningService();
        var start = CreateLocalTime(2026, 1, 1, 2);

        for (var day = 0; day < 6; day++)
        {
            ObserveHourlyContext(service, start.AddDays(day));
            ObserveHourlyContext(service, start.AddDays(day).AddHours(1));
        }

        var before = service.GetDashboardSnapshot(
            start.AddDays(5).AddHours(1).AddMinutes(12));
        Assert.Empty(before.BroaderPatterns);
        Assert.Equal(
            MachineLearningPatternReadinessBlocker.InsufficientSamples,
            before.Readiness.PatternReadiness.PrimaryBlocker);

        ObserveHourlyContext(service, start.AddDays(6));
        Assert.Empty(service.BroaderPatterns);
        ObserveHourlyContext(service, start.AddDays(6).AddHours(1));

        var after = service.GetDashboardSnapshot(
            start.AddDays(6).AddHours(1).AddMinutes(12));
        Assert.All(after.ContextProfiles, profile => Assert.Equal(
            MachineLearningConfidence.Established,
            profile.Confidence));
        Assert.Single(after.BroaderPatterns);
        Assert.Equal(MachineLearningPatternReadinessBlocker.None,
            after.Readiness.PatternReadiness.PrimaryBlocker);
    }

    private static void ObserveHourlyContext(
        MachineLearningService service,
        DateTimeOffset start)
    {
        for (var sample = 0; sample < 24; sample++)
        {
            Assert.True(service.Observe(CreateObservation(
                start.AddSeconds(sample * 30))));
        }
    }

    private static MachineLearningObservation CreateObservation(
        DateTimeOffset timestamp,
        double cpu = 20,
        double memory = 50,
        MachineNetworkActivityClass network =
            MachineNetworkActivityClass.Unavailable) => new(
            timestamp,
            cpu,
            memory,
            MachineUserActivityState.Active,
            MachineOverallState.Stable,
            [],
            40,
            "stable",
            network);

    private static MachineLearningContextProfile CreateProfile(
        int hour,
        MachineUserActivityState activity = MachineUserActivityState.Active,
        MachineLearningRange? cpuRange = null,
        MachineLearningRange? memoryRange = null,
        MachineNetworkActivityClass? network =
            MachineNetworkActivityClass.Light,
        MachineLearningConfidence confidence =
            MachineLearningConfidence.Established,
        MachineLearningFreshness freshness =
            MachineLearningFreshness.Fresh)
    {
        var first = new DateTimeOffset(2026, 1, 1, 0, 0, 0,
            TimeSpan.Zero);
        var last = first.AddDays(9);
        cpuRange ??= new MachineLearningRange(10, 20);
        memoryRange ??= new MachineLearningRange(45, 55);
        var networkCount = network is null ? 0 : 180;
        return new MachineLearningContextProfile(
            hour,
            activity,
            confidence,
            freshness,
            240,
            TimeSpan.FromHours(2).Ticks,
            10,
            first,
            last,
            new MachineLearningMetricProfile(
                (cpuRange.Low + cpuRange.High) / 2,
                (cpuRange.High - cpuRange.Low) / 4,
                cpuRange),
            new MachineLearningMetricProfile(
                (memoryRange.Low + memoryRange.High) / 2,
                (memoryRange.High - memoryRange.Low) / 4,
                memoryRange),
            network,
            networkCount,
            networkCount,
            first,
            last,
            last);
    }

    private static MachineLearningBaselineState CreateBaselineState(
        DateTimeOffset start,
        long sampleCount,
        int observedDayCount,
        double cpuMean,
        double memoryMean,
        double adaptiveCpuMean,
        double adaptiveMemoryMean) => new(
            start.ToLocalTime().Hour,
            MachineUserActivityState.Active,
            sampleCount,
            cpuMean,
            0,
            memoryMean,
            0,
            start,
            start,
            NetworkUnavailableSampleCount: sampleCount,
            ObservedDayCount: observedDayCount,
            LastObservedLocalDate: DateOnly.FromDateTime(
                start.ToLocalTime().DateTime),
            ObservedDurationTicks: sampleCount *
                MachineLearningService.ObservationInterval.Ticks,
            AdaptiveCpuMean: adaptiveCpuMean,
            AdaptiveCpuVariance: 0,
            AdaptiveMemoryMean: adaptiveMemoryMean,
            AdaptiveMemoryVariance: 0,
            AdaptiveSampleCount: sampleCount,
            AdaptiveLastUpdatedAt: start);

    private static MachineLearningPersistedState CreateState(
        MachineLearningBaselineState baseline,
        long observationCount,
        long sessionCount) => new(
            MachineLearningService.PersistenceSchemaVersion,
            [baseline],
            [],
            observationCount,
            baseline.FirstObservedAt,
            baseline.LastObservedAt,
            baseline.LastObservedAt,
            observationCount * MachineLearningService.ObservationInterval.Ticks,
            Metadata: new MachineLearningMetadataState(
                observationCount,
                observationCount *
                    MachineLearningService.ObservationInterval.Ticks,
                sessionCount,
                baseline.FirstObservedAt,
                baseline.LastObservedAt,
                baseline.FirstObservedAt,
                null,
                baseline.LastObservedAt),
            ContextProfiles: [],
            BroaderPatterns: []);

    private static DateTimeOffset CreateLocalTime(
        int year,
        int month,
        int day,
        int hour)
    {
        var local = new DateTime(
            year,
            month,
            day,
            hour,
            0,
            0,
            DateTimeKind.Unspecified);
        return new DateTimeOffset(
            local,
            TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private sealed class RecordingStore(MachineLearningPersistedState? state)
        : IMachineLearningStore
    {
        public MachineLearningPersistedState? SavedState { get; private set; }

        public Task<MachineLearningPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(state);

        public Task SaveAsync(
            MachineLearningPersistedState persisted,
            CancellationToken cancellationToken = default)
        {
            SavedState = persisted;
            return Task.CompletedTask;
        }
    }
}
