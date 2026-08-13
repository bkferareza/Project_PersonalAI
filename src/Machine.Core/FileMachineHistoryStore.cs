using System.Text.Json;

namespace Machine.Core;

public sealed class FileMachineHistoryStore :
    IMachineHistoryStore,
    IMachineHistoryStoreDiagnostics
{
    public const string FileName = "matasuri-history-v1.json";

    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false
    };

    public FileMachineHistoryStore(string? directoryPath = null)
    {
        var directory = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Machine");
        _filePath = Path.Combine(directory, FileName);
    }

    public MachineHistoryStoreLoadStatus LastLoadStatus { get; private set; }

    public async Task<MachineHistoryPersistedState?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                LastLoadStatus = MachineHistoryStoreLoadStatus.NotFound;
                return null;
            }

            await using var stream = File.OpenRead(_filePath);
            var state = await JsonSerializer.DeserializeAsync<
                MachineHistoryPersistedState>(
                    stream,
                    _jsonOptions,
                    cancellationToken).ConfigureAwait(false);
            LastLoadStatus = state is null
                ? MachineHistoryStoreLoadStatus.Corrupt
                : MachineHistoryStoreLoadStatus.Loaded;
            return state;
        }
        catch (JsonException)
        {
            LastLoadStatus = MachineHistoryStoreLoadStatus.Corrupt;
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            LastLoadStatus = MachineHistoryStoreLoadStatus.Unavailable;
            return null;
        }
    }

    public async Task SaveAsync(
        MachineHistoryPersistedState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    state,
                    _jsonOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
