namespace Machine.Core;

public static class MachineRecurringPatternSynthesizer
{
    // Profiles are scanned once per activity state into maximal, non-
    // overlapping adjacent runs. Midnight end/start runs are joined only
    // when the combined run remains pairwise compatible.
    public static IReadOnlyList<MachineLearningRecurringPattern> Synthesize(
        IReadOnlyList<MachineLearningContextProfile> profiles,
        DateTimeOffset recognizedAt,
        IReadOnlyList<MachineLearningRecurringPattern>? previousPatterns = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var previous = previousPatterns ?? [];
        var results = new List<MachineLearningRecurringPattern>();
        foreach (var activityState in Enum.GetValues<MachineUserActivityState>())
        {
            var eligible = profiles
                .Where(profile =>
                    profile.ActivityState == activityState &&
                    profile.Confidence == MachineLearningConfidence.Established &&
                    profile.Freshness != MachineLearningFreshness.Stale &&
                    profile.Cpu.TypicalRange is not null &&
                    profile.Memory.TypicalRange is not null &&
                    profile.LocalHour is >= 0 and <= 23)
                .GroupBy(profile => profile.LocalHour)
                .Select(group => group
                    .OrderByDescending(profile => profile.LastReinforcedAt)
                    .First())
                .OrderBy(profile => profile.LocalHour)
                .ToArray();

            var runs = BuildRuns(eligible);
            MergeMidnightRun(runs);
            foreach (var run in runs.Where(run =>
                run.Count >= MachineLearningPolicy.MinimumPatternProfileCount &&
                run.Count < 24))
            {
                results.Add(CreatePattern(
                    run,
                    recognizedAt,
                    FindPrevious(previous, run)));
            }
        }

        return results
            .OrderBy(pattern => pattern.StartHour)
            .ThenBy(pattern => pattern.ActivityState)
            .Take(MachineLearningPolicy.MaximumPatternCount)
            .ToArray();
    }

    public static bool AreProfilesCompatible(
        MachineLearningContextProfile left,
        MachineLearningContextProfile right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return left.ActivityState == right.ActivityState &&
            left.Confidence == MachineLearningConfidence.Established &&
            right.Confidence == MachineLearningConfidence.Established &&
            left.Freshness != MachineLearningFreshness.Stale &&
            right.Freshness != MachineLearningFreshness.Stale &&
            left.Cpu.TypicalRange is { } leftCpu &&
            right.Cpu.TypicalRange is { } rightCpu &&
            MachineLearningPolicy.AreRangesCompatible(leftCpu, rightCpu) &&
            left.Memory.TypicalRange is { } leftMemory &&
            right.Memory.TypicalRange is { } rightMemory &&
            MachineLearningPolicy.AreRangesCompatible(
                leftMemory,
                rightMemory) &&
            AreNetworkClassesCompatible(
                left.DominantNetworkActivityClass,
                right.DominantNetworkActivityClass);
    }

    private static List<List<MachineLearningContextProfile>> BuildRuns(
        IReadOnlyList<MachineLearningContextProfile> profiles)
    {
        var runs = new List<List<MachineLearningContextProfile>>();
        foreach (var profile in profiles)
        {
            var current = runs.Count == 0 ? null : runs[^1];
            if (current is not null &&
                profile.LocalHour == current[^1].LocalHour + 1 &&
                current.All(member => AreProfilesCompatible(member, profile)))
            {
                current.Add(profile);
            }
            else
            {
                runs.Add([profile]);
            }
        }

        return runs;
    }

    private static void MergeMidnightRun(
        List<List<MachineLearningContextProfile>> runs)
    {
        if (runs.Count < 2 ||
            runs[0][0].LocalHour != 0 ||
            runs[^1][^1].LocalHour != 23)
        {
            return;
        }

        var combined = runs[^1].Concat(runs[0]).ToList();
        if (!AreAllCompatible(combined))
        {
            return;
        }

        runs.RemoveAt(runs.Count - 1);
        runs.RemoveAt(0);
        runs.Add(combined);
    }

    private static bool AreAllCompatible(
        IReadOnlyList<MachineLearningContextProfile> profiles)
    {
        for (var left = 0; left < profiles.Count; left++)
        {
            for (var right = left + 1; right < profiles.Count; right++)
            {
                if (!AreProfilesCompatible(profiles[left], profiles[right]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static MachineLearningRecurringPattern CreatePattern(
        IReadOnlyList<MachineLearningContextProfile> run,
        DateTimeOffset recognizedAt,
        MachineLearningRecurringPattern? previous)
    {
        var first = run[0];
        var endHourExclusive = (run[^1].LocalHour + 1) % 24;
        var networkClass = run
            .Select(profile => profile.DominantNetworkActivityClass)
            .FirstOrDefault(IsLearnedNetworkClass);
        var matchingNetworkProfiles = networkClass is null
            ? []
            : run.Where(profile =>
                profile.DominantNetworkActivityClass == networkClass).ToArray();

        return new MachineLearningRecurringPattern(
            first.ActivityState,
            first.LocalHour,
            endHourExclusive,
            endHourExclusive <= first.LocalHour,
            run.Select(profile => profile.ContextKey).ToArray(),
            run.Count >= MachineLearningPolicy.EstablishedPatternProfileCount
                ? MachineLearningConfidence.Established
                : MachineLearningConfidence.Provisional,
            run.Max(profile => profile.Freshness),
            SaturatingSum(run.Select(profile => profile.LifetimeSampleCount)),
            run.Min(profile => profile.DistinctObservedDayCount),
            new MachineLearningRange(
                run.Min(profile => profile.Cpu.TypicalRange!.Low),
                run.Max(profile => profile.Cpu.TypicalRange!.High)),
            new MachineLearningRange(
                run.Min(profile => profile.Memory.TypicalRange!.Low),
                run.Max(profile => profile.Memory.TypicalRange!.High)),
            networkClass,
            SaturatingSum(matchingNetworkProfiles.Select(profile =>
                profile.DominantNetworkActivityCount)),
            SaturatingSum(matchingNetworkProfiles.Select(profile =>
                profile.NetworkObservationCount)),
            previous?.CreatedAt ?? recognizedAt,
            run.Max(profile => profile.LastReinforcedAt));
    }

    private static MachineLearningRecurringPattern? FindPrevious(
        IReadOnlyList<MachineLearningRecurringPattern> previous,
        IReadOnlyList<MachineLearningContextProfile> run)
    {
        var keys = run.Select(profile => profile.ContextKey).ToArray();
        return previous.FirstOrDefault(pattern =>
            pattern.ActivityState == run[0].ActivityState &&
            pattern.MemberContexts.SequenceEqual(keys));
    }

    private static bool AreNetworkClassesCompatible(
        MachineNetworkActivityClass? left,
        MachineNetworkActivityClass? right)
    {
        var normalizedLeft = IsLearnedNetworkClass(left) ? left : null;
        var normalizedRight = IsLearnedNetworkClass(right) ? right : null;
        return normalizedLeft is null ||
            normalizedRight is null ||
            normalizedLeft == normalizedRight;
    }

    private static bool IsLearnedNetworkClass(
        MachineNetworkActivityClass? activityClass) =>
        activityClass is MachineNetworkActivityClass.Quiet or
            MachineNetworkActivityClass.Light or
            MachineNetworkActivityClass.Active;

    private static long SaturatingSum(IEnumerable<long> values)
    {
        var total = 0L;
        foreach (var value in values)
        {
            total = total >= long.MaxValue - value
                ? long.MaxValue
                : total + value;
        }

        return total;
    }
}
