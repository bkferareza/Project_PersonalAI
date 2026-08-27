namespace Machine.Core;

public enum MachineLearningConfidence
{
    Calibrating,
    Provisional,
    Established
}

public enum MachineLearningFreshness
{
    Fresh,
    Aging,
    Stale
}

public enum MachineLearningEvidenceMaturity
{
    Insufficient,
    Provisional,
    Established
}

public enum MachineLearningMemoryLayer
{
    ContextBaseline,
    CompactProfile,
    BroaderPattern,
    AggregateEpisode,
    HealthHistory
}

public enum MachineLearningDataHealth
{
    Healthy,
    NotYetPersisted,
    RecoveredFromCorruptState,
    PersistenceTemporarilyUnavailable
}

public enum MachineLearningMemoryState
{
    Calibrating,
    Active,
    PersistenceAtRisk
}

public enum MachineLearningPatternReadinessBlocker
{
    None,
    InsufficientProfiles,
    NoAdjacentContexts,
    InsufficientSamples,
    InsufficientDistinctDays,
    NoEstablishedAdjacentContexts,
    StaleEvidence,
    MissingTypicalRanges,
    IncompatibleCpuBehavior,
    IncompatibleMemoryBehavior,
    IncompatibleNetworkBehavior,
    FullDayRunExcluded
}

public sealed record MachineLearningObservation(
    DateTimeOffset Timestamp,
    double CpuUsagePercent,
    double MemoryUsagePercent,
    MachineUserActivityState ActivityState,
    MachineOverallState OverallState,
    IReadOnlyList<string> FindingKeys,
    double? SystemVolumeFreePercent,
    string ContextFingerprint,
    MachineNetworkActivityClass NetworkActivityClass =
        MachineNetworkActivityClass.Unavailable,
    double? ReceiveBytesPerSecond = null,
    double? SendBytesPerSecond = null,
    double? EstimatedWallPowerWatts = null);

public sealed record MachineLearningBaseline(
    int LocalHour,
    MachineUserActivityState ActivityState,
    long SampleCount,
    double CpuMean,
    double CpuStandardDeviation,
    double MemoryMean,
    double MemoryStandardDeviation,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    int ObservedDayCount,
    MachineLearningConfidence Confidence,
    long NetworkQuietSampleCount = 0,
    long NetworkLightSampleCount = 0,
    long NetworkActiveSampleCount = 0,
    long NetworkUnavailableSampleCount = 0,
    long ObservedDurationTicks = 0,
    double AdaptiveCpuMean = 0,
    double AdaptiveCpuStandardDeviation = 0,
    double AdaptiveMemoryMean = 0,
    double AdaptiveMemoryStandardDeviation = 0,
    long AdaptiveSampleCount = 0,
    DateTimeOffset? AdaptiveLastUpdatedAt = null,
    MachineLearningFreshness Freshness = MachineLearningFreshness.Fresh,
    long EstimatedWallPowerSampleCount = 0,
    double? EstimatedWallPowerMeanWatts = null,
    double? EstimatedWallPowerStandardDeviationWatts = null,
    int EstimatedWallPowerObservedDayCount = 0,
    DateTimeOffset? EstimatedWallPowerFirstObservedAt = null,
    DateTimeOffset? EstimatedWallPowerLastObservedAt = null,
    double? AdaptiveEstimatedWallPowerMeanWatts = null,
    double? AdaptiveEstimatedWallPowerStandardDeviationWatts = null,
    long AdaptiveEstimatedWallPowerSampleCount = 0,
    DateTimeOffset? AdaptiveEstimatedWallPowerLastUpdatedAt = null,
    MachineLearningFreshness? EstimatedWallPowerFreshness = null)
{
    public TimeSpan LifetimeObservedDuration => TimeSpan.FromTicks(
        Math.Clamp(ObservedDurationTicks, 0, TimeSpan.MaxValue.Ticks));

    public MachineLearningRange? CpuTypicalRange =>
        MachineLearningPolicy.CreateTypicalRange(
            AdaptiveCpuMean,
            AdaptiveCpuStandardDeviation,
            AdaptiveSampleCount);

    public MachineLearningRange? MemoryTypicalRange =>
        MachineLearningPolicy.CreateTypicalRange(
            AdaptiveMemoryMean,
            AdaptiveMemoryStandardDeviation,
            AdaptiveSampleCount);

    public MachineLearningRange? EstimatedWallPowerTypicalRange =>
        AdaptiveEstimatedWallPowerMeanWatts is { } mean &&
        AdaptiveEstimatedWallPowerStandardDeviationWatts is { } deviation
            ? MachineLearningPolicy.CreateNonnegativeTypicalRange(
                mean,
                deviation,
                AdaptiveEstimatedWallPowerSampleCount)
            : null;

    public MachineLearningEvidenceMaturity EstimatedWallPowerMaturity =>
        MachineLearningPolicy.GetEvidenceMaturity(
            EstimatedWallPowerSampleCount,
            EstimatedWallPowerObservedDayCount);

    public long NetworkObservationCount => SaturatingAdd(
        SaturatingAdd(NetworkQuietSampleCount, NetworkLightSampleCount),
        NetworkActiveSampleCount);

    public MachineNetworkActivityClass? DominantNetworkActivityClass =>
        MachineNetworkActivityClassifier.SelectDominant(
            NetworkQuietSampleCount,
            NetworkLightSampleCount,
            NetworkActiveSampleCount);

    public long DominantNetworkActivityCount =>
        DominantNetworkActivityClass is { } activityClass
            ? MachineNetworkActivityClassifier.GetCount(
                activityClass,
                NetworkQuietSampleCount,
                NetworkLightSampleCount,
                NetworkActiveSampleCount)
            : 0;

    private static long SaturatingAdd(long left, long right) =>
        left >= long.MaxValue - right ? long.MaxValue : left + right;
}

public readonly record struct MachineLearningContextKey(
    int LocalHour,
    MachineUserActivityState ActivityState);

public sealed record MachineLearningRange(
    double Low,
    double High);

public sealed record MachineLearningMetricProfile(
    double AdaptiveMean,
    double AdaptiveStandardDeviation,
    MachineLearningRange? TypicalRange);

public sealed record MachineLearningEstimatedWallPowerProfile(
    long EvidenceCount,
    int DistinctObservedDayCount,
    double HistoricalMeanWatts,
    double HistoricalStandardDeviationWatts,
    double AdaptiveMeanWatts,
    double AdaptiveStandardDeviationWatts,
    MachineLearningRange? TypicalRange,
    MachineLearningEvidenceMaturity Maturity,
    MachineLearningFreshness Freshness,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    DateTimeOffset AdaptiveLastUpdatedAt);

public sealed record MachineLearningContextProfile(
    int LocalHour,
    MachineUserActivityState ActivityState,
    MachineLearningConfidence Confidence,
    MachineLearningFreshness Freshness,
    long LifetimeSampleCount,
    long LifetimeObservedDurationTicks,
    int DistinctObservedDayCount,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    MachineLearningMetricProfile Cpu,
    MachineLearningMetricProfile Memory,
    MachineNetworkActivityClass? DominantNetworkActivityClass,
    long DominantNetworkActivityCount,
    long NetworkObservationCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastReinforcedAt,
    DateTimeOffset LastMateriallyChangedAt,
    MachineLearningEstimatedWallPowerProfile? EstimatedWallPower = null)
{
    public MachineLearningContextKey ContextKey => new(
        LocalHour,
        ActivityState);

    public TimeSpan LifetimeObservedDuration => TimeSpan.FromTicks(
        Math.Clamp(LifetimeObservedDurationTicks, 0,
            TimeSpan.MaxValue.Ticks));
}

public sealed record MachineLearningRecurringPattern(
    MachineUserActivityState ActivityState,
    int StartHour,
    int EndHourExclusive,
    bool CrossesMidnight,
    IReadOnlyList<MachineLearningContextKey> MemberContexts,
    MachineLearningConfidence Confidence,
    MachineLearningFreshness Freshness,
    long CombinedSampleCount,
    int MinimumDistinctObservedDayCount,
    MachineLearningRange CpuTypicalRange,
    MachineLearningRange MemoryTypicalRange,
    MachineNetworkActivityClass? DominantNetworkActivityClass,
    long DominantNetworkActivityCount,
    long NetworkObservationCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastReinforcedAt);

public sealed record MachineLearningMetadata(
    long LifetimeAcceptedObservationCount,
    TimeSpan LifetimeObservedDuration,
    long LifetimeMachineSessionCount,
    DateTimeOffset? FirstLearningAt,
    DateTimeOffset? LastLearningAt,
    DateTimeOffset CurrentSessionStartedAt,
    DateTimeOffset? PreviousMachineSessionEndedAt,
    DateTimeOffset? LastPersistedAt,
    int PersistedSchemaVersion);

public sealed record MachineLearningEpisode(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    MachineUserActivityState ActivityState,
    MachineOverallState OverallState,
    int SampleCount,
    double AverageCpuUsagePercent,
    double PeakCpuUsagePercent,
    double AverageMemoryUsagePercent,
    IReadOnlyList<string> FindingKeys,
    string? Outcome);

public sealed record MachineLearningDashboardSnapshot(
    long ObservationCount,
    TimeSpan ObservedDuration,
    MachineLearningObservation? CurrentObservation,
    MachineLearningBaseline? CurrentBaseline,
    int RecentEpisodeCount,
    int RawObservationCount,
    IReadOnlyList<MachineLearningBaseline> Baselines,
    IReadOnlyList<MachineLearningEpisode> RecentEpisodes,
    IReadOnlyList<MachineLearnedItem> LearnedItems,
    MachineLearningDiagnostics Diagnostics,
    DateTimeOffset? LastPersistedAt,
    bool IsDirty,
    MachineLearningDataHealth DataHealth,
    MachineLearningMetadata Metadata,
    IReadOnlyList<MachineLearningContextProfile> ContextProfiles,
    IReadOnlyList<MachineLearningRecurringPattern> BroaderPatterns,
    MachineLearningReadinessSummary Readiness);

public sealed record MachineLearningReadinessSummary(
    MachineLearningMemoryState MemoryState,
    MachineLearningPatternReadiness PatternReadiness);

public sealed record MachineLearningPatternReadiness(
    int TotalProfileCount,
    int ProfilesWithSufficientSamples,
    int ProfilesWithSufficientDistinctDays,
    int EstablishedProfileCount,
    int FreshEstablishedProfileCount,
    int TemporallyEligibleProfileCount,
    int AdjacentCandidatePairCount,
    int PairsWithSufficientSamples,
    int PairsWithSufficientDistinctDays,
    int PairsMeetingEvidenceThresholds,
    int EstablishedPairCount,
    int TemporallyEligiblePairCount,
    int PairsReachingCompatibilityComparison,
    int CompatiblePairCount,
    int ConfidenceRejectedPairCount,
    int StaleRejectedPairCount,
    int MissingRangeRejectedPairCount,
    int CpuRejectedPairCount,
    int MemoryRejectedPairCount,
    int NetworkRejectedPairCount,
    int CandidateRunCount,
    int FullDayRunRejectedCount,
    int PatternLimitTruncatedCount,
    int PatternsProduced,
    MachineLearningPatternReadinessBlocker PrimaryBlocker);

public sealed record MachineLearningDiagnostics(
    long AcceptedObservationCount,
    long ThrottledObservationCount,
    long MissingPrerequisiteCount,
    DateTimeOffset? LastAcceptedObservationAt);

public sealed record MachineLearnedItem(
    string Text,
    long EvidenceCount,
    MachineLearningConfidence? Confidence,
    bool IsEarlyObservation,
    MachineLearningMemoryLayer Layer =
        MachineLearningMemoryLayer.ContextBaseline);

public sealed record MachineLearningEpisodeSummary(
    MachineUserActivityState ActivityState,
    MachineOverallState OverallState,
    int SampleCount,
    double AverageCpuUsagePercent,
    double PeakCpuUsagePercent,
    double AverageMemoryUsagePercent,
    IReadOnlyList<string> FindingKeys,
    string? Outcome);

public sealed record MachineLearnedContext(
    MachineLearningBaseline CurrentBaseline,
    MachineLearningContextProfile? MatchingProfile,
    MachineLearningRecurringPattern? MatchingBroaderPattern,
    IReadOnlyList<MachineLearningEpisodeSummary> RecentEpisodes);
