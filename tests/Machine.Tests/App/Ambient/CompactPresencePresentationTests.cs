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
        Assert.InRange(frame.GetAlpha(
            AmbientOrbFrameSequence.CanvasSize / 2,
            AmbientOrbFrameSequence.CanvasSize / 2), 120, 255);
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
            Assert.Equal(0, frame.GetAlpha(
                AmbientOrbFrameSequence.CanvasSize - 1,
                AmbientOrbFrameSequence.CanvasSize - 1));
            Assert.True(frame.GetAlpha(
                AmbientOrbFrameSequence.CanvasSize / 2,
                AmbientOrbFrameSequence.CanvasSize / 2) >= 20);
        }
    }

    [Fact]
    public void OrbFramesFadeSoftlyFromCenterToTransparentEdge()
    {
        var frame = AmbientOrbFrameSequence.Create().Frames[12];

        var center = AmbientOrbFrameSequence.CanvasSize / 2;
        var centerAlpha = frame.GetAlpha(center, center);
        var glowAlpha = frame.GetAlpha(center + 24, center);
        var outerAlpha = frame.GetAlpha(
            AmbientOrbFrameSequence.CanvasSize - 5,
            center);

        Assert.True(centerAlpha > glowAlpha);
        Assert.True(glowAlpha > outerAlpha);
        Assert.Equal(0, outerAlpha);
    }

    [Fact]
    public void StableVisibleFootprintIsSmallWithATransparentSurround()
    {
        var frame = AmbientOrbFrameSequence.Create().Frames[12];
        var body = GetVisibleBounds(frame, minimumAlpha: 20);
        var halo = GetVisibleBounds(frame, minimumAlpha: 2);

        Assert.InRange(body.Width, 42, 50);
        Assert.InRange(body.Height, 42, 50);
        Assert.InRange(halo.Width, 52, 68);
        Assert.InRange(halo.Height, 52, 68);
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
    public void StableBreathingIsMaterialAndLoopSmoothly()
    {
        var sequence = AmbientOrbFrameSequence.Create();

        var minimum = sequence.Frames[0];
        var maximum = sequence.Frames[sequence.Frames.Count / 4];
        var settled = sequence.Frames[sequence.Frames.Count * 3 / 4];

        Assert.True(AmbientOrbFrameSequence.MeanAlphaDifference(
            minimum, maximum) > 2d);
        Assert.True(AmbientOrbFrameSequence.MeanLuminance(maximum) >
            AmbientOrbFrameSequence.MeanLuminance(minimum) * 1.03d);
        Assert.True(AmbientOrbFrameSequence.MeanAlphaDifference(
            maximum, settled) > 2d);
        Assert.InRange(
            AmbientOrbFrameSequence.MeanAlphaDifference(
                sequence.Frames[0],
                sequence.Frames[^1]),
            0d,
            2d);
        Assert.Equal(20, AmbientOrbFrameSequence.FramesPerSecond);
        Assert.InRange(sequence.FrameInterval.TotalSeconds, 0.04d, 0.06d);
        Assert.Equal(
            TimeSpan.FromSeconds(5),
            sequence.FrameInterval * sequence.Frames.Count);
    }

    [Fact]
    public void StableBreathingChangesTheActualAlphaSilhouette()
    {
        var sequence = AmbientOrbFrameSequence.Create();
        var inhale = sequence.Frames[28];
        var exhale = sequence.Frames[72];

        Assert.True(AmbientOrbFrameSequence.SilhouetteDifferencePixels(
            inhale,
            exhale) >= 40);
    }

    [Fact]
    public void OrganicDeformationMovesBoundaryInBothDirections()
    {
        var sequence = AmbientOrbFrameSequence.Create();
        var first = sequence.Frames[28];
        var second = sequence.Frames[40];
        var firstOnly = 0;
        var secondOnly = 0;
        for (var y = 0; y < first.Height; y++)
        {
            for (var x = 0; x < first.Width; x++)
            {
                var firstInside = first.GetAlpha(x, y) >=
                    AmbientOrbFrameSequence.SilhouetteAlphaThreshold;
                var secondInside = second.GetAlpha(x, y) >=
                    AmbientOrbFrameSequence.SilhouetteAlphaThreshold;
                if (firstInside && !secondInside)
                {
                    firstOnly++;
                }
                else if (secondInside && !firstInside)
                {
                    secondOnly++;
                }
            }
        }

        Assert.True(firstOnly >= 4);
        Assert.True(secondOnly >= 4);
    }

    [Fact]
    public void BodyAreaGrowsOnInhaleAndRelaxesOnExhale()
    {
        var sequence = AmbientOrbFrameSequence.Create();
        var inhaleArea = AmbientOrbFrameSequence.SilhouetteArea(
            sequence.Frames[30]);
        var exhaleArea = AmbientOrbFrameSequence.SilhouetteArea(
            sequence.Frames[75]);

        Assert.True(inhaleArea >= exhaleArea + 80);
    }

    [Fact]
    public void NonRestContourIsSmoothlyAsymmetric()
    {
        var motion = AmbientOrbMotionModel.CreateForProgress(
            0.31d,
            CompactPresenceVisualMode.Stable);
        var radii = Enumerable.Range(0, 72)
            .Select(index => AmbientOrbMotionModel.GetContourRadius(
                Math.Tau * index / 72d,
                motion))
            .ToArray();

        Assert.True(radii.Max() - radii.Min() > 1.5d);
        Assert.NotEqual(radii[0], radii[36], precision: 2);
    }

    [Fact]
    public void ReducedMotionKeepsOrganicGeometryStaticAcrossElapsedTime()
    {
        var first = AmbientOrbMotionModel.Create(
            TimeSpan.Zero,
            CompactPresenceVisualMode.Stable,
            reducedMotion: true);
        var later = AmbientOrbMotionModel.Create(
            TimeSpan.FromSeconds(17),
            CompactPresenceVisualMode.Stable,
            reducedMotion: true);

        Assert.Equal(first, later);
        Assert.True(radiiVary(first));

        static bool radiiVary(AmbientOrbMotionParameters motion)
        {
            var radii = Enumerable.Range(0, 24)
                .Select(index => AmbientOrbMotionModel.GetContourRadius(
                    Math.Tau * index / 24d,
                    motion));
            return radii.Max() - radii.Min() > 1d;
        }
    }

    [Fact]
    public void PostureTransitionKeepsPhaseAndBlendsByElapsedTime()
    {
        var stable = AmbientOrbTransitionModel.CreateTarget(
            new(
                CompactPresenceVisualMode.Stable,
                IsGenerating: false,
                HasNewUnseenInsight: false),
            isHovered: false);
        var attention = AmbientOrbTransitionModel.CreateTarget(
            new(
                CompactPresenceVisualMode.Attention,
                IsGenerating: false,
                HasNewUnseenInsight: false),
            isHovered: false);

        var entering = AmbientOrbTransitionModel.Advance(
            stable,
            attention,
            TimeSpan.FromMilliseconds(50));
        var continuing = AmbientOrbTransitionModel.Advance(
            entering,
            attention,
            TimeSpan.FromMilliseconds(50));
        var before = AmbientOrbMotionModel.CreateForProgress(
            0.42d,
            CompactPresenceVisualMode.Stable,
            stable);
        var during = AmbientOrbMotionModel.CreateForProgress(
            0.42d,
            CompactPresenceVisualMode.Attention,
            entering);

        Assert.InRange(entering.AttentionAmount, 0.01d, 0.99d);
        Assert.True(continuing.AttentionAmount >
            entering.AttentionAmount);
        Assert.Equal(before.CycleProgress, during.CycleProgress);
        Assert.Equal(before.BreathAmount, during.BreathAmount);
    }

    [Fact]
    public void EverySemanticTransitionPreservesTheUnderlyingBreathPhase()
    {
        var modes = new[]
        {
            CompactPresenceVisualMode.Stable,
            CompactPresenceVisualMode.Attention,
            CompactPresenceVisualMode.Warning,
            CompactPresenceVisualMode.Critical,
            CompactPresenceVisualMode.Unknown
        };
        const double phase = 0.37d;

        foreach (var fromMode in modes)
        {
            foreach (var toMode in modes)
            {
                var current = AmbientOrbTransitionModel.CreateTarget(
                    new(fromMode, false, false),
                    isHovered: false);
                var target = AmbientOrbTransitionModel.CreateTarget(
                    new(toMode, false, false),
                    isHovered: false);
                var transition = AmbientOrbTransitionModel.Advance(
                    current,
                    target,
                    TimeSpan.FromMilliseconds(80));
                var motion = AmbientOrbMotionModel.CreateForProgress(
                    phase,
                    toMode,
                    transition);

                Assert.Equal(phase, motion.CycleProgress, precision: 12);
                Assert.Equal(
                    AmbientOrbMotionModel.CreateForProgress(
                        phase,
                        fromMode,
                        current).BreathAmount,
                    motion.BreathAmount);
            }
        }
    }

    [Fact]
    public void TransitionInterpolationDependsOnTimeNotFrameCount()
    {
        var current = AmbientOrbTransitionModel.CreateTarget(
            new(
                CompactPresenceVisualMode.Warning,
                IsGenerating: false,
                HasNewUnseenInsight: false),
            isHovered: false);
        var target = AmbientOrbTransitionModel.CreateTarget(
            new(
                CompactPresenceVisualMode.Stable,
                IsGenerating: true,
                HasNewUnseenInsight: false),
            isHovered: true);

        var oneStep = AmbientOrbTransitionModel.Advance(
            current,
            target,
            TimeSpan.FromMilliseconds(200));
        var fourSteps = current;
        for (var index = 0; index < 4; index++)
        {
            fourSteps = AmbientOrbTransitionModel.Advance(
                fourSteps,
                target,
                TimeSpan.FromMilliseconds(50));
        }

        Assert.Equal(oneStep.WarningAmount,
            fourSteps.WarningAmount, precision: 12);
        Assert.Equal(oneStep.GeneratingAmount,
            fourSteps.GeneratingAmount, precision: 12);
        Assert.Equal(oneStep.HoverAmount,
            fourSteps.HoverAmount, precision: 12);
    }

    [Fact]
    public void HoverGeneratingAndInsightAreAdditiveWithoutPhaseReset()
    {
        var ordinary = new CompactPresenceVisualState(
            CompactPresenceVisualMode.Warning,
            IsGenerating: false,
            HasNewUnseenInsight: false);
        var modified = ordinary with
        {
            IsGenerating = true,
            HasNewUnseenInsight = true
        };
        var current = AmbientOrbTransitionModel.CreateTarget(
            ordinary,
            isHovered: false);
        var target = AmbientOrbTransitionModel.CreateTarget(
            modified,
            isHovered: true);
        var blend = AmbientOrbTransitionModel.Advance(
            current,
            target,
            TimeSpan.FromMilliseconds(100));
        var normal = AmbientOrbMotionModel.CreateForProgress(
            0.63d,
            ordinary.PostureMode,
            current);
        var wake = AmbientOrbMotionModel.CreateForProgress(
            0.63d,
            modified.PostureMode,
            blend,
            AmbientOrbInsightModifier.Wake,
            insightProgress: 0.42d);

        Assert.Equal(CompactPresenceVisualMode.Warning,
            modified.PostureMode);
        Assert.True(modified.IsGenerating);
        Assert.Equal(normal.CycleProgress, wake.CycleProgress);
        Assert.Equal(normal.BreathAmount, wake.BreathAmount);
        Assert.True(wake.GeneratingAmount > 0d);
        Assert.True(wake.HoverAmount > 0d);
        Assert.True(wake.NewInsightAmount > 0d);
    }

    [Fact]
    public void InsightNewnessDoesNotReplacePostureOrGeneratingTargets()
    {
        var unseen = new CompactPresenceVisualState(
            CompactPresenceVisualMode.Attention,
            IsGenerating: true,
            HasNewUnseenInsight: true);
        var seen = unseen with { HasNewUnseenInsight = false };

        Assert.Equal(
            AmbientOrbTransitionModel.CreateTarget(
                unseen,
                isHovered: false),
            AmbientOrbTransitionModel.CreateTarget(
                seen,
                isHovered: false));
        Assert.Equal(CompactPresenceVisualMode.Generating, unseen.Mode);
        Assert.Equal(CompactPresenceVisualMode.Attention,
            unseen.PostureMode);
    }

    [Fact]
    public void PhaseWrapIsVisuallyContinuous()
    {
        var sequence = AmbientOrbFrameSequence.Create();
        var beforePixels = new byte[
            AmbientOrbFrameSequence.CanvasSize *
            AmbientOrbFrameSequence.CanvasSize * 4];
        var afterPixels = new byte[beforePixels.Length];
        sequence.RenderInto(beforePixels, 0.9999d, animationsEnabled: true);
        sequence.RenderInto(afterPixels, 0.0001d, animationsEnabled: true);
        var before = new AmbientOrbFrame(
            AmbientOrbFrameSequence.CanvasSize,
            AmbientOrbFrameSequence.CanvasSize,
            beforePixels);
        var after = new AmbientOrbFrame(
            AmbientOrbFrameSequence.CanvasSize,
            AmbientOrbFrameSequence.CanvasSize,
            afterPixels);

        Assert.InRange(AmbientOrbFrameSequence.MeanAlphaDifference(
            before,
            after), 0d, 0.15d);
    }

    [Fact]
    public void HiddenDashboardIntervalAdvancesLogicalPhaseWithoutFrameZero()
    {
        var before = AmbientOrbMotionModel.Create(
            TimeSpan.FromSeconds(2.3d),
            CompactPresenceVisualMode.Stable);
        var after = AmbientOrbMotionModel.Create(
            TimeSpan.FromSeconds(12.3d),
            CompactPresenceVisualMode.Stable);

        Assert.Equal(before.CycleProgress,
            after.CycleProgress, precision: 12);
        Assert.NotEqual(AmbientOrbMotionModel.StaticCycleProgress,
            after.CycleProgress);
        Assert.NotEqual(before.SlowDriftProgress,
            after.SlowDriftProgress);
    }

    [Fact]
    public void ReducedMotionAppliesSemanticTargetWithoutAFrameTimerRamp()
    {
        var stable = AmbientOrbTransitionModel.CreateTarget(
            new(
                CompactPresenceVisualMode.Stable,
                IsGenerating: false,
                HasNewUnseenInsight: false),
            isHovered: false);
        var critical = AmbientOrbTransitionModel.CreateTarget(
            new(
                CompactPresenceVisualMode.Critical,
                IsGenerating: true,
                HasNewUnseenInsight: true),
            isHovered: false);

        Assert.Equal(
            critical,
            AmbientOrbTransitionModel.Advance(
                stable,
                critical,
                TimeSpan.Zero,
                animationsEnabled: false));
    }

    [Fact]
    public void NewInsightIsAnOverlayWithoutChangingPosture()
    {
        var state = CompactPresenceLayout.SelectVisualState(
            Machine.Core.MachineOverallState.Stable,
            isGenerating: false,
            hasNewUnseenInsight: true);
        var normal = AmbientOrbMotionModel.CreateForProgress(
            0.25d,
            state.PostureMode);
        var wake = AmbientOrbMotionModel.CreateForProgress(
            0.25d,
            state.PostureMode,
            insightModifier: AmbientOrbInsightModifier.Wake,
            insightProgress: 0.42d);

        Assert.Equal(CompactPresenceVisualMode.Stable, state.PostureMode);
        Assert.False(state.IsGenerating);
        Assert.True(state.HasNewUnseenInsight);
        Assert.Equal(normal.PostureMode, wake.PostureMode);
        Assert.True(wake.HasNewUnseenInsight);
        Assert.True(wake.NewInsightAmount > 0d);
        Assert.True(wake.Expansion > normal.Expansion);
    }

    [Fact]
    public void VisibleOrbPixelsHitTestButTransparentPixelsDoNot()
    {
        var sequence = AmbientOrbFrameSequence.Create();

        Assert.True(sequence.IsHitTestVisible(48, 48));
        Assert.False(sequence.IsHitTestVisible(0, 0));
        Assert.False(sequence.IsHitTestVisible(95, 95));
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
    public void HoverPreservesModeAndIsSofterThanIdleBreathing()
    {
        var normal = AmbientOrbFrameSequence.Create();
        var hovered = AmbientOrbFrameSequence.Create(
            CompactPresenceVisualMode.Stable,
            isHovered: true);

        Assert.Equal(normal.Mode, hovered.Mode);
        Assert.True(hovered.IsHovered);
        var hoverIncrease = AmbientOrbFrameSequence.MeanLuminance(
            hovered.Frames[0]) - AmbientOrbFrameSequence.MeanLuminance(
            normal.Frames[0]);
        var breathingIncrease = AmbientOrbFrameSequence.MeanLuminance(
            normal.Frames[normal.Frames.Count / 4]) -
            AmbientOrbFrameSequence.MeanLuminance(normal.Frames[0]);
        Assert.True(hoverIncrease > 0d);
        Assert.True(hoverIncrease < breathingIncrease);
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

        Assert.True(sequence.IsHitTestVisible(48, 48));
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

    [Fact]
    public void NativeAnimationLifecycleStartsVisibleWithoutHoverAndNeverDuplicates()
    {
        var lifecycle = new AmbientOrbLifecycle();

        Assert.Equal(
            AmbientOrbTimerTransition.Start,
            lifecycle.ShowWithTimerTransition());
        Assert.True(lifecycle.ShouldAnimate);
        Assert.True(lifecycle.IsTimerRunning);
        Assert.Equal(
            AmbientOrbTimerTransition.None,
            lifecycle.ShowWithTimerTransition());

        Assert.Equal(
            AmbientOrbTimerTransition.Stop,
            lifecycle.SetAnimationsEnabled(false));
        Assert.False(lifecycle.ShouldAnimate);
        Assert.False(lifecycle.IsTimerRunning);
        Assert.Equal(
            AmbientOrbTimerTransition.None,
            lifecycle.SetAnimationsEnabled(false));

        Assert.Equal(
            AmbientOrbTimerTransition.Start,
            lifecycle.SetAnimationsEnabled(true));
        Assert.Equal(
            AmbientOrbTimerTransition.Stop,
            lifecycle.Hide());
        Assert.False(lifecycle.IsTimerRunning);
    }

    [Fact]
    public void DashboardPresentationIsFramelessAndEscapeReturnsToAmbient()
    {
        Assert.False(DashboardChromeLayout.HasBorder);
        Assert.False(DashboardChromeLayout.HasTitleBar);
        Assert.True(DashboardChromeLayout.IsReturnToAmbientKey(27));
        Assert.False(DashboardChromeLayout.IsReturnToAmbientKey(13));

        var interaction = new CompactPresenceInteraction();
        interaction.OpenDashboard();
        Assert.True(interaction.CloseDashboard());
        Assert.Equal(CompactPresencePresentation.Ambient,
            interaction.Presentation);
    }

    [Fact]
    public void DashboardCaptionRegionIsDpiAwareAndExcludesCloseControl()
    {
        var region = DashboardChromeLayout.CalculateCaptionRegion(
            x: 8,
            y: 3,
            width: 472,
            height: 28,
            rasterizationScale: 1.5d);

        Assert.Equal(new DashboardCaptionRegion(12, 4, 708, 42), region);
        Assert.True(region.Right <= 480 * 1.5d);
    }

    [Fact]
    public void DashboardUsesSmallDwmCornerClipToAvoidMicaDiagonalSeam()
    {
        Assert.Equal(33,
            DashboardChromeLayout.DwmWindowCornerPreferenceAttribute);
        Assert.Equal(3,
            DashboardChromeLayout.DwmRoundSmallCornerPreference);
        Assert.False(DashboardChromeLayout.HasBorder);
        Assert.False(DashboardChromeLayout.HasTitleBar);

        var xaml = ReadDashboardXaml();
        Assert.Contains("<MicaBackdrop Kind=\"BaseAlt\"", xaml);
        Assert.Contains("Background=\"Transparent\"", xaml);
        Assert.DoesNotContain("OpaqueOverlay", xaml,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DashboardCloseCommandInvokesBoundedCloseAction()
    {
        var closeCount = 0;

        DashboardChromeLayout.InvokeClose(() => closeCount++);

        Assert.Equal(1, closeCount);
    }

    [Fact]
    public void DashboardXamlExposesIntegratedChromeAndLearningAutomationIds()
    {
        var xaml = ReadDashboardXaml();

        Assert.Contains("AutomationProperties.AutomationId=\"DashboardCloseButton\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"LearningNavigationItem\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"LearningPage\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"LearningPageLifetimeObservationsText\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"LearningProfilesList\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"LearningPatternsList\"", xaml);
        Assert.Contains("Text=\"Learning Lab\"", xaml);
        Assert.Contains("Text=\"Live Learning\"", xaml);
        Assert.Contains("Text=\"Learned Contexts\"", xaml);
        Assert.Contains("Text=\"Recurring Behavior\"", xaml);
        Assert.Contains("Text=\"Memory\"", xaml);
        Assert.Contains("Text=\"Recent Learning Changes\"", xaml);
        Assert.Contains("Text=\"AI Knowledge\"", xaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"LearningLiveAcceptanceText\"",
            xaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"LearningLiveSignalsList\"",
            xaml);
        Assert.DoesNotContain("observation period", xaml,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("calibrating", xaml,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AutomationProperties.AutomationId=\"OverviewNextHourEnergyText\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"AiOutlookText\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"RefreshUsageOutlookButton\"", xaml);
        Assert.Contains("Text=\"AI outlook\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"NetworkNavigationItem\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"NetworkPage\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"HealthNavigationItem\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"HealthPage\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"WindowsUpdateHistoryList\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"ReliabilityIncidentsList\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"HistoryNavigationItem\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"HistoryPage\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"HardwareNavigationItem\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"HardwarePage\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"ServicesNavigationItem\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"TasksNavigationItem\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"DevicesNavigationItem\"", xaml);
        Assert.Contains(
            "AutomationProperties.Name=\"Filter scheduled tasks by enabled state\"",
            xaml);
        Assert.Contains(
            "AutomationProperties.Name=\"Filter devices by Windows-reported problem state\"",
            xaml);
        Assert.DoesNotContain("Maximize", xaml, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadDashboardXaml() => string.Join(
        Environment.NewLine,
        Directory.EnumerateFiles(
                AppContext.BaseDirectory,
                "*.xaml",
                SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText));

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
                inset: 24));
    }

    public static TheoryData<CompactPresenceSize, CompactPresencePosition>
        OrbSizesAndPositions => new()
        {
            {
                CompactPresenceLayout.AmbientOrbSize,
                new CompactPresencePosition(1900, 1010)
            },
            {
                new CompactPresenceSize(520, 760),
                new CompactPresencePosition(1476, 346)
            }
        };

    private static (int Width, int Height) GetVisibleBounds(
        AmbientOrbFrame frame,
        byte minimumAlpha)
    {
        var minimumX = frame.Width;
        var minimumY = frame.Height;
        var maximumX = -1;
        var maximumY = -1;
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (frame.GetAlpha(x, y) < minimumAlpha)
                {
                    continue;
                }

                minimumX = Math.Min(minimumX, x);
                minimumY = Math.Min(minimumY, y);
                maximumX = Math.Max(maximumX, x);
                maximumY = Math.Max(maximumY, y);
            }
        }

        Assert.True(maximumX >= minimumX);
        Assert.True(maximumY >= minimumY);
        return (maximumX - minimumX + 1, maximumY - minimumY + 1);
    }

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
