namespace Machine.Core;

public sealed class MachineInsightArbiter
{
    public static readonly TimeSpan RepeatSignalCooldown =
        TimeSpan.FromHours(6);

    private const int MaximumRememberedSignals = 64;
    private readonly Dictionary<string, DateTimeOffset> _lastSignaledAt =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _surfacedEligibleIds =
        new(StringComparer.Ordinal);
    private MachineInsightCandidate? _currentInsight;
    private bool _hasNewUnseenInsight;

    public MachineInsightCandidate? CurrentInsight => _currentInsight;

    public bool HasNewUnseenInsight => _hasNewUnseenInsight;

    public MachineInsightArbitrationSnapshot Evaluate(
        IEnumerable<MachineInsightCandidate> candidates,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var eligible = candidates
            .Where(candidate => candidate is not null &&
                candidate.IsEligibleAt(now))
            .GroupBy(candidate => candidate.Id, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(candidate => candidate.CreatedAt)
                .First())
            .ToArray();
        var eligibleIds = eligible
            .Select(candidate => candidate.Id)
            .ToHashSet(StringComparer.Ordinal);
        _surfacedEligibleIds.IntersectWith(eligibleIds);
        var selected = eligible
            .OrderByDescending(candidate => candidate.Importance)
            .ThenByDescending(candidate => GetKindPriority(candidate.Kind))
            .ThenByDescending(candidate => candidate.CreatedAt)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        if (selected is null)
        {
            _currentInsight = null;
            _hasNewUnseenInsight = false;
        }
        else
        {
            var sameCurrent = string.Equals(
                selected.Id,
                _currentInsight?.Id,
                StringComparison.Ordinal);
            var firstSurfacingDuringEligibility =
                _surfacedEligibleIds.Add(selected.Id);

            if (!sameCurrent)
            {
                _hasNewUnseenInsight = false;
            }

            _currentInsight = selected;
            if (firstSurfacingDuringEligibility &&
                selected.CanSignalNew &&
                CooldownHasElapsed(selected.Id, now))
            {
                _lastSignaledAt[selected.Id] = now;
                _hasNewUnseenInsight = true;
            }
        }

        PruneRememberedSignals(now, eligibleIds);
        return GetSnapshot();
    }

    public MachineInsightArbitrationSnapshot MarkCurrentViewed()
    {
        _hasNewUnseenInsight = false;
        return GetSnapshot();
    }

    private MachineInsightArbitrationSnapshot GetSnapshot() => new(
        _currentInsight,
        _hasNewUnseenInsight);

    private bool CooldownHasElapsed(string id, DateTimeOffset now) =>
        !_lastSignaledAt.TryGetValue(id, out var lastSignaledAt) ||
        now - lastSignaledAt >= RepeatSignalCooldown;

    private void PruneRememberedSignals(
        DateTimeOffset now,
        IReadOnlySet<string> eligibleIds)
    {
        var obsolete = _lastSignaledAt
            .Where(pair => !eligibleIds.Contains(pair.Key) &&
                now - pair.Value >= RepeatSignalCooldown)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var id in obsolete)
        {
            _lastSignaledAt.Remove(id);
        }

        if (_lastSignaledAt.Count <= MaximumRememberedSignals)
        {
            return;
        }

        foreach (var id in _lastSignaledAt
            .OrderBy(pair => pair.Value)
            .Take(_lastSignaledAt.Count - MaximumRememberedSignals)
            .Select(pair => pair.Key)
            .ToArray())
        {
            _lastSignaledAt.Remove(id);
        }
    }

    private static int GetKindPriority(MachineInsightKind kind) => kind switch
    {
        MachineInsightKind.MachineFinding => 4,
        MachineInsightKind.LearnedEnergyDeviation => 3,
        MachineInsightKind.RunningBill => 2,
        _ => 1
    };
}
