using Machine.Core;
using Machine.Ollama;
using Machine.Windows;
using Microsoft.UI.Xaml;

namespace Machine.App;

public partial class App : Application
{
    private MainWindow? _window;
    private HttpClient? _ollamaHttpClient;
    private HttpClient? _ollamaInferenceHttpClient;
    private HttpClient? _electricityRateHttpClient;
    private IOllamaRuntimeBootstrapper? _ollamaRuntimeBootstrapper;
    private IMachineGpuTelemetryProvider? _gpuTelemetryProvider;
    private readonly CancellationTokenSource _appCancellationTokenSource = new();
    private MachineShutdownCoordinator? _shutdownCoordinator;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var activationDisposition = MatasuriActivationRouter.Connect(this);
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
        var actionOutcomeMemory = new MachineActionOutcomeMemory(
            new FileMachineActionOutcomeStore());
        var startupActionService = new WindowsStartupActionService(
            startupInventoryProvider,
            actionOutcomeMemory);
        IMachineUserActivityProvider userActivityProvider =
            new WindowsMachineUserActivityProvider();
        IMachineNetworkProvider networkProvider =
            new WindowsMachineNetworkProvider();
        IMachineSessionProvider sessionProvider =
            new WindowsMachineSessionProvider(userActivityProvider);
        IMachineWindowsUpdateProvider windowsUpdateProvider =
            new WindowsMachineUpdateProvider();
        IMachineRebootPendingProvider rebootPendingProvider =
            new WindowsMachineRebootPendingProvider();
        IMachineReliabilityProvider reliabilityProvider =
            new WindowsMachineReliabilityProvider();
        IMachineServiceInventoryProvider serviceInventoryProvider =
            new WindowsMachineServiceInventoryProvider();
        IMachineScheduledTaskInventoryProvider taskInventoryProvider =
            new WindowsMachineScheduledTaskInventoryProvider();
        IMachineDeviceInventoryProvider deviceInventoryProvider =
            new WindowsMachineDeviceInventoryProvider();
        IMachineGpuTelemetryProvider gpuTelemetryProvider =
            new WindowsMachineGpuTelemetryProvider();
        IMachineCpuHardwareProvider cpuHardwareProvider =
            new WindowsMachineCpuHardwareProvider();
        IMachineStorageDeviceHealthProvider storageDeviceHealthProvider =
            new WindowsMachineStorageDeviceHealthProvider();
        _gpuTelemetryProvider = gpuTelemetryProvider;
        var learningActivityLog = new MachineLearningActivityLog();
        var learningService = new MachineLearningService(
            activityLog: learningActivityLog);
        IMachineLearningStore learningStore = new FileMachineLearningStore();
        IMachineLearningActivityStore learningActivityStore =
            new FileMachineLearningActivityStore();
        var healthHistoryService = new MachineHealthHistoryService();
        IMachineHealthHistoryStore healthHistoryStore =
            new FileMachineHealthHistoryStore();
        var historyService = new MachineHistoryService();
        IMachineHistoryStore historyStore = new FileMachineHistoryStore();
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
        var electricityRateHttpClient = new HttpClient(
            new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        _electricityRateHttpClient = electricityRateHttpClient;
        var electricityRateEnrichment = new ElectricityRateEnrichmentService(
            electricityRateHttpClient, new FileElectricityRateCache());
        IMachineStateExplainer machineStateExplainer =
            new OllamaMachineStateExplainer(
                inferenceHttpClient,
                "qwen3.5:4b");
        string? presentationValidationArguments = null;
#if DEBUG
        presentationValidationArguments = string.Join(
            ' ',
            Environment.GetCommandLineArgs().Skip(1));
        if (!string.IsNullOrWhiteSpace(args.Arguments))
        {
            presentationValidationArguments = string.Join(
                ' ',
                presentationValidationArguments,
                args.Arguments);
        }
#endif

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
            startupActionService,
            userActivityProvider,
            networkProvider,
            sessionProvider,
            windowsUpdateProvider,
            rebootPendingProvider,
            reliabilityProvider,
            learningService,
            learningStore,
            learningActivityStore,
            healthHistoryService,
            healthHistoryStore,
            historyService,
            historyStore,
            serviceInventoryProvider,
            taskInventoryProvider,
            deviceInventoryProvider,
            gpuTelemetryProvider,
            cpuHardwareProvider,
            storageDeviceHealthProvider,
            electricityRateEnrichment,
            presentationValidationArguments);
        _window = window;
        _shutdownCoordinator = new MachineShutdownCoordinator(
            learningService,
            learningStore,
            runtimeBootstrapper,
            _appCancellationTokenSource,
            window.StopForApplicationShutdown,
            DisposeHttpResources,
            healthHistoryService: healthHistoryService,
            healthHistoryStore: healthHistoryStore,
            historyService: historyService,
            historyStore: historyStore,
            learningActivityStore: learningActivityStore);
        window.Closed += OnWindowClosed;
        window.StartPresence(
            activationDisposition ==
                MatasuriActivationDisposition.EstablishAmbientPresence);
        _ = BootstrapOllamaAsync();
        _ = EnsureStartupTaskEnabledAsync();
        MatasuriActivationRouter.ProcessPendingRedirectedActivation(this);
#if DEBUG
        if (activationDisposition ==
                MatasuriActivationDisposition.DevelopmentShutdown)
        {
            _ = RequestControlledShutdownAsync();
        }
#endif
    }

    internal void HandleRedirectedActivation(
        MatasuriActivationDisposition disposition)
    {
        var window = _window;
        if (window is not null && !window.DispatcherQueue.HasThreadAccess)
        {
            window.DispatcherQueue.TryEnqueue(
                () => HandleRedirectedActivation(disposition));
            return;
        }

#if DEBUG
        if (disposition == MatasuriActivationDisposition.DevelopmentShutdown)
        {
            _ = RequestControlledShutdownAsync();
            return;
        }
#endif
        if (disposition == MatasuriActivationDisposition.SummonDashboard)
        {
            _window?.SummonDashboard();
        }
    }

    internal async Task RequestControlledShutdownAsync()
    {
        var window = _window;
        if (window is not null)
        {
            await MatasuriDevelopmentShutdownGate
                .WaitForRuntimeRestorationAsync(
                    window.RuntimeInitialization);
        }

        if (window is not null && !window.DispatcherQueue.HasThreadAccess)
        {
            window.DispatcherQueue.TryEnqueue(
                () => BeginControlledShutdown(window));
            return;
        }

        BeginControlledShutdown(window);
    }

    private void BeginControlledShutdown(MainWindow? window)
    {
        var shutdown = _shutdownCoordinator?.BeginShutdown();
        if (shutdown is null)
        {
            return;
        }

        _ = CloseWhenShutdownCompletesAsync(window, shutdown);
    }

    private async Task CloseWhenShutdownCompletesAsync(
        MainWindow? window,
        Task shutdown)
    {
        await shutdown.ConfigureAwait(false);
        if (window is not null)
        {
            window.DispatcherQueue.TryEnqueue(
                window.CloseForControlledShutdown);
        }
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

    private static async Task EnsureStartupTaskEnabledAsync()
    {
        try
        {
            await MatasuriStartupTaskEnabler.EnsureEnabledAsync();
        }
        catch
        {
            // Windows and user policy remain authoritative for startup.
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
        _electricityRateHttpClient?.Dispose();
        _electricityRateHttpClient = null;
        _ollamaRuntimeBootstrapper = null;
        _gpuTelemetryProvider?.Dispose();
        _gpuTelemetryProvider = null;
    }
}
