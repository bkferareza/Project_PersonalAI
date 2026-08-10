namespace Machine.Core;

public static class MachineLearningEpisodeProjector
{
    public const int DefaultMaximumEpisodeCount = 50;

    public static IReadOnlyList<MachineLearningEpisode> Project(
        IReadOnlyList<MachineLearningEpisode> episodes,
        int maximumEpisodeCount = DefaultMaximumEpisodeCount)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEpisodeCount);

        return episodes
            .OrderByDescending(episode => episode.EndedAt)
            .Take(maximumEpisodeCount)
            .ToArray();
    }
}
