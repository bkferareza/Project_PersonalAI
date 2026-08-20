using System.Text.Json;

namespace Machine.Core;

public sealed class FileMachineLearningStore :
    IMachineLearningStore,
    IMachineLearningStoreDiagnostics,
    IMachineLearningStoreSaveDiagnostics
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

    public MachineLearningStoreLoadStatus LastLoadStatus { get; private set; }

    public long? LastSavedByteCount { get; private set; }

    public async Task<MachineLearningPersistedState?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                LastLoadStatus = MachineLearningStoreLoadStatus.NotFound;
                return null;
            }
            await using var stream = File.OpenRead(_filePath);
            var state = await JsonSerializer.DeserializeAsync<
                MachineLearningPersistedState>(stream, _jsonOptions,
                cancellationToken).ConfigureAwait(false);
            LastLoadStatus = state is null
                ? MachineLearningStoreLoadStatus.Corrupt
                : MachineLearningStoreLoadStatus.Loaded;
            return state;
        }
        catch (JsonException)
        {
            LastLoadStatus = MachineLearningStoreLoadStatus.Corrupt;
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            LastLoadStatus = MachineLearningStoreLoadStatus.Unavailable;
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
        LastSavedByteCount = new FileInfo(_filePath).Length;
    }
}
