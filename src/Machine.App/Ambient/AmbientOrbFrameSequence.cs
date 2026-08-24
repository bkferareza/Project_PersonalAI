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
    double BreathAmount,
    double BaseRadius,
    double Expansion,
    double DeformationPhase,
    double CenterX,
    double CenterY,
    double HighlightX,
    double HighlightY,
    double HoverAmount,
    double NewInsightAmount,
    bool HasNewUnseenInsight,
    bool ReducedMotion);

public static class AmbientOrbMotionModel
{
    public static readonly TimeSpan StableCycleDuration =
        TimeSpan.FromSeconds(5);
    public const double StaticCycleProgress = 0.16d;

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
        return CreateForProgress(
            progress,
            postureMode,
            hoverAmount,
            insightModifier,
            insightProgress,
            reducedMotion);
    }

    public static AmbientOrbMotionParameters CreateForProgress(
        double cycleProgress,
        CompactPresenceVisualMode postureMode,
        double hoverAmount = 0d,
        AmbientOrbInsightModifier insightModifier =
            AmbientOrbInsightModifier.None,
        double insightProgress = 0d,
        bool reducedMotion = false)
    {
        var mode = postureMode == CompactPresenceVisualMode.NewInsight
            ? CompactPresenceVisualMode.Stable
            : postureMode;
        var progress = reducedMotion
            ? StaticCycleProgress
            : WrapUnit(cycleProgress);
        var phase = Math.Tau * progress;
        var breath = CreateOrganicBreathEnvelope(progress);
        var hover = Math.Clamp(hoverAmount, 0d, 1d);
        var wake = insightModifier == AmbientOrbInsightModifier.Wake &&
            !reducedMotion
                ? CreateWakeEnvelope(Math.Clamp(insightProgress, 0d, 1d))
                : 0d;
        var baseRadius = mode switch
        {
            CompactPresenceVisualMode.Critical => 21.65d,
            CompactPresenceVisualMode.Warning => 21.55d,
            CompactPresenceVisualMode.Attention => 21.5d,
            CompactPresenceVisualMode.Unknown => 21.1d,
            _ => 21.4d
        };
        var breathExpansion = mode switch
        {
            CompactPresenceVisualMode.Critical => 0.95d,
            CompactPresenceVisualMode.Warning => 1.08d,
            CompactPresenceVisualMode.Attention => 1.25d,
            CompactPresenceVisualMode.Unknown => 0.85d,
            _ => 1.45d
        };
        var expansion = breathExpansion * breath +
            0.18d * hover + 0.82d * wake;
        var drift = 0.52d * Math.Sin(phase * 0.72d + 0.35d) +
            0.17d * Math.Sin(phase * 1.31d - 0.8d);
        var centerX = 47.5d + drift + 0.10d * hover;
        var centerY = 47.5d - 0.46d * breath +
            0.20d * Math.Sin(phase * 0.63d - 0.4d) -
            0.18d * wake;
        var highlightX = centerX - 5.3d - 0.62d * breath -
            0.55d * wake;
        var highlightY = centerY - 5.8d -
            0.35d * Math.Sin(phase * 0.78d + 0.2d) -
            0.45d * wake;

        return new(
            mode,
            progress,
            breath,
            baseRadius,
            expansion,
            phase + 0.24d * Math.Sin(phase * 0.57d),
            centerX,
            centerY,
            highlightX,
            highlightY,
            hover,
            wake,
            insightModifier != AmbientOrbInsightModifier.None,
            reducedMotion);
    }

    public static double GetContourRadius(
        double angle,
        AmbientOrbMotionParameters motion)
    {
        var phase = motion.DeformationPhase;
        return motion.BaseRadius + motion.Expansion +
            0.72d * Math.Sin(2d * angle + 0.35d + phase * 0.22d) +
            0.46d * Math.Sin(3d * angle - 0.75d - phase * 0.17d) +
            0.22d * Math.Sin(5d * angle + 1.10d + phase * 0.11d) +
            (0.28d + 0.34d * motion.BreathAmount) *
                Math.Sin(angle - 0.80d + phase * 0.31d) +
            motion.NewInsightAmount *
                (0.22d + 0.38d * Math.Sin(
                    3d * angle - phase * 0.35d));
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
        double insightProgress = 0d)
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
            reducedMotion: !animationsEnabled);
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
        bool reducedMotion)
    {
        Array.Clear(pixels);
        var motion = AmbientOrbMotionModel.CreateForProgress(
            cycleProgress,
            mode,
            hoverAmount,
            insightModifier,
            insightProgress,
            reducedMotion);
        var profile = GetProfile(motion.PostureMode);
        var phase2 = 0.35d + motion.DeformationPhase * 0.22d;
        var phase3 = -0.75d - motion.DeformationPhase * 0.17d;
        var phase5 = 1.10d + motion.DeformationPhase * 0.11d;
        var phase1 = -0.80d + motion.DeformationPhase * 0.31d;
        var wakePhase3 = -motion.DeformationPhase * 0.35d;
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
        var generatingPhase = Math.Tau * motion.CycleProgress * 0.82d;
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

                if (motion.PostureMode ==
                    CompactPresenceVisualMode.Generating)
                {
                    var activity = Gaussian(
                        geometry.X - generatingX,
                        geometry.Y - generatingY,
                        4.8d,
                        3.5d);
                    AddLayer(ref red, ref green, ref blue, ref alpha,
                        bodyMask * activity * 0.30d,
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

    private static OrbProfile GetProfile(
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
