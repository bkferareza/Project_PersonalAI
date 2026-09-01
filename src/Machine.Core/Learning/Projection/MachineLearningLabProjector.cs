namespace Machine.Core;

public enum MachineLearningIntakeOutcome
{
    Waiting,
    Accepted,
    Rejected,
    Throttled
}

public sealed record MachineLearningLiveSnapshot(
    MachineLearningObservation? CurrentObservation,
    MachineLearningBaseline? CurrentContext,
    DateTimeOffset? LastIntakeAt,
    TimeSpan? LastIntakeAge,
    MachineLearningIntakeOutcome LastIntakeOutcome,
    string LastIntakeReason,
    long LifetimeObservationCount,
    long SessionObservationCount,
    bool? PowerEvidenceAccepted);

public sealed record MachineLearningMemorySummary(
    int RawObservationCount,
    int RawObservationCapacity,
    TimeSpan RawObservationRetention,
    int BaselineCount,
    int ProfileCount,
    int ProfileCapacity,
    int PatternCount,
    int EpisodeCount,
    int EpisodeCapacity,
    int SchemaVersion,
    MachineLearningDataHealth DataHealth,
    DateTimeOffset? LastPersistedAt,
    bool HasPendingChanges,
    TimeSpan DetailedActivityRetention);

public sealed record MachineLearningLabSnapshot(
    MachineLearningLiveSnapshot Live,
    IReadOnlyList<MachineLearningBaseline> LearnedContexts,
    MachineLearningMemorySummary Memory,
    MachineLearningPatternReadiness PatternReadiness,
    IReadOnlyList<MachineLearningActivityEvent> RecentChanges);

public static class MachineLearningLabProjector
{
    public const int MaximumRecentChangeCount = 16;

    public static MachineLearningLabSnapshot Project(
        MachineLearningDashboardSnapshot learning,
        MachineLearningActivitySnapshot activity,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(learning);
        ArgumentNullException.ThrowIfNull(activity);

        var latestIntake = activity.RecentEvents
            .Where(item => item.Kind is
                MachineLearningActivityKind.ObservationAccepted or
                MachineLearningActivityKind.ObservationSkipped)
            .OrderByDescending(item => item.OccurredAt)
            .FirstOrDefault();
        var outcome = GetOutcome(latestIntake);
        var reason = GetOutcomeReason(latestIntake, outcome);
        TimeSpan? age = latestIntake is null
            ? null
            : now <= latestIntake.OccurredAt
                ? TimeSpan.Zero
                : now - latestIntake.OccurredAt;
        var current = learning.CurrentBaseline;
        var contexts = learning.Baselines
            .OrderBy(item => current is not null &&
                item.LocalHour == current.LocalHour &&
                item.ActivityState == current.ActivityState ? 0 : 1)
            .ThenBy(item => item.LocalHour)
            .ThenBy(item => item.ActivityState)
            .ToArray();
        var recentChanges = activity.RecentEvents
            .Where(IsLearningChange)
            .OrderByDescending(item => item.OccurredAt)
            .Take(MaximumRecentChangeCount)
            .ToArray();

        return new(
            new(
                learning.CurrentObservation,
                current,
                latestIntake?.OccurredAt,
                age,
                outcome,
                reason,
                learning.Metadata.LifetimeAcceptedObservationCount,
                learning.Diagnostics.AcceptedObservationCount,
                latestIntake?.PowerEvidenceAccepted),
            contexts,
            new(
                learning.RawObservationCount,
                MachineLearningService.MaximumObservationCount,
                TimeSpan.FromTicks(
                    MachineLearningService.ObservationInterval.Ticks *
                    MachineLearningService.MaximumObservationCount),
                learning.Baselines.Count,
                learning.ContextProfiles.Count,
                MachineLearningService.MaximumContextProfileCount,
                learning.BroaderPatterns.Count,
                learning.RecentEpisodeCount,
                MachineLearningService.MaximumEpisodeCount,
                learning.Metadata.PersistedSchemaVersion,
                learning.DataHealth,
                learning.LastPersistedAt,
                learning.IsDirty,
                MachineLearningActivityLog.DetailedRetention),
            learning.Readiness.PatternReadiness,
            recentChanges);
    }

    private static MachineLearningIntakeOutcome GetOutcome(
        MachineLearningActivityEvent? item)
    {
        if (item is null)
        {
            return MachineLearningIntakeOutcome.Waiting;
        }
        if (item.Kind == MachineLearningActivityKind.ObservationAccepted)
        {
            return MachineLearningIntakeOutcome.Accepted;
        }
        return string.Equals(item.Detail, "Throttled",
                StringComparison.Ordinal)
            ? MachineLearningIntakeOutcome.Throttled
            : MachineLearningIntakeOutcome.Rejected;
    }

    private static string GetOutcomeReason(
        MachineLearningActivityEvent? item,
        MachineLearningIntakeOutcome outcome) => outcome switch
    {
        MachineLearningIntakeOutcome.Accepted =>
            item?.PowerEvidenceAccepted == false
                ? "Core signals accepted; eligible power evidence was unavailable."
                : "Accepted into the current learned context.",
        MachineLearningIntakeOutcome.Throttled =>
            "The intake cadence had not elapsed; no duplicate sample was stored.",
        MachineLearningIntakeOutcome.Rejected =>
            string.IsNullOrWhiteSpace(item?.Detail)
                ? "Rejected by deterministic intake validation."
                : item.Detail,
        _ => "Waiting for the first verified intake attempt."
    };

    private static bool IsLearningChange(MachineLearningActivityEvent item) =>
        item.Kind is
            MachineLearningActivityKind.ObservationAccepted or
            MachineLearningActivityKind.ObservationSkipped or
            MachineLearningActivityKind.ProfileUpdated or
            MachineLearningActivityKind.EpisodeUpdated or
            MachineLearningActivityKind.RestoreSucceeded or
            MachineLearningActivityKind.RestoreMigrated or
            MachineLearningActivityKind.RestoreCorrupt or
            MachineLearningActivityKind.RestoreUnavailable or
            MachineLearningActivityKind.LearningContinuityRegressionDetected or
            MachineLearningActivityKind.PersistenceSucceeded or
            MachineLearningActivityKind.PersistenceFailed;
}
