namespace Machine.Core;

public sealed class FileMachineHistoryStore :
    IMachineHistoryStore,
    IMachineHistoryStoreDiagnostics
{
    public const string FileName = "matasuri-history-v1.json";

    private readonly SafeJsonFile<MachineHistoryPersistedState> _safeFile;

    public FileMachineHistoryStore(string? directoryPath = null)
    {
        var directory = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Machine");
        _safeFile = new SafeJsonFile<MachineHistoryPersistedState>(
            Path.Combine(directory, FileName),
            new()
            {
                WriteIndented = false
            },
            MachineHistoryService.ValidatePersistedState);
    }

    public MachineHistoryStoreLoadStatus LastLoadStatus { get; private set; }

    public async Task<MachineHistoryPersistedState?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _safeFile.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        LastLoadStatus = result.Status switch
        {
            MachineSafeJsonLoadStatus.NotFound =>
                MachineHistoryStoreLoadStatus.NotFound,
            MachineSafeJsonLoadStatus.Loaded =>
                MachineHistoryStoreLoadStatus.Loaded,
            MachineSafeJsonLoadStatus.Rejected =>
                MachineHistoryStoreLoadStatus.Corrupt,
            MachineSafeJsonLoadStatus.Incompatible =>
                MachineHistoryStoreLoadStatus.Incompatible,
            _ => MachineHistoryStoreLoadStatus.Unavailable
        };
        return result.Value;
    }

    public async Task SaveAsync(
        MachineHistoryPersistedState state,
        CancellationToken cancellationToken = default)
    {
        await _safeFile.SaveAsync(state, cancellationToken)
            .ConfigureAwait(false);
    }
}
