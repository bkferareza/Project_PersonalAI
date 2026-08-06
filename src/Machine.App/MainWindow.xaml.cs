using System.Diagnostics;
using Machine.Core;
using Microsoft.UI.Xaml;

namespace Machine.App;

public sealed partial class MainWindow : Window
{
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
        Closed += OnWindowClosed;
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
