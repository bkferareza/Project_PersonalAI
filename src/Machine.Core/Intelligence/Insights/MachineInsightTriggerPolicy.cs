namespace Machine.Core;

public enum MachineInsightTriggerReason
{
    None,
    StateChanged,
    FindingsChanged,
    Recovery,
    DashboardOpened,
    Manual
}

public sealed record MachineInsightTriggerDecision(
    bool ShouldGenerate,
    MachineInsightTriggerReason Reason,
    string ContextFingerprint,
    long ContextVersion)
{
    public bool IsAutomatic =>
        ShouldGenerate && Reason != MachineInsightTriggerReason.Manual;
}

public sealed class MachineInsightTriggerPolicy
{
    public const int RequiredEquivalentEvaluationCount = 2;

    public static readonly TimeSpan AutomaticCooldown =
        TimeSpan.FromMinutes(2);

    private static readonly MachineInsightTriggerDecision NoGeneration =
        new(
            ShouldGenerate: false,
            Reason: MachineInsightTriggerReason.None,
            ContextFingerprint: string.Empty,
            ContextVersion: 0);

    private string? _candidateFingerprint;
    private int _candidateEvaluationCount;
    private string? _currentFingerprint;
    private MachineOverallState _currentState =
        MachineOverallState.Unknown;
    private long _currentContextVersion;
    private long? _currentInsightVersion;
    private long? _lastRequestedVersion;
    private DateTimeOffset? _lastAutomaticRequestAt;
    private MachineInsightTriggerDecision? _activeRequest;
    private PendingAutomaticRequest? _pendingAutomaticRequest;

    public bool IsRequestInFlight => _activeRequest is not null;

    public bool HasInsightForCurrentContext =>
        _currentContextVersion > 0 &&
        _currentInsightVersion == _currentContextVersion;

    public MachineInsightTriggerDecision ObserveTelemetry(
        MachineFindingsSnapshot snapshot,
        DateTimeOffset observedAt,
        bool isOllamaOnline,
        bool allowAutomaticGeneration = true)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var fingerprint =
            MachineInsightContextFingerprint.Create(snapshot);

        if (string.Equals(
            fingerprint,
            _candidateFingerprint,
            StringComparison.Ordinal))
        {
            _candidateEvaluationCount++;
        }
        else
        {
            _candidateFingerprint = fingerprint;
            _candidateEvaluationCount = 1;
        }

        if (_candidateEvaluationCount <
            RequiredEquivalentEvaluationCount)
        {
            return NoGeneration;
        }

        if (_currentFingerprint is null)
        {
            EstablishContext(snapshot, fingerprint);
            return NoGeneration;
        }

        if (string.Equals(
            fingerprint,
            _currentFingerprint,
            StringComparison.Ordinal))
        {
            if (!allowAutomaticGeneration)
            {
                _pendingAutomaticRequest = null;
                return NoGeneration;
            }

            return TryBeginPendingAutomaticRequest(
                observedAt,
                isOllamaOnline);
        }

        var previousState = _currentState;
        EstablishContext(snapshot, fingerprint);

        if (!allowAutomaticGeneration)
        {
            _pendingAutomaticRequest = null;
            return NoGeneration;
        }

        var reason = GetChangeReason(
            previousState,
            snapshot.OverallState);

        return TryBeginAutomaticRequest(
            reason,
            observedAt,
            isOllamaOnline);
    }

    public void EstablishBaseline(MachineFindingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var fingerprint =
            MachineInsightContextFingerprint.Create(snapshot);

        if (_currentFingerprint is null ||
            !string.Equals(
                fingerprint,
                _currentFingerprint,
                StringComparison.Ordinal))
        {
            EstablishContext(snapshot, fingerprint);
        }
        else
        {
            _candidateFingerprint = fingerprint;
            _candidateEvaluationCount =
                RequiredEquivalentEvaluationCount;
        }

        _pendingAutomaticRequest = null;
    }

    public MachineInsightTriggerDecision RequestForDashboard(
        MachineFindingsSnapshot snapshot,
        DateTimeOffset requestedAt,
        bool isOllamaOnline)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var fingerprint =
            MachineInsightContextFingerprint.Create(snapshot);

        if (_currentFingerprint is null)
        {
            EstablishContext(snapshot, fingerprint);
        }
        else if (!string.Equals(
            fingerprint,
            _currentFingerprint,
            StringComparison.Ordinal))
        {
            return NoGeneration;
        }

        if (HasInsightForCurrentContext ||
            _lastRequestedVersion == _currentContextVersion)
        {
            return NoGeneration;
        }

        return TryBeginAutomaticRequest(
            MachineInsightTriggerReason.DashboardOpened,
            requestedAt,
            isOllamaOnline);
    }

    public MachineInsightTriggerDecision RequestManual(
        MachineFindingsSnapshot snapshot,
        bool isOllamaOnline)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!isOllamaOnline || IsRequestInFlight)
        {
            return NoGeneration;
        }

        var fingerprint =
            MachineInsightContextFingerprint.Create(snapshot);

        if (_currentFingerprint is null ||
            !string.Equals(
                fingerprint,
                _currentFingerprint,
                StringComparison.Ordinal))
        {
            EstablishContext(snapshot, fingerprint);
        }

        return BeginRequest(MachineInsightTriggerReason.Manual);
    }

    public MachineInsightTriggerDecision CompleteRequest(
        MachineInsightTriggerDecision request,
        bool insightAccepted,
        DateTimeOffset completedAt,
        bool isOllamaOnline)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_activeRequest is null ||
            !_activeRequest.Equals(request))
        {
            throw new InvalidOperationException(
                "The insight request is not active.");
        }

        if (insightAccepted && IsCurrentContext(request))
        {
            _currentInsightVersion = request.ContextVersion;
        }

        _activeRequest = null;

        return TryBeginPendingAutomaticRequest(
            completedAt,
            isOllamaOnline);
    }

    public bool IsCurrentContext(
        MachineInsightTriggerDecision request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.ContextVersion == _currentContextVersion &&
            string.Equals(
                request.ContextFingerprint,
                _currentFingerprint,
                StringComparison.Ordinal);
    }

    private void EstablishContext(
        MachineFindingsSnapshot snapshot,
        string fingerprint)
    {
        _currentFingerprint = fingerprint;
        _currentState = snapshot.OverallState;
        _currentContextVersion++;
        _candidateFingerprint = fingerprint;
        _candidateEvaluationCount =
            RequiredEquivalentEvaluationCount;

        if (_activeRequest is not null &&
            !IsCurrentContext(_activeRequest))
        {
            _pendingAutomaticRequest = null;
        }
    }

    private MachineInsightTriggerDecision TryBeginAutomaticRequest(
        MachineInsightTriggerReason reason,
        DateTimeOffset requestedAt,
        bool isOllamaOnline)
    {
        if (!isOllamaOnline)
        {
            _pendingAutomaticRequest = null;
            return NoGeneration;
        }

        if (_lastRequestedVersion == _currentContextVersion)
        {
            _pendingAutomaticRequest = null;
            return NoGeneration;
        }

        if (IsRequestInFlight || !CooldownHasElapsed(requestedAt))
        {
            _pendingAutomaticRequest = new PendingAutomaticRequest(
                reason,
                _currentFingerprint!,
                _currentContextVersion);
            return NoGeneration;
        }

        _pendingAutomaticRequest = null;
        _lastAutomaticRequestAt = requestedAt;
        return BeginRequest(reason);
    }

    private MachineInsightTriggerDecision
        TryBeginPendingAutomaticRequest(
            DateTimeOffset requestedAt,
            bool isOllamaOnline)
    {
        var pending = _pendingAutomaticRequest;

        if (pending is null)
        {
            return NoGeneration;
        }

        if (!isOllamaOnline)
        {
            _pendingAutomaticRequest = null;
            return NoGeneration;
        }

        if (!string.Equals(
                pending.ContextFingerprint,
                _currentFingerprint,
                StringComparison.Ordinal) ||
            pending.ContextVersion != _currentContextVersion ||
            _lastRequestedVersion == _currentContextVersion)
        {
            _pendingAutomaticRequest = null;
            return NoGeneration;
        }

        if (IsRequestInFlight || !CooldownHasElapsed(requestedAt))
        {
            return NoGeneration;
        }

        _pendingAutomaticRequest = null;
        _lastAutomaticRequestAt = requestedAt;
        return BeginRequest(pending.Reason);
    }

    private MachineInsightTriggerDecision BeginRequest(
        MachineInsightTriggerReason reason)
    {
        var decision = new MachineInsightTriggerDecision(
            ShouldGenerate: true,
            Reason: reason,
            ContextFingerprint: _currentFingerprint!,
            ContextVersion: _currentContextVersion);

        _activeRequest = decision;
        _lastRequestedVersion = _currentContextVersion;
        return decision;
    }

    private bool CooldownHasElapsed(DateTimeOffset requestedAt) =>
        _lastAutomaticRequestAt is null ||
        requestedAt - _lastAutomaticRequestAt >= AutomaticCooldown;

    private static MachineInsightTriggerReason GetChangeReason(
        MachineOverallState previousState,
        MachineOverallState currentState)
    {
        if (currentState == MachineOverallState.Stable &&
            previousState is MachineOverallState.Attention or
                MachineOverallState.Warning or
                MachineOverallState.Critical)
        {
            return MachineInsightTriggerReason.Recovery;
        }

        return previousState != currentState
            ? MachineInsightTriggerReason.StateChanged
            : MachineInsightTriggerReason.FindingsChanged;
    }

    private sealed record PendingAutomaticRequest(
        MachineInsightTriggerReason Reason,
        string ContextFingerprint,
        long ContextVersion);
}
