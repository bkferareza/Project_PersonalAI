namespace Machine.Core;

public enum MachineLearningActivityKind
{
    RuntimeStarted,
    RestoreStarted,
    RestoreSucceeded,
    RestoreMissing,
    RestoreCorrupt,
    RestoreUnavailable,
    RestoreMigrated,
    LearningContinuityRegressionDetected,
    SessionStarted,
    ObservationAccepted,
    ObservationSkipped,
    ProfileUpdated,
    EpisodeUpdated,
    MarkedDirty,
    PersistenceStarted,
    PersistenceSucceeded,
    PersistenceFailed,
    ShutdownStarted,
    ShutdownSucceeded,
    ShutdownFailed,
    RuntimeStopped
}

public enum MachineLearningActivityStatus
{
    Starting,
    Active,
    Waiting,
    PersistenceDelayed,
    Unavailable
}

public sealed record MachineLearningActivityEvent(
    DateTimeOffset OccurredAt,
    MachineLearningActivityKind Kind,
    long? ObservationCount = null,
    int? ProfileCount = null,
    int? EpisodeCount = null,
    int? SchemaVersion = null,
    long Count = 1,
    string? Detail = null,
    long? ByteCount = null,
    long? DurationMilliseconds = null,
    bool? PowerEvidenceAccepted = null,
    long? PowerEvidenceCount = null);

public sealed record MachineLearningActivityPersistedState(
    IReadOnlyList<MachineLearningActivityEvent> Events);

public interface IMachineLearningActivityStore
{
    Task<MachineLearningActivityPersistedState?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(MachineLearningActivityPersistedState state,
        CancellationToken cancellationToken = default);
}

public sealed record MachineLearningActivitySnapshot(
    MachineLearningActivityStatus Status,
    IReadOnlyList<MachineLearningActivityEvent> RecentEvents,
    DateTimeOffset? LastSuccessfulPersistenceAt,
    long? LastSuccessfulPersistenceObservationCount,
    bool HasRestoredState);

public sealed class MachineLearningActivityLog
{
    public const int MaximumEventCount = 1_000;
    public static readonly TimeSpan DetailedRetention = TimeSpan.FromHours(48);
    public static readonly TimeSpan ImportantRetention = TimeSpan.FromDays(14);
    public static readonly TimeSpan PersistenceInterval = TimeSpan.FromMinutes(5);

    private readonly object _sync = new();
    private List<MachineLearningActivityEvent> _events = [];
    private DateTimeOffset? _lastSavedAt;
    private bool _isDirty;

    public long? LastSuccessfulPersistenceObservationCount
    {
        get
        {
            lock (_sync)
            {
                return _events.LastOrDefault(item =>
                    item.Kind == MachineLearningActivityKind.PersistenceSucceeded)
                    ?.ObservationCount;
            }
        }
    }

    public void Record(MachineLearningActivityKind kind,
        DateTimeOffset occurredAt,
        long? observationCount = null,
        int? profileCount = null,
        int? episodeCount = null,
        int? schemaVersion = null,
        string? detail = null,
        long? byteCount = null,
        long? durationMilliseconds = null,
        bool? powerEvidenceAccepted = null,
        long? powerEvidenceCount = null)
    {
        lock (_sync)
        {
            var shouldCoalesce = kind is
                MachineLearningActivityKind.ObservationSkipped or
                MachineLearningActivityKind.MarkedDirty;
            var previous = _events.LastOrDefault();
            if (shouldCoalesce && previous?.Kind == kind &&
                occurredAt - previous.OccurredAt < TimeSpan.FromMinutes(1))
            {
                _events[^1] = previous with { Count = previous.Count + 1 };
            }
            else
            {
                _events.Add(new MachineLearningActivityEvent(occurredAt,
                    kind, observationCount, profileCount, episodeCount,
                    schemaVersion, Detail: detail, ByteCount: byteCount,
                    DurationMilliseconds: durationMilliseconds,
                    PowerEvidenceAccepted: powerEvidenceAccepted,
                    PowerEvidenceCount: powerEvidenceCount));
            }

            Prune(occurredAt);
            _isDirty = true;
        }
    }

    public async Task LoadAsync(IMachineLearningActivityStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            var state = await store.LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            if (state?.Events is null)
            {
                return;
            }

            lock (_sync)
            {
                _events = state.Events.Concat(_events)
                    .Where(item => item.OccurredAt != default)
                    .OrderBy(item => item.OccurredAt)
                    .TakeLast(MaximumEventCount)
                    .ToList();
                Prune(DateTimeOffset.UtcNow);
                _isDirty = false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Activity auditing is diagnostic only and must not block learning.
        }
    }

    public async Task<bool> SaveIfDueAsync(IMachineLearningActivityStore store,
        DateTimeOffset now, bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        MachineLearningActivityPersistedState state;
        lock (_sync)
        {
            if (!_isDirty || (!force && _lastSavedAt is not null &&
                now - _lastSavedAt.Value < PersistenceInterval))
            {
                return false;
            }
            Prune(now);
            state = new MachineLearningActivityPersistedState(_events.ToArray());
        }

        try
        {
            await store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                _lastSavedAt = now;
                _isDirty = false;
            }
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public MachineLearningActivitySnapshot GetSnapshot(
        MachineLearningDashboardSnapshot learning,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(learning);
        lock (_sync)
        {
            var restored = _events.Any(item => item.Kind ==
                MachineLearningActivityKind.RestoreSucceeded);
            var persisted = _events.LastOrDefault(item => item.Kind ==
                MachineLearningActivityKind.PersistenceSucceeded);
            var recent = _events
                .Where(item => item.Kind != MachineLearningActivityKind.ObservationAccepted)
                .TakeLast(27)
                .Concat(_events.Where(item => item.Kind ==
                    MachineLearningActivityKind.ObservationAccepted).TakeLast(3))
                .OrderByDescending(item => item.OccurredAt)
                .Take(30)
                .ToArray();
            var status = learning.DataHealth ==
                    MachineLearningDataHealth.PersistenceTemporarilyUnavailable
                ? MachineLearningActivityStatus.Unavailable
                : !restored && learning.ObservationCount == 0
                    ? MachineLearningActivityStatus.Starting
                    : learning.IsDirty && learning.LastPersistedAt is not null &&
                      now - learning.LastPersistedAt.Value >
                        MachineLearningService.PersistenceInterval + TimeSpan.FromMinutes(2)
                        ? MachineLearningActivityStatus.PersistenceDelayed
                        : learning.Diagnostics.LastAcceptedObservationAt is not null &&
                          now - learning.Diagnostics.LastAcceptedObservationAt.Value <=
                            MachineLearningService.ObservationInterval + TimeSpan.FromMinutes(1)
                            ? MachineLearningActivityStatus.Active
                            : MachineLearningActivityStatus.Waiting;
            return new MachineLearningActivitySnapshot(status, recent,
                persisted?.OccurredAt, persisted?.ObservationCount, restored);
        }
    }

    private void Prune(DateTimeOffset now)
    {
        _events = _events.Where(item => now - item.OccurredAt <=
            (item.Kind is MachineLearningActivityKind.ObservationAccepted or
                MachineLearningActivityKind.ObservationSkipped
                ? DetailedRetention : ImportantRetention))
            .TakeLast(MaximumEventCount).ToList();
    }
}
