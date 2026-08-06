namespace Machine.Core;

public static class MachineExplanationFallbackComposer
{
    public static string Compose(
        string requiredOpening,
        MachineFindingsSnapshot? findings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredOpening);

        var observation = (findings?.Findings ?? [])
            .Where(finding =>
                finding.Severity == MachineFindingSeverity.Info)
            .OrderBy(finding => finding.Code, StringComparer.Ordinal)
            .Select(finding => ComposeObservation(finding.Code))
            .FirstOrDefault(text => text is not null);

        return observation is null
            ? requiredOpening
            : $"{requiredOpening} {observation}";
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
        _ => null
    };
}
