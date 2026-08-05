using System.Diagnostics;
using Machine.Core;
using Microsoft.UI.Xaml;

namespace Machine.App;

public sealed partial class MainWindow : Window
{
    private const string UnavailableValue = "Unavailable";
    private const double BytesPerGibibyte =
        1024d * 1024d * 1024d;

    private static readonly TimeSpan TelemetryRefreshInterval =
        TimeSpan.FromSeconds(2);

    private readonly IMachineIdentityProvider _identityProvider;
    private readonly IMachineResourceProvider _resourceProvider;
    private readonly CancellationTokenSource
        _telemetryCancellationTokenSource = new();
    private bool _contentLoadStarted;

    public MainWindow(
        IMachineIdentityProvider identityProvider,
        IMachineResourceProvider resourceProvider)
    {
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(resourceProvider);

        _identityProvider = identityProvider;
        _resourceProvider = resourceProvider;

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
        await LoadIdentityAsync();
        await RunTelemetryLoopAsync(
            _telemetryCancellationTokenSource.Token);
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
        finally
        {
            _telemetryCancellationTokenSource.Dispose();
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

    private void OnWindowClosed(
        object sender,
        WindowEventArgs args)
    {
        _telemetryCancellationTokenSource.Cancel();
    }
}
