using Machine.Core;
using Machine.Ollama;
using Machine.Windows;
using Microsoft.UI.Xaml;

namespace Machine.App;

public partial class App : Application
{
    private Window? _window;
    private HttpClient? _ollamaHttpClient;
    private HttpClient? _ollamaInferenceHttpClient;
    private IOllamaRuntimeBootstrapper? _ollamaRuntimeBootstrapper;
    private readonly CancellationTokenSource _appCancellationTokenSource = new();
    private MachineShutdownCoordinator? _shutdownCoordinator;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        IMachineIdentityProvider identityProvider =
            new WindowsMachineIdentityProvider();
        IMachineResourceProvider resourceProvider =
            new WindowsMachineResourceProvider();
        IMachineProcessProvider processProvider =
            new WindowsMachineProcessProvider();
        IMachineStorageProvider storageProvider =
            new WindowsMachineStorageProvider();
        IMachineFolderInspectionProvider folderInspectionProvider =
            new WindowsMachineFolderInspectionProvider();
        IMachineSoftwareInventoryProvider softwareInventoryProvider =
            new WindowsMachineSoftwareInventoryProvider();
        IMachinePackagedSoftwareInventoryProvider
            packagedSoftwareInventoryProvider =
                new WindowsMachinePackagedSoftwareInventoryProvider();
        IMachineStartupInventoryProvider startupInventoryProvider =
            new WindowsMachineStartupInventoryProvider();
        IMachineUserActivityProvider userActivityProvider =
            new WindowsMachineUserActivityProvider();
        var learningService = new MachineLearningService();
        IMachineLearningStore learningStore = new FileMachineLearningStore();
        var ollamaHttpClient = new HttpClient
        {
            BaseAddress = new Uri(
                "http://127.0.0.1:11434/",
                UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(2),
        };
        _ollamaHttpClient = ollamaHttpClient;
        IOllamaStatusProvider ollamaStatusProvider =
            new OllamaStatusProvider(ollamaHttpClient);
        var runtimeBootstrapper = new OllamaRuntimeBootstrapper(
            ollamaHttpClient);
        _ollamaRuntimeBootstrapper = runtimeBootstrapper;
        var inferenceHttpClient = new HttpClient
        {
            BaseAddress = new Uri(
                "http://127.0.0.1:11434/",
                UriKind.Absolute),
            Timeout = TimeSpan.FromMinutes(2),
        };
        _ollamaInferenceHttpClient = inferenceHttpClient;
        IMachineStateExplainer machineStateExplainer =
            new OllamaMachineStateExplainer(
                inferenceHttpClient,
                "qwen3.5:4b");

        var window = new MainWindow(
            identityProvider,
            resourceProvider,
            processProvider,
            ollamaStatusProvider,
            machineStateExplainer,
            storageProvider,
            folderInspectionProvider,
            softwareInventoryProvider,
            packagedSoftwareInventoryProvider,
            startupInventoryProvider,
            userActivityProvider,
            learningService,
            learningStore);
        _window = window;
        _shutdownCoordinator = new MachineShutdownCoordinator(
            learningService,
            learningStore,
            runtimeBootstrapper,
            _appCancellationTokenSource,
            window.StopForApplicationShutdown,
            DisposeHttpResources);
        window.Closed += OnWindowClosed;
        window.Activate();
        _ = BootstrapOllamaAsync();
    }

    private async Task BootstrapOllamaAsync()
    {
        try
        {
            if (_ollamaRuntimeBootstrapper is not null)
            {
                await _ollamaRuntimeBootstrapper.EnsureAvailableAsync(
                    _appCancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException)
            when (_appCancellationTokenSource.IsCancellationRequested)
        {
        }
        catch
        {
            // The regular Ollama status flow reports an unavailable runtime.
        }
    }

    private void OnWindowClosed(
        object sender,
        WindowEventArgs args)
    {
        if (_window is not null)
        {
            _window.Closed -= OnWindowClosed;
            _window = null;
        }

        var shutdownTask = _shutdownCoordinator?.BeginShutdown();
        if (shutdownTask is not null)
        {
            _ = ObserveShutdownAsync(shutdownTask);
        }
    }

    private async Task ObserveShutdownAsync(Task shutdownTask)
    {
        try
        {
            await shutdownTask;
        }
        catch (Exception exception)
        {
            // Shutdown failures must never escape an event callback.
            System.Diagnostics.Debug.WriteLine(exception);
        }
    }

    private void DisposeHttpResources()
    {
        _ollamaHttpClient?.Dispose();
        _ollamaHttpClient = null;
        _ollamaInferenceHttpClient?.Dispose();
        _ollamaInferenceHttpClient = null;
        _ollamaRuntimeBootstrapper = null;
    }
}
