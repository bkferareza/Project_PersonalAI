namespace Machine.Core;

public interface IMachineHealthHistoryStore
{
    Task<MachineHealthHistoryPersistedState?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        MachineHealthHistoryPersistedState state,
        CancellationToken cancellationToken = default);
}

public enum MachineHealthHistoryStoreLoadStatus
{
    NotAttempted,
    NotFound,
    Loaded,
    Corrupt,
    Incompatible,
    Unavailable
}

public interface IMachineHealthHistoryStoreDiagnostics
{
    MachineHealthHistoryStoreLoadStatus LastLoadStatus { get; }
}
