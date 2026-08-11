using System.Globalization;

namespace Machine.Core;

public static class MachineLearnedItemProjector
{
    public const int DefaultMaximumItemCount = 16;

    public static IReadOnlyList<MachineLearnedItem> Project(
        IReadOnlyList<MachineLearningBaseline> baselines,
        IReadOnlyList<MachineLearningEpisode> episodes,
        MachineLearningObservation? currentObservation,
        int maximumItemCount = DefaultMaximumItemCount)
    {
        ArgumentNullException.ThrowIfNull(baselines);
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItemCount);

        var currentHour = currentObservation?.Timestamp.ToLocalTime().Hour;
        var currentActivity = currentObservation?.ActivityState;
        var orderedBaselines = baselines
            .Where(baseline => baseline.Confidence !=
                MachineLearningConfidence.Calibrating)
            .OrderBy(baseline => currentHour == baseline.LocalHour &&
                currentActivity == baseline.ActivityState ? 0 : 1)
            .ThenByDescending(baseline => baseline.LastObservedAt)
            .ThenBy(baseline => baseline.LocalHour)
            .ThenBy(baseline => baseline.ActivityState)
            .ToArray();

        var items = new List<MachineLearnedItem>(maximumItemCount);
        var baselineItemLimit = episodes.Count == 0
            ? maximumItemCount
            : Math.Max(0, maximumItemCount - Math.Min(3, maximumItemCount));
        foreach (var baseline in orderedBaselines)
        {
            if (items.Count >= baselineItemLimit)
            {
                break;
            }

            var hour = FormatHour(baseline.LocalHour);
            var evidence = baseline.SampleCount.ToString(
                "N0", CultureInfo.InvariantCulture);
            var early = baseline.Confidence !=
                MachineLearningConfidence.Established;
            items.Add(new MachineLearnedItem(
                $"{baseline.ActivityState} periods around {hour} have " +
                $"averaged {baseline.CpuMean.ToString("F1", CultureInfo.InvariantCulture)}% " +
                $"CPU across {evidence} samples.",
                baseline.SampleCount,
                baseline.Confidence,
                early));
            if (items.Count >= baselineItemLimit)
            {
                break;
            }

            items.Add(new MachineLearnedItem(
                $"Memory during {baseline.ActivityState} {hour} observations " +
                $"has averaged {baseline.MemoryMean.ToString("F1", CultureInfo.InvariantCulture)}% " +
                $"across {evidence} samples.",
                baseline.SampleCount,
                baseline.Confidence,
                early));
            if (items.Count >= baselineItemLimit)
            {
                break;
            }

            if (baseline.DominantNetworkActivityClass is { } dominantClass)
            {
                var dominantCount = baseline.DominantNetworkActivityCount
                    .ToString("N0", CultureInfo.InvariantCulture);
                var networkEvidence = baseline.NetworkObservationCount
                    .ToString("N0", CultureInfo.InvariantCulture);
                items.Add(new MachineLearnedItem(
                    $"{dominantCount} of {networkEvidence} " +
                    $"{baseline.ActivityState} observations at {hour} had " +
                    $"{dominantClass} network activity.",
                    baseline.NetworkObservationCount,
                    baseline.Confidence,
                    early));
                if (items.Count >= baselineItemLimit)
                {
                    break;
                }
            }
        }

        var completed = episodes
            .OrderByDescending(episode => episode.EndedAt)
            .ToArray();
        if (completed.Length > 0)
        {
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
                    false));
            }

            if (items.Count < maximumItemCount)
            {
                var longest = completed
                    .OrderByDescending(episode => episode.EndedAt - episode.StartedAt)
                    .First();
                items.Add(new MachineLearnedItem(
                    $"The longest completed observed {longest.ActivityState} episode " +
                    $"lasted {FormatDuration(longest.EndedAt - longest.StartedAt)} " +
                    $"across {FormatCount(longest.SampleCount, "sample")}.",
                    longest.SampleCount,
                    null,
                    false));
            }

            if (items.Count < maximumItemCount)
            {
                var recoveries = completed.Where(episode => string.Equals(
                    episode.Outcome,
                    "Recovered to Stable",
                    StringComparison.Ordinal)).ToArray();
                if (recoveries.Length > 0)
                {
                    var recoverySamples = recoveries.Sum(episode =>
                        (long)episode.SampleCount);
                    items.Add(new MachineLearnedItem(
                        $"{FormatCount(recoveries.Length, "completed episode")} " +
                        $"recorded a verified recovery to Stable across " +
                        $"{FormatCount(recoverySamples, "sample")}.",
                        recoverySamples,
                        null,
                        false));
                }
            }
        }

        return items.Take(maximumItemCount).ToArray();
    }

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
