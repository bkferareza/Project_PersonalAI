using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Machine.Core;

public sealed record ElectricityRateEnrichmentResult(
    ElectricityRateSnapshot? Rate,
    string? ProbableUtility,
    MachinePowerEstimateConfidence UtilityConfidence,
    bool UsedCache,
    int RequestCount,
    ElectricityRateProvenance Provenance =
        ElectricityRateProvenance.Unavailable,
    DateTimeOffset? LastSuccessfulVerificationAt = null,
    DateTimeOffset? LastAutomaticRefreshAttemptAt = null);

public enum ElectricityRateRefreshMode
{
    Automatic,
    Manual
}

/// <summary>
/// Retrieves only the minimum coarse evidence needed to select a published
/// residential reference rate. It deliberately owns a tiny allowlist rather
/// than providing a reusable networking facility.
/// </summary>
public sealed class ElectricityRateEnrichmentService
{
    internal const string LocationHost = "ipinfo.io";
    internal const string MeralcoHost = "company.meralco.com.ph";
    private static readonly Uri LocationUri = new("https://ipinfo.io/json");
    private static readonly Regex MonthYearPattern = new(
        @"\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+(20\d{2})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CurrentResidentialRatePattern = new(
        @"(?:overall\s+rate|residential\s+rate).{0,160}?(?:\bto\s*(?:₱|P(?:hp)?)\s*(\d{1,2}\.\d{2,4})\s*(?:from\s*(?:₱|P(?:hp)?)\s*\d{1,2}\.\d{2,4}\s*)?per\s*kWh|\b(?:at|is)\s*(?:₱|P(?:hp)?)\s*(\d{1,2}\.\d{2,4})\s*per\s*kWh)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant |
        RegexOptions.Singleline);
    private readonly HttpClient _httpClient;
    private readonly FileElectricityRateCache _cache;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _lastAutomaticRefreshAttemptAt;
    private static readonly HashSet<string> MeralcoCalabarzonCities = new(
    [
        "Alfonso", "Amadeo", "Bacoor", "Carmona", "Cavite City",
        "Dasmarinas", "Dasmariñas", "General Trias", "Imus", "Indang",
        "Kawit", "Magallanes", "Maragondon", "Mendez", "Naic",
        "Noveleta", "Rosario", "Silang", "Tagaytay", "Tanza", "Ternate",
        "Trece Martires"
    ], StringComparer.OrdinalIgnoreCase);

    public ElectricityRateEnrichmentService(HttpClient httpClient,
        FileElectricityRateCache cache)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<ElectricityRateEnrichmentResult> GetCurrentAsync(
        DateTimeOffset now, CancellationToken cancellationToken = default)
        => await RefreshAsync(now, ElectricityRateRefreshMode.Automatic,
            cancellationToken).ConfigureAwait(false);

    public async Task<ElectricityRateEnrichmentResult> RefreshAsync(
        DateTimeOffset now,
        ElectricityRateRefreshMode mode,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var month = new DateOnly(now.LocalDateTime.Year,
                now.LocalDateTime.Month, 1);
            var cache = await _cache.LoadAsync(cancellationToken).ConfigureAwait(false);
            cache = cache with
            {
                LastSuccessfulVerificationAt =
                    cache.LastSuccessfulVerificationAt ?? cache.Rates
                        .OrderByDescending(rate => rate.RetrievedAt)
                        .Select(rate => (DateTimeOffset?)rate.RetrievedAt)
                        .FirstOrDefault()
            };
            var lastAttempt = Latest(cache.LastAutomaticRefreshAttemptAt,
                _lastAutomaticRefreshAttemptAt);
            if (mode == ElectricityRateRefreshMode.Automatic &&
                IsSameLocalDay(lastAttempt, now))
            {
                var sameDayResult = CreateFallbackResult(cache, month, 0,
                    lastAttempt);
                await TrySaveStateAsync(cache with
                {
                    ActiveProvenance = sameDayResult.Provenance,
                    LastAutomaticRefreshAttemptAt = lastAttempt
                }, cancellationToken).ConfigureAwait(false);
                return sameDayResult;
            }

            if (mode == ElectricityRateRefreshMode.Automatic)
            {
                lastAttempt = now;
                _lastAutomaticRefreshAttemptAt = now;
                cache = cache with
                {
                    LastAutomaticRefreshAttemptAt = now,
                    ActiveProvenance = SelectFallback(cache.Rates, month).Provenance
                };
                await TrySaveStateAsync(cache, cancellationToken)
                    .ConfigureAwait(false);
            }

            (string Name, MachinePowerEstimateConfidence Confidence)? utility = null;
            ElectricityRateSnapshot? rate = null;
            var requestCount = 0;
            try
            {
                requestCount++;
                var locationText = await GetAllowedTextAsync(LocationUri,
                    LocationHost, cancellationToken).ConfigureAwait(false);
                utility = ResolveUtility(locationText);
                if (utility is not null)
                {
                    foreach (var candidate in GetMeralcoAdvisoryCandidates(month))
                    {
                        requestCount++;
                        try
                        {
                            var rateText = await GetAllowedTextAsync(candidate,
                                MeralcoHost, cancellationToken).ConfigureAwait(false);
                            rate = ParseMeralcoResidentialRate(rateText, candidate,
                                month, now, utility.Value.Confidence);
                        }
                        catch (HttpRequestException)
                        {
                            continue;
                        }
                        if (rate is not null)
                        {
                            break;
                        }
                    }
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (JsonException)
            {
            }

            if (rate is null)
            {
                var fallback = CreateFallbackResult(cache, month, requestCount,
                    lastAttempt, utility);
                await TrySaveStateAsync(cache with
                {
                    ActiveProvenance = fallback.Provenance,
                    LastAutomaticRefreshAttemptAt = lastAttempt
                }, cancellationToken).ConfigureAwait(false);
                return fallback;
            }

            var successfulState = cache with
            {
                Rates = cache.Rates.Append(rate).ToArray(),
                LastSuccessfulVerificationAt = now,
                LastAutomaticRefreshAttemptAt = lastAttempt,
                ActiveProvenance =
                    ElectricityRateProvenance.CurrentOnlineVerified
            };
            await TrySaveStateAsync(successfulState, cancellationToken)
                .ConfigureAwait(false);
            return new(rate, rate.ProviderName, rate.UtilityConfidence, false,
                requestCount,
                ElectricityRateProvenance.CurrentOnlineVerified, now,
                lastAttempt);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task TrySaveStateAsync(ElectricityRateCacheState state,
        CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SaveAsync(state, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or
                UnauthorizedAccessException)
        {
            // Persistence rejection must preserve the original file. The
            // verified live result and in-process daily gate remain usable.
        }
    }

    private static ElectricityRateEnrichmentResult CreateFallbackResult(
        ElectricityRateCacheState cache,
        DateOnly month,
        int requestCount,
        DateTimeOffset? lastAttempt,
        (string Name, MachinePowerEstimateConfidence Confidence)? utility = null)
    {
        var selected = SelectFallback(cache.Rates, month);
        var rate = selected.Rate;
        return new(rate, utility?.Name ?? rate?.ProviderName,
            utility?.Confidence ?? rate?.UtilityConfidence ??
                MachinePowerEstimateConfidence.Unavailable,
            rate is not null, requestCount, selected.Provenance,
            cache.LastSuccessfulVerificationAt ?? rate?.RetrievedAt,
            lastAttempt);
    }

    private static (ElectricityRateSnapshot? Rate,
        ElectricityRateProvenance Provenance) SelectFallback(
        IReadOnlyList<ElectricityRateSnapshot> rates,
        DateOnly month)
    {
        var current = rates.Where(rate => rate.EffectiveMonth == month)
            .OrderByDescending(rate => rate.RetrievedAt).FirstOrDefault();
        if (current is not null)
        {
            return (current,
                ElectricityRateProvenance.CurrentPeriodCached);
        }
        var latest = rates.Where(rate => rate.EffectiveMonth <= month)
            .OrderByDescending(rate => rate.EffectiveMonth)
            .ThenByDescending(rate => rate.RetrievedAt).FirstOrDefault();
        return latest is null
            ? (null, ElectricityRateProvenance.Unavailable)
            : (latest, ElectricityRateProvenance.LastKnownVerifiedFallback);
    }

    private static bool IsSameLocalDay(DateTimeOffset? timestamp,
        DateTimeOffset now) => timestamp is { } value &&
        value.ToOffset(now.Offset).Date == now.Date;

    private static DateTimeOffset? Latest(DateTimeOffset? left,
        DateTimeOffset? right) => left is null ? right : right is null ? left :
        left > right ? left : right;

    public Task<ElectricityRateCacheState> LoadCachedAsync(
        CancellationToken cancellationToken = default) =>
        _cache.LoadAsync(cancellationToken);

    internal static (string Name, MachinePowerEstimateConfidence Confidence)?
        ResolveUtility(string locationJson)
    {
        using var document = JsonDocument.Parse(locationJson);
        var root = document.RootElement;
        var country = root.TryGetProperty("country", out var countryValue)
            ? countryValue.GetString()
            : root.TryGetProperty("country_code", out countryValue)
                ? countryValue.GetString() : null;
        var region = root.TryGetProperty("region", out var regionValue)
            ? regionValue.GetString()
            : root.TryGetProperty("region_code", out regionValue)
                ? regionValue.GetString() : null;
        var city = root.TryGetProperty("city", out var cityValue)
            ? cityValue.GetString() : null;
        if (!string.Equals(country, "PH", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(region))
        {
            return null;
        }

        // These entire provinces, plus the National Capital Region, are within
        // Meralco's franchise. Partial-province areas intentionally stay
        // unavailable without a deterministic city/municipality boundary.
        if (string.Equals(region.Trim(), "Calabarzon",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(city) &&
            MeralcoCalabarzonCities.Contains(city.Trim()))
        {
            return ("Meralco", MachinePowerEstimateConfidence.HighEstimate);
        }

        return region.Trim().ToUpperInvariant() switch
        {
            "00" or "NCR" or "METRO MANILA" or "NATIONAL CAPITAL REGION" or
            "CAVITE" or "RIZAL" or "BULACAN" => ("Meralco",
                MachinePowerEstimateConfidence.HighEstimate),
            _ => null
        };
    }

    internal static ElectricityRateSnapshot? ParseMeralcoResidentialRate(
        string html, Uri sourceUri, DateOnly expectedMonth,
        DateTimeOffset retrievedAt,
        MachinePowerEstimateConfidence utilityConfidence)
    {
        if (string.IsNullOrWhiteSpace(html) || sourceUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(sourceUri.Host, MeralcoHost,
                StringComparison.OrdinalIgnoreCase)) return null;
        var text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));
        var monthMatches = MonthYearPattern.Matches(text)
            .Select(match => ToMonth(match))
            .Where(month => month is not null)
            .Select(month => month!.Value)
            .Distinct()
            .ToArray();
        if (!monthMatches.Contains(expectedMonth))
        {
            return null;
        }

        var values = CurrentResidentialRatePattern.Matches(text)
            .Select(match => match.Groups[1].Success
                ? match.Groups[1].Value : match.Groups[2].Value)
            .Select(valueText => decimal.TryParse(valueText,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
                    ? value : 0m)
            .Where(value => value is >= 1m and <= 100m)
            .Distinct()
            .ToArray();
        if (values.Length != 1) return null;

        return new ElectricityRateSnapshot(1, "Meralco", "PHP", values[0],
            expectedMonth, retrievedAt, retrievedAt.AddMonths(1),
            sourceUri.AbsoluteUri, utilityConfidence,
            MachinePowerEstimateConfidence.HighEstimate);
    }

    private static IReadOnlyList<Uri> GetMeralcoAdvisoryCandidates(
        DateOnly month)
    {
        var monthName = month.ToDateTime(TimeOnly.MinValue).ToString("MMMM",
            System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant();
        var year = month.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return
        [
            CreateMeralcoAdvisoryUri($"lower-rates-{monthName}-{year}"),
            CreateMeralcoAdvisoryUri($"higher-residential-rates-{monthName}-{year}"),
            CreateMeralcoAdvisoryUri($"higher-rates-{monthName}-{year}"),
            CreateMeralcoAdvisoryUri($"power-rates-stable-{monthName}-{year}")
        ];
    }

    private static Uri CreateMeralcoAdvisoryUri(string slug) => new(
        $"https://{MeralcoHost}/news-and-advisories/{slug}");

    private async Task<string> GetAllowedTextAsync(Uri uri, string allowedHost,
        CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, allowedHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException("Rate endpoint is not allowlisted.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request,
            HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.RequestMessage?.RequestUri is not { } finalUri ||
            finalUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(finalUri.Host, allowedHost,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException("Rate endpoint redirected outside allowlist.");
        }
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        if (text.Length > 256_000)
        {
            throw new HttpRequestException("Rate response exceeded bounded size.");
        }
        return text;
    }

    private static DateOnly? ToMonth(Match match) =>
        DateTime.TryParse($"{match.Groups[1].Value} 1 {match.Groups[2].Value}",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed)
            ? DateOnly.FromDateTime(parsed) : null;
}
