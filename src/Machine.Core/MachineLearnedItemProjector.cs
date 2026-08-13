using System.Globalization;

namespace Machine.Core;

public static class MachineLearnedItemProjector
{
    public const int DefaultMaximumItemCount = 16;

    public static IReadOnlyList<MachineLearnedItem> Project(
        IReadOnlyList<MachineLearningBaseline> baselines,
        IReadOnlyList<MachineLearningContextProfile> profiles,
        IReadOnlyList<MachineLearningRecurringPattern> patterns,
        IReadOnlyList<MachineLearningEpisode> episodes,
        MachineLearningObservation? currentObservation,
        int maximumItemCount = DefaultMaximumItemCount)
    {
        ArgumentNullException.ThrowIfNull(baselines);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItemCount);

        var currentHour = currentObservation?.Timestamp.ToLocalTime().Hour;
        var currentActivity = currentObservation?.ActivityState;
        var items = new List<MachineLearnedItem>(maximumItemCount);

        foreach (var baseline in baselines
            .Where(baseline =>
                baseline.Confidence != MachineLearningConfidence.Calibrating &&
                baseline.DominantNetworkActivityClass is not null)
            .OrderBy(baseline => currentHour == baseline.LocalHour &&
                currentActivity == baseline.ActivityState ? 0 : 1)
            .ThenByDescending(baseline => baseline.LastObservedAt)
            .Take(2))
        {
            if (items.Count >= maximumItemCount)
            {
                break;
            }

            var dominant = baseline.DominantNetworkActivityClass!.Value;
            var evidenceLabel = baseline.Confidence ==
                    MachineLearningConfidence.Established
                ? "Cumulative evidence"
                : "Early observation";
            items.Add(new MachineLearnedItem(
                $"{evidenceLabel} \u00B7 " +
                $"{baseline.DominantNetworkActivityCount.ToString("N0", CultureInfo.InvariantCulture)} " +
                $"of {baseline.NetworkObservationCount.ToString("N0", CultureInfo.InvariantCulture)} " +
                $"{baseline.ActivityState} observations at " +
                $"{FormatHour(baseline.LocalHour)} had {dominant} network activity.",
                baseline.NetworkObservationCount,
                baseline.Confidence,
                baseline.Confidence != MachineLearningConfidence.Established,
                MachineLearningMemoryLayer.ContextBaseline));
        }

        foreach (var profile in profiles
            .OrderBy(profile => currentHour == profile.LocalHour &&
                currentActivity == profile.ActivityState ? 0 : 1)
            .ThenBy(profile => profile.Freshness)
            .ThenByDescending(profile => profile.LastReinforcedAt)
            .Take(8))
        {
            if (items.Count >= maximumItemCount)
            {
                break;
            }

            var cpu = FormatRange(profile.Cpu.TypicalRange);
            var memory = FormatRange(profile.Memory.TypicalRange);
            var confidenceLabel = profile.Confidence ==
                    MachineLearningConfidence.Established
                ? "Established"
                : "Early profile";
            var behaviorLabel = profile.Confidence ==
                    MachineLearningConfidence.Established &&
                profile.Freshness != MachineLearningFreshness.Stale
                    ? "has typically stayed"
                    : "has an adaptive learned range";
            items.Add(new MachineLearnedItem(
                $"{confidenceLabel} \u00B7 During {FormatHour(profile.LocalHour)} " +
                $"{profile.ActivityState} periods, CPU {behaviorLabel} around " +
                $"{cpu} and memory around {memory} across " +
                $"{FormatCount(profile.LifetimeSampleCount, "observation")} " +
                $"on {FormatCount(profile.DistinctObservedDayCount, "day")}." +
                (profile.Freshness == MachineLearningFreshness.Stale
                    ? " This profile is historical and currently stale."
                    : string.Empty),
                profile.LifetimeSampleCount,
                profile.Confidence,
                profile.Confidence != MachineLearningConfidence.Established,
                MachineLearningMemoryLayer.CompactProfile));
        }

        foreach (var pattern in patterns
            .OrderByDescending(pattern =>
                pattern.Confidence == MachineLearningConfidence.Established)
            .ThenByDescending(pattern => pattern.LastReinforcedAt)
            .Take(3))
        {
            if (items.Count >= maximumItemCount)
            {
                break;
            }

            items.Add(new MachineLearnedItem(
                $"{(pattern.Confidence == MachineLearningConfidence.Established ? "Established pattern" : "Early broader pattern")} \u00B7 " +
                $"Observed {pattern.ActivityState} behavior from " +
                $"{FormatHour(pattern.StartHour)}\u2013" +
                $"{FormatHour(pattern.EndHourExclusive)} has been statistically " +
                $"similar across {FormatCount(pattern.MemberContexts.Count, "learned hourly context")}, " +
                $"with {FormatCount(pattern.CombinedSampleCount, "observation")}.",
                pattern.CombinedSampleCount,
                pattern.Confidence,
                pattern.Confidence != MachineLearningConfidence.Established,
                MachineLearningMemoryLayer.BroaderPattern));
        }

        AddEpisodeItems(items, episodes, maximumItemCount);
        return items.Take(maximumItemCount).ToArray();
    }

    private static void AddEpisodeItems(
        ICollection<MachineLearnedItem> items,
        IReadOnlyList<MachineLearningEpisode> episodes,
        int maximumItemCount)
    {
        if (items.Count >= maximumItemCount)
        {
            return;
        }

        var completed = episodes
            .OrderByDescending(episode => episode.EndedAt)
            .ToArray();
        var stable = completed.Where(episode =>
            episode.OverallState == MachineOverallState.Stable).ToArray();
        if (stable.Length > 0)
        {
            var stableSamples = stable.Sum(episode => (long)episode.SampleCount);
            items.Add(new MachineLearnedItem(
                $"{FormatCount(stable.Length, "completed Stable episode")} " +
                $"{(stable.Length == 1 ? "was" : "were")} recorded across " +
                $"{FormatCount(stableSamples, "sample")}.",
                stableSamples,
                null,
                false,
                MachineLearningMemoryLayer.AggregateEpisode));
        }

        if (items.Count >= maximumItemCount || completed.Length == 0)
        {
            return;
        }

        var longest = completed
            .OrderByDescending(episode => episode.EndedAt - episode.StartedAt)
            .First();
        items.Add(new MachineLearnedItem(
            $"The longest completed observed {longest.ActivityState} episode " +
            $"lasted {FormatDuration(longest.EndedAt - longest.StartedAt)} " +
            $"across {FormatCount(longest.SampleCount, "sample")}.",
            longest.SampleCount,
            null,
            false,
            MachineLearningMemoryLayer.AggregateEpisode));
    }

    private static string FormatRange(MachineLearningRange? range) =>
        range is null
            ? "not yet available"
            : $"{range.Low.ToString("F1", CultureInfo.InvariantCulture)}\u2013" +
                $"{range.High.ToString("F1", CultureInfo.InvariantCulture)}%";

    private static string FormatHour(int hour)
    {
        var normalizedHour = ((hour % 24) + 24) % 24;
        var suffix = normalizedHour < 12 ? "AM" : "PM";
        var displayHour = normalizedHour % 12;
        return $"{(displayHour == 0 ? 12 : displayHour)} {suffix}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var bounded = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return bounded.TotalHours >= 1d
            ? $"{(int)bounded.TotalHours}h {bounded.Minutes}m"
            : $"{Math.Max(0, bounded.Minutes)}m";
    }

    private static string FormatCount(long count, string singular) =>
        $"{count.ToString("N0", CultureInfo.InvariantCulture)} " +
        (count == 1 ? singular : singular + "s");
}
