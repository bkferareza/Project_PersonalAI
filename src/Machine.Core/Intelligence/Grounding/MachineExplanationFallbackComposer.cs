namespace Machine.Core;

public static class MachineExplanationFallbackComposer
{
    public static string Compose(MachineFindingsSnapshot? findings)
    {
        if (findings is null ||
            findings.OverallState == MachineOverallState.Unknown)
        {
            return "Kulang ang verified data para matukoy ang current state.";
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
                "Wala akong nakikitang deterministic issue sa current snapshot.",
            MachineOverallState.Attention =>
                "May verified condition na kailangan kong bantayan.",
            MachineOverallState.Warning =>
                "May verified warning condition sa current snapshot.",
            MachineOverallState.Critical =>
                "May verified critical condition sa current snapshot.",
            _ =>
                "Kulang ang verified data para matukoy ang current state."
        };
    }

    private static string? ComposeObservation(string code) => code switch
    {
        "data.folder-scan.partial" =>
            "Partial pa ang storage inspection, kaya lower bounds " +
                "lang ang measured folder sizes.",
        "data.software.classic.partial" =>
            "Partial ang latest classic software inventory.",
        "data.software.packaged.partial" =>
            "Partial ang latest packaged-software inventory.",
        "data.startup.partial" =>
            "Partial ang latest startup inventory.",
        "health.restart.pending" =>
            "May pending Windows restart na recorded.",
        "data.windows-update.partial" =>
            "Partial ang latest Windows Update status.",
        "data.reboot-pending.partial" =>
            "Partial ang latest restart evidence.",
        "data.reliability.partial" =>
            "Partial ang latest Windows reliability history.",
        _ => null
    };
}
