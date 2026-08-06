using System.Globalization;

namespace Machine.Core;

public static class MachineExplanationOpeningComposer
{
    private const string CpuFindingCode = "cpu.usage.high";
    private const string MemoryFindingCode = "memory.usage.high";
    private const string StorageFindingCode =
        "storage.system-volume.low-free-space";

    public static string Compose(
        MachineFindingsSnapshot? findings,
        MachineResourceSnapshot? resources,
        MachineStorageExplanationContext? storage)
    {
        if (findings is null ||
            findings.OverallState == MachineOverallState.Unknown)
        {
            return "Hindi sapat ang current data para matukoy " +
                "ang overall state.";
        }

        if (findings.OverallState == MachineOverallState.Stable)
        {
            return "Stable ako ngayon.";
        }

        var severity = findings.OverallState switch
        {
            MachineOverallState.Attention =>
                MachineFindingSeverity.Attention,
            MachineOverallState.Warning =>
                MachineFindingSeverity.Warning,
            MachineOverallState.Critical =>
                MachineFindingSeverity.Critical,
            _ => (MachineFindingSeverity?)null
        };

        var applicableFindings = (findings.Findings ?? [])
            .Where(finding => finding.Severity == severity)
            .OrderBy(finding => finding.Code, StringComparer.Ordinal);

        foreach (var finding in applicableFindings)
        {
            var opening = ComposeForFinding(
                findings.OverallState,
                finding.Code,
                resources,
                storage);

            if (opening is not null)
            {
                return opening;
            }
        }

        return findings.OverallState switch
        {
            MachineOverallState.Attention =>
                "May condition akong kailangang bantayan ngayon.",
            MachineOverallState.Warning =>
                "Under pressure ako ngayon.",
            MachineOverallState.Critical =>
                "May critical condition akong nakikita ngayon.",
            _ => "Hindi sapat ang current data para matukoy " +
                "ang overall state."
        };
    }

    private static string? ComposeForFinding(
        MachineOverallState overallState,
        string code,
        MachineResourceSnapshot? resources,
        MachineStorageExplanationContext? storage)
    {
        if (code == CpuFindingCode &&
            resources is not null &&
            IsValidPercentage(resources.CpuUsagePercent))
        {
            return ComposeUsageOpening(
                overallState,
                "CPU",
                resources.CpuUsagePercent);
        }

        if (code == MemoryFindingCode &&
            TryGetMemoryUsagePercent(resources, out var memoryPercent))
        {
            return ComposeUsageOpening(
                overallState,
                "memory",
                memoryPercent);
        }

        if (code == StorageFindingCode)
        {
            if (overallState == MachineOverallState.Critical)
            {
                return "May critical storage condition akong " +
                    "nakikita ngayon.";
            }

            if (TryGetAvailableStoragePercent(
                storage,
                out var availablePercent))
            {
                var formattedPercent = FormatPercent(
                    availablePercent);

                return overallState == MachineOverallState.Warning
                    ? "Mababa na ang system-volume free space ko—" +
                        $"{formattedPercent}% na lang ang available."
                    : "Medyo limitado ang storage headroom ko—" +
                        $"{formattedPercent}% na lang ang free space.";
            }
        }

        return null;
    }

    private static string ComposeUsageOpening(
        MachineOverallState overallState,
        string metricName,
        double usagePercent)
    {
        var stateText = overallState == MachineOverallState.Warning
            ? "Under pressure ako ngayon"
            : "Medyo busy ako ngayon";

        return $"{stateText}—{FormatPercent(usagePercent)}% ang " +
            $"{metricName} usage.";
    }

    private static bool TryGetMemoryUsagePercent(
        MachineResourceSnapshot? resources,
        out double usagePercent)
    {
        usagePercent = 0d;

        if (resources is null ||
            resources.TotalMemoryBytes == 0 ||
            resources.UsedMemoryBytes > resources.TotalMemoryBytes)
        {
            return false;
        }

        usagePercent = resources.UsedMemoryBytes /
            (double)resources.TotalMemoryBytes * 100d;
        return IsValidPercentage(usagePercent);
    }

    private static bool TryGetAvailableStoragePercent(
        MachineStorageExplanationContext? storage,
        out double availablePercent)
    {
        availablePercent = 0d;

        if (storage is null ||
            storage.TotalSizeBytes <= 0 ||
            storage.AvailableSizeBytes < 0 ||
            storage.AvailableSizeBytes > storage.TotalSizeBytes)
        {
            return false;
        }

        availablePercent = storage.AvailableSizeBytes /
            (double)storage.TotalSizeBytes * 100d;
        return IsValidPercentage(availablePercent);
    }

    private static bool IsValidPercentage(double value) =>
        double.IsFinite(value) && value is >= 0d and <= 100d;

    private static string FormatPercent(double value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture);
}
