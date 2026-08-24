namespace Machine.Core;

public sealed record ElectricityRateCacheState(
    IReadOnlyList<ElectricityRateSnapshot> Rates);

public sealed class FileElectricityRateCache
{
    public const int MaximumRateCount = 24;
    private const string FileName = "electricity-rate-v1.json";
    private const int MaximumProviderNameLength = 100;
    private const int MaximumCurrencyCodeLength = 8;
    private const int MaximumSourceIdentityLength = 2_048;
    private readonly SafeJsonFile<ElectricityRateCacheState> _safeFile;

    public FileElectricityRateCache(string? directoryPath = null)
    {
        var directory = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Machine");
        _safeFile = new(
            Path.Combine(directory, FileName),
            new() { WriteIndented = false },
            Validate);
    }

    public async Task<ElectricityRateCacheState> LoadAsync(CancellationToken cancellationToken = default)
    {
        var result = await _safeFile.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        return result.Value ?? new([]);
    }

    public async Task SaveAsync(IEnumerable<ElectricityRateSnapshot> rates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rates);
        var state = new ElectricityRateCacheState(rates
            .Where(IsSafe)
            .OrderByDescending(item => item.EffectiveMonth)
            .Take(MaximumRateCount)
            .ToArray());
        await _safeFile.SaveAsync(state, cancellationToken)
            .ConfigureAwait(false);
    }

    private static MachinePersistenceValidationResult Validate(
        ElectricityRateCacheState state)
    {
        if (state.Rates is null || state.Rates.Count > MaximumRateCount)
        {
            return MachinePersistenceValidationResult.Rejected;
        }

        if (state.Rates.Any(rate => rate is not null &&
            rate.SchemaVersion > 1))
        {
            return MachinePersistenceValidationResult.Incompatible;
        }

        return state.Rates.All(IsSafe)
            ? MachinePersistenceValidationResult.Accepted
            : MachinePersistenceValidationResult.Rejected;
    }

    private static bool IsSafe(ElectricityRateSnapshot? rate) =>
        rate is not null &&
        rate.SchemaVersion == 1 &&
        !string.IsNullOrWhiteSpace(rate.ProviderName) &&
        rate.ProviderName.Length <= MaximumProviderNameLength &&
        !string.IsNullOrWhiteSpace(rate.CurrencyCode) &&
        rate.CurrencyCode.Length <= MaximumCurrencyCodeLength &&
        rate.RatePerKWh > 0 &&
        rate.EffectiveMonth.Day == 1 &&
        rate.RetrievedAt != default &&
        rate.ExpiresAt > rate.RetrievedAt &&
        !string.IsNullOrWhiteSpace(rate.SourceIdentity) &&
        rate.SourceIdentity.Length <= MaximumSourceIdentityLength &&
        Uri.TryCreate(rate.SourceIdentity, UriKind.Absolute,
            out var source) && source.Scheme == Uri.UriSchemeHttps &&
        Enum.IsDefined(rate.UtilityConfidence) &&
        rate.UtilityConfidence != MachinePowerEstimateConfidence.Unavailable &&
        Enum.IsDefined(rate.RateConfidence) &&
        rate.RateConfidence != MachinePowerEstimateConfidence.Unavailable;
}
