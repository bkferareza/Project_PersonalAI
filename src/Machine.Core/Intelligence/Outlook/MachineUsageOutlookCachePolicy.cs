using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Machine.Core;

public sealed class MachineUsageOutlookCachePolicy
{
    public static readonly TimeSpan CacheTimeToLive =
        TimeSpan.FromMinutes(60);
    public static readonly TimeSpan FailureRetryDelay =
        TimeSpan.FromMinutes(5);

    private static readonly MachineUsageOutlookDecision NoDecision = new(
        MachineUsageOutlookDecisionKind.None,
        string.Empty,
        null);

    private CachedOutlook? _cache;
    private MachineUsageOutlookDecision? _activeDecision;
    private DateTimeOffset? _retryAfter;

    public bool IsRequestInFlight => _activeDecision is not null;

    public MachineUsageOutlookDecision Request(
        MachineUsageOutlookRequest request,
        DateTimeOffset requestedAt,
        bool isOverviewVisible,
        bool forceRefresh = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!isOverviewVisible || IsRequestInFlight)
        {
            return NoDecision;
        }

        var fingerprint = CreateFingerprint(request);
        if (!forceRefresh &&
            _cache is { } cache &&
            string.Equals(cache.Fingerprint, fingerprint,
                StringComparison.Ordinal) &&
            requestedAt - cache.CachedAt < CacheTimeToLive)
        {
            return new(
                MachineUsageOutlookDecisionKind.UseCached,
                fingerprint,
                cache.Outlook);
        }

        if (!forceRefresh && _retryAfter is { } retryAfter &&
            requestedAt < retryAfter)
        {
            return NoDecision;
        }

        var decision = new MachineUsageOutlookDecision(
            MachineUsageOutlookDecisionKind.Generate,
            fingerprint,
            null);
        _activeDecision = decision;
        return decision;
    }

    public void Complete(
        MachineUsageOutlookDecision decision,
        MachineUsageOutlook? outlook,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (_activeDecision is null ||
            !_activeDecision.Equals(decision))
        {
            throw new InvalidOperationException(
                "The Outlook request is not active.");
        }

        _activeDecision = null;
        if (outlook is null)
        {
            _retryAfter = completedAt + FailureRetryDelay;
            return;
        }

        _cache = new(decision.Fingerprint, outlook, completedAt);
        _retryAfter = null;
    }

    public static string CreateFingerprint(
        MachineUsageOutlookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var forecast = request.Forecast;
        var today = forecast.Today;
        var currentUsage = forecast.CurrentHourUsage;
        var patterns = request.RelevantPatterns
            .Where(pattern =>
                pattern.Confidence == MachineLearningConfidence.Established &&
                pattern.Freshness != MachineLearningFreshness.Stale)
            .OrderBy(pattern => pattern.StartHour)
            .ThenBy(pattern => pattern.ActivityState)
            .Take(2)
            .Select(pattern => string.Join(',',
                pattern.ActivityState,
                pattern.StartHour,
                pattern.EndHourExclusive,
                pattern.CrossesMidnight))
            .ToArray();
        var material = string.Join('|',
            forecast.CurrentContext?.LocalHour,
            forecast.CurrentContext?.ActivityState,
            request.GlobalLearningState,
            forecast.CurrentContextMaturity,
            forecast.CurrentPowerMaturity,
            Bucket(forecast.TypicalPowerWatts, 10d),
            Bucket(forecast.TypicalPowerLowerWatts, 10d),
            Bucket(forecast.TypicalPowerUpperWatts, 10d),
            Bucket(currentUsage?.ActiveFraction, 0.1d),
            currentUsage?.Maturity,
            today.ComparisonState,
            Bucket(today.ActualObservedEnergyKilowattHours, 0.05d),
            Bucket(today.ExpectedLowerEnergyKilowattHours, 0.05d),
            Bucket(today.ExpectedUpperEnergyKilowattHours, 0.05d),
            forecast.AvailabilityReason,
            forecast.ForecastMaturity,
            Bucket(forecast.ForecastCoverage, 0.1d),
            Bucket(forecast.NextObservedHourEnergyLowerKilowattHours, 0.01d),
            Bucket(forecast.NextObservedHourEnergyUpperKilowattHours, 0.01d),
            Bucket(forecast.ProjectedEndOfDayLowerKilowattHours, 0.05d),
            Bucket(forecast.ProjectedEndOfDayUpperKilowattHours, 0.05d),
            forecast.RateReference?.ProviderName,
            forecast.RateReference?.EffectiveMonth.ToString(
                "yyyy-MM",
                CultureInfo.InvariantCulture),
            string.Join(';', patterns));
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(material)));
    }

    private static string Bucket(double? value, double size) =>
        value is { } finite && double.IsFinite(finite)
            ? Math.Round(
                finite / size,
                MidpointRounding.AwayFromZero).ToString(
                    CultureInfo.InvariantCulture)
            : "missing";

    private sealed record CachedOutlook(
        string Fingerprint,
        MachineUsageOutlook Outlook,
        DateTimeOffset CachedAt);
}
