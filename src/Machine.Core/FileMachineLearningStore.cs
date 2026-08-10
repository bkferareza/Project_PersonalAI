using System.Text.Json;

namespace Machine.Core;

public sealed class FileMachineLearningStore : IMachineLearningStore
{
    private const string FileName = "learning-state.json";
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false
    };

    public FileMachineLearningStore(string? directoryPath = null)
    {
        var directory = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Machine");
        _filePath = Path.Combine(directory, FileName);
    }

    public async Task<MachineLearningPersistedState?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<
                MachineLearningPersistedState>(stream, _jsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task SaveAsync(MachineLearningPersistedState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, _jsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        File.Move(temporaryPath, _filePath, overwrite: true);
    }
}
