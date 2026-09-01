namespace Machine.Core;

public sealed class FileMachineLearningActivityStore : IMachineLearningActivityStore
{
    private const string FileName = "learning-activity.json";
    private const int MaximumDetailLength = 512;
    private readonly SafeJsonFile<MachineLearningActivityPersistedState>
        _safeFile;

    public FileMachineLearningActivityStore(string? directoryPath = null)
    {
        var directory = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Machine");
        _safeFile = new(
            Path.Combine(directory, FileName),
            new() { WriteIndented = false },
            Validate);
    }

    public async Task<MachineLearningActivityPersistedState?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _safeFile.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        return result.Value;
    }

    public async Task SaveAsync(MachineLearningActivityPersistedState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _safeFile.SaveAsync(state, cancellationToken)
            .ConfigureAwait(false);
    }

    private static MachinePersistenceValidationResult Validate(
        MachineLearningActivityPersistedState state) =>
        state.Events is not null &&
        state.Events.Count <= MachineLearningActivityLog.MaximumEventCount &&
        state.Events.All(IsValid)
            ? MachinePersistenceValidationResult.Accepted
            : MachinePersistenceValidationResult.Rejected;

    private static bool IsValid(MachineLearningActivityEvent? item) =>
        item is not null &&
        item.OccurredAt != default &&
        Enum.IsDefined(item.Kind) &&
        item.Count > 0 &&
        item.ObservationCount is null or >= 0 &&
        item.ProfileCount is null or >= 0 &&
        item.EpisodeCount is null or >= 0 &&
        item.SchemaVersion is null or > 0 &&
        item.ByteCount is null or >= 0 &&
        item.DurationMilliseconds is null or >= 0 &&
        item.PowerEvidenceCount is null or >= 0 &&
        (item.ContextChange is null || IsValid(item.ContextChange)) &&
        (item.Detail is null || item.Detail.Length <= MaximumDetailLength);

    private static bool IsValid(MachineLearningContextChange item) =>
        item.LocalHour is >= 0 and <= 23 &&
        Enum.IsDefined(item.ActivityState) &&
        item.PreviousSampleCount >= 0 &&
        item.SampleCount > item.PreviousSampleCount &&
        item.PreviousObservedDayCount >= 0 &&
        item.ObservedDayCount >= item.PreviousObservedDayCount &&
        item.ObservedDayCount <= item.SampleCount &&
        (item.PreviousMaturity is null ||
            Enum.IsDefined(item.PreviousMaturity.Value)) &&
        Enum.IsDefined(item.Maturity) &&
        IsFiniteOrNull(item.PreviousAdaptiveCpuMean, 0d, 100d) &&
        double.IsFinite(item.AdaptiveCpuMean) &&
        item.AdaptiveCpuMean is >= 0d and <= 100d &&
        IsFiniteOrNull(item.PreviousAdaptiveMemoryMean, 0d, 100d) &&
        double.IsFinite(item.AdaptiveMemoryMean) &&
        item.AdaptiveMemoryMean is >= 0d and <= 100d &&
        item.PreviousPowerEvidenceCount >= 0 &&
        item.PowerEvidenceCount >= item.PreviousPowerEvidenceCount &&
        IsFiniteOrNull(item.PreviousPowerMeanWatts, 0d, double.MaxValue) &&
        IsFiniteOrNull(item.PowerMeanWatts, 0d, double.MaxValue) &&
        (item.PreviousPowerMaturity is null ||
            Enum.IsDefined(item.PreviousPowerMaturity.Value)) &&
        Enum.IsDefined(item.PowerMaturity) &&
        Enum.IsDefined(item.Freshness);

    private static bool IsFiniteOrNull(double? value, double minimum,
        double maximum) =>
        value is null ||
        double.IsFinite(value.Value) &&
        value.Value >= minimum &&
        value.Value <= maximum;
}
