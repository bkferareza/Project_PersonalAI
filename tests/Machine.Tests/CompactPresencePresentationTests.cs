using Machine.App;
using Machine.Core;

namespace Machine.Tests;

public sealed class CompactPresencePresentationTests
{
    [Theory]
    [InlineData(MachineOverallState.Stable, "All quiet")]
    [InlineData(MachineOverallState.Attention, "Keeping watch")]
    [InlineData(MachineOverallState.Warning, "Under pressure")]
    [InlineData(MachineOverallState.Critical, "Critical condition")]
    [InlineData(MachineOverallState.Unknown, "Status unclear")]
    public void IdlePhraseMatchesDeterministicState(
        MachineOverallState state,
        string expectedPhrase)
    {
        Assert.Equal(
            expectedPhrase,
            CompactPresenceLayout.GetIdlePhrase(state));
    }

    [Fact]
    public void PointerTransitionWaitsForCollapseCompletion()
    {
        var interaction = new CompactPresenceInteraction();

        Assert.Equal(
            CompactPresencePresentation.Idle,
            interaction.Presentation);

        interaction.PointerEntered();
        Assert.Equal(
            CompactPresencePresentation.Context,
            interaction.Presentation);

        var request = interaction.PointerExited();
        Assert.Equal(
            CompactPresencePresentation.Context,
            interaction.Presentation);

        Assert.True(interaction.TryCompleteCollapse(request));
        Assert.Equal(
            CompactPresencePresentation.Idle,
            interaction.Presentation);
    }

    [Fact]
    public void CollapseDelayStaysWithinGuardedInteractionRange()
    {
        Assert.InRange(
            CompactPresenceLayout.CollapseDelay.TotalMilliseconds,
            250d,
            400d);
    }

    [Fact]
    public void PointerReentryCancelsPendingCollapse()
    {
        var interaction = new CompactPresenceInteraction();
        interaction.PointerEntered();
        var staleRequest = interaction.PointerExited();

        interaction.PointerEntered();

        Assert.False(
            interaction.TryCompleteCollapse(staleRequest));
        Assert.Equal(
            CompactPresencePresentation.Context,
            interaction.Presentation);
    }

    [Fact]
    public void KeyboardFocusPreventsCollapse()
    {
        var interaction = new CompactPresenceInteraction();
        interaction.SetKeyboardFocus(true);
        var pointerExitRequest = interaction.PointerExited();

        Assert.False(
            interaction.TryCompleteCollapse(pointerExitRequest));
        Assert.Equal(
            CompactPresencePresentation.Context,
            interaction.Presentation);

        var focusExitRequest = interaction.SetKeyboardFocus(false);

        Assert.True(
            interaction.TryCompleteCollapse(focusExitRequest));
        Assert.Equal(
            CompactPresencePresentation.Idle,
            interaction.Presentation);
    }

    [Fact]
    public void WholeSurfaceActivationOpensDashboard()
    {
        var interaction = new CompactPresenceInteraction();

        Assert.True(interaction.OpenDashboard());
        Assert.Equal(
            CompactPresencePresentation.Dashboard,
            interaction.Presentation);
        Assert.False(interaction.OpenDashboard());

        Assert.True(interaction.CloseDashboard());
        Assert.Equal(
            CompactPresencePresentation.Idle,
            interaction.Presentation);
    }

    [Theory]
    [InlineData(CompactPresencePresentation.Idle, true)]
    [InlineData(CompactPresencePresentation.Context, true)]
    [InlineData(CompactPresencePresentation.Dashboard, false)]
    public void SurfaceInteractionMatchesPresentation(
        CompactPresencePresentation presentation,
        bool expectedInteractive)
    {
        Assert.Equal(
            expectedInteractive,
            CompactPresenceLayout.IsSurfaceInteractive(presentation));
    }

    [Theory]
    [InlineData(
        MachineOverallState.Stable,
        CompactPresenceVisualMode.Stable)]
    [InlineData(
        MachineOverallState.Attention,
        CompactPresenceVisualMode.Attention)]
    [InlineData(
        MachineOverallState.Warning,
        CompactPresenceVisualMode.Warning)]
    [InlineData(
        MachineOverallState.Critical,
        CompactPresenceVisualMode.Critical)]
    [InlineData(
        MachineOverallState.Unknown,
        CompactPresenceVisualMode.Unknown)]
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
        Assert.Equal(
            CompactPresenceVisualMode.Warning,
            CompactPresenceLayout.SelectVisualMode(
                MachineOverallState.Warning,
                isGenerating: false,
                showNewInsightBloom: false));
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
            new CompactPresencePosition(-224, 1000),
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
    public void PositionClampsToSmallWorkAreaOrigin()
    {
        Assert.Equal(
            new CompactPresencePosition(10, 20),
            CompactPresenceLayout.CalculateBottomRightPosition(
                new CompactPresenceWorkArea(
                    X: 10,
                    Y: 20,
                    Width: 180,
                    Height: 50),
                CompactPresenceLayout.IdleSize,
                inset: 16));
    }

    [Fact]
    public void CompactMarkupRemovesObsoleteVisibleControls()
    {
        var markup = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "MainWindow.xaml"));
        var navigationStart = markup.IndexOf(
            "<NavigationView",
            StringComparison.Ordinal);

        Assert.True(navigationStart > 0);

        var compactMarkup = markup[..navigationStart];
        var idleStart = compactMarkup.IndexOf(
            "x:Name=\"CompactIdlePanel\"",
            StringComparison.Ordinal);
        var contextStart = compactMarkup.IndexOf(
            "x:Name=\"CompactContextPanel\"",
            StringComparison.Ordinal);

        Assert.True(idleStart > 0);
        Assert.True(contextStart > idleStart);

        var idleMarkup = compactMarkup[idleStart..contextStart];
        var contextTagEnd = compactMarkup.IndexOf(
            '>',
            contextStart);
        Assert.True(contextTagEnd > contextStart);
        var contextTag = compactMarkup[contextStart..contextTagEnd];

        Assert.Contains(
            "x:Name=\"CompactPresenceSurface\"",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.Contains(
            "Click=\"OnCompactPresenceClicked\"",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.Contains(
            "<ControlTemplate TargetType=\"Button\">",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.Contains(
            "<Setter Property=\"HorizontalAlignment\" Value=\"Stretch\" />",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.Contains(
            "<Setter Property=\"VerticalAlignment\" Value=\"Stretch\" />",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.Contains(
            "Background=\"{TemplateBinding Background}\"",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.Contains(
            "Visibility=\"Collapsed\"",
            contextTag,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DetailsToggleButton",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CompactDragRegion",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "OllamaPresenceStatusText",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Content=\"Open dashboard\"",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Content=\"View dashboard\"",
            compactMarkup,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PresenceTelemetryText",
            idleMarkup,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CompactInsightPreviewText",
            idleMarkup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompactSurfaceUsesNativeButtonActivation()
    {
        var markup = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "MainWindow.xaml"));
        var surfaceName = markup.IndexOf(
            "x:Name=\"CompactPresenceSurface\"",
            StringComparison.Ordinal);
        var surfaceStart = markup.LastIndexOf(
            "<Button",
            surfaceName,
            StringComparison.Ordinal);
        var surfaceTagEnd = markup.IndexOf(
            '>',
            surfaceName);

        Assert.True(surfaceStart >= 0);
        Assert.True(surfaceTagEnd > surfaceStart);

        var surfaceTag = markup[surfaceStart..surfaceTagEnd];

        // WinUI Button.Click is the native pointer, touch, Enter,
        // and Space activation contract for this single surface.
        Assert.Contains(
            "Click=\"OnCompactPresenceClicked\"",
            surfaceTag,
            StringComparison.Ordinal);
        Assert.Contains(
            "Style=\"{StaticResource AmbientSurfaceControlStyle}\"",
            surfaceTag,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "KeyDown=",
            surfaceTag,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Tapped=",
            surfaceTag,
            StringComparison.Ordinal);
    }

    public static TheoryData<
        CompactPresenceSize,
        CompactPresencePosition> CompactSizesAndPositions => new()
        {
            {
                CompactPresenceLayout.IdleSize,
                new CompactPresencePosition(1796, 1050)
            },
            {
                CompactPresenceLayout.ContextSize,
                new CompactPresencePosition(1676, 1002)
            }
        };
}
