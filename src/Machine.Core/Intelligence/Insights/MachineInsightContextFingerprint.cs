namespace Machine.Core;

public static class MachineInsightContextFingerprint
{
    public static string Create(MachineFindingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Findings);

        var triggerFindings = snapshot.Findings
            .Where(finding => !IsPassiveHealthFinding(finding.Code))
            .ToArray();
        var findingKeys = triggerFindings
            .Select(finding =>
                $"{finding.Code}:{finding.Severity}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal);

        var triggerState = snapshot.OverallState == MachineOverallState.Unknown
            ? MachineOverallState.Unknown
            : GetTriggerState(triggerFindings);
        return $"{triggerState}|" +
            string.Join(';', findingKeys);
    }

    private static bool IsPassiveHealthFinding(string code) =>
        code.StartsWith("health.", StringComparison.Ordinal) ||
        code is "data.windows-update.partial" or
            "data.reboot-pending.partial" or
            "data.reliability.partial";

    private static MachineOverallState GetTriggerState(
        IEnumerable<MachineFinding> findings)
    {
        var severity = findings
            .Where(finding => finding.Severity != MachineFindingSeverity.Info)
            .Select(finding => (MachineFindingSeverity?)finding.Severity)
            .OrderByDescending(value => value)
            .FirstOrDefault();
        return severity switch
        {
            MachineFindingSeverity.Critical => MachineOverallState.Critical,
            MachineFindingSeverity.Warning => MachineOverallState.Warning,
            MachineFindingSeverity.Attention => MachineOverallState.Attention,
            _ => MachineOverallState.Stable
        };
    }
}
