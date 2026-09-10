using System.Text.Json;

namespace Machine.Core;

public sealed class FileMachineAiMetricStore : IMachineAiMetricStore
{
    public const string FileName = "matasuri-ai-performance-v1.json";
    private readonly SafeJsonFile<MachineAiPerformancePersistedState> _safeFile;

    public FileMachineAiMetricStore(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "Machine");
        _safeFile = new(Path.Combine(directory, FileName),
            new JsonSerializerOptions(), state =>
                state.SchemaVersion > MachineAiPerformanceService.CurrentSchemaVersion
                    ? MachinePersistenceValidationResult.Incompatible
                    : MachineAiPerformanceService.Validate(state)
                        ? MachinePersistenceValidationResult.Accepted
                        : MachinePersistenceValidationResult.Rejected);
    }

    public async Task<MachineAiPerformancePersistedState?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        (await _safeFile.LoadAsync(cancellationToken).ConfigureAwait(false)).Value;

    public Task SaveAsync(
        MachineAiPerformancePersistedState state,
        CancellationToken cancellationToken = default) =>
        _safeFile.SaveAsync(state, cancellationToken);
}
