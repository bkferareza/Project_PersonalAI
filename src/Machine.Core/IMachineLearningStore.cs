namespace Machine.Core;

public interface IMachineLearningStore
{
    Task<MachineLearningPersistedState?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        MachineLearningPersistedState state,
        CancellationToken cancellationToken = default);
}
