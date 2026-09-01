using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Machine.Core;

public sealed partial class MachineBriefCachePolicy
{
    public static readonly TimeSpan CacheTimeToLive = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan FailureRetryDelay = TimeSpan.FromMinutes(5);

    private static readonly MachineBriefDecision NoDecision = new(
        MachineBriefDecisionKind.None, string.Empty, null);

    private CachedBrief? _cache;
    private MachineBriefDecision? _activeDecision;
    private DateTimeOffset? _retryAfter;
    private readonly string _promptVersion;
    private readonly int _responseSchemaVersion;

    public MachineBriefCachePolicy(
        string promptVersion = MachineBriefPromptPolicy.CurrentVersion,
        int responseSchemaVersion =
            MachineBriefPromptPolicy.ResponseSchemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        if (responseSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(responseSchemaVersion));
        }
        _promptVersion = promptVersion;
        _responseSchemaVersion = responseSchemaVersion;
    }

    public bool IsRequestInFlight => _activeDecision is not null;

    public MachineBriefDecision Request(
        MachineBriefRequest request,
        DateTimeOffset requestedAt,
        bool isOverviewVisible,
        bool forceRefresh = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!isOverviewVisible || IsRequestInFlight)
        {
            return NoDecision;
        }

        var fingerprint = CreateRequestFingerprint(request);
        if (!forceRefresh &&
            _cache is { } cache &&
            string.Equals(cache.Fingerprint, fingerprint,
                StringComparison.Ordinal) &&
            requestedAt - cache.CachedAt < CacheTimeToLive)
        {
            return new(MachineBriefDecisionKind.UseCached, fingerprint,
                cache.Brief);
        }

        if (!forceRefresh && _retryAfter is { } retryAfter &&
            requestedAt < retryAfter)
        {
            return NoDecision;
        }

        var decision = new MachineBriefDecision(
            MachineBriefDecisionKind.Generate, fingerprint, null);
        _activeDecision = decision;
        return decision;
    }

    public void Complete(
        MachineBriefDecision decision,
        MachineBrief? brief,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (_activeDecision is null || !_activeDecision.Equals(decision))
        {
            throw new InvalidOperationException(
                "The Matasuri Brief request is not active.");
        }

        _activeDecision = null;
        if (brief is null)
        {
            _retryAfter = completedAt + FailureRetryDelay;
            return;
        }

        _cache = new(decision.Fingerprint, brief, completedAt);
        _retryAfter = null;
    }

    public string CreateRequestFingerprint(MachineBriefRequest request) =>
        CreateFingerprint(request, _promptVersion, _responseSchemaVersion);

    public static string CreateFingerprint(MachineBriefRequest request) =>
        CreateFingerprint(
            request,
            MachineBriefPromptPolicy.CurrentVersion,
            MachineBriefPromptPolicy.ResponseSchemaVersion);

    public static string CreateFingerprint(
        MachineBriefRequest request,
        string promptVersion,
        int responseSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Situation);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModelIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RuntimeVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        if (responseSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(responseSchemaVersion));
        }

        var situation = request.Situation;
        var learning = situation.LearningAwareness;
        var evidence = situation.Evidence
            .Select(item => string.Join('~',
                item.Id,
                item.Category,
                item.TimeScope,
                item.Importance,
                item.Freshness,
                item.Maturity,
                item.AllowsCausalLanguage,
                NormalizeMaterialText(item.Summary),
                string.Join(',', item.DisplayValues.Select(
                    NormalizeMaterialText)),
                string.Join(',', item.EntityNames.Order(
                    StringComparer.Ordinal))))
            .ToArray();
        var material = string.Join('|',
            promptVersion,
            responseSchemaVersion,
            situation.SchemaVersion,
            request.ModelIdentity,
            request.RuntimeVersion,
            situation.GlobalPosture,
            learning.GlobalState,
            learning.CurrentContext,
            Bucket(learning.CurrentContextSampleCount, 20),
            learning.CurrentContextObservedDayCount,
            learning.CurrentContextMaturity,
            learning.CurrentPowerMaturity,
            learning.PatternReadinessBlocker,
            learning.ForecastAvailability,
            Bucket(learning.ForecastCoverage, 0.1d),
            string.Join(';', evidence));
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(material)));
    }

    private static string NormalizeMaterialText(string value)
    {
        var bucketSize = value.Contains("kWh",
                StringComparison.OrdinalIgnoreCase)
            ? 0.05d
            : value.Contains('%', StringComparison.Ordinal)
                ? 5d
                : value.Contains(" W", StringComparison.OrdinalIgnoreCase)
                    ? 10d
                    : value.Contains('₱', StringComparison.Ordinal) ||
                      value.Contains("cost", StringComparison.OrdinalIgnoreCase)
                        ? 0.25d
                        : value.Contains("sample", StringComparison.OrdinalIgnoreCase) ||
                          value.Contains("observation", StringComparison.OrdinalIgnoreCase)
                            ? 20d
                            : 5d;
        return MaterialNumberRegex().Replace(value, match =>
            double.TryParse(match.Value.Replace(",", string.Empty,
                    StringComparison.Ordinal),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsed)
                    ? $"#{Bucket(parsed, bucketSize)}"
                    : "#");
    }

    private static string Bucket(long value, long size) =>
        (value / Math.Max(1, size)).ToString(CultureInfo.InvariantCulture);

    private static string Bucket(double value, double size) =>
        double.IsFinite(value)
            ? Math.Round(value / size, MidpointRounding.AwayFromZero)
                .ToString(CultureInfo.InvariantCulture)
            : "missing";

    [GeneratedRegex(@"(?<![\p{L}\d])[-+]?\d+(?:,\d{3})*(?:\.\d+)?")]
    private static partial Regex MaterialNumberRegex();

    private sealed record CachedBrief(
        string Fingerprint,
        MachineBrief Brief,
        DateTimeOffset CachedAt);
}
