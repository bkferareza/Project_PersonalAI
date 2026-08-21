using System.Text.Json;

namespace Machine.Core;

public sealed record ElectricityRateCacheState(
    IReadOnlyList<ElectricityRateSnapshot> Rates);

public sealed class FileElectricityRateCache
{
    public const int MaximumRateCount = 24;
    private const string FileName = "electricity-rate-v1.json";
    private readonly string _filePath;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = false };

    public FileElectricityRateCache(string? directoryPath = null)
    {
        var directory = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Machine");
        _filePath = Path.Combine(directory, FileName);
    }

    public async Task<ElectricityRateCacheState> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_filePath)) return new([]);
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<ElectricityRateCacheState>(stream, _options, cancellationToken).ConfigureAwait(false) ?? new([]);
        }
        catch (JsonException) { return new([]); }
        catch (IOException) { return new([]); }
        catch (UnauthorizedAccessException) { return new([]); }
    }

    public async Task SaveAsync(IEnumerable<ElectricityRateSnapshot> rates, CancellationToken cancellationToken = default)
    {
        var state = new ElectricityRateCacheState(rates.Where(IsSafe).OrderByDescending(item => item.EffectiveMonth).Take(MaximumRateCount).ToArray());
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporary = _filePath + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, state, _options, cancellationToken).ConfigureAwait(false);
        }
        File.Move(temporary, _filePath, true);
    }

    private static bool IsSafe(ElectricityRateSnapshot rate) =>
        rate.SchemaVersion == 1 && !string.IsNullOrWhiteSpace(rate.ProviderName) &&
        !string.IsNullOrWhiteSpace(rate.CurrencyCode) && rate.RatePerKWh > 0 &&
        !string.IsNullOrWhiteSpace(rate.SourceIdentity);
}
