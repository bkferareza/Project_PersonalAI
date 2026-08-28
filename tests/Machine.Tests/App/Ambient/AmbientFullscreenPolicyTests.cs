using Machine.App;

namespace Machine.Tests;

public sealed class AmbientFullscreenPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MonitorSizedForegroundOnOrbMonitorIsFullscreen()
    {
        var result = AmbientFullscreenClassifier.Classify(
            Snapshot(),
            orbMonitorIdentity: 1);

        Assert.Equal(
            AmbientForegroundClassification.FullscreenOrbMonitor,
            result);
    }

    [Fact]
    public void FewPixelFrameDifferenceRemainsFullscreen()
    {
        var result = AmbientFullscreenClassifier.Classify(
            Snapshot(windowBounds: new(-3, 2, 1924, 1077)),
            orbMonitorIdentity: 1);

        Assert.Equal(
            AmbientForegroundClassification.FullscreenOrbMonitor,
            result);
    }

    [Fact]
    public void WorkAreaMaximizedWindowIsNotFullscreen()
    {
        var result = AmbientFullscreenClassifier.Classify(
            Snapshot(
                windowBounds: new(0, 0, 1920, 1040),
                isMaximized: true,
                hasCaption: true,
                hasThickFrame: true),
            orbMonitorIdentity: 1);

        Assert.Equal(AmbientForegroundClassification.NotFullscreen,
            result);
    }

    [Fact]
    public void CaptionedMaximizedWindowIsNotFullscreenWithAutoHideWorkArea()
    {
        var monitor = new AmbientScreenBounds(0, 0, 1920, 1080);
        var result = AmbientFullscreenClassifier.Classify(
            Snapshot(
                monitorBounds: monitor,
                workAreaBounds: monitor,
                isMaximized: true,
                hasCaption: true,
                hasThickFrame: true),
            orbMonitorIdentity: 1);

        Assert.Equal(AmbientForegroundClassification.NotFullscreen,
            result);
    }

    [Fact]
    public void InvisibleCloakedAndMatasuriWindowsAreIgnored()
    {
        Assert.Equal(AmbientForegroundClassification.Ignored,
            AmbientFullscreenClassifier.Classify(
                Snapshot(isVisible: false), 1));
        Assert.Equal(AmbientForegroundClassification.Ignored,
            AmbientFullscreenClassifier.Classify(
                Snapshot(isCloaked: true), 1));
        Assert.Equal(AmbientForegroundClassification.Ignored,
            AmbientFullscreenClassifier.Classify(
                Snapshot(isMatasuri: true), 1));
    }

    [Fact]
    public void FullscreenOnAnotherMonitorDoesNotSuppressOrbMonitor()
    {
        var result = AmbientFullscreenClassifier.Classify(
            Snapshot(monitorIdentity: 2),
            orbMonitorIdentity: 1);

        Assert.Equal(
            AmbientForegroundClassification.FullscreenOtherMonitor,
            result);
    }

    [Fact]
    public void TinyToolAndOwnedSurfacesAreTransientOverlays()
    {
        var bounds = new AmbientScreenBounds(700, 400, 1_100, 700);

        Assert.Equal(
            AmbientForegroundClassification.TransientOverlay,
            AmbientFullscreenClassifier.Classify(
                Snapshot(windowBounds: bounds, isToolWindow: true),
                1));
        Assert.Equal(
            AmbientForegroundClassification.TransientOverlay,
            AmbientFullscreenClassifier.Classify(
                Snapshot(windowBounds: bounds, hasOwner: true),
                1));
    }

    [Fact]
    public void MonitorFillingOwnedSurfaceStillCountsAsFullscreen()
    {
        var result = AmbientFullscreenClassifier.Classify(
            Snapshot(hasOwner: true, isToolWindow: true),
            orbMonitorIdentity: 1);

        Assert.Equal(
            AmbientForegroundClassification.FullscreenOrbMonitor,
            result);
    }

    [Fact]
    public void ShellSurfaceIsNeverTreatedAsFullscreenContent()
    {
        var result = AmbientFullscreenClassifier.Classify(
            Snapshot(isShellSurface: true),
            orbMonitorIdentity: 1);

        Assert.Equal(
            AmbientForegroundClassification.TransientOverlay,
            result);
    }

    [Fact]
    public void TransientForegroundUsesReleaseHysteresisWithoutFlicker()
    {
        var entered = AmbientFullscreenSuppressionPolicy.Evaluate(
            default,
            AmbientForegroundClassification.FullscreenOrbMonitor,
            Now);
        var overlay = AmbientFullscreenSuppressionPolicy.Evaluate(
            entered.State,
            AmbientForegroundClassification.TransientOverlay,
            Now.AddMilliseconds(100));

        Assert.True(overlay.State.IsSuppressed);
        Assert.Equal(
            AmbientFullscreenSuppressionPolicy.ExitHysteresis,
            overlay.RecheckAfter);

        var fullscreenReturned =
            AmbientFullscreenSuppressionPolicy.Evaluate(
                overlay.State,
                AmbientForegroundClassification.FullscreenOrbMonitor,
                Now.AddMilliseconds(500));
        Assert.True(fullscreenReturned.State.IsSuppressed);
        Assert.Null(fullscreenReturned.State.ReleaseAt);
        Assert.Null(fullscreenReturned.RecheckAfter);
    }

    [Fact]
    public void GenuineFullscreenExitRestoresAfterBoundedDelay()
    {
        var entered = AmbientFullscreenSuppressionPolicy.Evaluate(
            default,
            AmbientForegroundClassification.FullscreenOrbMonitor,
            Now);
        var exitPending = AmbientFullscreenSuppressionPolicy.Evaluate(
            entered.State,
            AmbientForegroundClassification.NotFullscreen,
            Now.AddSeconds(1));
        var beforeDeadline =
            AmbientFullscreenSuppressionPolicy.Evaluate(
                exitPending.State,
                AmbientForegroundClassification.NotFullscreen,
                Now.AddMilliseconds(1_699));
        var released = AmbientFullscreenSuppressionPolicy.Evaluate(
            beforeDeadline.State,
            AmbientForegroundClassification.NotFullscreen,
            Now.AddMilliseconds(1_700));

        Assert.True(exitPending.State.IsSuppressed);
        Assert.True(beforeDeadline.State.IsSuppressed);
        Assert.False(released.State.IsSuppressed);
        Assert.Null(released.RecheckAfter);
    }

    [Fact]
    public void SuppressedOrReducedMotionPresenceDoesNotRasterize()
    {
        Assert.True(AmbientFullscreenPresentationPolicy.ShouldRasterize(
            isPresentationRequested: true,
            isFullscreenSuppressed: false,
            animationsEnabled: true));
        Assert.False(AmbientFullscreenPresentationPolicy.ShouldRasterize(
            isPresentationRequested: true,
            isFullscreenSuppressed: true,
            animationsEnabled: true));
        Assert.False(AmbientFullscreenPresentationPolicy.ShouldRasterize(
            isPresentationRequested: true,
            isFullscreenSuppressed: false,
            animationsEnabled: false));
        Assert.False(AmbientFullscreenPresentationPolicy.ShouldPresent(
            isPresentationRequested: false,
            isFullscreenSuppressed: false));
    }

    private static AmbientForegroundWindowSnapshot Snapshot(
        long monitorIdentity = 1,
        bool isVisible = true,
        bool isCloaked = false,
        bool isMatasuri = false,
        bool isToolWindow = false,
        bool hasOwner = false,
        bool isShellSurface = false,
        bool isMaximized = false,
        bool hasCaption = false,
        bool hasThickFrame = false,
        AmbientScreenBounds? windowBounds = null,
        AmbientScreenBounds? monitorBounds = null,
        AmbientScreenBounds? workAreaBounds = null) => new(
            WindowIdentity: 100,
            MonitorIdentity: monitorIdentity,
            IsVisible: isVisible,
            IsCloaked: isCloaked,
            IsMatasuri: isMatasuri,
            IsToolWindow: isToolWindow,
            HasOwner: hasOwner,
            IsShellSurface: isShellSurface,
            IsMaximized: isMaximized,
            HasCaption: hasCaption,
            HasThickFrame: hasThickFrame,
            WindowBounds: windowBounds ?? new(0, 0, 1920, 1080),
            MonitorBounds: monitorBounds ?? new(0, 0, 1920, 1080),
            WorkAreaBounds: workAreaBounds ?? new(0, 0, 1920, 1040));
}
