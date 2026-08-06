using Machine.Core;

namespace Machine.App;

public enum CompactPresencePresentation
{
    Idle,
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

public static class CompactPresenceLayout
{
    public static readonly CompactPresenceSize IdleSize =
        new(208, 64);

    public static readonly CompactPresenceSize ContextSize =
        new(328, 112);

    public static readonly TimeSpan CollapseDelay =
        TimeSpan.FromMilliseconds(325);

    public static string GetIdlePhrase(
        MachineOverallState overallState) => overallState switch
        {
            MachineOverallState.Stable => "All quiet",
            MachineOverallState.Attention => "Keeping watch",
            MachineOverallState.Warning => "Under pressure",
            MachineOverallState.Critical => "Critical condition",
            _ => "Status unclear"
        };

    public static CompactPresenceVisualMode SelectVisualMode(
        MachineOverallState overallState,
        bool isGenerating,
        bool showNewInsightBloom)
    {
        if (showNewInsightBloom)
        {
            return CompactPresenceVisualMode.NewInsight;
        }

        if (isGenerating)
        {
            return CompactPresenceVisualMode.Generating;
        }

        return overallState switch
        {
            MachineOverallState.Stable =>
                CompactPresenceVisualMode.Stable,
            MachineOverallState.Attention =>
                CompactPresenceVisualMode.Attention,
            MachineOverallState.Warning =>
                CompactPresenceVisualMode.Warning,
            MachineOverallState.Critical =>
                CompactPresenceVisualMode.Critical,
            _ => CompactPresenceVisualMode.Unknown
        };
    }

    public static bool IsSurfaceInteractive(
        CompactPresencePresentation presentation) =>
        presentation != CompactPresencePresentation.Dashboard;

    public static CompactPresencePosition CalculateBottomRightPosition(
        CompactPresenceWorkArea workArea,
        CompactPresenceSize windowSize,
        int inset)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workArea));
        }

        if (windowSize.Width <= 0 || windowSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowSize));
        }

        if (inset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inset));
        }

        return new CompactPresencePosition(
            X: Math.Max(
                workArea.X,
                workArea.X + workArea.Width -
                    windowSize.Width - inset),
            Y: Math.Max(
                workArea.Y,
                workArea.Y + workArea.Height -
                    windowSize.Height - inset));
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
                : CompactPresencePresentation.Idle;

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
        if (requestVersion != _collapseRequestVersion ||
            _isPointerOver ||
            _hasKeyboardFocus ||
            _isDashboardExpanded)
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
        _isContextVisible = _isPointerOver ||
            _hasKeyboardFocus;
        _collapseRequestVersion++;
        return true;
    }
}
