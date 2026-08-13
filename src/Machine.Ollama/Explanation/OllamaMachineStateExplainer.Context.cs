using System.Text.Json;
using Machine.Core;

namespace Machine.Ollama;

public sealed partial class OllamaMachineStateExplainer
{
    private static string CreateUserMessage(
        MachineStateExplanationRequest request)
    {
        var payload = new MachineSnapshotPayload(
            CpuUsagePercent: request.Resources.CpuUsagePercent,
            UsedMemoryBytes: request.Resources.UsedMemoryBytes,
            TotalMemoryBytes: request.Resources.TotalMemoryBytes,
            Storage: CreateStoragePayload(request.Storage),
            Software: CreateSoftwarePayload(request.Software),
            Startup: CreateStartupPayload(request.Startup),
            Findings: CreateFindingsPayload(request.Findings),
            LearnedContext: CreateLearnedContextPayload(request.LearnedContext),
            Network: CreateNetworkPayload(request.Network),
            Session: CreateSessionPayload(request.Session),
            Health: CreateHealthPayload(request.Health),
            History: CreateHistoryPayload(request.History),
            Gpu: CreateGpuPayload(request.Gpu));

        var payloadJson = JsonSerializer.Serialize(
            payload,
            ExplainerJsonSerializerContext.Default.MachineSnapshotPayload);

        return $"{UserMessagePrefix}\n{payloadJson}";
    }

    private static StorageSnapshotPayload? CreateStoragePayload(
        MachineStorageExplanationContext? storage)
    {
        if (storage is null)
        {
            return null;
        }

        return new StorageSnapshotPayload(
            SystemVolumeRoot: storage.SystemVolumeRoot,
            TotalBytes: storage.TotalSizeBytes,
            AvailableBytes: storage.AvailableSizeBytes,
            LargeFolderScanIsComplete:
                storage.LargeFolderScan?.IsComplete);
    }

    private static SoftwareSnapshotPayload? CreateSoftwarePayload(
        MachineSoftwareExplanationContext? software)
    {
        if (software is null)
        {
            return null;
        }

        return new SoftwareSnapshotPayload(
            ClassicDesktop: CreateSoftwareInventoryPayload(
                software.ClassicDesktop),
            PackagedApplications: CreateSoftwareInventoryPayload(
                software.PackagedApplications));
    }

    private static SoftwareInventoryPayload?
        CreateSoftwareInventoryPayload(
            MachineSoftwareInventoryExplanationSummary? inventory) =>
        inventory is null
            ? null
            : new SoftwareInventoryPayload(
                RegistrationCount: inventory.RegistrationCount,
                IsComplete: inventory.IsComplete,
                SkippedEntryCount: inventory.SkippedEntryCount);

    private static StartupSnapshotPayload? CreateStartupPayload(
        MachineStartupExplanationContext? startup)
    {
        if (startup is null)
        {
            return null;
        }

        return new StartupSnapshotPayload(
            RegistrationCount: startup.RegistrationCount,
            RegistryRunCount: startup.RegistryRunCount,
            StartupFolderCount: startup.StartupFolderCount,
            MachineCount: startup.MachineCount,
            CurrentUserCount: startup.CurrentUserCount,
            IsComplete: startup.IsComplete);
    }

    private static FindingsSnapshotPayload? CreateFindingsPayload(
        MachineFindingsSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        ArgumentNullException.ThrowIfNull(snapshot.Findings);

        var findings = snapshot.Findings
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .Take(FindingsContextLimit)
            .Select(finding => new FindingPayload(
                Code: finding.Code,
                Severity: finding.Severity.ToString(),
                Title: finding.Title,
                Detail: finding.Detail))
            .ToArray();

        return new FindingsSnapshotPayload(
            OverallState: snapshot.OverallState.ToString(),
            Findings: findings);
    }

    private static LearnedContextPayload? CreateLearnedContextPayload(
        MachineLearnedContext? context)
    {
        if (context is null)
        {
            return null;
        }

        return new LearnedContextPayload(
            CurrentBaseline: CreateBaselinePayload(
                context.CurrentBaseline),
            MatchingProfile: CreateProfilePayload(
                context.MatchingProfile),
            MatchingBroaderPattern: CreatePatternPayload(
                context.MatchingBroaderPattern),
            RecentEpisodes: context.RecentEpisodes.Take(2).Select(episode =>
                new LearnedEpisodePayload(
                    episode.ActivityState.ToString(),
                    episode.OverallState.ToString(),
                    episode.SampleCount,
                    episode.AverageCpuUsagePercent,
                    episode.PeakCpuUsagePercent,
                    episode.AverageMemoryUsagePercent,
                    episode.FindingKeys.Take(8).ToArray(),
                    episode.Outcome)).ToArray());
    }

    private static LearnedBaselinePayload CreateBaselinePayload(
        MachineLearningBaseline baseline) => new(
            baseline.ActivityState.ToString(),
            baseline.LocalHour,
            baseline.Confidence.ToString(),
            baseline.Freshness.ToString(),
            baseline.SampleCount,
            baseline.ObservedDayCount,
            baseline.CpuMean,
            baseline.MemoryMean,
            baseline.AdaptiveCpuMean,
            baseline.AdaptiveMemoryMean);

    private static LearnedProfilePayload? CreateProfilePayload(
        MachineLearningContextProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        var hasNetworkEvidence = HasNetworkLearningEvidence(profile);
        return new LearnedProfilePayload(
            profile.ActivityState.ToString(),
            profile.LocalHour,
            profile.Confidence.ToString(),
            profile.Freshness.ToString(),
            profile.LifetimeSampleCount,
            profile.DistinctObservedDayCount,
            profile.Cpu.TypicalRange?.Low,
            profile.Cpu.TypicalRange?.High,
            profile.Memory.TypicalRange?.Low,
            profile.Memory.TypicalRange?.High,
            hasNetworkEvidence
                ? profile.DominantNetworkActivityClass?.ToString()
                : null,
            hasNetworkEvidence
                ? profile.DominantNetworkActivityCount
                : 0,
            hasNetworkEvidence
                ? profile.NetworkObservationCount
                : 0);
    }

    private static LearnedPatternPayload? CreatePatternPayload(
        MachineLearningRecurringPattern? pattern)
    {
        if (pattern is null ||
            pattern.Confidence != MachineLearningConfidence.Established ||
            pattern.Freshness == MachineLearningFreshness.Stale)
        {
            return null;
        }

        return new LearnedPatternPayload(
            pattern.ActivityState.ToString(),
            pattern.StartHour,
            pattern.EndHourExclusive,
            pattern.CrossesMidnight,
            pattern.Confidence.ToString(),
            pattern.Freshness.ToString(),
            pattern.MemberContexts.Count,
            pattern.CombinedSampleCount,
            pattern.MinimumDistinctObservedDayCount,
            pattern.CpuTypicalRange.Low,
            pattern.CpuTypicalRange.High,
            pattern.MemoryTypicalRange.Low,
            pattern.MemoryTypicalRange.High,
            pattern.DominantNetworkActivityClass?.ToString(),
            pattern.DominantNetworkActivityCount,
            pattern.NetworkObservationCount);
    }

    private static NetworkSnapshotPayload? CreateNetworkPayload(
        MachineNetworkInsightContext? network)
    {
        if (network is null || !Enum.IsDefined(network.ActivityClass))
        {
            return null;
        }

        return new NetworkSnapshotPayload(
            network.ActivityClass.ToString(),
            GetValidRate(network.ReceiveBytesPerSecond),
            GetValidRate(network.SendBytesPerSecond));
    }

    private static SessionSnapshotPayload? CreateSessionPayload(
        MachineSessionInsightContext? session)
    {
        if (session is null)
        {
            return null;
        }

        return new SessionSnapshotPayload(
            ToElapsedSeconds(session.SystemUptime),
            ToElapsedSeconds(session.MachineUptime));
    }

    private static HealthSnapshotPayload? CreateHealthPayload(
        MachineHealthInsightContext? health)
    {
        if (health is null)
        {
            return null;
        }

        var recurringApplication =
            health.RecurringApplicationFailure is null
                ? null
                : MachineReliabilityAggregator.NormalizeApplicationIdentity(
                    health.RecurringApplicationFailure.ApplicationName);

        return new HealthSnapshotPayload(
            WindowsUpdateState: health.UpdateState?.ToString(),
            PendingUpdateCount: health.PendingUpdateCount,
            UpdateVerifiedAt: health.UpdateVerifiedAt?.ToUniversalTime()
                .ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            RebootPending: health.IsRebootPending,
            RebootReasons: health.RebootReasons
                .Take(MachineHealthInsightProjector.MaximumRebootReasonCount)
                .Select(reason => reason.ToString())
                .ToArray(),
            RebootVerifiedAt: FormatVerifiedAt(health.RebootVerifiedAt),
            RebootConfidence: health.RebootConfidence.ToString(),
            ReliabilityLast7Days: health.ReliabilityLast7Days is null
                ? null
                : new ReliabilityCountsPayload(
                    health.ReliabilityLast7Days.ApplicationCrashCount,
                    health.ReliabilityLast7Days.ApplicationHangCount,
                    health.ReliabilityLast7Days.UnexpectedShutdownCount,
                    health.ReliabilityLast7Days.UpdateFailureCount,
                    health.ReliabilityLast7Days.HardwareFailureCount,
                    health.ReliabilityLast7Days.OtherFailureCount),
            MostRecentSignificantIncident: CreateHealthIncidentPayload(
                health.MostRecentSignificantIncident),
            RecurringApplicationFailure:
                health.RecurringApplicationFailure is null ||
                recurringApplication is null
                    ? null
                    : new RecurringApplicationFailurePayload(
                        recurringApplication,
                        health.RecurringApplicationFailure
                            .IncidentCountLast7Days,
                        health.RecurringApplicationFailure
                            .IncidentCountLast30Days),
            ReliabilityDataStatus: health.ReliabilityDataStatus.ToString(),
            ReliabilityVerifiedAt: FormatVerifiedAt(
                health.ReliabilityVerifiedAt));
    }

    private static string? FormatVerifiedAt(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString(
            "O",
            System.Globalization.CultureInfo.InvariantCulture);

    private static HistorySnapshotPayload? CreateHistoryPayload(
        MachineHistoryInsightContext? history) => history is null
        ? null
        : new(
            CreateHistoryPeriodPayload(history.CurrentPeriod),
            history.RecentComparable is null
                ? null
                : CreateHistoryPeriodPayload(history.RecentComparable),
            history.SignificantEvent is null
                ? null
                : new(
                    FormatVerifiedAt(
                        history.SignificantEvent.OccurredAt)!,
                    history.SignificantEvent.Kind.ToString(),
                    history.SignificantEvent.Title,
                    history.SignificantEvent.Detail,
                    history.SignificantEvent.Count));

    private static GpuSnapshotPayload? CreateGpuPayload(
        MachineGpuInsightContext? gpu) => gpu is null
        ? null
        : new(
            gpu.UtilizationPercent,
            gpu.MemoryUtilizationPercent,
            gpu.TemperatureCelsius,
            gpu.BoardPowerWatts);

    private static HistoryPeriodPayload CreateHistoryPeriodPayload(
        MachineHistoryInsightPeriod period) => new(
        FormatVerifiedAt(period.StartedAt)!,
        FormatVerifiedAt(period.EndedAt)!,
        period.ObservedDurationSeconds,
        period.CpuMeanPercent,
        period.MemoryMeanPercent,
        period.NetworkReceiveMeanBytesPerSecond,
        period.NetworkSendMeanBytesPerSecond,
        period.GpuMeanPercent,
        period.GpuMemoryMeanPercent,
        period.GpuTemperatureMeanCelsius,
        period.GpuBoardPowerMeanWatts);

    private static HealthIncidentPayload? CreateHealthIncidentPayload(
        MachineReliabilityIncident? incident)
    {
        var normalized = MachineReliabilityAggregator.NormalizeIncident(
            incident);
        return normalized is null
            ? null
            : new HealthIncidentPayload(
            OccurredAt: normalized.OccurredAt.ToUniversalTime().ToString(
                "O",
                System.Globalization.CultureInfo.InvariantCulture),
            Category: normalized.Category.ToString(),
            Severity: normalized.Severity.ToString(),
            ApplicationName: normalized.ApplicationName,
            EventId: normalized.EventId,
            SummaryCode: normalized.SummaryCode);
    }

    private static bool HasNetworkLearningEvidence(
        MachineLearningContextProfile profile) =>
        profile.DominantNetworkActivityClass is
            MachineNetworkActivityClass.Quiet or
            MachineNetworkActivityClass.Light or
            MachineNetworkActivityClass.Active &&
        profile.DominantNetworkActivityCount >=
            MachineNetworkActivityClassifier.MinimumDominantObservationCount &&
        profile.NetworkObservationCount >=
            profile.DominantNetworkActivityCount;

    private static double? GetValidRate(double? value) =>
        value is not null && double.IsFinite(value.Value) && value.Value >= 0d
            ? value
            : null;

    private static long ToElapsedSeconds(TimeSpan elapsed) =>
        elapsed <= TimeSpan.Zero
            ? 0
            : elapsed.TotalSeconds >= long.MaxValue
                ? long.MaxValue
                : (long)elapsed.TotalSeconds;
}
