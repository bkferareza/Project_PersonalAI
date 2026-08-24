namespace Machine.Core;

public sealed class FileMachineHealthHistoryStore :
    IMachineHealthHistoryStore,
    IMachineHealthHistoryStoreDiagnostics
{
    private const string FileName = "health-history-v1.json";
    private readonly SafeJsonFile<MachineHealthHistoryPersistedState>
        _safeFile;

    public FileMachineHealthHistoryStore(string? directoryPath = null)
    {
        var directory = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Machine");
        _safeFile = new SafeJsonFile<MachineHealthHistoryPersistedState>(
            Path.Combine(directory, FileName),
            new()
            {
                WriteIndented = false
            },
            MachineHealthHistoryService.ValidatePersistedState);
    }

    public MachineHealthHistoryStoreLoadStatus LastLoadStatus
    {
        get;
        private set;
    }

    public async Task<MachineHealthHistoryPersistedState?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _safeFile.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        LastLoadStatus = result.Status switch
        {
            MachineSafeJsonLoadStatus.NotFound =>
                MachineHealthHistoryStoreLoadStatus.NotFound,
            MachineSafeJsonLoadStatus.Loaded =>
                MachineHealthHistoryStoreLoadStatus.Loaded,
            MachineSafeJsonLoadStatus.Rejected =>
                MachineHealthHistoryStoreLoadStatus.Corrupt,
            MachineSafeJsonLoadStatus.Incompatible =>
                MachineHealthHistoryStoreLoadStatus.Incompatible,
            _ => MachineHealthHistoryStoreLoadStatus.Unavailable
        };
        return result.Value;
    }

    public async Task SaveAsync(
        MachineHealthHistoryPersistedState state,
        CancellationToken cancellationToken = default)
    {
        await _safeFile.SaveAsync(state, cancellationToken)
            .ConfigureAwait(false);
    }
}
