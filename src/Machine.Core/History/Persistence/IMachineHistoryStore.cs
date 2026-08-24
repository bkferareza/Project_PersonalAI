namespace Machine.Core;

public interface IMachineHistoryStore
{
    Task<MachineHistoryPersistedState?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        MachineHistoryPersistedState state,
        CancellationToken cancellationToken = default);
}
public enum MachineHistoryStoreLoadStatus
{
    NotAttempted,
    NotFound,
    Loaded,
    Corrupt,
    Incompatible,
    Unavailable
}

public interface IMachineHistoryStoreDiagnostics
{
    MachineHistoryStoreLoadStatus LastLoadStatus { get; }
}
