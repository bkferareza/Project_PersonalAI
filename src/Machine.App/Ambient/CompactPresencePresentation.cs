namespace Machine.App;

public enum CompactPresencePresentation
{
    Idle,
    Ambient = Idle,
    Context,
    Dashboard
}

public enum CompactPresenceVisualMode
{
    Stable,
    Attention,
    Warning,
    Critical,
    Unknown,
    Generating,
    NewInsight
}

public readonly record struct CompactPresenceVisualState(
    CompactPresenceVisualMode PostureMode,
    bool IsGenerating,
    bool HasNewUnseenInsight)
{
    public CompactPresenceVisualMode Mode => IsGenerating
        ? CompactPresenceVisualMode.Generating
        : PostureMode;
}

public readonly record struct AmbientOrbBlendState(
    double AttentionAmount,
    double WarningAmount,
    double CriticalAmount,
    double UnknownAmount,
    double GeneratingAmount,
    double HoverAmount);

public static class AmbientOrbTransitionModel
{
    private const double AppearanceResponsePerSecond = 7.5d;
    private const double ModifierResponsePerSecond = 9d;

    public static AmbientOrbBlendState CreateTarget(
        CompactPresenceVisualState state,
        bool isHovered) => new(
            state.PostureMode == CompactPresenceVisualMode.Attention
                ? 1d
                : 0d,
            state.PostureMode == CompactPresenceVisualMode.Warning
                ? 1d
                : 0d,
            state.PostureMode == CompactPresenceVisualMode.Critical
                ? 1d
                : 0d,
            state.PostureMode == CompactPresenceVisualMode.Unknown
                ? 1d
                : 0d,
            state.IsGenerating ? 1d : 0d,
            isHovered ? 1d : 0d);

    public static AmbientOrbBlendState Advance(
        AmbientOrbBlendState current,
        AmbientOrbBlendState target,
        TimeSpan elapsed,
        bool animationsEnabled = true)
    {
        if (!animationsEnabled)
        {
            return target;
        }

        var seconds = Math.Max(0d, elapsed.TotalSeconds);
        var appearanceAmount = 1d - Math.Exp(
            -AppearanceResponsePerSecond * seconds);
        var modifierAmount = 1d - Math.Exp(
            -ModifierResponsePerSecond * seconds);
        return new(
            Lerp(current.AttentionAmount, target.AttentionAmount,
                appearanceAmount),
            Lerp(current.WarningAmount, target.WarningAmount,
                appearanceAmount),
            Lerp(current.CriticalAmount, target.CriticalAmount,
                appearanceAmount),
            Lerp(current.UnknownAmount, target.UnknownAmount,
                appearanceAmount),
            Lerp(current.GeneratingAmount, target.GeneratingAmount,
                modifierAmount),
            Lerp(current.HoverAmount, target.HoverAmount,
                modifierAmount));
    }

    private static double Lerp(double current, double target, double amount) =>
        Math.Clamp(
            current + (target - current) * Math.Clamp(amount, 0d, 1d),
            0d,
            1d);
}

public readonly record struct CompactPresenceSize(
    int Width,
    int Height);

public readonly record struct CompactPresenceWorkArea(
    int X,
    int Y,
    int Width,
    int Height);

public readonly record struct CompactPresencePosition(
    int X,
    int Y);

public readonly record struct DashboardCaptionRegion(
    int X,
    int Y,
    int Width,
    int Height)
{
    public int Right => X + Width;
}

public static class DashboardChromeLayout
{
    public const bool HasBorder = false;
    public const bool HasTitleBar = false;
    public const int DwmWindowCornerPreferenceAttribute = 33;
    public const int DwmRoundSmallCornerPreference = 3;
    public const uint EscapeVirtualKey = 27;

    public static bool IsReturnToAmbientKey(uint virtualKey) =>
        virtualKey == EscapeVirtualKey;

    public static void InvokeClose(Action close)
    {
        ArgumentNullException.ThrowIfNull(close);
        close();
    }

    public static DashboardCaptionRegion CalculateCaptionRegion(
        double x,
        double y,
        double width,
        double height,
        double rasterizationScale)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(width) || !double.IsFinite(height) ||
            !double.IsFinite(rasterizationScale) ||
            width < 0d || height < 0d || rasterizationScale <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        return new DashboardCaptionRegion(
            (int)Math.Round(x * rasterizationScale),
            (int)Math.Round(y * rasterizationScale),
            Math.Max(0, (int)Math.Round(width * rasterizationScale)),
            Math.Max(0, (int)Math.Round(height * rasterizationScale)));
    }
}

public static class CompactPresenceLayout
{
    public static readonly CompactPresenceSize AmbientOrbSize =
        new(AmbientOrbFrameSequence.CanvasSize, AmbientOrbFrameSequence.CanvasSize);

    public static readonly CompactPresenceSize ContextSize =
        new(280, 100);

    public static readonly TimeSpan CollapseDelay =
        TimeSpan.FromMilliseconds(300);

    public static CompactPresenceVisualMode SelectVisualMode(
        Machine.Core.MachineOverallState overallState,
        bool isGenerating,
        bool showNewInsightBloom) => SelectVisualState(
            overallState,
            isGenerating,
            showNewInsightBloom).Mode;

    public static CompactPresenceVisualState SelectVisualState(
        Machine.Core.MachineOverallState overallState,
        bool isGenerating,
        bool hasNewUnseenInsight) => new(
            overallState switch
            {
                Machine.Core.MachineOverallState.Stable =>
                    CompactPresenceVisualMode.Stable,
                Machine.Core.MachineOverallState.Attention =>
                    CompactPresenceVisualMode.Attention,
                Machine.Core.MachineOverallState.Warning =>
                    CompactPresenceVisualMode.Warning,
                Machine.Core.MachineOverallState.Critical =>
                    CompactPresenceVisualMode.Critical,
                _ => CompactPresenceVisualMode.Unknown
            },
            isGenerating,
            hasNewUnseenInsight);

    public static bool IsSurfaceInteractive(
        CompactPresencePresentation presentation) =>
        presentation != CompactPresencePresentation.Dashboard;

    public static bool IsDashboardActivationKey(uint virtualKey) =>
        virtualKey is 13 or 32;

    public static CompactPresencePosition CalculateBottomRightPosition(
        CompactPresenceWorkArea workArea,
        CompactPresenceSize windowSize,
        int inset)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea));
        }

        if (windowSize.Width <= 0 || windowSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSize));
        }

        if (inset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inset));
        }

        return new CompactPresencePosition(
            X: Math.Max(
                workArea.X,
                workArea.X + workArea.Width - windowSize.Width - inset),
            Y: Math.Max(
                workArea.Y,
                workArea.Y + workArea.Height - windowSize.Height - inset));
    }
}

public sealed class CompactPresenceInteraction
{
    private int _collapseRequestVersion;
    private bool _isPointerOver;
    private bool _hasKeyboardFocus;
    private bool _isContextVisible;
    private bool _isDashboardExpanded;

    public CompactPresencePresentation Presentation =>
        _isDashboardExpanded
            ? CompactPresencePresentation.Dashboard
            : _isContextVisible
                ? CompactPresencePresentation.Context
                : CompactPresencePresentation.Ambient;

    public void PointerEntered()
    {
        _isPointerOver = true;
        _isContextVisible = true;
        _collapseRequestVersion++;
    }

    public int PointerExited()
    {
        _isPointerOver = false;
        return ++_collapseRequestVersion;
    }

    public int SetKeyboardFocus(bool hasKeyboardFocus)
    {
        _hasKeyboardFocus = hasKeyboardFocus;
        _collapseRequestVersion++;
        if (hasKeyboardFocus)
        {
            _isContextVisible = true;
        }

        return _collapseRequestVersion;
    }

    public bool TryCompleteCollapse(int requestVersion)
    {
        if (requestVersion != _collapseRequestVersion || _isPointerOver ||
            _hasKeyboardFocus || _isDashboardExpanded)
        {
            return false;
        }

        _isContextVisible = false;
        return true;
    }

    public bool OpenDashboard()
    {
        if (_isDashboardExpanded)
        {
            return false;
        }

        _isDashboardExpanded = true;
        _isContextVisible = true;
        _collapseRequestVersion++;
        return true;
    }

    public bool CloseDashboard()
    {
        if (!_isDashboardExpanded)
        {
            return false;
        }

        _isDashboardExpanded = false;
        _isContextVisible = _isPointerOver || _hasKeyboardFocus;
        _collapseRequestVersion++;
        return true;
    }
}

public enum AmbientOrbTimerTransition
{
    None,
    Start,
    Stop
}

public sealed class AmbientOrbLifecycle : IDisposable
{
    public bool IsVisible { get; private set; }

    public bool IsDisposed { get; private set; }

    public bool AnimationsEnabled { get; private set; } = true;

    public bool IsTimerRunning { get; private set; }

    public bool ShouldAnimate =>
        IsVisible && AnimationsEnabled && !IsDisposed;

    public void Show() => _ = ShowWithTimerTransition();

    public AmbientOrbTimerTransition ShowWithTimerTransition()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        IsVisible = true;
        return UpdateTimerState();
    }

    public AmbientOrbTimerTransition Hide()
    {
        if (!IsDisposed)
        {
            IsVisible = false;
        }

        return UpdateTimerState();
    }

    public AmbientOrbTimerTransition SetAnimationsEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        AnimationsEnabled = enabled;
        return UpdateTimerState();
    }

    public void MarkTimerStartFailed()
    {
        IsTimerRunning = false;
    }

    public void Dispose()
    {
        IsVisible = false;
        IsTimerRunning = false;
        IsDisposed = true;
    }

    private AmbientOrbTimerTransition UpdateTimerState()
    {
        var shouldRun = ShouldAnimate;
        if (shouldRun == IsTimerRunning)
        {
            return AmbientOrbTimerTransition.None;
        }

        IsTimerRunning = shouldRun;
        return shouldRun
            ? AmbientOrbTimerTransition.Start
            : AmbientOrbTimerTransition.Stop;
    }
}
