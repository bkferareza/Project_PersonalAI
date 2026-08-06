namespace Machine.Core;

public static class MachineInsightContextFingerprint
{
    public static string Create(MachineFindingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Findings);

        var findingKeys = snapshot.Findings
            .Select(finding =>
                $"{finding.Code}:{finding.Severity}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal);

        return $"{snapshot.OverallState}|" +
            string.Join(';', findingKeys);
    }
}
