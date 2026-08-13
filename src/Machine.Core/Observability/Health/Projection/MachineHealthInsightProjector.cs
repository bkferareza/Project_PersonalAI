namespace Machine.Core;

public static class MachineHealthInsightProjector
{
    public const int MaximumRebootReasonCount = 4;

    public static MachineHealthInsightContext? Project(
        MachineWindowsUpdateSnapshot? windowsUpdate,
        MachineRebootPendingSnapshot? rebootPending,
        MachineReliabilitySnapshot? reliability)
    {
        if (windowsUpdate is null && rebootPending is null &&
            reliability is null)
        {
            return null;
        }

        var verifiedReliability = reliability?.VerifiedAt is null
            ? null
            : reliability;
        var mostRecentSignificant = verifiedReliability?.Incidents
            .Where(incident => incident.Severity is
                MachineReliabilityIncidentSeverity.Significant or
                MachineReliabilityIncidentSeverity.Severe)
            .OrderByDescending(incident => incident.OccurredAt)
            .FirstOrDefault();
        var recurring = verifiedReliability?.Summary.RecurringApplications
            .OrderByDescending(item => item.IncidentCountLast7Days)
            .ThenByDescending(item => item.IncidentCountLast30Days)
            .FirstOrDefault();

        return new MachineHealthInsightContext(
            UpdateState: windowsUpdate?.VerifiedAt is null
                ? null
                : windowsUpdate.UpdateState,
            PendingUpdateCount: windowsUpdate?.VerifiedAt is null
                ? null
                : windowsUpdate.PendingUpdateCount,
            UpdateVerifiedAt: windowsUpdate?.VerifiedAt,
            IsRebootPending: rebootPending?.IsPending,
            RebootReasons: (rebootPending?.Reasons ?? [])
                .Distinct()
                .Take(MaximumRebootReasonCount)
                .ToArray(),
            ReliabilityLast7Days:
                verifiedReliability?.Summary.Last7Days,
            MostRecentSignificantIncident: mostRecentSignificant,
            RecurringApplicationFailure: recurring,
            ReliabilityDataStatus: reliability?.DataStatus ??
                MachineHealthDataStatus.Unavailable,
            RebootVerifiedAt: rebootPending?.CapturedAt,
            ReliabilityVerifiedAt: reliability?.VerifiedAt,
            RebootConfidence: rebootPending?.Confidence ??
                MachineRebootPendingConfidence.Unknown);
    }
}
