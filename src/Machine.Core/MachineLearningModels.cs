namespace Machine.Core;

public enum MachineLearningConfidence
{
    Calibrating,
    Provisional,
    Established
}

public enum MachineLearningDataHealth
{
    Healthy,
    NotYetPersisted,
    RecoveredFromCorruptState,
    PersistenceTemporarilyUnavailable
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
    double? SendBytesPerSecond = null);

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
    long NetworkUnavailableSampleCount = 0)
{
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
    MachineLearningDataHealth DataHealth);

public sealed record MachineLearningDiagnostics(
    long AcceptedObservationCount,
    long ThrottledObservationCount,
    long MissingPrerequisiteCount,
    DateTimeOffset? LastAcceptedObservationAt);

public sealed record MachineLearnedItem(
    string Text,
    long EvidenceCount,
    MachineLearningConfidence? Confidence,
    bool IsEarlyObservation);

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
    MachineUserActivityState ActivityState,
    int LocalHour,
    MachineLearningConfidence Confidence,
    long SampleCount,
    double CpuMean,
    double CpuStandardDeviation,
    double MemoryMean,
    double MemoryStandardDeviation,
    IReadOnlyList<MachineLearningEpisodeSummary> RecentEpisodes,
    MachineNetworkActivityClass? DominantNetworkActivityClass = null,
    long DominantNetworkActivityCount = 0,
    long NetworkObservationCount = 0);
