using System.Text.Json;

namespace Machine.Core;

public sealed class FileMachineHealthHistoryStore :
    IMachineHealthHistoryStore,
    IMachineHealthHistoryStoreDiagnostics
{
    private const string FileName = "health-history-v1.json";
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false
    };

    public FileMachineHealthHistoryStore(string? directoryPath = null)
    {
        var directory = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Machine");
        _filePath = Path.Combine(directory, FileName);
    }

    public MachineHealthHistoryStoreLoadStatus LastLoadStatus
    {
        get;
        private set;
    }

    public async Task<MachineHealthHistoryPersistedState?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                LastLoadStatus =
                    MachineHealthHistoryStoreLoadStatus.NotFound;
                return null;
            }

            await using var stream = File.OpenRead(_filePath);
            var state = await JsonSerializer.DeserializeAsync<
                MachineHealthHistoryPersistedState>(
                    stream,
                    _jsonOptions,
                    cancellationToken).ConfigureAwait(false);
            LastLoadStatus = state is null
                ? MachineHealthHistoryStoreLoadStatus.Corrupt
                : MachineHealthHistoryStoreLoadStatus.Loaded;
            return state;
        }
        catch (JsonException)
        {
            LastLoadStatus = MachineHealthHistoryStoreLoadStatus.Corrupt;
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            LastLoadStatus = MachineHealthHistoryStoreLoadStatus.Unavailable;
            return null;
        }
    }

    public async Task SaveAsync(
        MachineHealthHistoryPersistedState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";
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
}
