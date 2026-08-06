using Machine.App;
using Machine.Core;

namespace Machine.Tests;

public sealed class CompactPresencePresentationTests
{
    [Fact]
    public void IdleAndContextSizesMatchLivingOrbContract()
    {
        Assert.InRange(CompactPresenceLayout.IdleSize.Width, 88, 104);
        Assert.InRange(CompactPresenceLayout.IdleSize.Height, 88, 104);
        Assert.InRange(CompactPresenceLayout.ContextSize.Width, 260, 300);
        Assert.InRange(CompactPresenceLayout.ContextSize.Height, 92, 108);
    }

    [Theory]
    [InlineData(MachineOverallState.Stable, CompactPresenceVisualMode.Stable)]
    [InlineData(MachineOverallState.Attention, CompactPresenceVisualMode.Attention)]
    [InlineData(MachineOverallState.Warning, CompactPresenceVisualMode.Warning)]
    [InlineData(MachineOverallState.Critical, CompactPresenceVisualMode.Critical)]
    [InlineData(MachineOverallState.Unknown, CompactPresenceVisualMode.Unknown)]
    public void VisualModeMatchesDeterministicState(
        MachineOverallState state,
        CompactPresenceVisualMode expectedMode)
    {
        Assert.Equal(
            expectedMode,
            CompactPresenceLayout.SelectVisualMode(
                state,
                isGenerating: false,
                showNewInsightBloom: false));
    }

    [Fact]
    public void GeneratingAndNewInsightOverrideStateMotion()
    {
        Assert.Equal(
            CompactPresenceVisualMode.Generating,
            CompactPresenceLayout.SelectVisualMode(
                MachineOverallState.Warning,
                isGenerating: true,
                showNewInsightBloom: false));
        Assert.Equal(
            CompactPresenceVisualMode.NewInsight,
            CompactPresenceLayout.SelectVisualMode(
                MachineOverallState.Warning,
                isGenerating: true,
                showNewInsightBloom: true));
    }

    [Fact]
    public void PointerHoverRevealsContextAndDelayedExitReturnsToIdle()
    {
        var interaction = new CompactPresenceInteraction();

        Assert.Equal(CompactPresencePresentation.Idle, interaction.Presentation);

        interaction.PointerEntered();
        Assert.Equal(CompactPresencePresentation.Context, interaction.Presentation);

        var request = interaction.PointerExited();
        Assert.Equal(CompactPresencePresentation.Context, interaction.Presentation);
        Assert.True(interaction.TryCompleteCollapse(request));
        Assert.Equal(CompactPresencePresentation.Idle, interaction.Presentation);
    }

    [Fact]
    public void PointerReentryCancelsPendingCollapse()
    {
        var interaction = new CompactPresenceInteraction();
        interaction.PointerEntered();
        var staleRequest = interaction.PointerExited();

        interaction.PointerEntered();

        Assert.False(interaction.TryCompleteCollapse(staleRequest));
        Assert.Equal(CompactPresencePresentation.Context, interaction.Presentation);
    }

    [Fact]
    public void KeyboardFocusRevealsContextAndPreventsCollapse()
    {
        var interaction = new CompactPresenceInteraction();
        interaction.SetKeyboardFocus(true);
        var pointerExitRequest = interaction.PointerExited();

        Assert.False(interaction.TryCompleteCollapse(pointerExitRequest));
        Assert.Equal(CompactPresencePresentation.Context, interaction.Presentation);

        var focusExitRequest = interaction.SetKeyboardFocus(false);

        Assert.True(interaction.TryCompleteCollapse(focusExitRequest));
        Assert.Equal(CompactPresencePresentation.Idle, interaction.Presentation);
    }

    [Fact]
    public void CollapseDelayIsApproximatelyThreeHundredMilliseconds()
    {
        Assert.InRange(
            CompactPresenceLayout.CollapseDelay.TotalMilliseconds,
            280d,
            320d);
    }

    [Theory]
    [InlineData(13, true)]
    [InlineData(32, true)]
    [InlineData(27, false)]
    public void EnterAndSpaceAreTheOnlyDashboardActivationKeys(
        uint virtualKey,
        bool expected)
    {
        Assert.Equal(
            expected,
            CompactPresenceLayout.IsDashboardActivationKey(virtualKey));
    }

    [Fact]
    public void WholeSurfaceActivationOpensDashboard()
    {
        var interaction = new CompactPresenceInteraction();

        Assert.True(interaction.OpenDashboard());
        Assert.Equal(
            CompactPresencePresentation.Dashboard,
            interaction.Presentation);
        Assert.True(interaction.CloseDashboard());
        Assert.Equal(CompactPresencePresentation.Idle, interaction.Presentation);
    }

    [Theory]
    [MemberData(nameof(CompactSizesAndPositions))]
    public void CompactSizesStayAnchoredToBottomRight(
        CompactPresenceSize size,
        CompactPresencePosition expectedPosition)
    {
        var workArea = new CompactPresenceWorkArea(
            X: 100,
            Y: 50,
            Width: 1920,
            Height: 1080);

        Assert.Equal(
            expectedPosition,
            CompactPresenceLayout.CalculateBottomRightPosition(
                workArea,
                size,
                inset: 16));
    }

    [Fact]
    public void PositionSupportsNegativeOriginMonitor()
    {
        Assert.Equal(
            new CompactPresencePosition(-112, 968),
            CompactPresenceLayout.CalculateBottomRightPosition(
                new CompactPresenceWorkArea(
                    X: -1920,
                    Y: 0,
                    Width: 1920,
                    Height: 1080),
                CompactPresenceLayout.IdleSize,
                inset: 16));
    }

    [Fact]
    public void CompactMarkupUsesButtonlessOrbAndContextOnlyText()
    {
        var markup = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        var navigationStart = markup.IndexOf(
            "<NavigationView",
            StringComparison.Ordinal);
        var compactMarkup = markup[..navigationStart];
        var contextStart = compactMarkup.IndexOf(
            "x:Name=\"CompactContextPanel\"",
            StringComparison.Ordinal);
        var coreStart = compactMarkup.IndexOf(
            "x:Name=\"PresenceCoreHost\"",
            StringComparison.Ordinal);

        Assert.True(navigationStart > 0);
        Assert.True(contextStart > 0);
        Assert.True(coreStart > contextStart);
        Assert.Contains(
            "x:Name=\"CompactPresenceSurface\"",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.Contains(
            "Tapped=\"OnCompactPresenceTapped\"",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.Contains(
            "KeyDown=\"OnCompactPresenceKeyDown\"",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"PresenceOuterGlow\"",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"PresenceEnergyLayer\"",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"PresenceCore\"",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"PresenceSweep\"",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.DoesNotContain("<Button", compactMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("CompactIdlePanel", compactMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("PresenceStateText", compactMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("Open dashboard", compactMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("View dashboard", compactMarkup, StringComparison.Ordinal);
    }

    public static TheoryData<CompactPresenceSize, CompactPresencePosition>
        CompactSizesAndPositions => new()
        {
            {
                CompactPresenceLayout.IdleSize,
                new CompactPresencePosition(1908, 1018)
            },
            {
                CompactPresenceLayout.ContextSize,
                new CompactPresencePosition(1724, 1014)
            }
        };
}
