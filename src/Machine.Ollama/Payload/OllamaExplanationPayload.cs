using System.Text.Json;
using System.Text.Json.Serialization;

namespace Machine.Ollama;

public sealed partial class OllamaMachineStateExplainer
{
    private sealed record ChatRequest(
        [property: JsonPropertyName("model")]
        string Model,
        [property: JsonPropertyName("stream")]
        bool Stream,
        [property: JsonPropertyName("think")]
        bool Think,
        [property: JsonPropertyName("keep_alive")]
        string KeepAlive,
        [property: JsonPropertyName("messages")]
        ChatMessage[] Messages,
        [property: JsonPropertyName("options")]
        ChatOptions Options);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")]
        string Role,
        [property: JsonPropertyName("content")]
        string Content);

    private sealed record ChatOptions(
        [property: JsonPropertyName("temperature")]
        double Temperature,
        [property: JsonPropertyName("num_ctx")]
        int ContextLength,
        [property: JsonPropertyName("num_predict")]
        int MaximumPredictedTokens);

    private sealed record ChatResponse(
        [property: JsonPropertyName("model")]
        string? Model,
        [property: JsonPropertyName("message")]
        ChatResponseMessage? Message);

    private sealed record ChatResponseMessage(
        [property: JsonPropertyName("content")]
        string? Content,
        [property: JsonPropertyName("tool_calls")]
        JsonElement ToolCalls);

    private sealed record MachineSnapshotPayload(
        [property: JsonPropertyName("cpu_usage_percent")]
        double? CpuUsagePercent,
        [property: JsonPropertyName("used_memory_bytes")]
        ulong? UsedMemoryBytes,
        [property: JsonPropertyName("total_memory_bytes")]
        ulong? TotalMemoryBytes,
        [property: JsonPropertyName("storage")]
        StorageSnapshotPayload? Storage,
        [property: JsonPropertyName("software")]
        SoftwareSnapshotPayload? Software,
        [property: JsonPropertyName("startup")]
        StartupSnapshotPayload? Startup,
        [property: JsonPropertyName("findings")]
        FindingsSnapshotPayload? Findings,
        [property: JsonPropertyName("learned_context")]
        LearnedContextPayload? LearnedContext,
        [property: JsonPropertyName("network")]
        NetworkSnapshotPayload? Network,
        [property: JsonPropertyName("session")]
        SessionSnapshotPayload? Session,
        [property: JsonPropertyName("health")]
        HealthSnapshotPayload? Health,
        [property: JsonPropertyName("history")]
        HistorySnapshotPayload? History,
        [property: JsonPropertyName("gpu")]
        GpuSnapshotPayload? Gpu,
        [property: JsonPropertyName("energy_cost")]
        EnergyCostSnapshotPayload? EnergyCost,
        [property: JsonPropertyName("current_insight")]
        CurrentInsightPayload? CurrentInsight);

    private sealed record LearnedContextPayload(
        [property: JsonPropertyName("current_baseline")]
        LearnedBaselinePayload CurrentBaseline,
        [property: JsonPropertyName("matching_profile")]
        LearnedProfilePayload? MatchingProfile,
        [property: JsonPropertyName("matching_broader_pattern")]
        LearnedPatternPayload? MatchingBroaderPattern,
        [property: JsonPropertyName("recent_episodes")]
        LearnedEpisodePayload[] RecentEpisodes);

    private sealed record LearnedBaselinePayload(
        [property: JsonPropertyName("activity_state")]
        string ActivityState,
        [property: JsonPropertyName("local_hour")]
        int LocalHour,
        [property: JsonPropertyName("confidence")]
        string Confidence,
        [property: JsonPropertyName("freshness")]
        string Freshness,
        [property: JsonPropertyName("lifetime_sample_count")]
        long LifetimeSampleCount,
        [property: JsonPropertyName("observed_day_count")]
        int ObservedDayCount,
        [property: JsonPropertyName("lifetime_cpu_mean")]
        double LifetimeCpuMean,
        [property: JsonPropertyName("lifetime_memory_mean")]
        double LifetimeMemoryMean,
        [property: JsonPropertyName("adaptive_cpu_mean")]
        double AdaptiveCpuMean,
        [property: JsonPropertyName("adaptive_memory_mean")]
        double AdaptiveMemoryMean);

    private sealed record LearnedProfilePayload(
        [property: JsonPropertyName("activity_state")]
        string ActivityState,
        [property: JsonPropertyName("local_hour")]
        int LocalHour,
        [property: JsonPropertyName("confidence")]
        string Confidence,
        [property: JsonPropertyName("freshness")]
        string Freshness,
        [property: JsonPropertyName("lifetime_sample_count")]
        long LifetimeSampleCount,
        [property: JsonPropertyName("distinct_observed_day_count")]
        int DistinctObservedDayCount,
        [property: JsonPropertyName("cpu_typical_low")]
        double? CpuTypicalLow,
        [property: JsonPropertyName("cpu_typical_high")]
        double? CpuTypicalHigh,
        [property: JsonPropertyName("memory_typical_low")]
        double? MemoryTypicalLow,
        [property: JsonPropertyName("memory_typical_high")]
        double? MemoryTypicalHigh,
        [property: JsonPropertyName("dominant_network_activity_class")]
        string? DominantNetworkActivityClass,
        [property: JsonPropertyName("dominant_network_activity_count")]
        long DominantNetworkActivityCount,
        [property: JsonPropertyName("network_observation_count")]
        long NetworkObservationCount);

    private sealed record LearnedPatternPayload(
        [property: JsonPropertyName("activity_state")]
        string ActivityState,
        [property: JsonPropertyName("start_hour")]
        int StartHour,
        [property: JsonPropertyName("end_hour_exclusive")]
        int EndHourExclusive,
        [property: JsonPropertyName("crosses_midnight")]
        bool CrossesMidnight,
        [property: JsonPropertyName("confidence")]
        string Confidence,
        [property: JsonPropertyName("freshness")]
        string Freshness,
        [property: JsonPropertyName("member_profile_count")]
        int MemberProfileCount,
        [property: JsonPropertyName("combined_sample_count")]
        long CombinedSampleCount,
        [property: JsonPropertyName("minimum_observed_day_count")]
        int MinimumObservedDayCount,
        [property: JsonPropertyName("cpu_typical_low")]
        double CpuTypicalLow,
        [property: JsonPropertyName("cpu_typical_high")]
        double CpuTypicalHigh,
        [property: JsonPropertyName("memory_typical_low")]
        double MemoryTypicalLow,
        [property: JsonPropertyName("memory_typical_high")]
        double MemoryTypicalHigh,
        [property: JsonPropertyName("dominant_network_activity_class")]
        string? DominantNetworkActivityClass,
        [property: JsonPropertyName("dominant_network_activity_count")]
        long DominantNetworkActivityCount,
        [property: JsonPropertyName("network_observation_count")]
        long NetworkObservationCount);

    private sealed record NetworkSnapshotPayload(
        [property: JsonPropertyName("activity_class")]
        string ActivityClass,
        [property: JsonPropertyName("receive_bytes_per_second")]
        double? ReceiveBytesPerSecond,
        [property: JsonPropertyName("send_bytes_per_second")]
        double? SendBytesPerSecond);

    private sealed record SessionSnapshotPayload(
        [property: JsonPropertyName("system_uptime_seconds")]
        long SystemUptimeSeconds,
        [property: JsonPropertyName("machine_uptime_seconds")]
        long MachineUptimeSeconds);

    private sealed record HealthSnapshotPayload(
        [property: JsonPropertyName("windows_update_state")]
        string? WindowsUpdateState,
        [property: JsonPropertyName("pending_update_count")]
        int? PendingUpdateCount,
        [property: JsonPropertyName("update_verified_at")]
        string? UpdateVerifiedAt,
        [property: JsonPropertyName("reboot_pending")]
        bool? RebootPending,
        [property: JsonPropertyName("reboot_reasons")]
        string[] RebootReasons,
        [property: JsonPropertyName("reboot_verified_at")]
        string? RebootVerifiedAt,
        [property: JsonPropertyName("reboot_confidence")]
        string RebootConfidence,
        [property: JsonPropertyName("reliability_last_7_days")]
        ReliabilityCountsPayload? ReliabilityLast7Days,
        [property: JsonPropertyName("most_recent_significant_incident")]
        HealthIncidentPayload? MostRecentSignificantIncident,
        [property: JsonPropertyName("recurring_application_failure")]
        RecurringApplicationFailurePayload? RecurringApplicationFailure,
        [property: JsonPropertyName("reliability_data_status")]
        string ReliabilityDataStatus,
        [property: JsonPropertyName("reliability_verified_at")]
        string? ReliabilityVerifiedAt);

    private sealed record HistorySnapshotPayload(
        [property: JsonPropertyName("current_period")]
        HistoryPeriodPayload CurrentPeriod,
        [property: JsonPropertyName("recent_comparable")]
        HistoryPeriodPayload? RecentComparable,
        [property: JsonPropertyName("significant_event")]
        HistoryEventPayload? SignificantEvent);

    private sealed record HistoryPeriodPayload(
        [property: JsonPropertyName("started_at")]
        string StartedAt,
        [property: JsonPropertyName("ended_at")]
        string EndedAt,
        [property: JsonPropertyName("observed_duration_seconds")]
        long ObservedDurationSeconds,
        [property: JsonPropertyName("cpu_mean_percent")]
        double? CpuMeanPercent,
        [property: JsonPropertyName("memory_mean_percent")]
        double? MemoryMeanPercent,
        [property: JsonPropertyName("network_receive_mean_bytes_per_second")]
        double? NetworkReceiveMeanBytesPerSecond,
        [property: JsonPropertyName("network_send_mean_bytes_per_second")]
        double? NetworkSendMeanBytesPerSecond,
        [property: JsonPropertyName("gpu_mean_percent")]
        double? GpuMeanPercent,
        [property: JsonPropertyName("gpu_memory_mean_percent")]
        double? GpuMemoryMeanPercent,
        [property: JsonPropertyName("gpu_temperature_mean_celsius")]
        double? GpuTemperatureMeanCelsius,
        [property: JsonPropertyName("gpu_board_power_mean_watts")]
        double? GpuBoardPowerMeanWatts);

    private sealed record HistoryEventPayload(
        [property: JsonPropertyName("occurred_at")]
        string OccurredAt,
        [property: JsonPropertyName("kind")]
        string Kind,
        [property: JsonPropertyName("title")]
        string Title,
        [property: JsonPropertyName("detail")]
        string? Detail,
        [property: JsonPropertyName("count")]
        int Count);

    private sealed record GpuSnapshotPayload(
        [property: JsonPropertyName("utilization_percent")]
        double? UtilizationPercent,
        [property: JsonPropertyName("memory_utilization_percent")]
        double? MemoryUtilizationPercent,
        [property: JsonPropertyName("temperature_celsius")]
        double? TemperatureCelsius,
        [property: JsonPropertyName("board_power_watts")]
        double? BoardPowerWatts);

    private sealed record EnergyCostSnapshotPayload(
        [property: JsonPropertyName("wall_power_kind")] string WallPowerKind,
        [property: JsonPropertyName("estimated_wall_watts")] double? EstimatedWallWatts,
        [property: JsonPropertyName("wall_range_watts")] double? WallLowerWatts,
        [property: JsonPropertyName("wall_upper_watts")] double? WallUpperWatts,
        [property: JsonPropertyName("power_confidence")] string PowerConfidence,
        [property: JsonPropertyName("session_observed_kwh")] double? SessionKwh,
        [property: JsonPropertyName("today_observed_kwh")] double? TodayKwh,
        [property: JsonPropertyName("thirty_day_observed_kwh")] double? ThirtyDayKwh,
        [property: JsonPropertyName("session_estimated_cost")] decimal? SessionCost,
        [property: JsonPropertyName("today_estimated_cost")] decimal? TodayCost,
        [property: JsonPropertyName("thirty_day_estimated_cost")] decimal? ThirtyDayCost,
        [property: JsonPropertyName("thirty_day_cost_coverage")] string Coverage,
        [property: JsonPropertyName("published_reference_provider")] string? Provider,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("published_reference_rate_per_kwh")] decimal? RatePerKwh,
        [property: JsonPropertyName("rate_effective_month")] string? EffectiveMonth,
        [property: JsonPropertyName("rate_confidence")] string RateConfidence,
        [property: JsonPropertyName("energy_cost_kind")] string EnergyCostKind);

    private sealed record CurrentInsightPayload(
        [property: JsonPropertyName("candidate_id")] string CandidateId,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("primary_text")] string PrimaryText,
        [property: JsonPropertyName("secondary_text")] string SecondaryText,
        [property: JsonPropertyName("evidence_summary")] string EvidenceSummary,
        [property: JsonPropertyName("actual_observed_kwh")] double? ActualKwh,
        [property: JsonPropertyName("observed_duration_seconds")] long? ObservedDurationSeconds,
        [property: JsonPropertyName("expected_observed_kwh")] double? ExpectedKwh,
        [property: JsonPropertyName("expected_lower_kwh")] double? ExpectedLowerKwh,
        [property: JsonPropertyName("expected_upper_kwh")] double? ExpectedUpperKwh,
        [property: JsonPropertyName("difference_kwh")] double? DifferenceKwh,
        [property: JsonPropertyName("difference_percent")] double? DifferencePercent,
        [property: JsonPropertyName("learned_coverage")] double? LearnedCoverage,
        [property: JsonPropertyName("evidence_maturity")] string? EvidenceMaturity,
        [property: JsonPropertyName("actual_estimated_cost")] decimal? ActualCost,
        [property: JsonPropertyName("expected_estimated_cost")] decimal? ExpectedCost,
        [property: JsonPropertyName("expected_lower_cost")] decimal? ExpectedLowerCost,
        [property: JsonPropertyName("expected_upper_cost")] decimal? ExpectedUpperCost,
        [property: JsonPropertyName("published_reference_provider")] string? Provider,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("published_reference_rate_per_kwh")] decimal? RatePerKwh,
        [property: JsonPropertyName("rate_effective_month")] string? EffectiveMonth);

    private sealed record ReliabilityCountsPayload(
        [property: JsonPropertyName("application_crashes")]
        int ApplicationCrashes,
        [property: JsonPropertyName("application_hangs")]
        int ApplicationHangs,
        [property: JsonPropertyName("unexpected_shutdowns")]
        int UnexpectedShutdowns,
        [property: JsonPropertyName("update_failures")]
        int UpdateFailures,
        [property: JsonPropertyName("hardware_failures")]
        int HardwareFailures,
        [property: JsonPropertyName("other_failures")]
        int OtherFailures);

    private sealed record HealthIncidentPayload(
        [property: JsonPropertyName("occurred_at")]
        string OccurredAt,
        [property: JsonPropertyName("category")]
        string Category,
        [property: JsonPropertyName("historical_severity")]
        string Severity,
        [property: JsonPropertyName("application_name")]
        string? ApplicationName,
        [property: JsonPropertyName("event_id")]
        int? EventId,
        [property: JsonPropertyName("summary_code")]
        string SummaryCode);

    private sealed record RecurringApplicationFailurePayload(
        [property: JsonPropertyName("application_name")]
        string ApplicationName,
        [property: JsonPropertyName("incident_count_last_7_days")]
        int IncidentCountLast7Days,
        [property: JsonPropertyName("incident_count_last_30_days")]
        int IncidentCountLast30Days);

    private sealed record LearnedEpisodePayload(
        [property: JsonPropertyName("activity_state")]
        string ActivityState,
        [property: JsonPropertyName("overall_state")]
        string OverallState,
        [property: JsonPropertyName("sample_count")]
        int SampleCount,
        [property: JsonPropertyName("average_cpu_usage_percent")]
        double AverageCpuUsagePercent,
        [property: JsonPropertyName("peak_cpu_usage_percent")]
        double PeakCpuUsagePercent,
        [property: JsonPropertyName("average_memory_usage_percent")]
        double AverageMemoryUsagePercent,
        [property: JsonPropertyName("finding_keys")]
        IReadOnlyList<string> FindingKeys,
        [property: JsonPropertyName("outcome")]
        string? Outcome);

    private sealed record StorageSnapshotPayload(
        [property: JsonPropertyName("system_volume_root")]
        string SystemVolumeRoot,
        [property: JsonPropertyName("total_bytes")]
        long TotalBytes,
        [property: JsonPropertyName("available_bytes")]
        long AvailableBytes,
        [property: JsonPropertyName("large_folder_scan_is_complete")]
        bool? LargeFolderScanIsComplete);

    private sealed record SoftwareSnapshotPayload(
        [property: JsonPropertyName("classic_desktop")]
        SoftwareInventoryPayload? ClassicDesktop,
        [property: JsonPropertyName("packaged_applications")]
        SoftwareInventoryPayload? PackagedApplications);

    private sealed record SoftwareInventoryPayload(
        [property: JsonPropertyName("registration_count")]
        int RegistrationCount,
        [property: JsonPropertyName("is_complete")]
        bool IsComplete,
        [property: JsonPropertyName("skipped_entry_count")]
        int SkippedEntryCount);

    private sealed record StartupSnapshotPayload(
        [property: JsonPropertyName("registration_count")]
        int RegistrationCount,
        [property: JsonPropertyName("registry_run_count")]
        int RegistryRunCount,
        [property: JsonPropertyName("startup_folder_count")]
        int StartupFolderCount,
        [property: JsonPropertyName("machine_count")]
        int MachineCount,
        [property: JsonPropertyName("current_user_count")]
        int CurrentUserCount,
        [property: JsonPropertyName("is_complete")]
        bool IsComplete);

    private sealed record FindingsSnapshotPayload(
        [property: JsonPropertyName("overall_state")]
        string OverallState,
        [property: JsonPropertyName("findings")]
        FindingPayload[] Findings);

    private sealed record FindingPayload(
        [property: JsonPropertyName("code")]
        string Code,
        [property: JsonPropertyName("severity")]
        string Severity,
        [property: JsonPropertyName("title")]
        string Title,
        [property: JsonPropertyName("detail")]
        string Detail);

    [JsonSerializable(typeof(ChatRequest))]
    [JsonSerializable(typeof(ChatResponse))]
    [JsonSerializable(typeof(MachineSnapshotPayload))]
    private sealed partial class ExplainerJsonSerializerContext
        : JsonSerializerContext
    {
    }
}
