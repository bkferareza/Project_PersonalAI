using System.Diagnostics;
using Machine.Core;

namespace Machine.App;

public sealed class MachineShutdownCoordinator
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    private readonly MachineLearningService _learningService;
    private readonly IMachineLearningStore _learningStore;
    private readonly MachineHealthHistoryService? _healthHistoryService;
    private readonly IMachineHealthHistoryStore? _healthHistoryStore;
    private readonly IOllamaRuntimeBootstrapper _runtimeBootstrapper;
    private readonly CancellationTokenSource _applicationCancellation;
    private readonly Action _stopWindowWork;
    private readonly Action _disposeHttpResources;
    private readonly TimeSpan _timeout;
    private readonly object _sync = new();
    private Task? _shutdownTask;

    public MachineShutdownCoordinator(
        MachineLearningService learningService,
        IMachineLearningStore learningStore,
        IOllamaRuntimeBootstrapper runtimeBootstrapper,
        CancellationTokenSource applicationCancellation,
        Action stopWindowWork,
        Action disposeHttpResources,
        TimeSpan? timeout = null,
        MachineHealthHistoryService? healthHistoryService = null,
        IMachineHealthHistoryStore? healthHistoryStore = null)
    {
        ArgumentNullException.ThrowIfNull(learningService);
        ArgumentNullException.ThrowIfNull(learningStore);
        ArgumentNullException.ThrowIfNull(runtimeBootstrapper);
        ArgumentNullException.ThrowIfNull(applicationCancellation);
        ArgumentNullException.ThrowIfNull(stopWindowWork);
        ArgumentNullException.ThrowIfNull(disposeHttpResources);
        if ((healthHistoryService is null) != (healthHistoryStore is null))
        {
            throw new ArgumentException(
                "Health history service and store must be supplied together.");
        }
        _learningService = learningService;
        _learningStore = learningStore;
        _healthHistoryService = healthHistoryService;
        _healthHistoryStore = healthHistoryStore;
        _runtimeBootstrapper = runtimeBootstrapper;
        _applicationCancellation = applicationCancellation;
        _stopWindowWork = stopWindowWork;
        _disposeHttpResources = disposeHttpResources;
        _timeout = timeout ?? DefaultTimeout;
    }

    public Task BeginShutdown()
    {
        lock (_sync)
        {
            if (_shutdownTask is not null)
            {
                return _shutdownTask;
            }

            _stopWindowWork();
            _applicationCancellation.Cancel();
            _shutdownTask = CompleteShutdownAsync();
            return _shutdownTask;
        }
    }

    private async Task CompleteShutdownAsync()
    {
        using var timeout = new CancellationTokenSource(_timeout);
        try
        {
            var finalSave = _learningService.SaveFinalSnapshotAsync(
                _learningStore,
                DateTimeOffset.UtcNow,
                timeout.Token);
            var runtimeShutdown = _runtimeBootstrapper.ShutdownAsync(
                timeout.Token);
            var healthSave = _healthHistoryService is null ||
                _healthHistoryStore is null
                    ? Task.CompletedTask
                    : _healthHistoryService.SaveFinalSnapshotAsync(
                        _healthHistoryStore,
                        DateTimeOffset.UtcNow,
                        timeout.Token);
            await Task.WhenAll(finalSave, healthSave, runtimeShutdown)
                .WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested)
        {
            Debug.WriteLine("Machine shutdown timed out.");
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
        finally
        {
            _disposeHttpResources();
        }
    }
}
