namespace Machine.App;

public enum AmbientOrbInsightModifier
{
    None,
    UnseenCue,
    Wake
}

public readonly record struct AmbientOrbMotionParameters(
    CompactPresenceVisualMode PostureMode,
    double CycleProgress,
    double SlowDriftProgress,
    double BreathAmount,
    double BaseRadius,
    double Expansion,
    double DeformationPhase,
    double CenterX,
    double CenterY,
    double HighlightX,
    double HighlightY,
    double HoverAmount,
    double GeneratingAmount,
    double NewInsightAmount,
    bool HasNewUnseenInsight,
    bool ReducedMotion,
    AmbientOrbBlendState BlendState);

public static class AmbientOrbMotionModel
{
    public static readonly TimeSpan StableCycleDuration =
        TimeSpan.FromSeconds(5);
    public static readonly TimeSpan SlowDriftDuration =
        TimeSpan.FromSeconds(47);
    public const double StaticCycleProgress = 0.16d;
    public const double StaticSlowDriftProgress = 0.23d;

    public static AmbientOrbMotionParameters Create(
        TimeSpan elapsed,
        CompactPresenceVisualMode postureMode,
        double hoverAmount = 0d,
        AmbientOrbInsightModifier insightModifier =
            AmbientOrbInsightModifier.None,
        double insightProgress = 0d,
        bool reducedMotion = false)
    {
        var seconds = Math.Max(0d, elapsed.TotalSeconds);
        var progress = reducedMotion
            ? StaticCycleProgress
            : seconds / StableCycleDuration.TotalSeconds % 1d;
        var slowDriftProgress = reducedMotion
            ? StaticSlowDriftProgress
            : seconds / SlowDriftDuration.TotalSeconds % 1d;
        return CreateForProgress(
            progress,
            postureMode,
            hoverAmount,
            insightModifier,
            insightProgress,
            reducedMotion,
            slowDriftProgress);
    }

    public static AmbientOrbMotionParameters CreateForProgress(
        double cycleProgress,
        CompactPresenceVisualMode postureMode,
        double hoverAmount = 0d,
        AmbientOrbInsightModifier insightModifier =
            AmbientOrbInsightModifier.None,
        double insightProgress = 0d,
        bool reducedMotion = false,
        double slowDriftProgress = StaticSlowDriftProgress)
    {
        var mode = postureMode == CompactPresenceVisualMode.NewInsight
            ? CompactPresenceVisualMode.Stable
            : postureMode;
        var isGenerating = mode == CompactPresenceVisualMode.Generating;
        if (isGenerating)
        {
            mode = CompactPresenceVisualMode.Stable;
        }
        var blendState = AmbientOrbTransitionModel.CreateTarget(
            new CompactPresenceVisualState(
                mode,
                isGenerating,
                insightModifier != AmbientOrbInsightModifier.None),
            isHovered: false) with
        {
            HoverAmount = Math.Clamp(hoverAmount, 0d, 1d)
        };
        return CreateForProgress(
            cycleProgress,
            mode,
            blendState,
            insightModifier,
            insightProgress,
            reducedMotion,
            slowDriftProgress);
    }

    public static AmbientOrbMotionParameters CreateForProgress(
        double cycleProgress,
        CompactPresenceVisualMode postureMode,
        AmbientOrbBlendState blendState,
        AmbientOrbInsightModifier insightModifier =
            AmbientOrbInsightModifier.None,
        double insightProgress = 0d,
        bool reducedMotion = false,
        double slowDriftProgress = StaticSlowDriftProgress)
    {
        var progress = reducedMotion
            ? StaticCycleProgress
            : WrapUnit(cycleProgress);
        var cyclePhase = Math.Tau * progress;
        var slowProgress = reducedMotion
            ? StaticSlowDriftProgress
            : WrapUnit(slowDriftProgress);
        var slowPhase = Math.Tau * slowProgress;
        var breath = CreateOrganicBreathEnvelope(progress);
        var hover = Math.Clamp(blendState.HoverAmount, 0d, 1d);
        var generating = Math.Clamp(
            blendState.GeneratingAmount,
            0d,
            1d);
        var wake = insightModifier == AmbientOrbInsightModifier.Wake &&
            !reducedMotion
                ? CreateWakeEnvelope(Math.Clamp(insightProgress, 0d, 1d))
                : 0d;
        var baseRadius = BlendPostureValue(
            blendState,
            stable: 21.4d,
            attention: 21.5d,
            warning: 21.55d,
            critical: 21.65d,
            unknown: 21.1d);
        var breathExpansion = BlendPostureValue(
            blendState,
            stable: 1.45d,
            attention: 1.25d,
            warning: 1.08d,
            critical: 0.95d,
            unknown: 0.85d);
        var expansion = breathExpansion * breath +
            0.18d * hover + 0.10d * generating + 0.82d * wake;
        var drift = 0.52d * Math.Sin(slowPhase + 0.35d) +
            0.17d * Math.Sin(2d * slowPhase - 0.8d);
        var centerX = 47.5d + drift + 0.10d * hover;
        var centerY = 47.5d - 0.46d * breath +
            0.20d * Math.Sin(slowPhase - 0.4d) -
            0.18d * wake;
        var highlightX = centerX - 5.3d - 0.62d * breath -
            0.55d * wake;
        var highlightY = centerY - 5.8d -
            0.35d * Math.Sin(slowPhase + 0.2d) -
            0.45d * wake;

        return new(
            postureMode,
            progress,
            slowProgress,
            breath,
            baseRadius,
            expansion,
            slowPhase + 0.24d * Math.Sin(2d * slowPhase) +
                0.48d * Math.Sin(cyclePhase),
            centerX,
            centerY,
            highlightX,
            highlightY,
            hover,
            generating,
            wake,
            insightModifier != AmbientOrbInsightModifier.None,
            reducedMotion,
            blendState);
    }

    public static double GetContourRadius(
        double angle,
        AmbientOrbMotionParameters motion)
    {
        var phase = motion.DeformationPhase;
        return motion.BaseRadius + motion.Expansion +
            0.72d * Math.Sin(2d * angle + 0.35d + phase) +
            0.46d * Math.Sin(3d * angle - 0.75d - phase) +
            0.22d * Math.Sin(5d * angle + 1.10d + 2d * phase) +
            (0.28d + 0.34d * motion.BreathAmount) *
                Math.Sin(angle - 0.80d + phase) +
            motion.NewInsightAmount *
                (0.22d + 0.38d * Math.Sin(
                    3d * angle - phase));
    }

    private static double CreateOrganicBreathEnvelope(double progress)
    {
        if (progress < 0.34d)
        {
            return SmootherStep(progress / 0.34d);
        }

        if (progress < 0.44d)
        {
            return 1d - 0.025d *
                SmootherStep((progress - 0.34d) / 0.10d);
        }

        if (progress < 0.84d)
        {
            return 0.975d * (1d -
                SmootherStep((progress - 0.44d) / 0.40d));
        }

        return 0d;
    }

    private static double CreateWakeEnvelope(double progress)
    {
        if (progress < 0.42d)
        {
            return SmootherStep(progress / 0.42d);
        }

        return 1d - SmootherStep((progress - 0.42d) / 0.58d);
    }

    private static double SmootherStep(double value)
    {
        var t = Math.Clamp(value, 0d, 1d);
        return t * t * t * (t * (t * 6d - 15d) + 10d);
    }

    private static double WrapUnit(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0d;
        }

        var wrapped = value - Math.Floor(value);
        return wrapped < 0d ? wrapped + 1d : wrapped;
    }

    private static double BlendPostureValue(
        AmbientOrbBlendState blend,
        double stable,
        double attention,
        double warning,
        double critical,
        double unknown)
    {
        var attentionAmount = Math.Clamp(blend.AttentionAmount, 0d, 1d);
        var warningAmount = Math.Clamp(blend.WarningAmount, 0d, 1d);
        var criticalAmount = Math.Clamp(blend.CriticalAmount, 0d, 1d);
        var unknownAmount = Math.Clamp(blend.UnknownAmount, 0d, 1d);
        var stableAmount = Math.Max(
            0d,
            1d - attentionAmount - warningAmount - criticalAmount -
                unknownAmount);
        var total = stableAmount + attentionAmount + warningAmount +
            criticalAmount + unknownAmount;
        return total <= double.Epsilon
            ? stable
            : (stable * stableAmount + attention * attentionAmount +
                warning * warningAmount + critical * criticalAmount +
                unknown * unknownAmount) / total;
    }
}

public sealed class AmbientOrbFrameSequence
{
    public const int FramesPerSecond = 20;
    public const int FrameCount = 100;
    public const int WakeFrameCount = 40;
    public const int CanvasSize = 96;
    public const byte HitTestAlphaThreshold = 20;
    public const byte SilhouetteAlphaThreshold = 72;

    private readonly Lazy<AmbientOrbFrame[]> _frames;

    private AmbientOrbFrameSequence(
        CompactPresenceVisualMode mode,
        bool isHovered,
        AmbientOrbInsightModifier insightModifier)
    {
        Mode = mode;
        IsHovered = isHovered;
        InsightModifier = mode == CompactPresenceVisualMode.NewInsight &&
            insightModifier == AmbientOrbInsightModifier.None
                ? AmbientOrbInsightModifier.Wake
                : insightModifier;
        IsLooping = InsightModifier != AmbientOrbInsightModifier.Wake;
        _frames = new Lazy<AmbientOrbFrame[]>(
            CreateFrames,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public CompactPresenceVisualMode Mode { get; }

    public bool IsHovered { get; }

    public AmbientOrbInsightModifier InsightModifier { get; }

    public bool IsLooping { get; }

    public IReadOnlyList<AmbientOrbFrame> Frames => _frames.Value;

    public TimeSpan FrameInterval => TimeSpan.FromSeconds(
        1d / FramesPerSecond);

    public TimeSpan CycleDuration => FrameInterval * FrameCount;

    public int StaticFrameIndex => Math.Min(
        (int)Math.Round(
            AmbientOrbMotionModel.StaticCycleProgress * FrameCount),
        Frames.Count - 1);

    public static AmbientOrbFrameSequence Create(
        CompactPresenceVisualMode mode = CompactPresenceVisualMode.Stable,
        bool isHovered = false,
        AmbientOrbInsightModifier insightModifier =
            AmbientOrbInsightModifier.None) =>
        new(mode, isHovered, insightModifier);

    public AmbientOrbFrame GetFrame(int frameIndex, bool animationsEnabled)
    {
        if (!animationsEnabled)
        {
            return Frames[StaticFrameIndex];
        }

        return Frames[IsLooping
            ? PositiveModulo(frameIndex, Frames.Count)
            : Math.Clamp(frameIndex, 0, Frames.Count - 1)];
    }

    public void RenderInto(
        byte[] destination,
        double cycleProgress,
        bool animationsEnabled,
        double insightProgress = 0d,
        AmbientOrbBlendState? blendState = null,
        double slowDriftProgress =
            AmbientOrbMotionModel.StaticSlowDriftProgress)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Length != CanvasSize * CanvasSize * 4)
        {
            throw new ArgumentException(
                "The orb buffer must be one 96x96 BGRA surface.",
                nameof(destination));
        }

        AmbientOrbProceduralRenderer.Render(
            destination,
            Mode,
            animationsEnabled
                ? cycleProgress
                : AmbientOrbMotionModel.StaticCycleProgress,
            IsHovered ? 1d : 0d,
            !animationsEnabled &&
                InsightModifier == AmbientOrbInsightModifier.Wake
                    ? AmbientOrbInsightModifier.UnseenCue
                    : InsightModifier,
            insightProgress,
            reducedMotion: !animationsEnabled,
            blendState: blendState,
            slowDriftProgress: slowDriftProgress);
    }

    public bool IsHitTestVisible(int x, int y, int frameIndex = 0)
    {
        if (x < 0 || y < 0 || x >= CanvasSize || y >= CanvasSize)
        {
            return false;
        }

        return GetFrame(frameIndex, animationsEnabled: true).GetAlpha(x, y) >=
            HitTestAlphaThreshold;
    }

    public static bool IsNeutralStableColor(byte red, byte green, byte blue) =>
        green <= Math.Max(red, blue);

    public static int SilhouetteDifferencePixels(
        AmbientOrbFrame first,
        AmbientOrbFrame second,
        byte threshold = SilhouetteAlphaThreshold)
    {
        ValidateMatchingFrames(first, second);
        var count = 0;
        for (var pixel = 0; pixel < first.Width * first.Height; pixel++)
        {
            var offset = pixel * 4 + 3;
            if ((first.Pixels[offset] >= threshold) !=
                (second.Pixels[offset] >= threshold))
            {
                count++;
            }
        }

        return count;
    }

    public static int SilhouetteArea(
        AmbientOrbFrame frame,
        byte threshold = SilhouetteAlphaThreshold)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var count = 0;
        for (var pixel = 0; pixel < frame.Width * frame.Height; pixel++)
        {
            if (frame.Pixels[pixel * 4 + 3] >= threshold)
            {
                count++;
            }
        }

        return count;
    }

    public static double MeanAlphaDifference(
        AmbientOrbFrame first,
        AmbientOrbFrame second)
    {
        ValidateMatchingFrames(first, second);
        var total = 0d;
        var pixelCount = first.Width * first.Height;
        for (var pixel = 0; pixel < pixelCount; pixel++)
        {
            var offset = pixel * 4 + 3;
            total += Math.Abs(
                first.Pixels[offset] - second.Pixels[offset]);
        }

        return total / pixelCount;
    }

    public static double MeanLuminance(AmbientOrbFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var total = 0d;
        for (var pixel = 0; pixel < frame.Width * frame.Height; pixel++)
        {
            var offset = pixel * 4;
            total += 0.2126d * frame.Pixels[offset + 2] +
                0.7152d * frame.Pixels[offset + 1] +
                0.0722d * frame.Pixels[offset];
        }

        return total / (frame.Width * frame.Height);
    }

    public static double MeanAlpha(AmbientOrbFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var total = 0d;
        for (var pixel = 0; pixel < frame.Width * frame.Height; pixel++)
        {
            total += frame.Pixels[pixel * 4 + 3];
        }

        return total / (frame.Width * frame.Height);
    }

    private AmbientOrbFrame[] CreateFrames()
    {
        var count = IsLooping ? FrameCount : WakeFrameCount;
        var frames = new AmbientOrbFrame[count];
        for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            var cycleProgress = IsLooping
                ? frameIndex / (double)FrameCount
                : frameIndex / (double)WakeFrameCount;
            var insightProgress = frames.Length == 1
                ? 0d
                : frameIndex / (double)(frames.Length - 1);
            var pixels = new byte[CanvasSize * CanvasSize * 4];
            AmbientOrbProceduralRenderer.Render(
                pixels,
                Mode,
                cycleProgress,
                IsHovered ? 1d : 0d,
                InsightModifier,
                insightProgress,
                reducedMotion: false);
            frames[frameIndex] = new AmbientOrbFrame(
                CanvasSize,
                CanvasSize,
                pixels);
        }

        return frames;
    }

    private static void ValidateMatchingFrames(
        AmbientOrbFrame first,
        AmbientOrbFrame second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Width != second.Width || first.Height != second.Height)
        {
            throw new ArgumentException("Frame dimensions must match.");
        }
    }

    private static int PositiveModulo(int value, int modulus)
    {
        var remainder = value % modulus;
        return remainder < 0 ? remainder + modulus : remainder;
    }
}

internal static class AmbientOrbProceduralRenderer
{
    private static readonly PixelGeometry[] Geometry = CreateGeometry();

    public static void Render(
        byte[] pixels,
        CompactPresenceVisualMode mode,
        double cycleProgress,
        double hoverAmount,
        AmbientOrbInsightModifier insightModifier,
        double insightProgress,
        bool reducedMotion,
        AmbientOrbBlendState? blendState = null,
        double slowDriftProgress =
            AmbientOrbMotionModel.StaticSlowDriftProgress)
    {
        Array.Clear(pixels);
        var motion = blendState is { } blend
            ? AmbientOrbMotionModel.CreateForProgress(
                cycleProgress,
                NormalizePostureMode(mode),
                blend,
                insightModifier,
                insightProgress,
                reducedMotion,
                slowDriftProgress)
            : AmbientOrbMotionModel.CreateForProgress(
                cycleProgress,
                mode,
                hoverAmount,
                insightModifier,
                insightProgress,
                reducedMotion,
                slowDriftProgress);
        var profile = GetProfile(motion.BlendState);
        var phase2 = 0.35d + motion.DeformationPhase;
        var phase3 = -0.75d - motion.DeformationPhase;
        var phase5 = 1.10d + 2d * motion.DeformationPhase;
        var phase1 = -0.80d + motion.DeformationPhase;
        var wakePhase3 = -motion.DeformationPhase;
        var phase2Sin = Math.Sin(phase2);
        var phase2Cos = Math.Cos(phase2);
        var phase3Sin = Math.Sin(phase3);
        var phase3Cos = Math.Cos(phase3);
        var phase5Sin = Math.Sin(phase5);
        var phase5Cos = Math.Cos(phase5);
        var phase1Sin = Math.Sin(phase1);
        var phase1Cos = Math.Cos(phase1);
        var wake3Sin = Math.Sin(wakePhase3);
        var wake3Cos = Math.Cos(wakePhase3);
        var generatingPhase = Math.Tau * motion.CycleProgress;
        var generatingX = motion.CenterX + 8d * Math.Cos(generatingPhase);
        var generatingY = motion.CenterY + 6d * Math.Sin(generatingPhase);

        for (var index = 0; index < Geometry.Length; index++)
        {
            ref readonly var geometry = ref Geometry[index];
            var dx = geometry.X - motion.CenterX;
            var dy = geometry.Y - motion.CenterY;
            var radius = Math.Sqrt(dx * dx + dy * dy);
            if (radius > 35d)
            {
                continue;
            }

            var boundary = motion.BaseRadius + motion.Expansion +
                0.72d * Combine(
                    geometry.Sin2,
                    geometry.Cos2,
                    phase2Sin,
                    phase2Cos) +
                0.46d * Combine(
                    geometry.Sin3,
                    geometry.Cos3,
                    phase3Sin,
                    phase3Cos) +
                0.22d * Combine(
                    geometry.Sin5,
                    geometry.Cos5,
                    phase5Sin,
                    phase5Cos) +
                (0.28d + 0.34d * motion.BreathAmount) * Combine(
                    geometry.Sin1,
                    geometry.Cos1,
                    phase1Sin,
                    phase1Cos) +
                motion.NewInsightAmount * (0.22d + 0.38d * Combine(
                    geometry.Sin3,
                    geometry.Cos3,
                    wake3Sin,
                    wake3Cos));
            var signedDistance = radius - boundary;
            var bodyMask = 1d - SmoothStep(-1.15d, 1.15d, signedDistance);
            var haloDistance = Math.Max(0d, signedDistance);
            var halo = profile.HaloAlpha * Math.Exp(
                -0.5d * haloDistance * haloDistance / 10.5d) *
                SmoothStep(8.5d, -1.5d, signedDistance);
            if (bodyMask <= 0.0001d && halo <= 0.001d)
            {
                continue;
            }

            var red = 0d;
            var green = 0d;
            var blue = 0d;
            var alpha = 0d;
            AddLayer(ref red, ref green, ref blue, ref alpha,
                halo,
                profile.Halo);

            if (bodyMask > 0.0001d)
            {
                var normalizedX = dx / Math.Max(1d, boundary);
                var normalizedY = dy / Math.Max(1d, boundary);
                var lowerShadow = Math.Clamp(
                    0.42d + 0.25d * normalizedX +
                    0.32d * normalizedY,
                    0d,
                    1d);
                AddLayer(ref red, ref green, ref blue, ref alpha,
                    bodyMask * profile.BodyAlpha,
                    profile.Body);
                AddLayer(ref red, ref green, ref blue, ref alpha,
                    bodyMask * lowerShadow * profile.ShadowAlpha,
                    profile.Shadow);

                var broadLight = Gaussian(
                    geometry.X - (motion.HighlightX + 2.2d),
                    geometry.Y - (motion.HighlightY + 2.8d),
                    10.5d,
                    9.3d);
                var coreLight = Gaussian(
                    geometry.X - motion.HighlightX,
                    geometry.Y - motion.HighlightY,
                    5.2d,
                    4.5d);
                AddLayer(ref red, ref green, ref blue, ref alpha,
                    bodyMask * broadLight * profile.AccentAlpha *
                    (1d + 0.10d * motion.BreathAmount +
                        0.24d * motion.NewInsightAmount),
                    profile.Accent);
                AddLayer(ref red, ref green, ref blue, ref alpha,
                    bodyMask * coreLight * profile.CoreAlpha *
                    (1d + 0.12d * motion.HoverAmount +
                        0.30d * motion.NewInsightAmount),
                    profile.Core);

                var membrane = Math.Exp(
                    -0.5d * signedDistance * signedDistance / 1.15d) *
                    bodyMask;
                AddLayer(ref red, ref green, ref blue, ref alpha,
                    membrane * profile.MembraneAlpha,
                    profile.Membrane);

                if (motion.GeneratingAmount > 0.001d)
                {
                    var activity = Gaussian(
                        geometry.X - generatingX,
                        geometry.Y - generatingY,
                        4.8d,
                        3.5d);
                    AddLayer(ref red, ref green, ref blue, ref alpha,
                        bodyMask * activity * 0.30d *
                            motion.GeneratingAmount,
                        profile.Membrane);
                }

                if (motion.HasNewUnseenInsight)
                {
                    var cue = Gaussian(
                        geometry.X - (motion.CenterX + 10.2d),
                        geometry.Y - (motion.CenterY - 9.5d),
                        3.3d,
                        3.0d);
                    AddLayer(ref red, ref green, ref blue, ref alpha,
                        bodyMask * cue *
                            (0.16d + 0.22d * motion.NewInsightAmount),
                        profile.Cue);
                }
            }

            var offset = index * 4;
            pixels[offset] = ToByte(blue * 255d);
            pixels[offset + 1] = ToByte(green * 255d);
            pixels[offset + 2] = ToByte(red * 255d);
            pixels[offset + 3] = ToByte(alpha * 255d);
        }
    }

    private static PixelGeometry[] CreateGeometry()
    {
        var result = new PixelGeometry[
            AmbientOrbFrameSequence.CanvasSize *
            AmbientOrbFrameSequence.CanvasSize];
        for (var y = 0; y < AmbientOrbFrameSequence.CanvasSize; y++)
        {
            for (var x = 0; x < AmbientOrbFrameSequence.CanvasSize; x++)
            {
                var angle = Math.Atan2(y - 47.5d, x - 47.5d);
                var offset = y * AmbientOrbFrameSequence.CanvasSize + x;
                result[offset] = new(
                    x,
                    y,
                    Math.Sin(angle),
                    Math.Cos(angle),
                    Math.Sin(2d * angle),
                    Math.Cos(2d * angle),
                    Math.Sin(3d * angle),
                    Math.Cos(3d * angle),
                    Math.Sin(5d * angle),
                    Math.Cos(5d * angle));
            }
        }

        return result;
    }

    private static OrbProfile GetProfile(AmbientOrbBlendState blend)
    {
        var attention = Math.Clamp(blend.AttentionAmount, 0d, 1d);
        var warning = Math.Clamp(blend.WarningAmount, 0d, 1d);
        var critical = Math.Clamp(blend.CriticalAmount, 0d, 1d);
        var unknown = Math.Clamp(blend.UnknownAmount, 0d, 1d);
        var sum = attention + warning + critical + unknown;
        if (sum > 1d)
        {
            attention /= sum;
            warning /= sum;
            critical /= sum;
            unknown /= sum;
            sum = 1d;
        }

        var posture = BlendProfiles(
            GetProfileForMode(CompactPresenceVisualMode.Stable),
            1d - sum,
            GetProfileForMode(CompactPresenceVisualMode.Attention),
            attention,
            GetProfileForMode(CompactPresenceVisualMode.Warning),
            warning,
            GetProfileForMode(CompactPresenceVisualMode.Critical),
            critical,
            GetProfileForMode(CompactPresenceVisualMode.Unknown),
            unknown);
        return LerpProfile(
            posture,
            GetProfileForMode(CompactPresenceVisualMode.Generating),
            Math.Clamp(blend.GeneratingAmount, 0d, 1d) * 0.72d);
    }

    private static CompactPresenceVisualMode NormalizePostureMode(
        CompactPresenceVisualMode mode) => mode switch
        {
            CompactPresenceVisualMode.Generating or
            CompactPresenceVisualMode.NewInsight =>
                CompactPresenceVisualMode.Stable,
            _ => mode
        };

    private static OrbProfile GetProfileForMode(
        CompactPresenceVisualMode mode) => mode switch
    {
        CompactPresenceVisualMode.Attention => new(
            0.13d, 0.62d, 0.24d, 0.31d, 0.18d, 0.20d,
            new(108, 88, 68), new(151, 124, 91), new(67, 57, 53),
            new(193, 159, 112), new(236, 220, 190),
            new(246, 234, 211), new(217, 194, 151)),
        CompactPresenceVisualMode.Warning => new(
            0.14d, 0.66d, 0.25d, 0.33d, 0.20d, 0.22d,
            new(126, 64, 42), new(174, 91, 49), new(72, 42, 38),
            new(211, 128, 65), new(246, 211, 171),
            new(251, 229, 201), new(228, 166, 101)),
        CompactPresenceVisualMode.Critical => new(
            0.15d, 0.70d, 0.27d, 0.35d, 0.22d, 0.24d,
            new(112, 46, 40), new(157, 58, 46), new(65, 32, 36),
            new(197, 93, 62), new(241, 193, 164),
            new(249, 220, 197), new(218, 135, 92)),
        CompactPresenceVisualMode.Unknown => new(
            0.08d, 0.45d, 0.18d, 0.23d, 0.13d, 0.13d,
            new(68, 76, 88), new(91, 103, 116), new(45, 52, 63),
            new(123, 136, 151), new(185, 197, 207),
            new(206, 216, 223), new(156, 177, 188)),
        CompactPresenceVisualMode.Generating => new(
            0.12d, 0.59d, 0.23d, 0.32d, 0.18d, 0.20d,
            new(68, 88, 104), new(91, 121, 137), new(43, 55, 69),
            new(119, 153, 168), new(196, 218, 225),
            new(230, 239, 242), new(158, 203, 208)),
        _ => new(
            0.11d, 0.56d, 0.21d, 0.29d, 0.16d, 0.17d,
            new(66, 78, 94), new(92, 112, 137), new(43, 50, 65),
            new(121, 142, 167), new(190, 207, 225),
            new(232, 239, 247), new(158, 190, 209))
    };

    private static OrbProfile BlendProfiles(
        OrbProfile stable,
        double stableAmount,
        OrbProfile attention,
        double attentionAmount,
        OrbProfile warning,
        double warningAmount,
        OrbProfile critical,
        double criticalAmount,
        OrbProfile unknown,
        double unknownAmount)
    {
        return new(
            BlendValue(stable.HaloAlpha, attention.HaloAlpha,
                warning.HaloAlpha, critical.HaloAlpha,
                unknown.HaloAlpha),
            BlendValue(stable.BodyAlpha, attention.BodyAlpha,
                warning.BodyAlpha, critical.BodyAlpha,
                unknown.BodyAlpha),
            BlendValue(stable.ShadowAlpha, attention.ShadowAlpha,
                warning.ShadowAlpha, critical.ShadowAlpha,
                unknown.ShadowAlpha),
            BlendValue(stable.AccentAlpha, attention.AccentAlpha,
                warning.AccentAlpha, critical.AccentAlpha,
                unknown.AccentAlpha),
            BlendValue(stable.CoreAlpha, attention.CoreAlpha,
                warning.CoreAlpha, critical.CoreAlpha,
                unknown.CoreAlpha),
            BlendValue(stable.MembraneAlpha, attention.MembraneAlpha,
                warning.MembraneAlpha, critical.MembraneAlpha,
                unknown.MembraneAlpha),
            BlendColor(stable.Halo, attention.Halo, warning.Halo,
                critical.Halo, unknown.Halo),
            BlendColor(stable.Body, attention.Body, warning.Body,
                critical.Body, unknown.Body),
            BlendColor(stable.Shadow, attention.Shadow, warning.Shadow,
                critical.Shadow, unknown.Shadow),
            BlendColor(stable.Accent, attention.Accent, warning.Accent,
                critical.Accent, unknown.Accent),
            BlendColor(stable.Core, attention.Core, warning.Core,
                critical.Core, unknown.Core),
            BlendColor(stable.Membrane, attention.Membrane,
                warning.Membrane, critical.Membrane, unknown.Membrane),
            BlendColor(stable.Cue, attention.Cue, warning.Cue,
                critical.Cue, unknown.Cue));

        double BlendValue(
            double stableValue,
            double attentionValue,
            double warningValue,
            double criticalValue,
            double unknownValue) =>
            stableValue * stableAmount +
            attentionValue * attentionAmount +
            warningValue * warningAmount +
            criticalValue * criticalAmount +
            unknownValue * unknownAmount;

        OrbColor BlendColor(
            OrbColor stableColor,
            OrbColor attentionColor,
            OrbColor warningColor,
            OrbColor criticalColor,
            OrbColor unknownColor) => new(
                ToByte(BlendValue(
                    stableColor.Red,
                    attentionColor.Red,
                    warningColor.Red,
                    criticalColor.Red,
                    unknownColor.Red)),
                ToByte(BlendValue(
                    stableColor.Green,
                    attentionColor.Green,
                    warningColor.Green,
                    criticalColor.Green,
                    unknownColor.Green)),
                ToByte(BlendValue(
                    stableColor.Blue,
                    attentionColor.Blue,
                    warningColor.Blue,
                    criticalColor.Blue,
                    unknownColor.Blue)));
    }

    private static OrbProfile LerpProfile(
        OrbProfile from,
        OrbProfile to,
        double amount)
    {
        var t = Math.Clamp(amount, 0d, 1d);
        return new(
            LerpValue(from.HaloAlpha, to.HaloAlpha),
            LerpValue(from.BodyAlpha, to.BodyAlpha),
            LerpValue(from.ShadowAlpha, to.ShadowAlpha),
            LerpValue(from.AccentAlpha, to.AccentAlpha),
            LerpValue(from.CoreAlpha, to.CoreAlpha),
            LerpValue(from.MembraneAlpha, to.MembraneAlpha),
            LerpColor(from.Halo, to.Halo),
            LerpColor(from.Body, to.Body),
            LerpColor(from.Shadow, to.Shadow),
            LerpColor(from.Accent, to.Accent),
            LerpColor(from.Core, to.Core),
            LerpColor(from.Membrane, to.Membrane),
            LerpColor(from.Cue, to.Cue));

        double LerpValue(double first, double second) =>
            first + (second - first) * t;

        OrbColor LerpColor(OrbColor first, OrbColor second) => new(
            ToByte(LerpValue(first.Red, second.Red)),
            ToByte(LerpValue(first.Green, second.Green)),
            ToByte(LerpValue(first.Blue, second.Blue)));
    }

    private static double Combine(
        double sinAngle,
        double cosAngle,
        double sinPhase,
        double cosPhase) =>
        sinAngle * cosPhase + cosAngle * sinPhase;

    private static double Gaussian(
        double x,
        double y,
        double horizontalRadius,
        double verticalRadius) => Math.Exp(-0.5d * (
            x * x / (horizontalRadius * horizontalRadius) +
            y * y / (verticalRadius * verticalRadius)));

    private static double SmoothStep(
        double edge0,
        double edge1,
        double value)
    {
        var t = Math.Clamp(
            (value - edge0) / (edge1 - edge0),
            0d,
            1d);
        return t * t * (3d - 2d * t);
    }

    private static void AddLayer(
        ref double red,
        ref double green,
        ref double blue,
        ref double alpha,
        double layerAlpha,
        OrbColor color)
    {
        var sourceAlpha = Math.Clamp(layerAlpha, 0d, 1d);
        var inverseAlpha = 1d - sourceAlpha;
        red = color.Red / 255d * sourceAlpha + red * inverseAlpha;
        green = color.Green / 255d * sourceAlpha + green * inverseAlpha;
        blue = color.Blue / 255d * sourceAlpha + blue * inverseAlpha;
        alpha = sourceAlpha + alpha * inverseAlpha;
    }

    private static byte ToByte(double value) =>
        (byte)Math.Clamp(Math.Round(value), 0d, 255d);

    private readonly record struct PixelGeometry(
        double X,
        double Y,
        double Sin1,
        double Cos1,
        double Sin2,
        double Cos2,
        double Sin3,
        double Cos3,
        double Sin5,
        double Cos5);

    private readonly record struct OrbColor(byte Red, byte Green, byte Blue);

    private readonly record struct OrbProfile(
        double HaloAlpha,
        double BodyAlpha,
        double ShadowAlpha,
        double AccentAlpha,
        double CoreAlpha,
        double MembraneAlpha,
        OrbColor Halo,
        OrbColor Body,
        OrbColor Shadow,
        OrbColor Accent,
        OrbColor Core,
        OrbColor Membrane,
        OrbColor Cue);
}

public sealed class AmbientOrbFrame
{
    public AmbientOrbFrame(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != width * height * 4)
        {
            throw new ArgumentException(
                "Pixels must be premultiplied BGRA.",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public byte GetAlpha(int x, int y) =>
        Pixels[(y * Width + x) * 4 + 3];

    public (byte Blue, byte Green, byte Red, byte Alpha) GetPixel(
        int x,
        int y)
    {
        var offset = (y * Width + x) * 4;
        return (
            Pixels[offset],
            Pixels[offset + 1],
            Pixels[offset + 2],
            Pixels[offset + 3]);
    }
}
