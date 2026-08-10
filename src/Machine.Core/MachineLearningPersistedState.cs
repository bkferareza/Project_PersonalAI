namespace Machine.Core;

public sealed record MachineLearningPersistedState(
    int SchemaVersion,
    IReadOnlyList<MachineLearningBaselineState> Baselines,
    IReadOnlyList<MachineLearningEpisode> Episodes,
    long ObservationCount,
    DateTimeOffset? FirstObservedAt,
    DateTimeOffset? LastObservedAt,
    DateTimeOffset? PersistedAt = null,
    long ObservedDurationTicks = 0,
    MachineLearningEpisode? ActiveEpisode = null);

public sealed record MachineLearningBaselineState(
    int LocalHour,
    MachineUserActivityState ActivityState,
    long SampleCount,
    double CpuMean,
    double CpuM2,
    double MemoryMean,
    double MemoryM2,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    IReadOnlyList<DateOnly>? ObservedLocalDates = null);
