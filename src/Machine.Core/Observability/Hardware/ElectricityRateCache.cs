namespace Machine.Core;

public enum ElectricityRateProvenance
{
    Unavailable,
    CurrentOnlineVerified,
    CurrentPeriodCached,
    LastKnownVerifiedFallback
}

public sealed record ElectricityRateCacheState(
    IReadOnlyList<ElectricityRateSnapshot> Rates,
    DateTimeOffset? LastSuccessfulVerificationAt = null,
    DateTimeOffset? LastAutomaticRefreshAttemptAt = null,
    ElectricityRateProvenance ActiveProvenance =
        ElectricityRateProvenance.Unavailable);

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
        await SaveAsync(new ElectricityRateCacheState(rates.ToArray()),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(ElectricityRateCacheState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var normalized = state with
        {
            Rates = state.Rates
            .Where(IsSafe)
            .GroupBy(item => (item.ProviderName, item.EffectiveMonth))
            .Select(group => group
                .OrderByDescending(item => item.RetrievedAt).First())
            .OrderByDescending(item => item.EffectiveMonth)
            .ThenByDescending(item => item.RetrievedAt)
            .Take(MaximumRateCount)
            .ToArray()
        };
        await _safeFile.SaveAsync(normalized, cancellationToken)
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

        if (!Enum.IsDefined(state.ActiveProvenance) ||
            state.LastSuccessfulVerificationAt is { } successful &&
                successful == default ||
            state.LastAutomaticRefreshAttemptAt is { } attempted &&
                attempted == default)
        {
            return MachinePersistenceValidationResult.Rejected;
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
        rate.RatePerKWh is >= 1m and <= 100m &&
        rate.EffectiveMonth.Day == 1 &&
        rate.RetrievedAt != default &&
        rate.ExpiresAt > rate.RetrievedAt &&
        !string.IsNullOrWhiteSpace(rate.SourceIdentity) &&
        rate.SourceIdentity.Length <= MaximumSourceIdentityLength &&
        Uri.TryCreate(rate.SourceIdentity, UriKind.Absolute,
            out var source) && source.Scheme == Uri.UriSchemeHttps &&
        string.Equals(source.Host,
            ElectricityRateEnrichmentService.MeralcoHost,
            StringComparison.OrdinalIgnoreCase) &&
        Enum.IsDefined(rate.UtilityConfidence) &&
        rate.UtilityConfidence != MachinePowerEstimateConfidence.Unavailable &&
        Enum.IsDefined(rate.RateConfidence) &&
        rate.RateConfidence != MachinePowerEstimateConfidence.Unavailable;
}
