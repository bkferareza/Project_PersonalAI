using System.Diagnostics;
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
    private const int CompactWindowHeight = 170;
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

    private readonly IMachineIdentityProvider _identityProvider;
    private readonly IMachineResourceProvider _resourceProvider;
    private readonly IMachineProcessProvider _processProvider;
    private readonly CancellationTokenSource
        _windowCancellationTokenSource = new();
    private bool _contentLoadStarted;
    private bool _detailsExpanded;

    public MainWindow(
        IMachineIdentityProvider identityProvider,
        IMachineResourceProvider resourceProvider,
        IMachineProcessProvider processProvider)
    {
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(resourceProvider);
        ArgumentNullException.ThrowIfNull(processProvider);

        _identityProvider = identityProvider;
        _resourceProvider = resourceProvider;
        _processProvider = processProvider;

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
                RunProcessLoopAsync(cancellationToken));
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

            DeviceNameText.Text = identity.DeviceName;
            OperatingSystemText.Text = identity.OperatingSystem;
            ArchitectureText.Text = identity.Architecture;
            LoadStatusText.Text = string.Empty;
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

            TopProcessesList.ItemsSource = snapshots
                .Select(snapshot => new ProcessDisplayItem(
                    snapshot.Name,
                    $"PID {snapshot.ProcessId} · " +
                    $"{snapshot.CpuUsagePercent:F1}% CPU · " +
                    FormatBytes(snapshot.WorkingSetBytes)))
                .ToArray();
            ProcessStatusText.Text = string.Empty;
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
