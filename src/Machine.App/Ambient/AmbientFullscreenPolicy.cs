namespace Machine.App;

public readonly record struct AmbientScreenBounds(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Math.Max(0, Right - Left);

    public int Height => Math.Max(0, Bottom - Top);

    public long Area => (long)Width * Height;

    public bool IsUsable => Width > 0 && Height > 0;
}

public readonly record struct AmbientForegroundWindowSnapshot(
    long WindowIdentity,
    long MonitorIdentity,
    bool IsVisible,
    bool IsCloaked,
    bool IsMatasuri,
    bool IsToolWindow,
    bool HasOwner,
    bool IsShellSurface,
    bool IsMaximized,
    bool HasCaption,
    bool HasThickFrame,
    AmbientScreenBounds WindowBounds,
    AmbientScreenBounds MonitorBounds,
    AmbientScreenBounds WorkAreaBounds);

public enum AmbientForegroundClassification
{
    Ignored,
    NotFullscreen,
    TransientOverlay,
    FullscreenOtherMonitor,
    FullscreenOrbMonitor
}

public static class AmbientFullscreenClassifier
{
    public const int BoundsTolerance = 4;
    private const double TransientAreaRatio = 0.25d;

    public static AmbientForegroundClassification Classify(
        AmbientForegroundWindowSnapshot snapshot,
        long orbMonitorIdentity)
    {
        if (snapshot.WindowIdentity == 0 ||
            snapshot.MonitorIdentity == 0 ||
            !snapshot.IsVisible ||
            snapshot.IsCloaked ||
            snapshot.IsMatasuri ||
            !snapshot.WindowBounds.IsUsable ||
            !snapshot.MonitorBounds.IsUsable)
        {
            return AmbientForegroundClassification.Ignored;
        }

        if (snapshot.IsShellSurface)
        {
            return AmbientForegroundClassification.TransientOverlay;
        }

        var fillsMonitor = MatchesBounds(
            snapshot.WindowBounds,
            snapshot.MonitorBounds);
        if (fillsMonitor &&
            !(snapshot.IsMaximized &&
                snapshot.HasCaption &&
                snapshot.HasThickFrame))
        {
            return snapshot.MonitorIdentity == orbMonitorIdentity
                ? AmbientForegroundClassification.FullscreenOrbMonitor
                : AmbientForegroundClassification.FullscreenOtherMonitor;
        }

        if (snapshot.WorkAreaBounds.IsUsable &&
            MatchesBounds(
                snapshot.WindowBounds,
                snapshot.WorkAreaBounds))
        {
            return AmbientForegroundClassification.NotFullscreen;
        }

        if (snapshot.IsToolWindow ||
            snapshot.HasOwner ||
            IsSmallTransientSurface(snapshot))
        {
            return AmbientForegroundClassification.TransientOverlay;
        }

        return AmbientForegroundClassification.NotFullscreen;
    }

    public static bool MatchesBounds(
        AmbientScreenBounds candidate,
        AmbientScreenBounds monitor) =>
        Math.Abs((long)candidate.Left - monitor.Left) <=
            BoundsTolerance &&
        Math.Abs((long)candidate.Top - monitor.Top) <=
            BoundsTolerance &&
        Math.Abs((long)candidate.Right - monitor.Right) <=
            BoundsTolerance &&
        Math.Abs((long)candidate.Bottom - monitor.Bottom) <=
            BoundsTolerance;

    private static bool IsSmallTransientSurface(
        AmbientForegroundWindowSnapshot snapshot) =>
        snapshot.MonitorBounds.Area > 0 &&
        snapshot.WindowBounds.Area /
            (double)snapshot.MonitorBounds.Area < TransientAreaRatio;
}

public readonly record struct AmbientFullscreenSuppressionState(
    bool IsSuppressed,
    DateTimeOffset? ReleaseAt);

public readonly record struct AmbientFullscreenSuppressionDecision(
    AmbientFullscreenSuppressionState State,
    TimeSpan? RecheckAfter);

public static class AmbientFullscreenSuppressionPolicy
{
    public static readonly TimeSpan ExitHysteresis =
        TimeSpan.FromMilliseconds(700);

    public static AmbientFullscreenSuppressionDecision Evaluate(
        AmbientFullscreenSuppressionState current,
        AmbientForegroundClassification classification,
        DateTimeOffset observedAt)
    {
        if (classification ==
            AmbientForegroundClassification.FullscreenOrbMonitor)
        {
            return new(
                new(IsSuppressed: true, ReleaseAt: null),
                RecheckAfter: null);
        }

        if (!current.IsSuppressed)
        {
            return new(
                new(IsSuppressed: false, ReleaseAt: null),
                RecheckAfter: null);
        }

        var releaseAt = current.ReleaseAt ??
            observedAt + ExitHysteresis;
        if (observedAt < releaseAt)
        {
            return new(
                new(IsSuppressed: true, releaseAt),
                releaseAt - observedAt);
        }

        return new(
            new(IsSuppressed: false, ReleaseAt: null),
            RecheckAfter: null);
    }
}

public static class AmbientFullscreenPresentationPolicy
{
    public static bool ShouldPresent(
        bool isPresentationRequested,
        bool isFullscreenSuppressed) =>
        isPresentationRequested && !isFullscreenSuppressed;

    public static bool ShouldRasterize(
        bool isPresentationRequested,
        bool isFullscreenSuppressed,
        bool animationsEnabled) =>
        ShouldPresent(isPresentationRequested, isFullscreenSuppressed) &&
        animationsEnabled;
}
