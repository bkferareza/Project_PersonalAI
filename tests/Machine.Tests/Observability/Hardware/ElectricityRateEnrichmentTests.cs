using System.Net;
using System.Text;
using System.Text.Json;
using Machine.Core;

namespace Machine.Tests;

public sealed class ElectricityRateEnrichmentTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        "MachineTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CacheBoundsSafeRatesOnSave()
    {
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0,
            TimeSpan.Zero);
        var cache = new FileElectricityRateCache(_directory);
        await cache.SaveAsync(Enumerable.Range(0,
                FileElectricityRateCache.MaximumRateCount + 6)
            .Select(index => Rate(now) with
            {
                EffectiveMonth = new DateOnly(2026, 8, 1)
                    .AddMonths(-index)
            }));

        var restored = await cache.LoadAsync();

        Assert.Equal(FileElectricityRateCache.MaximumRateCount,
            restored.Rates.Count);
        Assert.False(File.Exists(Path.Combine(_directory,
            "electricity-rate-v1.json.tmp")));
    }

    [Fact]
    public async Task CachePreservesNewerSchemaAndBlocksWrites()
    {
        Directory.CreateDirectory(_directory);
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0,
            TimeSpan.Zero);
        var filePath = Path.Combine(_directory,
            "electricity-rate-v1.json");
        var json = JsonSerializer.Serialize(new ElectricityRateCacheState(
        [
            Rate(now) with { SchemaVersion = 2 }
        ]));
        await File.WriteAllTextAsync(filePath, json);
        var cache = new FileElectricityRateCache(_directory);

        Assert.Empty((await cache.LoadAsync()).Rates);
        Assert.Equal(json, await File.ReadAllTextAsync(filePath));
        var rejected = Assert.Single(Directory.GetFiles(_directory,
            "electricity-rate-v1.json.rejected-*"));
        Assert.Equal(json, await File.ReadAllTextAsync(rejected));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.SaveAsync([Rate(now)]));
        Assert.Equal(json, await File.ReadAllTextAsync(filePath));
        Assert.False(File.Exists(filePath + ".tmp"));
    }

    [Fact]
    public async Task CachePreservesSemanticallyUnsafeRate()
    {
        Directory.CreateDirectory(_directory);
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0,
            TimeSpan.Zero);
        var filePath = Path.Combine(_directory,
            "electricity-rate-v1.json");
        var json = JsonSerializer.Serialize(new ElectricityRateCacheState(
        [
            Rate(now) with { ExpiresAt = now.AddMinutes(-1) }
        ]));
        await File.WriteAllTextAsync(filePath, json);

        Assert.Empty((await new FileElectricityRateCache(_directory)
            .LoadAsync()).Rates);

        Assert.Equal(json, await File.ReadAllTextAsync(filePath));
        Assert.Single(Directory.GetFiles(_directory,
            "electricity-rate-v1.json.rejected-*"));
    }

    [Fact]
    public async Task CurrentMonthCacheIsReusedWithoutNetworkRequest()
    {
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0,
            TimeSpan.Zero);
        var cache = new FileElectricityRateCache(_directory);
        await cache.SaveAsync([Rate(now)]);
        var handler = new FixtureHandler(_ => throw new InvalidOperationException());
        using var client = new HttpClient(handler);
        var service = new ElectricityRateEnrichmentService(client, cache);

        var result = await service.GetCurrentAsync(now);

        Assert.True(result.UsedCache);
        Assert.Equal(0, result.RequestCount);
        Assert.Equal(0, handler.RequestCount);
        Assert.Equal(14.1234m, result.Rate?.RatePerKWh);
    }

    [Fact]
    public async Task ValidNcrLocationAndOfficialRateAreCachedWithoutSensitiveFields()
    {
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0,
            TimeSpan.Zero);
        var handler = new FixtureHandler(request => request.RequestUri!.Host switch
        {
            "ipinfo.io" => """{"country":"PH","region":"Cavite","ip":"203.0.113.5","loc":"14.6,120.9"}""",
            "company.meralco.com.ph" => """
                <article>Power rates are stable in August 2026. The overall rate for a typical household is ₱14.1234 per kWh. A separate charge is ₱0.0371.</article>
                """,
            _ => throw new InvalidOperationException()
        });
        using var client = new HttpClient(handler);
        var cache = new FileElectricityRateCache(_directory);
        var service = new ElectricityRateEnrichmentService(client, cache);

        var result = await service.GetCurrentAsync(now);
        var persisted = await File.ReadAllTextAsync(Path.Combine(_directory,
            "electricity-rate-v1.json"));

        Assert.False(result.UsedCache);
        Assert.Equal(2, result.RequestCount);
        Assert.Equal("Meralco", result.ProbableUtility);
        Assert.Equal(MachinePowerEstimateConfidence.HighEstimate,
            result.UtilityConfidence);
        Assert.Equal(14.1234m, result.Rate?.RatePerKWh);
        Assert.Contains("company.meralco.com.ph", persisted);
        Assert.DoesNotContain("203.0.113.5", persisted);
        Assert.DoesNotContain("latitude", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longitude", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("14.6,120.9", persisted);
    }

    [Fact]
    public async Task CurrentOverallRateIsSelectedOverPreviousRateAndChangeAmount()
    {
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0,
            TimeSpan.Zero);
        var handler = new FixtureHandler(request => request.RequestUri!.Host == "ipinfo.io"
            ? """{"country":"PH","region":"Cavite"}"""
            : """
                <title>Lower Rates this August 2026</title>
                <p>The overall rate for a typical household went down by P0.0428 per kWh, bringing the overall rate to P14.7833 from P14.8261 per kWh in July 2026.</p>
                <p>The generation charge is P9.2504 per kWh.</p>
                """);
        using var client = new HttpClient(handler);
        var service = new ElectricityRateEnrichmentService(client,
            new FileElectricityRateCache(_directory));

        var result = await service.GetCurrentAsync(now);

        Assert.Equal(14.7833m, result.Rate?.RatePerKWh);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Rate?.EffectiveMonth);
        Assert.Contains("lower-rates-august-2026", result.Rate?.SourceIdentity);
    }

    [Theory]
    [InlineData("{\"country\":\"PH\",\"region\":\"Calabarzon\"}")]
    [InlineData("{\"country\":\"US\",\"region\":\"California\"}")]
    [InlineData("{\"country\":\"PH\"}")]
    public async Task InsufficientLocationDoesNotRequestRate(string location)
    {
        var handler = new FixtureHandler(request => request.RequestUri!.Host == "ipinfo.io"
            ? location : throw new InvalidOperationException("Rate source must not be requested."));
        using var client = new HttpClient(handler);
        var service = new ElectricityRateEnrichmentService(client,
            new FileElectricityRateCache(_directory));

        var result = await service.GetCurrentAsync(new DateTimeOffset(2026, 8,
            21, 12, 0, 0, TimeSpan.Zero));

        Assert.Null(result.Rate);
        Assert.Null(result.ProbableUtility);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task WrongEffectiveMonthFailsClosed()
    {
        var handler = new FixtureHandler(request => request.RequestUri!.Host == "ipinfo.io"
            ? """{"country":"PH","region":"Cavite"}"""
            : "The overall residential rate for September 2026 is ₱14.1234 per kWh.");
        using var client = new HttpClient(handler);
        var service = new ElectricityRateEnrichmentService(client,
            new FileElectricityRateCache(_directory));

        var result = await service.GetCurrentAsync(new DateTimeOffset(2026, 8,
            21, 12, 0, 0, TimeSpan.Zero));

        Assert.Null(result.Rate);
        Assert.Equal("Meralco", result.ProbableUtility);
    }

    [Fact]
    public async Task MultipleContextualRatesFailClosed()
    {
        var handler = new FixtureHandler(request => request.RequestUri!.Host == "ipinfo.io"
            ? """{"country":"PH","region":"Cavite"}"""
            : "The overall rate for August 2026 is ₱14.1234 per kWh. The residential rate for August 2026 is ₱13.0000 per kWh.");
        using var client = new HttpClient(handler);
        var service = new ElectricityRateEnrichmentService(client,
            new FileElectricityRateCache(_directory));

        var result = await service.GetCurrentAsync(new DateTimeOffset(2026, 8,
            21, 12, 0, 0, TimeSpan.Zero));

        Assert.Null(result.Rate);
    }

    [Fact]
    public async Task ExpiredSameMonthCacheDoesNotSuppressEnrichment()
    {
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var cache = new FileElectricityRateCache(_directory);
        await cache.SaveAsync([Rate(now) with { ExpiresAt = now.AddSeconds(-1) }]);
        var handler = new FixtureHandler(request => request.RequestUri!.Host == "ipinfo.io"
            ? """{"country":"PH","region":"Cavite"}"""
            : "The overall rate for August 2026 is ₱14.1234 per kWh.");
        using var client = new HttpClient(handler);
        var service = new ElectricityRateEnrichmentService(client, cache);

        var result = await service.GetCurrentAsync(now);

        Assert.False(result.UsedCache);
        Assert.Equal(2, handler.RequestCount);
        Assert.NotNull(result.Rate);
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneEnrichmentAndThenReuseCache()
    {
        var handler = new FixtureHandler(request => request.RequestUri!.Host == "ipinfo.io"
            ? """{"country":"PH","region":"Cavite"}"""
            : "The overall residential rate for August 2026 is ₱14.1234 per kWh.");
        using var client = new HttpClient(handler);
        var service = new ElectricityRateEnrichmentService(client,
            new FileElectricityRateCache(_directory));
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

        var results = await Task.WhenAll(service.GetCurrentAsync(now),
            service.GetCurrentAsync(now));

        Assert.Equal(2, handler.RequestCount);
        Assert.Single(results, result => !result.UsedCache);
        Assert.Single(results, result => result.UsedCache);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private static ElectricityRateSnapshot Rate(DateTimeOffset now) => new(1,
        "Meralco", "PHP", 14.1234m, new DateOnly(2026, 8, 1), now,
        now.AddMonths(1), "https://company.meralco.com.ph/news",
        MachinePowerEstimateConfidence.HighEstimate,
        MachinePowerEstimateConfidence.HighEstimate);

    private sealed class FixtureHandler(Func<HttpRequestMessage, string> content) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string> _content = content;
        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(_content(request), Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
