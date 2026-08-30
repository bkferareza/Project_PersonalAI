namespace Machine.Core;

public static class MachineExplanationFallbackComposer
{
    public static string Compose(MachineFindingsSnapshot? findings)
    {
        if (findings is null ||
            findings.OverallState == MachineOverallState.Unknown)
        {
            return "There is not enough verified data to determine the current state.";
        }

        var primaryFinding = findings.Findings
            .Where(finding =>
                finding.Severity != MachineFindingSeverity.Info)
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(primaryFinding?.Detail))
        {
            return primaryFinding.Detail;
        }

        var observation = findings.Findings
            .Where(finding =>
                finding.Severity == MachineFindingSeverity.Info)
            .OrderBy(finding => finding.Code, StringComparer.Ordinal)
            .Select(finding => ComposeObservation(finding.Code))
            .FirstOrDefault(text => text is not null);

        if (observation is not null)
        {
            return observation;
        }

        return findings.OverallState switch
        {
            MachineOverallState.Stable =>
                "No deterministic issue is visible in the current snapshot.",
            MachineOverallState.Attention =>
                "A verified condition currently needs attention.",
            MachineOverallState.Warning =>
                "The current snapshot contains a verified warning condition.",
            MachineOverallState.Critical =>
                "The current snapshot contains a verified critical condition.",
            _ =>
                "There is not enough verified data to determine the current state."
        };
    }

    private static string? ComposeObservation(string code) => code switch
    {
        "data.folder-scan.partial" =>
            "The storage inspection is partial, so measured folder sizes " +
                "are lower bounds.",
        "data.software.classic.partial" =>
            "The latest classic software inventory is partial.",
        "data.software.packaged.partial" =>
            "The latest packaged-software inventory is partial.",
        "data.startup.partial" =>
            "The latest startup inventory is partial.",
        "health.restart.pending" =>
            "Windows reports a pending restart.",
        "data.windows-update.partial" =>
            "The latest Windows Update status is partial.",
        "data.reboot-pending.partial" =>
            "The latest restart evidence is partial.",
        "data.reliability.partial" =>
            "The latest Windows reliability history is partial.",
        _ => null
    };
}
