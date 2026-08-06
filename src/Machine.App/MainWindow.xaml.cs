using System.Diagnostics;
using System.Globalization;
using Machine.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Machine.App;

public sealed partial class MainWindow : Window
{
    private const int CompactWindowWidth = 400;
    private const int CompactWindowHeight = 200;
    private const int ExpandedWindowWidth = 520;
    private const int ExpandedWindowHeight = 760;
    private const int WorkAreaMargin = 16;
    private const int TopProcessCount = 5;
    private const string UnavailableValue = "Unavailable";
    private const double BytesPerMebibyte =
        1024d * 1024d;
    private const double BytesPerGibibyte =
        1024d * 1024d * 1024d;

    private static readonly TimeSpan TelemetryRefreshInterval =
        TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProcessRefreshInterval =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OllamaRefreshInterval =
        TimeSpan.FromSeconds(10);

    private readonly IMachineIdentityProvider _identityProvider;
    private readonly IMachineResourceProvider _resourceProvider;
    private readonly IMachineProcessProvider _processProvider;
    private readonly IOllamaStatusProvider _ollamaStatusProvider;
    private readonly IMachineStateExplainer _machineStateExplainer;
    private readonly CancellationTokenSource
        _windowCancellationTokenSource = new();
    private MachineIdentity? _latestIdentity;
    private MachineResourceSnapshot? _latestResourceSnapshot;
    private IReadOnlyList<MachineProcessSnapshot>
        _latestProcessSnapshots =
            Array.Empty<MachineProcessSnapshot>();
    private bool _contentLoadStarted;
    private bool _detailsExpanded;
    private bool _isOllamaServiceAvailable;
    private bool _isExplanationRequestRunning;

    public MainWindow(
        IMachineIdentityProvider identityProvider,
        IMachineResourceProvider resourceProvider,
        IMachineProcessProvider processProvider,
        IOllamaStatusProvider ollamaStatusProvider,
        IMachineStateExplainer machineStateExplainer)
    {
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(resourceProvider);
        ArgumentNullException.ThrowIfNull(processProvider);
        ArgumentNullException.ThrowIfNull(ollamaStatusProvider);
        ArgumentNullException.ThrowIfNull(machineStateExplainer);

        _identityProvider = identityProvider;
        _resourceProvider = resourceProvider;
        _processProvider = processProvider;
        _ollamaStatusProvider = ollamaStatusProvider;
        _machineStateExplainer = machineStateExplainer;

        InitializeComponent();
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;
    }

    private void OnWindowActivated(
        object sender,
        WindowActivatedEventArgs args)
    {
        Activated -= OnWindowActivated;

        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsMaximizable = false;
                presenter.IsResizable = false;
                presenter.IsMinimizable = true;
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }

        ResizeAndPositionWindow(
            CompactWindowWidth,
            CompactWindowHeight);
    }

    private async void OnMainContentLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_contentLoadStarted)
        {
            return;
        }

        _contentLoadStarted = true;

        try
        {
            await LoadIdentityAsync();

            var cancellationToken =
                _windowCancellationTokenSource.Token;

            await Task.WhenAll(
                RunTelemetryLoopAsync(cancellationToken),
                RunProcessLoopAsync(cancellationToken),
                RunOllamaStatusLoopAsync(cancellationToken));
        }
        finally
        {
            _windowCancellationTokenSource.Dispose();
        }
    }

    private async Task LoadIdentityAsync()
    {
        try
        {
            var identity = await _identityProvider.GetAsync();

            _latestIdentity = identity;

            DeviceNameText.Text = identity.DeviceName;
            OperatingSystemText.Text = identity.OperatingSystem;
            ArchitectureText.Text = identity.Architecture;
            LoadStatusText.Text = string.Empty;
            UpdateExplainMachineStateButtonState();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            DeviceNameText.Text = UnavailableValue;
            OperatingSystemText.Text = UnavailableValue;
            ArchitectureText.Text = UnavailableValue;
            LoadStatusText.Text = "Machine identity could not be loaded.";
        }
    }

    private async Task RunTelemetryLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await RefreshTelemetryAsync(cancellationToken);
                await Task.Delay(
                    TelemetryRefreshInterval,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshTelemetryAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _resourceProvider.GetAsync(
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            _latestResourceSnapshot = snapshot;

            CpuUsageText.Text =
                $"{snapshot.CpuUsagePercent:F1}%";

            var usedMemory =
                snapshot.UsedMemoryBytes / BytesPerGibibyte;
            var totalMemory =
                snapshot.TotalMemoryBytes / BytesPerGibibyte;

            MemoryUsageText.Text =
                $"{usedMemory:F1} GB / {totalMemory:F1} GB";
            TelemetryStatusText.Text = string.Empty;

            PresenceTelemetryText.Text =
                $"CPU {snapshot.CpuUsagePercent:F1}% · " +
                $"Memory {usedMemory:F1} / {totalMemory:F1} GB";

            var memoryUsagePercent =
                snapshot.TotalMemoryBytes == 0
                    ? 100d
                    : snapshot.UsedMemoryBytes /
                        (double)snapshot.TotalMemoryBytes *
                        100d;

            UpdatePresenceState(
                snapshot.CpuUsagePercent,
                memoryUsagePercent);
            UpdateExplainMachineStateButtonState();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            CpuUsageText.Text = UnavailableValue;
            MemoryUsageText.Text = UnavailableValue;
            TelemetryStatusText.Text =
                "Resource telemetry could not be loaded.";
            PresenceStateText.Text = "Status unavailable";
            PresenceTelemetryText.Text =
                "CPU unavailable · Memory unavailable";
            PresenceIndicator.Fill =
                new SolidColorBrush(Colors.Gray);
        }
    }

    private void UpdatePresenceState(
        double cpuUsagePercent,
        double memoryUsagePercent)
    {
        if (cpuUsagePercent >= 90d ||
            memoryUsagePercent >= 90d)
        {
            PresenceStateText.Text = "Under pressure";
            PresenceIndicator.Fill =
                new SolidColorBrush(Colors.Red);
        }
        else if (cpuUsagePercent >= 70d ||
                 memoryUsagePercent >= 80d)
        {
            PresenceStateText.Text = "Busy";
            PresenceIndicator.Fill =
                new SolidColorBrush(Colors.Orange);
        }
        else
        {
            PresenceStateText.Text = "Stable";
            PresenceIndicator.Fill =
                new SolidColorBrush(Colors.Green);
        }
    }

    private async Task RunProcessLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await RefreshProcessesAsync(cancellationToken);
                await Task.Delay(
                    ProcessRefreshInterval,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshProcessesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshots = await _processProvider.GetTopAsync(
                TopProcessCount,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var verifiedSnapshots = snapshots.ToArray();
            _latestProcessSnapshots = verifiedSnapshots;

            TopProcessesList.ItemsSource = verifiedSnapshots
                .Select(snapshot => new ProcessDisplayItem(
                    snapshot.Name,
                    $"PID {snapshot.ProcessId} · " +
                    $"{snapshot.CpuUsagePercent:F1}% CPU · " +
                    FormatBytes(snapshot.WorkingSetBytes)))
                .ToArray();
            ProcessStatusText.Text = string.Empty;
            UpdateExplainMachineStateButtonState();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            ProcessStatusText.Text =
                "Process information is temporarily unavailable.";
        }
    }

    private async Task RunOllamaStatusLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await RefreshOllamaStatusAsync(cancellationToken);
                await Task.Delay(
                    OllamaRefreshInterval,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshOllamaStatusAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _ollamaStatusProvider.GetStatusAsync(
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            UpdateOllamaStatus(snapshot);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            ShowOllamaOffline();
        }
    }

    private void UpdateOllamaStatus(
        OllamaStatusSnapshot snapshot)
    {
        if (!snapshot.IsServiceAvailable)
        {
            ShowOllamaOffline();
            return;
        }

        _isOllamaServiceAvailable = true;
        OllamaServiceStatusText.Text = "Online";
        OllamaVersionText.Text = string.IsNullOrWhiteSpace(
            snapshot.Version)
            ? UnavailableValue
            : snapshot.Version;

        if (!snapshot.IsRunningModelStatusAvailable)
        {
            OllamaPresenceStatusText.Text =
                "Ollama online · Model status unavailable";
            ClearOllamaModels(
                "Loaded-model status is unavailable.");
            UpdateExplainMachineStateButtonState();
            return;
        }

        var displayItems = snapshot.RunningModels
            .Select(CreateOllamaModelDisplayItem)
            .ToArray();

        OllamaRunningModelsList.ItemsSource = displayItems;

        if (displayItems.Length == 0)
        {
            OllamaPresenceStatusText.Text =
                "Ollama online · No model loaded";
            OllamaLoadedModelsStatusText.Text =
                "No models currently loaded.";
            UpdateExplainMachineStateButtonState();
            return;
        }

        OllamaPresenceStatusText.Text = displayItems.Length == 1
            ? $"Ollama online · {displayItems[0].Name} loaded"
            : $"Ollama online · {displayItems.Length} models loaded";
        OllamaLoadedModelsStatusText.Text = string.Empty;
        UpdateExplainMachineStateButtonState();
    }

    private void ShowOllamaOffline()
    {
        _isOllamaServiceAvailable = false;
        OllamaPresenceStatusText.Text = "Ollama offline";
        OllamaServiceStatusText.Text = "Offline";
        OllamaVersionText.Text = UnavailableValue;
        ClearOllamaModels(
            "Loaded-model status is unavailable.");
        UpdateExplainMachineStateButtonState();
    }

    private void ClearOllamaModels(string status)
    {
        OllamaRunningModelsList.ItemsSource =
            Array.Empty<OllamaModelDisplayItem>();
        OllamaLoadedModelsStatusText.Text = status;
    }

    private static OllamaModelDisplayItem
        CreateOllamaModelDisplayItem(
            OllamaRunningModel model)
    {
        var parameterSize = string.IsNullOrWhiteSpace(
            model.ParameterSize)
            ? UnavailableValue
            : model.ParameterSize;
        var quantizationLevel = string.IsNullOrWhiteSpace(
            model.QuantizationLevel)
            ? UnavailableValue
            : model.QuantizationLevel;

        return new OllamaModelDisplayItem(
            model.Name,
            $"{parameterSize} · {quantizationLevel}",
            $"{FormatBytes(model.SizeVramBytes)} VRAM · " +
            $"{model.ContextLength.ToString("N0", CultureInfo.InvariantCulture)} context");
    }

    private async void OnExplainMachineStateClicked(
        object sender,
        RoutedEventArgs e)
    {
        if (_isExplanationRequestRunning)
        {
            return;
        }

        var identity = _latestIdentity;
        var resources = _latestResourceSnapshot;
        var processSnapshots = _latestProcessSnapshots.ToArray();

        if (identity is null ||
            resources is null ||
            processSnapshots.Length == 0 ||
            !_isOllamaServiceAvailable)
        {
            UpdateExplainMachineStateButtonState();
            return;
        }

        _isExplanationRequestRunning = true;
        UpdateExplainMachineStateButtonState();
        MachineExplanationStatusText.Text = "Thinking...";

        var cancellationToken =
            _windowCancellationTokenSource.Token;

        try
        {
            var request = new MachineStateExplanationRequest(
                identity,
                resources,
                processSnapshots);
            var explanation =
                await _machineStateExplainer.ExplainAsync(
                    request,
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            MachineExplanationText.Text = explanation.Text;
            MachineExplanationStatusText.Text = string.Empty;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            MachineExplanationStatusText.Text =
                "Machine explanation is temporarily unavailable.";
        }
        finally
        {
            _isExplanationRequestRunning = false;
            if (!cancellationToken.IsCancellationRequested)
            {
                UpdateExplainMachineStateButtonState();
            }
        }
    }

    private void UpdateExplainMachineStateButtonState()
    {
        ExplainMachineStateButton.IsEnabled =
            _latestIdentity is not null &&
            _latestResourceSnapshot is not null &&
            _latestProcessSnapshots.Count > 0 &&
            _isOllamaServiceAvailable &&
            !_isExplanationRequestRunning &&
            !_windowCancellationTokenSource.IsCancellationRequested;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= BytesPerGibibyte)
        {
            return $"{bytes / BytesPerGibibyte:F1} GB";
        }

        if (bytes >= BytesPerMebibyte)
        {
            return $"{bytes / BytesPerMebibyte:F1} MB";
        }

        return $"{bytes} B";
    }

    private void OnDetailsToggleClicked(
        object sender,
        RoutedEventArgs e)
    {
        _detailsExpanded = !_detailsExpanded;

        DetailsPanel.Visibility = _detailsExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        DetailsToggleButton.Content = _detailsExpanded
            ? "Collapse"
            : "Show details";

        ResizeAndPositionWindow(
            _detailsExpanded
                ? ExpandedWindowWidth
                : CompactWindowWidth,
            _detailsExpanded
                ? ExpandedWindowHeight
                : CompactWindowHeight);
    }

    private void ResizeAndPositionWindow(
        int requestedWidth,
        int requestedHeight)
    {
        var rasterizationScale =
            MainContent.XamlRoot?.RasterizationScale ?? 1d;
        var requestedSize = new SizeInt32(
            Math.Max(
                1,
                (int)Math.Round(
                    requestedWidth * rasterizationScale)),
            Math.Max(
                1,
                (int)Math.Round(
                    requestedHeight * rasterizationScale)));

        DisplayArea? displayArea;

        try
        {
            displayArea = DisplayArea.GetFromWindowId(
                AppWindow.Id,
                DisplayAreaFallback.Nearest);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            TryResizeWindow(requestedSize);
            return;
        }

        if (displayArea is null)
        {
            TryResizeWindow(requestedSize);
            return;
        }

        try
        {
            var workArea = displayArea.WorkArea;
            if (workArea.Width <= 0 || workArea.Height <= 0)
            {
                TryResizeWindow(requestedSize);
                return;
            }

            var maximumWidth = Math.Max(
                1,
                workArea.Width - 2 * WorkAreaMargin);
            var maximumHeight = Math.Max(
                1,
                workArea.Height - 2 * WorkAreaMargin);
            var targetSize = new SizeInt32(
                Math.Min(requestedSize.Width, maximumWidth),
                Math.Min(requestedSize.Height, maximumHeight));

            AppWindow.Resize(targetSize);
            var windowSize = AppWindow.Size;

            var workAreaLeft =
                displayArea.OuterBounds.X + workArea.X;
            var workAreaTop =
                displayArea.OuterBounds.Y + workArea.Y;
            var positionX = Math.Max(
                workAreaLeft,
                workAreaLeft + workArea.Width -
                    windowSize.Width - WorkAreaMargin);
            var positionY = Math.Max(
                workAreaTop,
                workAreaTop + workArea.Height -
                    windowSize.Height - WorkAreaMargin);

            AppWindow.Move(new PointInt32(
                positionX,
                positionY));
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private void TryResizeWindow(SizeInt32 requestedSize)
    {
        try
        {
            AppWindow.Resize(requestedSize);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private void OnWindowClosed(
        object sender,
        WindowEventArgs args)
    {
        _windowCancellationTokenSource.Cancel();
    }
}

public sealed record ProcessDisplayItem(
    string Name,
    string Details);

public sealed record OllamaModelDisplayItem(
    string Name,
    string ModelDetails,
    string RuntimeDetails);
