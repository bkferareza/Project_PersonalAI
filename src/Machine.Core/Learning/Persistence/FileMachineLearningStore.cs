namespace Machine.Core;

public sealed class FileMachineLearningStore :
    IMachineLearningStore,
    IMachineLearningStoreDiagnostics,
    IMachineLearningStoreSaveDiagnostics
{
    private const string FileName = "learning-state.json";
    private readonly string _filePath;
    private readonly SafeJsonFile<MachineLearningPersistedState> _safeFile;

    public FileMachineLearningStore(string? directoryPath = null)
    {
        var directory = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Machine");
        _filePath = Path.Combine(directory, FileName);
        _safeFile = new(
            _filePath,
            new() { WriteIndented = false },
            MachineLearningService.ValidatePersistedStateForStorage);
    }

    public MachineLearningStoreLoadStatus LastLoadStatus { get; private set; }

    public long? LastSavedByteCount { get; private set; }

    public async Task<MachineLearningPersistedState?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _safeFile.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        LastLoadStatus = result.Status switch
        {
            MachineSafeJsonLoadStatus.NotFound =>
                MachineLearningStoreLoadStatus.NotFound,
            MachineSafeJsonLoadStatus.Loaded =>
                MachineLearningStoreLoadStatus.Loaded,
            MachineSafeJsonLoadStatus.Rejected =>
                MachineLearningStoreLoadStatus.Corrupt,
            MachineSafeJsonLoadStatus.Incompatible =>
                MachineLearningStoreLoadStatus.Incompatible,
            _ => MachineLearningStoreLoadStatus.Unavailable
        };
        return result.Value;
    }

    public async Task SaveAsync(MachineLearningPersistedState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _safeFile.SaveAsync(state, cancellationToken)
            .ConfigureAwait(false);
        LastSavedByteCount = new FileInfo(_filePath).Length;
    }
}
