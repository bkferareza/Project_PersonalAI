using System.Text.Json;

namespace Machine.Core;

public sealed class FileMachineLearningActivityStore : IMachineLearningActivityStore
{
    private const string FileName = "learning-activity.json";
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false
    };

    public FileMachineLearningActivityStore(string? directoryPath = null)
    {
        var directory = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Machine");
        _filePath = Path.Combine(directory, FileName);
    }

    public async Task<MachineLearningActivityPersistedState?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_filePath)) return null;
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<MachineLearningActivityPersistedState>(
                stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public async Task SaveAsync(MachineLearningActivityPersistedState state,
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
        File.Move(temporaryPath, _filePath, true);
    }
}
