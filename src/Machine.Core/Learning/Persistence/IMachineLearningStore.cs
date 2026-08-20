namespace Machine.Core;

public interface IMachineLearningStore
{
    Task<MachineLearningPersistedState?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        MachineLearningPersistedState state,
        CancellationToken cancellationToken = default);
}

public enum MachineLearningStoreLoadStatus
{
    NotAttempted,
    NotFound,
    Loaded,
    Corrupt,
    Unavailable
}

public interface IMachineLearningStoreDiagnostics
{
    MachineLearningStoreLoadStatus LastLoadStatus { get; }
}

public interface IMachineLearningStoreSaveDiagnostics
{
    long? LastSavedByteCount { get; }
}
