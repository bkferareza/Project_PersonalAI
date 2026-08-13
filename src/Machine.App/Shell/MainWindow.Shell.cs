using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Machine.Core;
using Machine.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace Machine.App;

public sealed partial class MainWindow
{
    private void OnWindowActivated(
        object sender,
        WindowActivatedEventArgs args)
    {
        if (_windowPresentationConfigured)
        {
            return;
        }

        _windowPresentationConfigured = true;

        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsMaximizable = false;
                presenter.IsResizable = false;
                presenter.IsMinimizable = true;
                presenter.SetBorderAndTitleBar(
                    DashboardChromeLayout.HasBorder,
                    DashboardChromeLayout.HasTitleBar);
            }

            _nonClientPointerSource =
                InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
            _powerBroadcastMonitor ??= new(
                WinRT.Interop.WindowNative.GetWindowHandle(this),
                OnPowerTransition);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }

        ApplyCompactPresentation(force: true);
        UpdateDashboardDragRegion();
    }

    private void OnPowerTransition(MachinePowerTransition transition)
    {
        var historyKind = transition.Kind switch
        {
            MachinePowerTransitionKind.Suspend =>
                MachineHistoryEventKind.SystemSuspend,
            MachinePowerTransitionKind.ResumeAutomatic =>
                MachineHistoryEventKind.SystemResumeAutomatic,
            MachinePowerTransitionKind.ResumeSuspend =>
                MachineHistoryEventKind.SystemResumeSuspend,
            _ => throw new ArgumentOutOfRangeException()
        };
        _historyService.RecordPowerTransition(
            historyKind,
            transition.OccurredAt);
    }

    private void ApplyDashboardCornerPreference()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var cornerPreference =
            DashboardChromeLayout.DwmRoundSmallCornerPreference;
        var result = DwmSetWindowAttribute(
            WinRT.Interop.WindowNative.GetWindowHandle(this),
            DashboardChromeLayout.DwmWindowCornerPreferenceAttribute,
            ref cornerPreference,
            Marshal.SizeOf<int>());
        if (result != 0)
        {
            Debug.WriteLine(
                $"DwmSetWindowAttribute failed with HRESULT 0x{result:X8}.");
        }
    }

    private void OnDashboardBackClicked(
        object sender,
        RoutedEventArgs args) => ReturnToAmbientPresence();

    private void OnDashboardCloseClicked(object sender, RoutedEventArgs args) =>
        DashboardChromeLayout.InvokeClose(Close);

    private void OnMainContentKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!_detailsExpanded ||
            !DashboardChromeLayout.IsReturnToAmbientKey(
                (uint)args.Key))
        {
            return;
        }

        args.Handled = ReturnToAmbientPresence();
    }

    private bool ReturnToAmbientPresence()
    {
        if (!_compactPresenceInteraction.CloseDashboard())
        {
            return false;
        }

        SetDashboardExpanded(false);
        return true;
    }

    private void SetDashboardExpanded(bool isExpanded)
    {
        _detailsExpanded = isExpanded;

        DetailsPanel.Visibility = _detailsExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardChrome.Visibility = _detailsExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyCompactPresentation();
        UpdateDashboardDragRegion();

        if (_detailsExpanded)
        {
            SelectNavigationButton(OverviewNavigationItem);
            ShowDashboardPage("overview");
            MainContent.DispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Low,
                () =>
                {
                    if (_detailsExpanded &&
                        !_windowCancellationTokenSource
                            .IsCancellationRequested)
                    {
                        OverviewNavigationItem.Focus(
                            FocusState.Programmatic);
                    }
                });
            TryRequestDashboardInsight();
        }
    }

    private void ApplyCompactPresentation(bool force = false)
    {
        var presentation =
            _compactPresenceInteraction.Presentation;

        if (!force &&
            _appliedCompactPresentation == presentation)
        {
            return;
        }

        _appliedCompactPresentation = presentation;
        var isDashboardExpanded = presentation ==
            CompactPresencePresentation.Dashboard;
        UpdateWindowChrome(isDashboardExpanded);

        if (isDashboardExpanded)
        {
            _ambientOrbWindow.Hide();
            AppWindow.Show();
            ResizeAndPositionWindow(
                ExpandedWindowWidth,
                ExpandedWindowHeight);
            MainContent.DispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Low,
                UpdateDashboardDragRegion);
            return;
        }

        ShowAmbientOrb();
        AppWindow.Hide();
    }

    private void ShowAmbientOrb()
    {
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(
                AppWindow.Id,
                DisplayAreaFallback.Nearest);
            var workArea = displayArea.WorkArea;
            var position = CompactPresenceLayout.CalculateBottomRightPosition(
                new CompactPresenceWorkArea(
                    displayArea.OuterBounds.X + workArea.X,
                    displayArea.OuterBounds.Y + workArea.Y,
                    workArea.Width,
                    workArea.Height),
                CompactPresenceLayout.AmbientOrbSize,
                WorkAreaMargin);
            _ambientOrbWindow.Show(position.X, position.Y);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private void OpenDashboardFromAmbientOrb()
    {
        if (_windowCancellationTokenSource.IsCancellationRequested ||
            !_compactPresenceInteraction.OpenDashboard())
        {
            return;
        }

        SetDashboardExpanded(true);
    }

    private void BeginNewInsightBloom()
    {
        if (_windowCancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        _showNewInsightBloom = true;
        ApplyPresenceVisualMode(force: true);
    }

    private void ApplyShellAtmosphere()
    {
        if (MainContent is null)
        {
            return;
        }
        var atmosphere = MatasuriShellAtmospherePolicy.Select(
            GetPresentationState(),
            IsGeneratingPresentation(),
            _uiSettings.AnimationsEnabled);
        if (atmosphere == _appliedShellAtmosphere)
        {
            return;
        }
        _appliedShellAtmosphere = atmosphere;
        var atmosphereBrush = (SolidColorBrush)Application.Current.Resources[
            "MatasuriAtmosphereBrush"];
        var accentBrush = (SolidColorBrush)Application.Current.Resources[
            "MatasuriStateAccentBrush"];
        var targetAtmosphere = ToColor(atmosphere.Atmosphere);
        var targetAccent = ToColor(atmosphere.Accent);
        var currentAtmosphere = atmosphereBrush.Color;
        var currentAccent = accentBrush.Color;
        _shellAtmosphereStoryboard?.Stop();
        _shellAtmosphereStoryboard = null;
        atmosphereBrush.Color = currentAtmosphere;
        accentBrush.Color = currentAccent;
        if (atmosphere.TransitionDuration == TimeSpan.Zero)
        {
            atmosphereBrush.Color = targetAtmosphere;
            accentBrush.Color = targetAccent;
        }
        else
        {
            var easing = new CubicEase
            {
                EasingMode = EasingMode.EaseInOut
            };
            var atmosphereAnimation = new ColorAnimation
            {
                To = targetAtmosphere,
                Duration = atmosphere.TransitionDuration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(atmosphereAnimation, atmosphereBrush);
            Storyboard.SetTargetProperty(atmosphereAnimation, "Color");
            var accentAnimation = new ColorAnimation
            {
                To = targetAccent,
                Duration = atmosphere.TransitionDuration,
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseInOut
                }
            };
            Storyboard.SetTarget(accentAnimation, accentBrush);
            Storyboard.SetTargetProperty(accentAnimation, "Color");
            _shellAtmosphereStoryboard = new Storyboard();
            _shellAtmosphereStoryboard.Children.Add(atmosphereAnimation);
            _shellAtmosphereStoryboard.Children.Add(accentAnimation);
            _shellAtmosphereStoryboard.Completed += (_, _) =>
            {
                atmosphereBrush.Color = targetAtmosphere;
                accentBrush.Color = targetAccent;
            };
            _shellAtmosphereStoryboard.Begin();
        }

        _generatingAtmosphereStoryboard?.Stop();
        _generatingAtmosphereStoryboard = null;
        if (!atmosphere.IsGenerating)
        {
            GeneratingAtmosphereLayer.Opacity = 0d;
        }
        else if (!atmosphere.AnimateGeneratingOverlay)
        {
            GeneratingAtmosphereLayer.Opacity = 0.055d;
        }
        else
        {
            var animation = new DoubleAnimation
            {
                From = 0.035d,
                To = 0.10d,
                Duration = TimeSpan.FromMilliseconds(900),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase
                {
                    EasingMode = EasingMode.EaseInOut
                }
            };
            Storyboard.SetTarget(
                animation,
                GeneratingAtmosphereLayer);
            Storyboard.SetTargetProperty(animation, "Opacity");
            _generatingAtmosphereStoryboard = new Storyboard();
            _generatingAtmosphereStoryboard.Children.Add(animation);
            _generatingAtmosphereStoryboard.Begin();
        }
    }

    private static global::Windows.UI.Color ToColor(
        MatasuriColor color) => global::Windows.UI.Color.FromArgb(
        color.Alpha,
        color.Red,
        color.Green,
        color.Blue);

    private void ApplyPresenceVisualMode(bool force = false)
    {
        if (_windowCancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        var mode = CompactPresenceLayout.SelectVisualMode(
            GetPresentationState(),
            IsGeneratingPresentation(),
            _showNewInsightBloom);

        if (!force && _activePresenceVisualMode == mode)
        {
            return;
        }

        _activePresenceVisualMode = mode;
        _ambientOrbWindow.SetAnimationsEnabled(_uiSettings.AnimationsEnabled);
        _ambientOrbWindow.SetVisualMode(mode);

        if (!_uiSettings.AnimationsEnabled)
        {
            if (mode == CompactPresenceVisualMode.NewInsight)
            {
                _showNewInsightBloom = false;
                _activePresenceVisualMode = null;
                ApplyPresenceVisualMode(force: true);
            }

            return;
        }

    }

    private void OnNewInsightBloomCompleted(object? sender, EventArgs args)
    {
        if (_windowCancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        _showNewInsightBloom = false;
        _activePresenceVisualMode = null;
        ApplyPresenceVisualMode(force: true);
    }

    private void OnSystemAnimationsEnabledChanged(
        UISettings sender,
        object args)
    {
        MainContent.DispatcherQueue.TryEnqueue(() =>
        {
            ApplyShellAtmosphere();
            ApplyPresenceVisualMode(force: true);
        });
    }

    private void UpdateWindowChrome(bool isDashboardExpanded)
    {
        try
        {
            var targetBackdrop = isDashboardExpanded
                ? _dashboardBackdrop
                : null;
            if (!ReferenceEquals(SystemBackdrop, targetBackdrop))
            {
                SystemBackdrop = targetBackdrop;
            }

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(
                    DashboardChromeLayout.HasBorder,
                    DashboardChromeLayout.HasTitleBar);
            }

            ApplyDashboardCornerPreference();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private void OnDashboardDragRegionSizeChanged(
        object sender,
        SizeChangedEventArgs args) => UpdateDashboardDragRegion();

    private void OnDashboardXamlRootChanged(
        XamlRoot sender,
        XamlRootChangedEventArgs args) => UpdateDashboardDragRegion();

    private void UpdateDashboardDragRegion()
    {
        if (_nonClientPointerSource is null)
        {
            return;
        }

        try
        {
            if (!_detailsExpanded ||
                DashboardDragRegion.ActualWidth <= 0d ||
                DashboardDragRegion.ActualHeight <= 0d)
            {
                _nonClientPointerSource.ClearRegionRects(
                    NonClientRegionKind.Caption);
                return;
            }

            var offset = DashboardDragRegion
                .TransformToVisual(MainContent)
                .TransformPoint(new global::Windows.Foundation.Point(0d, 0d));
            var region = DashboardChromeLayout.CalculateCaptionRegion(
                offset.X,
                offset.Y,
                DashboardDragRegion.ActualWidth,
                DashboardDragRegion.ActualHeight,
                MainContent.XamlRoot?.RasterizationScale ?? 1d);
            _nonClientPointerSource.SetRegionRects(
                NonClientRegionKind.Caption,
                [new RectInt32(
                    region.X,
                    region.Y,
                    region.Width,
                    region.Height)]);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private void OnDashboardNavigationClicked(
        object sender,
        RoutedEventArgs args)
    {
        if (OverviewPage is null || sender is not Button button)
        {
            return;
        }

        var tag = button.Tag?.ToString() ?? "overview";

        SelectNavigationButton(button);
        ShowDashboardPage(tag);
    }

    private void SelectNavigationButton(Button selected)
    {
        var buttons = new[]
        {
            OverviewNavigationItem,
            HistoryNavigationItem,
            LearningNavigationItem,
            HealthNavigationItem,
            NetworkNavigationItem,
            HardwareNavigationItem,
            StorageNavigationItem,
            SoftwareNavigationItem,
            StartupNavigationItem,
            ServicesNavigationItem,
            TasksNavigationItem,
            DevicesNavigationItem,
            RuntimeNavigationItem
        };
        var selectedBrush = (Brush)Application.Current.Resources[
            "MatasuriElevatedSurfaceBrush"];
        foreach (var button in buttons)
        {
            var isSelected = ReferenceEquals(button, selected);
            button.Background = isSelected
                ? selectedBrush
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.FontWeight = isSelected
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
            button.Opacity = isSelected ? 1d : 0.72d;
            AutomationProperties.SetName(
                button,
                $"{button.Content}{(isSelected ? ", selected" : string.Empty)}");
        }
    }

    private void ShowDashboardPage(string tag)
    {
        OverviewPage.Visibility = tag == "overview"
            ? Visibility.Visible
            : Visibility.Collapsed;
        HistoryPage.Visibility = tag == "history"
            ? Visibility.Visible
            : Visibility.Collapsed;
        LearningPage.Visibility = tag == "learning"
            ? Visibility.Visible
            : Visibility.Collapsed;
        NetworkPage.Visibility = tag == "network"
            ? Visibility.Visible
            : Visibility.Collapsed;
        HealthPage.Visibility = tag == "health"
            ? Visibility.Visible
            : Visibility.Collapsed;
        HardwarePage.Visibility = tag == "hardware"
            ? Visibility.Visible
            : Visibility.Collapsed;
        StoragePage.Visibility = tag == "storage"
            ? Visibility.Visible
            : Visibility.Collapsed;
        SoftwarePage.Visibility = tag == "software"
            ? Visibility.Visible
            : Visibility.Collapsed;
        StartupPage.Visibility = tag == "startup"
            ? Visibility.Visible
            : Visibility.Collapsed;
        ServicesPage.Visibility = tag == "services"
            ? Visibility.Visible
            : Visibility.Collapsed;
        TasksPage.Visibility = tag == "tasks"
            ? Visibility.Visible
            : Visibility.Collapsed;
        DevicesPage.Visibility = tag == "devices"
            ? Visibility.Visible
            : Visibility.Collapsed;
        RuntimePage.Visibility = tag == "runtime"
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (tag == "history")
        {
            HistoryPage.UpdateDashboard();
        }
        else if (tag == "learning")
        {
            UpdateLearningDashboard();
        }
        else if (tag == "health")
        {
            UpdateHealthDashboard();
            _ = RefreshHealthAsync(
                isManualRefresh: false,
                _windowCancellationTokenSource.Token);
        }
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

            var workAreaLeft =
                displayArea.OuterBounds.X + workArea.X;
            var workAreaTop =
                displayArea.OuterBounds.Y + workArea.Y;
            var targetCompactSize = new CompactPresenceSize(
                targetSize.Width,
                targetSize.Height);
            var position =
                CompactPresenceLayout.CalculateBottomRightPosition(
                    new CompactPresenceWorkArea(
                        workAreaLeft,
                        workAreaTop,
                        workArea.Width,
                        workArea.Height),
                    targetCompactSize,
                    WorkAreaMargin);

            AppWindow.MoveAndResize(new RectInt32(
                position.X,
                position.Y,
                targetSize.Width,
                targetSize.Height));
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
            var currentPosition = AppWindow.Position;
            var currentSize = AppWindow.Size;
            var right = currentPosition.X + currentSize.Width;
            var bottom = currentPosition.Y + currentSize.Height;

            AppWindow.MoveAndResize(new RectInt32(
                right - requestedSize.Width,
                bottom - requestedSize.Height,
                requestedSize.Width,
                requestedSize.Height));
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    internal void StopForApplicationShutdown()
    {
        _shellAtmosphereStoryboard?.Stop();
        _shellAtmosphereStoryboard = null;
        _generatingAtmosphereStoryboard?.Stop();
        _generatingAtmosphereStoryboard = null;
        _powerBroadcastMonitor?.Dispose();
        _powerBroadcastMonitor = null;
        _ambientOrbWindow.NewInsightCompleted -= OnNewInsightBloomCompleted;
        _ambientOrbWindow.Dispose();
        if (_isXamlRootChangeSubscribed && MainContent.XamlRoot is not null)
        {
            MainContent.XamlRoot.Changed -= OnDashboardXamlRootChanged;
            _isXamlRootChangeSubscribed = false;
        }
        if (_isAnimationSettingsChangeSubscribed &&
            OperatingSystem.IsWindowsVersionAtLeast(
                10,
                0,
                19041))
        {
            _uiSettings.AnimationsEnabledChanged -=
                OnSystemAnimationsEnabledChanged;
        }
        StoragePage.Stop();
        _windowCancellationTokenSource.Cancel();
    }
}
