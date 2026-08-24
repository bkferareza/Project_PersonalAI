namespace Machine.Core;

public sealed class FileMachineActionOutcomeStore :
    IMachineActionOutcomeStore,
    IMachineActionOutcomeStoreDiagnostics
{
    public const string FileName = "matasuri-actions-v1.json";

    private readonly SafeJsonFile<MachineActionOutcomePersistedState>
        _safeFile;

    public FileMachineActionOutcomeStore(string? directoryPath = null)
    {
        var directory = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Machine");
        _safeFile = new(
            Path.Combine(directory, FileName),
            new() { WriteIndented = false },
            MachineActionOutcomeMemory.ValidatePersistedState);
    }

    public MachineActionOutcomeStoreLoadStatus LastLoadStatus
    {
        get;
        private set;
    }

    public async Task<MachineActionOutcomePersistedState?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _safeFile.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        LastLoadStatus = result.Status switch
        {
            MachineSafeJsonLoadStatus.NotFound =>
                MachineActionOutcomeStoreLoadStatus.NotFound,
            MachineSafeJsonLoadStatus.Loaded =>
                MachineActionOutcomeStoreLoadStatus.Loaded,
            MachineSafeJsonLoadStatus.Rejected =>
                MachineActionOutcomeStoreLoadStatus.Corrupt,
            MachineSafeJsonLoadStatus.Incompatible =>
                MachineActionOutcomeStoreLoadStatus.Incompatible,
            _ => MachineActionOutcomeStoreLoadStatus.Unavailable
        };
        return result.Value;
    }

    public Task SaveAsync(
        MachineActionOutcomePersistedState state,
        CancellationToken cancellationToken = default) =>
        _safeFile.SaveAsync(state, cancellationToken);
}
