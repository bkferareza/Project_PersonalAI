namespace Machine.Core;

public static class MachineLearningReadinessProjector
{
    public static MachineLearningReadinessSummary Project(
        IReadOnlyList<MachineLearningContextProfile> profiles,
        IReadOnlyList<MachineLearningRecurringPattern> patterns,
        MachineLearningDataHealth dataHealth)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(patterns);

        var canonicalProfiles = profiles
            .Where(profile => profile.LocalHour is >= 0 and <= 23)
            .GroupBy(profile => profile.ContextKey)
            .Select(group => group
                .OrderByDescending(profile => profile.LastReinforcedAt)
                .First())
            .ToArray();
        var pairs = CreateAdjacentPairs(canonicalProfiles);

        var pairsWithSamples = 0;
        var pairsWithDays = 0;
        var pairsMeetingEvidence = 0;
        var establishedPairs = 0;
        var temporallyEligiblePairs = 0;
        var comparedPairs = 0;
        var compatiblePairs = 0;
        var confidenceRejected = 0;
        var staleRejected = 0;
        var missingRangeRejected = 0;
        var cpuRejected = 0;
        var memoryRejected = 0;
        var networkRejected = 0;

        foreach (var pair in pairs)
        {
            var sufficientSamples = pair.Left.LifetimeSampleCount >=
                    MachineLearningService.EstablishedSampleCount &&
                pair.Right.LifetimeSampleCount >=
                    MachineLearningService.EstablishedSampleCount;
            var sufficientDays = pair.Left.DistinctObservedDayCount >=
                    MachineLearningService.EstablishedObservedDayCount &&
                pair.Right.DistinctObservedDayCount >=
                    MachineLearningService.EstablishedObservedDayCount;
            if (sufficientSamples)
            {
                pairsWithSamples++;
            }
            if (sufficientDays)
            {
                pairsWithDays++;
            }
            if (sufficientSamples && sufficientDays)
            {
                pairsMeetingEvidence++;
            }

            if (pair.Left.Confidence != MachineLearningConfidence.Established ||
                pair.Right.Confidence != MachineLearningConfidence.Established)
            {
                confidenceRejected++;
                continue;
            }

            establishedPairs++;
            if (pair.Left.Freshness == MachineLearningFreshness.Stale ||
                pair.Right.Freshness == MachineLearningFreshness.Stale)
            {
                staleRejected++;
                continue;
            }

            temporallyEligiblePairs++;
            if (pair.Left.Cpu.TypicalRange is not { } leftCpu ||
                pair.Right.Cpu.TypicalRange is not { } rightCpu ||
                pair.Left.Memory.TypicalRange is not { } leftMemory ||
                pair.Right.Memory.TypicalRange is not { } rightMemory)
            {
                missingRangeRejected++;
                continue;
            }

            comparedPairs++;
            if (!MachineLearningPolicy.AreRangesCompatible(
                    leftCpu,
                    rightCpu))
            {
                cpuRejected++;
                continue;
            }
            if (!MachineLearningPolicy.AreRangesCompatible(
                    leftMemory,
                    rightMemory))
            {
                memoryRejected++;
                continue;
            }
            if (!MachineRecurringPatternSynthesizer
                    .AreNetworkClassesCompatible(
                        pair.Left.DominantNetworkActivityClass,
                        pair.Right.DominantNetworkActivityClass))
            {
                networkRejected++;
                continue;
            }

            compatiblePairs++;
        }

        var candidateRuns = 0;
        var fullDayRuns = 0;
        foreach (var activityState in
            Enum.GetValues<MachineUserActivityState>())
        {
            var eligible = MachineRecurringPatternSynthesizer
                .SelectEligibleProfiles(canonicalProfiles, activityState);
            var runs = MachineRecurringPatternSynthesizer.BuildRuns(eligible);
            MachineRecurringPatternSynthesizer.MergeMidnightRun(runs);
            candidateRuns += runs.Count(run =>
                run.Count >= MachineLearningPolicy.MinimumPatternProfileCount &&
                run.Count < 24);
            fullDayRuns += runs.Count(run => run.Count >= 24);
        }

        var patternLimitTruncated = Math.Max(
            0,
            candidateRuns - MachineLearningPolicy.MaximumPatternCount);
        var readiness = new MachineLearningPatternReadiness(
            TotalProfileCount: canonicalProfiles.Length,
            ProfilesWithSufficientSamples: canonicalProfiles.Count(profile =>
                profile.LifetimeSampleCount >=
                    MachineLearningService.EstablishedSampleCount),
            ProfilesWithSufficientDistinctDays: canonicalProfiles.Count(
                profile => profile.DistinctObservedDayCount >=
                    MachineLearningService.EstablishedObservedDayCount),
            EstablishedProfileCount: canonicalProfiles.Count(profile =>
                profile.Confidence == MachineLearningConfidence.Established),
            FreshEstablishedProfileCount: canonicalProfiles.Count(profile =>
                profile.Confidence == MachineLearningConfidence.Established &&
                profile.Freshness == MachineLearningFreshness.Fresh),
            TemporallyEligibleProfileCount: canonicalProfiles.Count(profile =>
                profile.Confidence == MachineLearningConfidence.Established &&
                profile.Freshness != MachineLearningFreshness.Stale),
            AdjacentCandidatePairCount: pairs.Count,
            PairsWithSufficientSamples: pairsWithSamples,
            PairsWithSufficientDistinctDays: pairsWithDays,
            PairsMeetingEvidenceThresholds: pairsMeetingEvidence,
            EstablishedPairCount: establishedPairs,
            TemporallyEligiblePairCount: temporallyEligiblePairs,
            PairsReachingCompatibilityComparison: comparedPairs,
            CompatiblePairCount: compatiblePairs,
            ConfidenceRejectedPairCount: confidenceRejected,
            StaleRejectedPairCount: staleRejected,
            MissingRangeRejectedPairCount: missingRangeRejected,
            CpuRejectedPairCount: cpuRejected,
            MemoryRejectedPairCount: memoryRejected,
            NetworkRejectedPairCount: networkRejected,
            CandidateRunCount: candidateRuns,
            FullDayRunRejectedCount: fullDayRuns,
            PatternLimitTruncatedCount: patternLimitTruncated,
            PatternsProduced: patterns.Count,
            PrimaryBlocker: SelectPrimaryBlocker(
                canonicalProfiles.Length,
                pairs.Count,
                pairsWithSamples,
                pairsWithDays,
                establishedPairs,
                temporallyEligiblePairs,
                comparedPairs,
                compatiblePairs,
                cpuRejected,
                memoryRejected,
                networkRejected,
                fullDayRuns,
                patterns.Count));

        return new MachineLearningReadinessSummary(
            SelectMemoryState(dataHealth, canonicalProfiles.Length),
            readiness);
    }

    private static MachineLearningMemoryState SelectMemoryState(
        MachineLearningDataHealth dataHealth,
        int profileCount) =>
        dataHealth ==
            MachineLearningDataHealth.PersistenceTemporarilyUnavailable
            ? MachineLearningMemoryState.PersistenceAtRisk
            : profileCount > 0
                ? MachineLearningMemoryState.Active
                : MachineLearningMemoryState.Calibrating;

    private static MachineLearningPatternReadinessBlocker
        SelectPrimaryBlocker(
            int profileCount,
            int adjacentPairs,
            int pairsWithSamples,
            int pairsWithDays,
            int establishedPairs,
            int temporallyEligiblePairs,
            int comparedPairs,
            int compatiblePairs,
            int cpuRejected,
            int memoryRejected,
            int networkRejected,
            int fullDayRuns,
            int patternCount)
    {
        if (patternCount > 0)
        {
            return MachineLearningPatternReadinessBlocker.None;
        }
        if (profileCount < MachineLearningPolicy.MinimumPatternProfileCount)
        {
            return MachineLearningPatternReadinessBlocker
                .InsufficientProfiles;
        }
        if (adjacentPairs == 0)
        {
            return MachineLearningPatternReadinessBlocker.NoAdjacentContexts;
        }
        if (pairsWithSamples == 0)
        {
            return MachineLearningPatternReadinessBlocker.InsufficientSamples;
        }
        if (pairsWithDays == 0)
        {
            return MachineLearningPatternReadinessBlocker
                .InsufficientDistinctDays;
        }
        if (establishedPairs == 0)
        {
            return MachineLearningPatternReadinessBlocker
                .NoEstablishedAdjacentContexts;
        }
        if (temporallyEligiblePairs == 0)
        {
            return MachineLearningPatternReadinessBlocker.StaleEvidence;
        }
        if (comparedPairs == 0)
        {
            return MachineLearningPatternReadinessBlocker
                .MissingTypicalRanges;
        }
        if (compatiblePairs == 0)
        {
            if (cpuRejected >= memoryRejected &&
                cpuRejected >= networkRejected)
            {
                return MachineLearningPatternReadinessBlocker
                    .IncompatibleCpuBehavior;
            }
            return memoryRejected >= networkRejected
                ? MachineLearningPatternReadinessBlocker
                    .IncompatibleMemoryBehavior
                : MachineLearningPatternReadinessBlocker
                    .IncompatibleNetworkBehavior;
        }
        return fullDayRuns > 0
            ? MachineLearningPatternReadinessBlocker.FullDayRunExcluded
            : MachineLearningPatternReadinessBlocker
                .NoEstablishedAdjacentContexts;
    }

    private static IReadOnlyList<AdjacentPair> CreateAdjacentPairs(
        IReadOnlyList<MachineLearningContextProfile> profiles)
    {
        var byContext = profiles.ToDictionary(
            profile => profile.ContextKey);
        var pairs = new List<AdjacentPair>();
        foreach (var profile in profiles)
        {
            var nextKey = new MachineLearningContextKey(
                (profile.LocalHour + 1) % 24,
                profile.ActivityState);
            if (byContext.TryGetValue(nextKey, out var next))
            {
                pairs.Add(new AdjacentPair(profile, next));
            }
        }
        return pairs;
    }

    private sealed record AdjacentPair(
        MachineLearningContextProfile Left,
        MachineLearningContextProfile Right);
}
