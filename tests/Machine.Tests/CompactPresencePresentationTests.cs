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
}
