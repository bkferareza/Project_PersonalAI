using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Machine.Core;

public sealed class MachineHealthHistoryService
{
    public const int PersistenceSchemaVersion = 1;
    public const int MaximumIncidentCount = 100;
    public const int MaximumKnownIncidentFingerprintCount = 512;
    public static readonly TimeSpan PersistenceInterval =
        TimeSpan.FromMinutes(10);
    public static readonly TimeSpan PersistenceFailureRetryInterval =
        TimeSpan.FromMinutes(5);

    private readonly Queue<string> _knownIncidentFingerprints = new();
    private readonly HashSet<string> _knownIncidentFingerprintSet =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);
    private MachineWindowsUpdateMemory? _windowsUpdate;
    private MachineRebootPendingMemory? _rebootPending;
    private MachineReliabilityMemory? _reliability;
    private long _lifetimeObservedIncidentCount;
    private DateTimeOffset? _firstObservedAt;
    private DateTimeOffset? _lastObservedAt;
    private DateTimeOffset? _lastPersistedAt;
    private DateTimeOffset? _nextPersistenceAttemptAt;
    private long _changeVersion = 1;
    private bool _isDirty = true;
    private bool _recoveredFromInvalidState;
    private MachineHealthHistoryDataStatus _dataStatus =
        MachineHealthHistoryDataStatus.NotYetPersisted;

    public MachineHealthHistorySnapshot GetSnapshot() => new(
        _windowsUpdate,
        _rebootPending,
        _reliability,
        _lifetimeObservedIncidentCount,
        _firstObservedAt,
        _lastObservedAt,
        _lastPersistedAt,
        _isDirty,
        _dataStatus);

    public void Observe(
        MachineWindowsUpdateSnapshot? windowsUpdate,
        MachineRebootPendingSnapshot? rebootPending,
        MachineReliabilitySnapshot? reliability,
        DateTimeOffset observedAt)
    {
        var changed = false;
        if (windowsUpdate?.VerifiedAt is { } updateVerifiedAt)
        {
            _windowsUpdate = new MachineWindowsUpdateMemory(
                windowsUpdate.UpdateState,
                windowsUpdate.PendingUpdateCount,
                updateVerifiedAt,
                windowsUpdate.LastSuccessfulUpdateInstall,
                windowsUpdate.DataStatus);
            changed = true;
        }

        if (rebootPending is not null)
        {
            _rebootPending = new MachineRebootPendingMemory(
                rebootPending.IsPending,
                rebootPending.Reasons
                    .Distinct()
                    .Take(MachineRebootPendingAggregator.MaximumReasonCount)
                    .ToArray(),
                rebootPending.CapturedAt,
                rebootPending.IsPartial);
            changed = true;
        }

        if (reliability?.VerifiedAt is { } reliabilityVerifiedAt)
        {
            var safeIncidents = reliability.Incidents
                .Select(MachineReliabilityAggregator.NormalizeIncident)
                .Where(incident => incident is not null)
                .Select(incident => incident!)
                .OrderByDescending(incident => incident.OccurredAt)
                .Take(MaximumIncidentCount)
                .ToArray();
            foreach (var incident in safeIncidents)
            {
                var fingerprint = CreateIncidentFingerprint(incident);
                if (_knownIncidentFingerprintSet.Add(fingerprint))
                {
                    _knownIncidentFingerprints.Enqueue(fingerprint);
                    _lifetimeObservedIncidentCount = SaturatingIncrement(
                        _lifetimeObservedIncidentCount);
                }
            }

            TrimFingerprints();
            _reliability = new MachineReliabilityMemory(
                reliability.Summary,
                safeIncidents,
                reliability.LastUnexpectedShutdownAt,
                reliability.LastVerifiedHardwareFailureAt,
                reliability.DataStatus,
                reliabilityVerifiedAt);
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        _firstObservedAt ??= observedAt;
        _lastObservedAt = observedAt;
        MarkDirty();
    }

    public async Task LoadAsync(
        IMachineHealthHistoryStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var state = await store.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var status = (store as IMachineHealthHistoryStoreDiagnostics)?
            .LastLoadStatus;

        if (state is null)
        {
            _recoveredFromInvalidState = status ==
                MachineHealthHistoryStoreLoadStatus.Corrupt;
            _dataStatus = status switch
            {
                MachineHealthHistoryStoreLoadStatus.Corrupt =>
                    MachineHealthHistoryDataStatus
                        .RecoveredFromInvalidState,
                MachineHealthHistoryStoreLoadStatus.Unavailable =>
                    MachineHealthHistoryDataStatus
                        .PersistenceTemporarilyUnavailable,
                MachineHealthHistoryStoreLoadStatus.Incompatible =>
                    MachineHealthHistoryDataStatus
                        .PersistenceTemporarilyUnavailable,
                _ => MachineHealthHistoryDataStatus.NotYetPersisted
            };
            return;
        }

        var validation = ValidatePersistedState(state);
        if (validation != MachinePersistenceValidationResult.Accepted)
        {
            _recoveredFromInvalidState = validation ==
                MachinePersistenceValidationResult.Rejected;
            _dataStatus = validation ==
                MachinePersistenceValidationResult.Incompatible
                    ? MachineHealthHistoryDataStatus
                        .PersistenceTemporarilyUnavailable
                    : MachineHealthHistoryDataStatus
                        .RecoveredFromInvalidState;
            return;
        }

        _windowsUpdate = state.WindowsUpdate;
        _rebootPending = state.RebootPending is null
            ? null
            : state.RebootPending with
            {
                Reasons = state.RebootPending.Reasons
                    .Where(reason => Enum.IsDefined(reason) &&
                        reason != MachineRebootPendingReason.Unknown)
                    .Distinct()
                    .Take(MachineRebootPendingAggregator.MaximumReasonCount)
                    .ToArray()
            };
        _reliability = NormalizeReliabilityMemory(state.Reliability);
        _lifetimeObservedIncidentCount =
            state.LifetimeObservedIncidentCount;
        _firstObservedAt = state.FirstObservedAt;
        _lastObservedAt = state.LastObservedAt;
        _lastPersistedAt = state.PersistedAt;
        _knownIncidentFingerprints.Clear();
        _knownIncidentFingerprintSet.Clear();
        foreach (var fingerprint in state.KnownIncidentFingerprints
            .Where(IsValidFingerprint)
            .TakeLast(MaximumKnownIncidentFingerprintCount))
        {
            if (_knownIncidentFingerprintSet.Add(fingerprint))
            {
                _knownIncidentFingerprints.Enqueue(fingerprint);
            }
        }

        _isDirty = false;
        _recoveredFromInvalidState = false;
        _dataStatus = MachineHealthHistoryDataStatus.Healthy;
    }

    public Task<bool> SaveIfDueAsync(
        IMachineHealthHistoryStore store,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        SaveIfDueAsync(store, now, force: false, cancellationToken);

    public async Task<bool> SaveIfDueAsync(
        IMachineHealthHistoryStore store,
        DateTimeOffset now,
        bool force,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!_isDirty ||
            !force &&
            ((_nextPersistenceAttemptAt is not null &&
              now < _nextPersistenceAttemptAt.Value) ||
             (_lastPersistedAt is not null &&
              now - _lastPersistedAt.Value < PersistenceInterval)))
        {
            return false;
        }

        var snapshotVersion = _changeVersion;
        var state = CreatePersistedState(now);
        await _persistenceGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            try
            {
                await store.SaveAsync(state, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                _dataStatus = MachineHealthHistoryDataStatus
                    .PersistenceTemporarilyUnavailable;
                _nextPersistenceAttemptAt =
                    now + PersistenceFailureRetryInterval;
                return false;
            }

            _lastPersistedAt = now;
            _nextPersistenceAttemptAt = null;
            _dataStatus = _recoveredFromInvalidState
                ? MachineHealthHistoryDataStatus.RecoveredFromInvalidState
                : MachineHealthHistoryDataStatus.Healthy;
            if (_changeVersion == snapshotVersion)
            {
                _isDirty = false;
            }
            return true;
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    public Task<bool> SaveFinalSnapshotAsync(
        IMachineHealthHistoryStore store,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        SaveIfDueAsync(store, now, force: true, cancellationToken);

    private MachineHealthHistoryPersistedState CreatePersistedState(
        DateTimeOffset persistedAt) => new(
        PersistenceSchemaVersion,
        _windowsUpdate,
        _rebootPending,
        _reliability,
        _lifetimeObservedIncidentCount,
        _knownIncidentFingerprints.ToArray(),
        _firstObservedAt,
        _lastObservedAt,
        persistedAt);

    internal static MachinePersistenceValidationResult
        ValidatePersistedState(MachineHealthHistoryPersistedState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion > PersistenceSchemaVersion)
        {
            return MachinePersistenceValidationResult.Incompatible;
        }

        if (state.SchemaVersion != PersistenceSchemaVersion)
        {
            return MachinePersistenceValidationResult.Rejected;
        }

        return IsValidState(state)
            ? MachinePersistenceValidationResult.Accepted
            : MachinePersistenceValidationResult.Rejected;
    }

    private static bool IsValidState(
        MachineHealthHistoryPersistedState state) =>
        state.LifetimeObservedIncidentCount >= 0 &&
        state.KnownIncidentFingerprints is not null &&
        state.KnownIncidentFingerprints.Count <=
            MaximumKnownIncidentFingerprintCount &&
        (state.Reliability is null ||
         state.Reliability.RecentIncidents is not null &&
         state.Reliability.RecentIncidents.Count <= MaximumIncidentCount) &&
        (state.WindowsUpdate is null ||
         Enum.IsDefined(state.WindowsUpdate.State) &&
         Enum.IsDefined(state.WindowsUpdate.DataStatus) &&
         state.WindowsUpdate.PendingUpdateCount is null or >= 0) &&
        (state.RebootPending is null ||
         state.RebootPending.Reasons is not null &&
         state.RebootPending.Reasons.Count <=
            MachineRebootPendingAggregator.MaximumReasonCount &&
         state.RebootPending.Reasons.All(Enum.IsDefined)) &&
        (state.Reliability is null ||
         state.Reliability.Summary is not null &&
         Enum.IsDefined(state.Reliability.DataStatus) &&
         state.Reliability.RecentIncidents is not null) &&
        (state.FirstObservedAt is null || state.LastObservedAt is null ||
         state.FirstObservedAt <= state.LastObservedAt);

    private static MachineReliabilityMemory? NormalizeReliabilityMemory(
        MachineReliabilityMemory? memory)
    {
        if (memory is null)
        {
            return null;
        }

        var normalized = MachineReliabilityAggregator.Aggregate(
            memory.RecentIncidents,
            memory.VerifiedAt,
            memory.DataStatus,
            verifiedAt: memory.VerifiedAt);
        return new MachineReliabilityMemory(
            normalized.Summary,
            normalized.Incidents,
            memory.LastUnexpectedShutdown,
            memory.LastVerifiedHardwareFailure,
            normalized.DataStatus,
            memory.VerifiedAt);
    }

    private static string CreateIncidentFingerprint(
        MachineReliabilityIncident incident)
    {
        var identity = string.Join(
            '|',
            incident.OccurredAt.UtcTicks,
            incident.Category,
            incident.ApplicationName?.ToUpperInvariant(),
            incident.UpdateIdentifier?.ToUpperInvariant(),
            incident.EventId,
            incident.SummaryCode,
            incident.CorrelationId?.ToUpperInvariant());
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(identity)));
    }

    private void TrimFingerprints()
    {
        while (_knownIncidentFingerprints.Count >
            MaximumKnownIncidentFingerprintCount)
        {
            _knownIncidentFingerprintSet.Remove(
                _knownIncidentFingerprints.Dequeue());
        }
    }

    private static bool IsValidFingerprint(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static long SaturatingIncrement(long value) =>
        value == long.MaxValue ? long.MaxValue : value + 1;

    private void MarkDirty()
    {
        _changeVersion = SaturatingIncrement(_changeVersion);
        _isDirty = true;
    }
}
