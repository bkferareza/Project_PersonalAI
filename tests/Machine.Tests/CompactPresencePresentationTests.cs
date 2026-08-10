using Machine.App;

namespace Machine.Tests;

public sealed class CompactPresencePresentationTests
{
    [Fact]
    public void OrbFramesHaveFullyTransparentCornersAndVisibleCenter()
    {
        var frame = AmbientOrbFrameSequence.Create().Frames[0];

        Assert.Equal(0, frame.GetAlpha(0, 0));
        Assert.Equal(0, frame.GetAlpha(
            AmbientOrbFrameSequence.CanvasSize - 1,
            AmbientOrbFrameSequence.CanvasSize - 1));
        Assert.InRange(frame.GetAlpha(64, 64), 180, 255);
    }

    [Theory]
    [InlineData(CompactPresenceVisualMode.Stable)]
    [InlineData(CompactPresenceVisualMode.Attention)]
    [InlineData(CompactPresenceVisualMode.Warning)]
    [InlineData(CompactPresenceVisualMode.Critical)]
    [InlineData(CompactPresenceVisualMode.Unknown)]
    [InlineData(CompactPresenceVisualMode.Generating)]
    [InlineData(CompactPresenceVisualMode.NewInsight)]
    public void EveryVisualModeHasValidFramesAndTransparentCorners(
        CompactPresenceVisualMode mode)
    {
        var sequence = AmbientOrbFrameSequence.Create(mode);

        Assert.NotEmpty(sequence.Frames);
        foreach (var frame in sequence.Frames)
        {
            Assert.Equal(AmbientOrbFrameSequence.CanvasSize, frame.Width);
            Assert.Equal(AmbientOrbFrameSequence.CanvasSize, frame.Height);
            Assert.Equal(0, frame.GetAlpha(0, 0));
            Assert.Equal(0, frame.GetAlpha(127, 127));
            Assert.True(frame.GetAlpha(64, 64) >= 20);
        }
    }

    [Fact]
    public void OrbFramesFadeSoftlyFromCenterToTransparentEdge()
    {
        var frame = AmbientOrbFrameSequence.Create().Frames[12];

        var centerAlpha = frame.GetAlpha(64, 64);
        var glowAlpha = frame.GetAlpha(100, 64);
        var outerAlpha = frame.GetAlpha(123, 64);

        Assert.True(centerAlpha > glowAlpha);
        Assert.True(glowAlpha > outerAlpha);
        Assert.Equal(0, outerAlpha);
    }

    [Fact]
    public void StablePaletteIsNeutralAndNeverGreenDominant()
    {
        var frame = AmbientOrbFrameSequence.Create().Frames[8];

        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var pixel = frame.GetPixel(x, y);
                if (pixel.Alpha >= 20)
                {
                    Assert.True(AmbientOrbFrameSequence.IsNeutralStableColor(
                        pixel.Red,
                        pixel.Green,
                        pixel.Blue));
                }
            }
        }
    }

    [Fact]
    public void AttentionIsVisiblyMoreAlertThanStable()
    {
        var stable = AmbientOrbFrameSequence.Create(
            CompactPresenceVisualMode.Stable).Frames[0];
        var attention = AmbientOrbFrameSequence.Create(
            CompactPresenceVisualMode.Attention).Frames[0];

        Assert.True(AmbientOrbFrameSequence.MeanAlphaDifference(
            stable,
            attention) > 1d);
        Assert.True(AmbientOrbFrameSequence.MeanLuminance(attention) >
            AmbientOrbFrameSequence.MeanLuminance(stable));
    }

    [Fact]
    public void WarningUsesWarmEmphasisAndCriticalIsStronger()
    {
        var warning = AmbientOrbFrameSequence.Create(
            CompactPresenceVisualMode.Warning).Frames[0];
        var critical = AmbientOrbFrameSequence.Create(
            CompactPresenceVisualMode.Critical).Frames[0];

        var warningColor = MeanColor(warning);
        var criticalColor = MeanColor(critical);
        Assert.True(warningColor.Red > warningColor.Blue);
        Assert.True(criticalColor.Red > criticalColor.Blue);
        Assert.True(AmbientOrbFrameSequence.MeanAlpha(critical) >
            AmbientOrbFrameSequence.MeanAlpha(warning));
    }

    [Fact]
    public void UnknownIsSubduedComparedToStable()
    {
        var stable = AmbientOrbFrameSequence.Create(
            CompactPresenceVisualMode.Stable).Frames[0];
        var unknown = AmbientOrbFrameSequence.Create(
            CompactPresenceVisualMode.Unknown).Frames[0];

        Assert.True(AmbientOrbFrameSequence.MeanLuminance(unknown) <
            AmbientOrbFrameSequence.MeanLuminance(stable));
    }

    [Fact]
    public void BreathingFramesAreDifferentAndLoopSmoothly()
    {
        var sequence = AmbientOrbFrameSequence.Create();

        Assert.NotEqual(
            sequence.Frames[0].Pixels,
            sequence.Frames[12].Pixels);
        Assert.InRange(
            AmbientOrbFrameSequence.MeanAlphaDifference(
                sequence.Frames[0],
                sequence.Frames[^1]),
            0d,
            2d);
        Assert.Equal(10, AmbientOrbFrameSequence.FramesPerSecond);
        Assert.InRange(sequence.FrameInterval.TotalSeconds, 0.09d, 0.11d);
    }

    [Fact]
    public void VisibleOrbPixelsHitTestButTransparentPixelsDoNot()
    {
        var sequence = AmbientOrbFrameSequence.Create();

        Assert.True(sequence.IsHitTestVisible(64, 64));
        Assert.False(sequence.IsHitTestVisible(0, 0));
        Assert.False(sequence.IsHitTestVisible(127, 127));
    }

    [Fact]
    public void GeneratingFramesMoveInternallyWithoutASeparateSpinner()
    {
        var sequence = AmbientOrbFrameSequence.Create(
            CompactPresenceVisualMode.Generating);

        Assert.True(sequence.IsLooping);
        Assert.True(AmbientOrbFrameSequence.MeanAlphaDifference(
            sequence.Frames[0],
            sequence.Frames[10]) > 0.5d);
    }

    [Fact]
    public void NewInsightIsOneShotAndSelectionReturnsToUnderlyingState()
    {
        var sequence = AmbientOrbFrameSequence.Create(
            CompactPresenceVisualMode.NewInsight);

        Assert.False(sequence.IsLooping);
        Assert.True(AmbientOrbFrameSequence.MeanLuminance(
            sequence.Frames[sequence.StaticFrameIndex]) >
            AmbientOrbFrameSequence.MeanLuminance(sequence.Frames[0]));
        Assert.Equal(
            CompactPresenceVisualMode.Warning,
            CompactPresenceLayout.SelectVisualMode(
                Machine.Core.MachineOverallState.Warning,
                isGenerating: false,
                showNewInsightBloom: false));
    }

    [Fact]
    public void HoverPreservesModeAndIncreasesVisualIntensity()
    {
        var normal = AmbientOrbFrameSequence.Create(
            CompactPresenceVisualMode.Warning);
        var hovered = AmbientOrbFrameSequence.Create(
            CompactPresenceVisualMode.Warning,
            isHovered: true);

        Assert.Equal(normal.Mode, hovered.Mode);
        Assert.True(hovered.IsHovered);
        Assert.True(AmbientOrbFrameSequence.MeanLuminance(hovered.Frames[0]) >
            AmbientOrbFrameSequence.MeanLuminance(normal.Frames[0]));
    }

    [Theory]
    [InlineData(CompactPresenceVisualMode.Stable)]
    [InlineData(CompactPresenceVisualMode.Warning)]
    [InlineData(CompactPresenceVisualMode.Critical)]
    [InlineData(CompactPresenceVisualMode.Generating)]
    public void ModeSwitchingKeepsVisiblePixelsInteractive(
        CompactPresenceVisualMode mode)
    {
        var sequence = AmbientOrbFrameSequence.Create(mode);

        Assert.True(sequence.IsHitTestVisible(64, 64));
        Assert.False(sequence.IsHitTestVisible(0, 0));
    }

    [Fact]
    public void ReducedMotionAlwaysSelectsOneStaticFrame()
    {
        var generating = AmbientOrbFrameSequence.Create(
            CompactPresenceVisualMode.Generating);
        var bloom = AmbientOrbFrameSequence.Create(
            CompactPresenceVisualMode.NewInsight);

        Assert.Same(
            generating.GetFrame(0, animationsEnabled: false),
            generating.GetFrame(19, animationsEnabled: false));
        Assert.Same(
            bloom.Frames[bloom.StaticFrameIndex],
            bloom.GetFrame(0, animationsEnabled: false));
    }

    [Fact]
    public void NativeOrbFrameSequencesHaveNoXamlAnimationState()
    {
        Assert.DoesNotContain(
            typeof(AmbientOrbFrameSequence).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic),
            field => field.FieldType.Name.Contains(
                "Transform",
                StringComparison.Ordinal));
    }

    [Fact]
    public void AmbientAndDashboardLifecycleRestoresTheOrb()
    {
        var interaction = new CompactPresenceInteraction();

        Assert.Equal(CompactPresencePresentation.Ambient, interaction.Presentation);
        Assert.True(interaction.OpenDashboard());
        Assert.Equal(CompactPresencePresentation.Dashboard, interaction.Presentation);
        Assert.True(interaction.CloseDashboard());
        Assert.Equal(CompactPresencePresentation.Ambient, interaction.Presentation);
    }

    [Fact]
    public void AmbientOrbLifecycleHidesAndDisposesCleanly()
    {
        var lifecycle = new AmbientOrbLifecycle();

        lifecycle.Show();
        Assert.True(lifecycle.IsVisible);
        lifecycle.Hide();
        Assert.False(lifecycle.IsVisible);
        lifecycle.Dispose();

        Assert.True(lifecycle.IsDisposed);
        Assert.False(lifecycle.IsVisible);
        Assert.Throws<ObjectDisposedException>(lifecycle.Show);
    }

    [Theory]
    [MemberData(nameof(OrbSizesAndPositions))]
    public void OrbStaysAnchoredToBottomRight(
        CompactPresenceSize size,
        CompactPresencePosition expectedPosition)
    {
        Assert.Equal(
            expectedPosition,
            CompactPresenceLayout.CalculateBottomRightPosition(
                new CompactPresenceWorkArea(100, 50, 1920, 1080),
                size,
                inset: 16));
    }

    public static TheoryData<CompactPresenceSize, CompactPresencePosition>
        OrbSizesAndPositions => new()
        {
            {
                CompactPresenceLayout.AmbientOrbSize,
                new CompactPresencePosition(1876, 986)
            },
            {
                new CompactPresenceSize(520, 760),
                new CompactPresencePosition(1484, 354)
            }
        };

    private static (double Red, double Green, double Blue) MeanColor(
        AmbientOrbFrame frame)
    {
        var red = 0d;
        var green = 0d;
        var blue = 0d;
        foreach (var pixel in frame.Pixels.Chunk(4))
        {
            blue += pixel[0];
            green += pixel[1];
            red += pixel[2];
        }

        var pixelCount = frame.Width * frame.Height;
        return (red / pixelCount, green / pixelCount, blue / pixelCount);
    }
}
