using System.Globalization;

namespace Machine.Core;

public static class MachineFindingsEvaluator
{
    private const double CpuAttentionPercent = 70d;
    private const double CpuWarningPercent = 90d;
    private const double MemoryAttentionPercent = 80d;
    private const double MemoryWarningPercent = 90d;
    private const double StorageAttentionPercent = 10d;
    private const double StorageWarningPercent = 5d;
    private const double StorageCriticalPercent = 1d;
    private const long BytesPerGibibyte = 1024L * 1024L * 1024L;
    private const long StorageAttentionBytes =
        20L * BytesPerGibibyte;
    private const long StorageWarningBytes =
        5L * BytesPerGibibyte;
    private const long StorageCriticalBytes = BytesPerGibibyte;

    public static MachineFindingsSnapshot Evaluate(
        MachineFindingsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var findings = new List<MachineFinding>();

        EvaluateResources(input.Resources, findings);

        var systemVolume = input.Storage?.Volumes
            .FirstOrDefault(volume =>
                volume.IsSystemVolume &&
                IsReadableSystemVolume(volume));

        if (systemVolume is not null)
        {
            EvaluateSystemVolume(systemVolume, findings);
        }

        EvaluateDataQuality(input, findings);

        var orderedFindings = findings
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .ToArray();
        var hasVerifiedCapacityState =
            input.Resources is not null || systemVolume is not null;
        var overallState = hasVerifiedCapacityState
            ? GetOverallState(orderedFindings)
            : MachineOverallState.Unknown;

        return new MachineFindingsSnapshot(
            OverallState: overallState,
            Findings: orderedFindings);
    }

    private static void EvaluateResources(
        MachineResourceSnapshot? resources,
        ICollection<MachineFinding> findings)
    {
        if (resources is null)
        {
            return;
        }

        if (double.IsFinite(resources.CpuUsagePercent) &&
            resources.CpuUsagePercent >= 0d)
        {
            if (resources.CpuUsagePercent >= CpuWarningPercent)
            {
                findings.Add(CreateUsageFinding(
                    code: "cpu.usage.high",
                    severity: MachineFindingSeverity.Warning,
                    title: "CPU usage is high",
                    metricName: "CPU",
                    usagePercent: resources.CpuUsagePercent));
            }
            else if (resources.CpuUsagePercent >=
                     CpuAttentionPercent)
            {
                findings.Add(CreateUsageFinding(
                    code: "cpu.usage.high",
                    severity: MachineFindingSeverity.Attention,
                    title: "CPU usage is high",
                    metricName: "CPU",
                    usagePercent: resources.CpuUsagePercent));
            }
        }

        if (resources.TotalMemoryBytes == 0 ||
            resources.UsedMemoryBytes > resources.TotalMemoryBytes)
        {
            return;
        }

        var memoryUsagePercent = resources.UsedMemoryBytes /
            (double)resources.TotalMemoryBytes * 100d;

        if (memoryUsagePercent >= MemoryWarningPercent)
        {
            findings.Add(CreateUsageFinding(
                code: "memory.usage.high",
                severity: MachineFindingSeverity.Warning,
                title: "Memory usage is high",
                metricName: "memory",
                usagePercent: memoryUsagePercent));
        }
        else if (memoryUsagePercent >= MemoryAttentionPercent)
        {
            findings.Add(CreateUsageFinding(
                code: "memory.usage.high",
                severity: MachineFindingSeverity.Attention,
                title: "Memory usage is high",
                metricName: "memory",
                usagePercent: memoryUsagePercent));
        }
    }

    private static MachineFinding CreateUsageFinding(
        string code,
        MachineFindingSeverity severity,
        string title,
        string metricName,
        double usagePercent) =>
        new(
            Code: code,
            Severity: severity,
            Title: title,
            Detail: $"Current {metricName} usage is " +
                $"{usagePercent.ToString("F1", CultureInfo.InvariantCulture)}%.");

    private static bool IsReadableSystemVolume(
        MachineStorageVolumeSnapshot volume) =>
        volume.TotalSizeBytes > 0 &&
        volume.AvailableFreeSpaceBytes >= 0 &&
        volume.AvailableFreeSpaceBytes <= volume.TotalSizeBytes;

    private static void EvaluateSystemVolume(
        MachineStorageVolumeSnapshot systemVolume,
        ICollection<MachineFinding> findings)
    {
        var availablePercent =
            systemVolume.AvailableFreeSpaceBytes /
            (double)systemVolume.TotalSizeBytes * 100d;
        MachineFindingSeverity? severity =
            availablePercent <= StorageCriticalPercent ||
            systemVolume.AvailableFreeSpaceBytes <=
                StorageCriticalBytes
                ? MachineFindingSeverity.Critical
                : availablePercent <= StorageWarningPercent ||
                  systemVolume.AvailableFreeSpaceBytes <=
                    StorageWarningBytes
                    ? MachineFindingSeverity.Warning
                    : availablePercent <= StorageAttentionPercent ||
                      systemVolume.AvailableFreeSpaceBytes <=
                        StorageAttentionBytes
                        ? MachineFindingSeverity.Attention
                        : null;

        if (severity is null)
        {
            return;
        }

        var availableGibibytes =
            systemVolume.AvailableFreeSpaceBytes /
            (double)BytesPerGibibyte;

        findings.Add(new MachineFinding(
            Code: "storage.system-volume.low-free-space",
            Severity: severity.Value,
            Title: "System-volume free space is low",
            Detail: "System-volume free space is " +
                $"{availablePercent.ToString("F1", CultureInfo.InvariantCulture)}% " +
                $"({availableGibibytes.ToString("F1", CultureInfo.InvariantCulture)} GiB available)."));
    }

    private static void EvaluateDataQuality(
        MachineFindingsInput input,
        ICollection<MachineFinding> findings)
    {
        var folderInspection = input.FolderInspection;
        if (folderInspection is not null &&
            (!folderInspection.IsComplete ||
             folderInspection.SkippedDirectoryCount > 0 ||
             folderInspection.Folders.Any(folder =>
                 !folder.IsComplete)))
        {
            findings.Add(new MachineFinding(
                Code: "data.folder-scan.partial",
                Severity: MachineFindingSeverity.Info,
                Title: "Storage inspection is partial",
                Detail: folderInspection.SkippedDirectoryCount > 0
                    ? "Measured folder sizes are lower bounds; " +
                        FormatCount(
                            folderInspection.SkippedDirectoryCount,
                            "directory was inaccessible",
                            "directories were inaccessible") + "."
                    : "Measured folder sizes are lower bounds because " +
                        "the latest inspection is partial."));
        }

        if (input.ClassicSoftware is { IsComplete: false } classic)
        {
            findings.Add(new MachineFinding(
                Code: "data.software.classic.partial",
                Severity: MachineFindingSeverity.Info,
                Title: "Classic software inventory is partial",
                Detail: CreatePartialInventoryDetail(
                    classic.SkippedEntryCount,
                    "registration was skipped",
                    "registrations were skipped")));
        }

        if (input.PackagedSoftware is { IsComplete: false } packaged)
        {
            findings.Add(new MachineFinding(
                Code: "data.software.packaged.partial",
                Severity: MachineFindingSeverity.Info,
                Title: "Packaged-software inventory is partial",
                Detail: CreatePartialInventoryDetail(
                    packaged.SkippedEntryCount,
                    "package was skipped",
                    "packages were skipped")));
        }

        if (input.Startup is { IsComplete: false } startup)
        {
            findings.Add(new MachineFinding(
                Code: "data.startup.partial",
                Severity: MachineFindingSeverity.Info,
                Title: "Startup inventory is partial",
                Detail: startup.ReadFailureCount > 0
                    ? "The inventory encountered " +
                        FormatCount(
                            startup.ReadFailureCount,
                            "read failure",
                            "read failures") + "."
                    : "The inventory did not return a complete result."));
        }
    }

    private static string CreatePartialInventoryDetail(
        int skippedEntryCount,
        string singularDescription,
        string pluralDescription) =>
        skippedEntryCount > 0
            ? "The inventory is partial; " +
                FormatCount(
                    skippedEntryCount,
                    singularDescription,
                    pluralDescription) + "."
            : "The inventory did not return a complete result.";

    private static string FormatCount(
        int count,
        string singularDescription,
        string pluralDescription) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} " +
        (count == 1 ? singularDescription : pluralDescription);

    private static MachineOverallState GetOverallState(
        IReadOnlyList<MachineFinding> findings)
    {
        var highestSeverity = findings
            .Where(finding =>
                finding.Severity != MachineFindingSeverity.Info)
            .Select(finding => (MachineFindingSeverity?)finding.Severity)
            .FirstOrDefault();

        return highestSeverity switch
        {
            MachineFindingSeverity.Critical =>
                MachineOverallState.Critical,
            MachineFindingSeverity.Warning =>
                MachineOverallState.Warning,
            MachineFindingSeverity.Attention =>
                MachineOverallState.Attention,
            _ => MachineOverallState.Stable,
        };
    }
}
