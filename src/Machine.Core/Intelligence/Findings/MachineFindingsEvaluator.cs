using System.Globalization;

namespace Machine.Core;

public static class MachineFindingsEvaluator
{
    public const int RecurringApplicationAttentionThreshold = 3;
    public const int RepeatedUpdateFailureAttentionThreshold = 3;
    public const int RepeatedUnexpectedShutdownAttentionThreshold = 2;
    public const int RepeatedHardwareFailureAttentionThreshold = 2;
    public const int ActiveApplicationCrashLoopMinimumIncidentCount = 3;
    public const int ResidentApplicationCrashLoopMinimumIncidentCount = 2;
    public const int IndependentApplicationFailureMinimumIncidentCount = 2;
    public const int IndependentApplicationFailureMinimumApplicationCount = 2;
    public static readonly TimeSpan ReliabilityCurrentWindow =
        TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ReliabilityFreshnessWindow =
        TimeSpan.FromMinutes(15);
    public static readonly TimeSpan UpdateFailureCurrentWindow =
        TimeSpan.FromHours(24);
    public static readonly TimeSpan UpdateFailureFreshnessWindow =
        TimeSpan.FromHours(4);
    public static readonly TimeSpan UnexpectedShutdownCurrentWindow =
        TimeSpan.FromHours(24);
    public static readonly TimeSpan UnexpectedShutdownFreshnessWindow =
        TimeSpan.FromHours(4);

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

        EvaluateHealth(input, findings);
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

        if (input.WindowsUpdate is { } update &&
            (update.DataStatus != MachineHealthDataStatus.Complete ||
             update.RefreshStatus ==
                MachineWindowsUpdateRefreshStatus.CachedAfterFailure))
        {
            findings.Add(new MachineFinding(
                Code: "data.windows-update.partial",
                Severity: MachineFindingSeverity.Info,
                Title: "Windows Update status is partial",
                Detail: update.VerifiedAt is null
                    ? "The current Windows Update state is unavailable."
                    : update.RefreshStatus ==
                        MachineWindowsUpdateRefreshStatus.CachedAfterFailure
                        ? "The last verified Windows Update state was " +
                            "preserved after a later refresh could not be " +
                            "completed."
                        : "Some Windows Update details could not be " +
                            "verified."));
        }

        if (input.RebootPending is { IsPartial: true })
        {
            findings.Add(new MachineFinding(
                Code: "data.reboot-pending.partial",
                Severity: MachineFindingSeverity.Info,
                Title: "Restart evidence is partial",
                Detail: "Some restart indicators could not be verified."));
        }

        if (input.Reliability is { } reliability &&
            reliability.DataStatus != MachineHealthDataStatus.Complete)
        {
            findings.Add(new MachineFinding(
                Code: "data.reliability.partial",
                Severity: MachineFindingSeverity.Info,
                Title: "Reliability history is partial",
                Detail: reliability.VerifiedAt is null
                    ? "Windows reliability history is unavailable."
                    : "Some reliability event sources could not be read."));
        }
    }

    private static void EvaluateHealth(
        MachineFindingsInput input,
        ICollection<MachineFinding> findings)
    {
        if (input.RebootPending?.IsPending == true)
        {
            findings.Add(new MachineFinding(
                Code: "health.restart.pending",
                Severity: MachineFindingSeverity.Info,
                Title: "Restart pending",
                Detail: "Windows has recorded a pending restart."));
        }

        var reliability = input.Reliability;
        if (reliability?.VerifiedAt is null)
        {
            return;
        }

        var recurring = reliability.Summary.RecurringApplications
            .Where(application =>
                application.IncidentCountLast7Days >=
                    RecurringApplicationAttentionThreshold)
            .OrderByDescending(application =>
                application.IncidentCountLast7Days)
            .ThenBy(application =>
                application.ApplicationName,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (recurring is not null)
        {
            findings.Add(new MachineFinding(
                Code: "health.reliability.application-recurrence",
                Severity: MachineFindingSeverity.Attention,
                Title: "Application failures have recurred",
                Detail: $"Windows recorded " +
                    $"{recurring.IncidentCountLast7Days} crashes or hangs " +
                    $"of {recurring.ApplicationName} during the last 7 days.",
                PostureImpact: MachineFindingPostureImpact.Local));
        }

        EvaluateCurrentReliabilityPosture(
            reliability,
            input.ResidentApplicationIdentity,
            findings);

        var sevenDays = reliability.Summary.Last7Days;
        if (sevenDays.UpdateFailureCount >=
            RepeatedUpdateFailureAttentionThreshold)
        {
            findings.Add(new MachineFinding(
                Code: "health.reliability.update-failures-repeated",
                Severity: MachineFindingSeverity.Attention,
                Title: "Update failures have recurred",
                Detail: $"Windows recorded {sevenDays.UpdateFailureCount} " +
                    "update failures during the last 7 days.",
                PostureImpact: MachineFindingPostureImpact.Local));
        }

        if (sevenDays.UnexpectedShutdownCount >=
            RepeatedUnexpectedShutdownAttentionThreshold)
        {
            findings.Add(new MachineFinding(
                Code: "health.reliability.unexpected-shutdowns-repeated",
                Severity: MachineFindingSeverity.Attention,
                Title: "Unexpected shutdowns have recurred",
                Detail: $"Windows recorded " +
                    $"{sevenDays.UnexpectedShutdownCount} unexpected " +
                    "shutdowns during the last 7 days.",
                PostureImpact: MachineFindingPostureImpact.Local));
        }

        if (sevenDays.HardwareFailureCount >=
            RepeatedHardwareFailureAttentionThreshold)
        {
            findings.Add(new MachineFinding(
                Code: "health.reliability.hardware-errors-repeated",
                Severity: MachineFindingSeverity.Attention,
                Title: "Hardware-error records have recurred",
                Detail: $"Windows recorded {sevenDays.HardwareFailureCount} " +
                    "hardware-error events during the last 7 days."));
        }

        EvaluateCurrentUpdateFailurePosture(
            reliability,
            input.WindowsUpdate,
            findings);
        EvaluateCurrentUnexpectedShutdownPosture(reliability, findings);
    }

    private static void EvaluateCurrentUpdateFailurePosture(
        MachineReliabilitySnapshot reliability,
        MachineWindowsUpdateSnapshot? update,
        ICollection<MachineFinding> findings)
    {
        if (update is not
            {
                DataStatus: MachineHealthDataStatus.Complete,
                RefreshStatus: MachineWindowsUpdateRefreshStatus.Verified,
                UpdateState: MachineWindowsUpdateState.Unknown,
                FailureCode: not null
            })
        {
            return;
        }

        var currentWindowStart = reliability.CapturedAt -
            UpdateFailureCurrentWindow;
        var freshnessStart = reliability.CapturedAt -
            UpdateFailureFreshnessWindow;
        var recentFailures = reliability.Incidents
            .Where(incident => incident.Category is
                MachineReliabilityIncidentCategory.UpdateFailure or
                MachineReliabilityIncidentCategory.InstallFailure)
            .Where(incident =>
                incident.OccurredAt >= currentWindowStart &&
                incident.OccurredAt <= reliability.CapturedAt)
            .ToArray();
        if (recentFailures.Length < RepeatedUpdateFailureAttentionThreshold ||
            !recentFailures.Any(incident =>
                incident.OccurredAt >= freshnessStart))
        {
            return;
        }

        findings.Add(new MachineFinding(
            Code: "health.reliability.update-failures-current",
            Severity: MachineFindingSeverity.Attention,
            Title: "Windows Update is currently failing repeatedly",
            Detail: "Windows reports a current update failure after " +
                "repeated recent installation failures."));
    }

    private static void EvaluateCurrentUnexpectedShutdownPosture(
        MachineReliabilitySnapshot reliability,
        ICollection<MachineFinding> findings)
    {
        var currentWindowStart = reliability.CapturedAt -
            UnexpectedShutdownCurrentWindow;
        var freshnessStart = reliability.CapturedAt -
            UnexpectedShutdownFreshnessWindow;
        var recentShutdowns = reliability.Incidents
            .Where(incident => incident.Category ==
                MachineReliabilityIncidentCategory.UnexpectedShutdown)
            .Where(incident =>
                incident.OccurredAt >= currentWindowStart &&
                incident.OccurredAt <= reliability.CapturedAt)
            .ToArray();
        if (!recentShutdowns.Any(incident =>
            incident.OccurredAt >= freshnessStart))
        {
            return;
        }

        findings.Add(new MachineFinding(
            Code: "health.reliability.unexpected-shutdowns-current",
            Severity: MachineFindingSeverity.Attention,
            Title: "The machine has shut down unexpectedly very recently",
            Detail: "Windows recorded an unexpected shutdown in the " +
                "current reliability window."));
    }

    private static void EvaluateCurrentReliabilityPosture(
        MachineReliabilitySnapshot reliability,
        string? residentApplicationIdentity,
        ICollection<MachineFinding> findings)
    {
        var currentWindowStart = reliability.CapturedAt -
            ReliabilityCurrentWindow;
        var freshnessStart = reliability.CapturedAt -
            ReliabilityFreshnessWindow;
        var recentFailures = reliability.Incidents
            .Where(incident =>
                incident.Category is
                    MachineReliabilityIncidentCategory.ApplicationCrash or
                    MachineReliabilityIncidentCategory.ApplicationHang)
            .Where(incident =>
                incident.OccurredAt >= currentWindowStart &&
                incident.OccurredAt <= reliability.CapturedAt &&
                incident.ApplicationName is not null)
            .GroupBy(
                incident => incident.ApplicationName!,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                ApplicationName = group.Key,
                Count = group.Count(),
                MostRecent = group.Max(incident => incident.OccurredAt)
            })
            .Where(group => group.MostRecent >= freshnessStart)
            .ToArray();

        var residentIdentity =
            MachineReliabilityAggregator.NormalizeApplicationIdentity(
                residentApplicationIdentity);
        var residentCrashLoop = residentIdentity is null
            ? null
            : recentFailures.FirstOrDefault(group =>
                string.Equals(
                    group.ApplicationName,
                    residentIdentity,
                    StringComparison.OrdinalIgnoreCase) &&
                group.Count >= ResidentApplicationCrashLoopMinimumIncidentCount);
        if (residentCrashLoop is not null)
        {
            findings.Add(new MachineFinding(
                Code: "health.reliability.resident-application-crash-loop",
                Severity: MachineFindingSeverity.Attention,
                Title: "Matasuri has recently failed repeatedly",
                Detail: "Windows recorded repeated recent failures of the " +
                    "resident Matasuri process."));
            return;
        }

        var crashLoop = recentFailures
            .Where(group =>
                group.Count >= ActiveApplicationCrashLoopMinimumIncidentCount)
            .OrderByDescending(group => group.Count)
            .ThenByDescending(group => group.MostRecent)
            .ThenBy(group => group.ApplicationName,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (crashLoop is not null)
        {
            findings.Add(new MachineFinding(
                Code: "health.reliability.application-crash-loop",
                Severity: MachineFindingSeverity.Attention,
                Title: "An application is failing repeatedly right now",
                Detail: $"Windows recorded {crashLoop.Count} recent crashes " +
                    $"or hangs of {crashLoop.ApplicationName}."));
            return;
        }

        var independentFailures = recentFailures
            .Where(group =>
                group.Count >= IndependentApplicationFailureMinimumIncidentCount)
            .OrderBy(group => group.ApplicationName,
                StringComparer.OrdinalIgnoreCase)
            .Take(IndependentApplicationFailureMinimumApplicationCount)
            .ToArray();
        if (independentFailures.Length >=
            IndependentApplicationFailureMinimumApplicationCount)
        {
            findings.Add(new MachineFinding(
                Code: "health.reliability.independent-application-failures",
                Severity: MachineFindingSeverity.Attention,
                Title: "Several applications are failing repeatedly right now",
                Detail: "Windows recorded repeated recent failures across " +
                    $"{independentFailures.Length} distinct applications."));
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
                finding.Severity != MachineFindingSeverity.Info &&
                finding.PostureImpact ==
                    MachineFindingPostureImpact.Machine)
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
