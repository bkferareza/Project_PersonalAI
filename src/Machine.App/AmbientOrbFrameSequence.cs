namespace Machine.App;

public sealed class AmbientOrbFrameSequence
{
    public const int FramesPerSecond = 10;
    public const int FrameCount = 50;
    public const int CanvasSize = 96;
    private const byte HitTestAlphaThreshold = 20;
    private static readonly IReadOnlyDictionary<SequenceKey, AmbientOrbFrameSequence>
        Sequences = CreateSequences();

    private AmbientOrbFrameSequence(
        CompactPresenceVisualMode mode,
        bool isHovered,
        bool isLooping,
        AmbientOrbFrame[] frames)
    {
        Mode = mode;
        IsHovered = isHovered;
        IsLooping = isLooping;
        Frames = frames;
    }

    public CompactPresenceVisualMode Mode { get; }

    public bool IsHovered { get; }

    public bool IsLooping { get; }

    public IReadOnlyList<AmbientOrbFrame> Frames { get; }

    public TimeSpan FrameInterval => TimeSpan.FromSeconds(1d / FramesPerSecond);

    public int StaticFrameIndex => Mode == CompactPresenceVisualMode.NewInsight
        ? Frames.Count / 2
        : 0;

    public static AmbientOrbFrameSequence Create(
        CompactPresenceVisualMode mode = CompactPresenceVisualMode.Stable,
        bool isHovered = false) => Sequences[new SequenceKey(mode, isHovered)];

    public AmbientOrbFrame GetFrame(int frameIndex, bool animationsEnabled)
    {
        if (!animationsEnabled)
        {
            return Frames[StaticFrameIndex];
        }

        return Frames[IsLooping
            ? Math.Abs(frameIndex) % Frames.Count
            : Math.Clamp(frameIndex, 0, Frames.Count - 1)];
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

    public static double MeanAlphaDifference(
        AmbientOrbFrame first,
        AmbientOrbFrame second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (first.Width != second.Width || first.Height != second.Height)
        {
            throw new ArgumentException("Frame dimensions must match.");
        }

        var total = 0d;
        var pixelCount = first.Width * first.Height;
        for (var y = 0; y < first.Height; y++)
        {
            for (var x = 0; x < first.Width; x++)
            {
                total += Math.Abs(first.GetAlpha(x, y) - second.GetAlpha(x, y));
            }
        }

        return total / pixelCount;
    }

    public static double MeanLuminance(AmbientOrbFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var total = 0d;
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var pixel = frame.GetPixel(x, y);
                total += 0.2126d * pixel.Red + 0.7152d * pixel.Green +
                    0.0722d * pixel.Blue;
            }
        }

        return total / (frame.Width * frame.Height);
    }

    public static double MeanAlpha(AmbientOrbFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var total = 0d;
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                total += frame.GetAlpha(x, y);
            }
        }

        return total / (frame.Width * frame.Height);
    }

    private static IReadOnlyDictionary<SequenceKey, AmbientOrbFrameSequence>
        CreateSequences()
    {
        var sequences = new Dictionary<SequenceKey, AmbientOrbFrameSequence>();
        foreach (var mode in Enum.GetValues<CompactPresenceVisualMode>())
        {
            sequences.Add(new SequenceKey(mode, false), CreateSequence(mode, false));
            sequences.Add(new SequenceKey(mode, true), CreateSequence(mode, true));
        }

        return sequences;
    }

    private static AmbientOrbFrameSequence CreateSequence(
        CompactPresenceVisualMode mode,
        bool isHovered)
    {
        var profile = GetProfile(mode);
        var frames = new AmbientOrbFrame[profile.FrameCount];
        for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            var progress = frames.Length == 1
                ? 0d
                : frameIndex / (double)frames.Length;
            frames[frameIndex] = new AmbientOrbFrame(
                CanvasSize,
                CanvasSize,
                RenderFrame(profile, progress, isHovered));
        }

        return new AmbientOrbFrameSequence(
            mode,
            isHovered,
            profile.IsLooping,
            frames);
    }

    private static byte[] RenderFrame(
        OrbProfile profile,
        double progress,
        bool isHovered)
    {
        var pixels = new byte[CanvasSize * CanvasSize * 4];
        var phase = Math.Tau * progress;
        var breathing = 0.5d - 0.5d * Math.Cos(phase);
        var bloom = profile.IsBloom ? Math.Sin(Math.PI * progress) : 0d;
        var intensity = (isHovered ? 1.08d : 1d) *
            (1d + profile.BreathIntensity * breathing + 0.32d * bloom);
        var scale = (isHovered ? 1.01d : 1d) *
            (1d + profile.BreathScale * breathing + 0.12d * bloom);
        var driftX = profile.Drift * Math.Sin(phase + 0.7d);
        var driftY = profile.Drift * 0.68d * Math.Sin(phase * 0.75d - 0.4d);

        for (var y = 0; y < CanvasSize; y++)
        {
            for (var x = 0; x < CanvasSize; x++)
            {
                var dx = x - CanvasSize / 2d;
                var dy = y - CanvasSize / 2d;
                if (dx * dx + dy * dy > 33d * 33d)
                {
                    continue;
                }

                var red = 0d;
                var green = 0d;
                var blue = 0d;
                var alpha = 0d;
                AddLayer(ref red, ref green, ref blue, ref alpha,
                    profile.OuterAlpha * intensity * Gaussian(
                        dx - driftX, dy - driftY, 16d * scale, 14d * scale),
                    profile.Outer);
                AddLayer(ref red, ref green, ref blue, ref alpha,
                    profile.EnergyAlpha * intensity * Gaussian(
                        dx + 5d - driftX,
                        dy - 2d - driftY,
                        11d * scale,
                        8d * scale),
                    profile.Energy);
                AddLayer(ref red, ref green, ref blue, ref alpha,
                    profile.EnergyAlpha * 1.05d * intensity * Gaussian(
                        dx - 5d - driftX,
                        dy + 5d - driftY,
                        9d * scale,
                        12d * scale),
                    profile.Accent);
                AddLayer(ref red, ref green, ref blue, ref alpha,
                    profile.CoreAlpha * intensity * Gaussian(
                        dx - driftX * 0.45d,
                        dy - driftY * 0.45d,
                        8.5d * scale,
                        8d * scale),
                    profile.Core);
                AddLayer(ref red, ref green, ref blue, ref alpha,
                    profile.HotAlpha * intensity * Gaussian(
                        dx + 2.5d - driftX * 0.35d,
                        dy + 3d - driftY * 0.35d,
                        4.5d * scale,
                        4d * scale),
                    profile.Hot);

                AddBrokenArc(ref red, ref green, ref blue, ref alpha,
                    dx, dy, phase, profile, intensity);

                if (profile.HasGeneratingSweep)
                {
                    var sweepAngle = phase * 1.7d - 0.8d;
                    var sweepX = 18d * Math.Cos(sweepAngle);
                    var sweepY = 12d * Math.Sin(sweepAngle);
                    AddLayer(ref red, ref green, ref blue, ref alpha,
                        0.38d * intensity * Gaussian(
                            dx - sweepX,
                            dy - sweepY,
                            8d,
                            4d),
                        new OrbColor(156, 238, 255));
                }

                var offset = (y * CanvasSize + x) * 4;
                pixels[offset] = ToByte(blue * 255d);
                pixels[offset + 1] = ToByte(green * 255d);
                pixels[offset + 2] = ToByte(red * 255d);
                pixels[offset + 3] = ToByte(alpha * 255d);
            }
        }

        return pixels;
    }

    private static void AddBrokenArc(
        ref double red,
        ref double green,
        ref double blue,
        ref double alpha,
        double dx,
        double dy,
        double phase,
        OrbProfile profile,
        double intensity)
    {
        if (profile.ArcAlpha <= 0d)
        {
            return;
        }

        var radius = Math.Sqrt(dx * dx + dy * dy);
        var angle = Math.Atan2(dy, dx);
        var shiftedAngle = angle + 0.22d * Math.Sin(phase);
        if (radius is > 25d and < 28d &&
            shiftedAngle is > 2.35d and < 4.18d)
        {
            var arcStrength = profile.ArcAlpha * intensity *
                (1d - Math.Abs(radius - 26.5d) / 1.5d) *
                Math.Sin((shiftedAngle - 2.35d) / 1.83d * Math.PI);
            AddLayer(ref red, ref green, ref blue, ref alpha,
                arcStrength, profile.Arc);
        }
    }

    private static OrbProfile GetProfile(CompactPresenceVisualMode mode) => mode switch
    {
        CompactPresenceVisualMode.Attention => new(
            38, true, 0.10d, 0.12d, 1.5d, 0.16d, 0.17d, 0.50d, 0.43d, 0.05d,
            new(75, 135, 247), new(73, 208, 255), new(255, 187, 104),
            new(166, 232, 255), new(248, 253, 255), new(199, 235, 255)),
        CompactPresenceVisualMode.Warning => new(
            28, true, 0.11d, 0.13d, 1.2d, 0.19d, 0.22d, 0.62d, 0.55d, 0.07d,
            new(240, 112, 47), new(255, 151, 53), new(255, 190, 91),
            new(255, 191, 108), new(255, 246, 220), new(255, 181, 88)),
        CompactPresenceVisualMode.Critical => new(
            20, true, 0.12d, 0.14d, 1d, 0.23d, 0.26d, 0.68d, 0.59d, 0.08d,
            new(226, 66, 48), new(255, 84, 50), new(255, 139, 62),
            new(255, 157, 92), new(255, 236, 210), new(255, 119, 71)),
        CompactPresenceVisualMode.Unknown => new(
            50, true, 0.05d, 0.06d, 0.6d, 0.08d, 0.08d, 0.25d, 0.22d, 0.02d,
            new(87, 111, 142), new(100, 130, 157), new(117, 128, 145),
            new(154, 171, 184), new(209, 220, 226), new(147, 167, 185)),
        CompactPresenceVisualMode.Generating => new(
            40, true, 0.10d, 0.12d, 1.4d, 0.16d, 0.17d, 0.48d, 0.40d, 0.05d,
            new(73, 125, 240), new(69, 208, 255), new(104, 89, 226),
            new(151, 226, 255), new(246, 253, 255), new(187, 235, 255),
            HasGeneratingSweep: true),
        CompactPresenceVisualMode.NewInsight => new(
            10, false, 0d, 0d, 1d, 0.19d, 0.22d, 0.62d, 0.54d, 0.07d,
            new(92, 125, 248), new(81, 222, 255), new(148, 101, 244),
            new(188, 239, 255), new(255, 255, 255), new(211, 238, 255),
            IsBloom: true),
        _ => new(
            FrameCount, true, 0.14d, 0.15d, 1.2d, 0.11d, 0.14d, 0.45d, 0.38d, 0d,
            new(83, 124, 241), new(76, 219, 255), new(108, 76, 222),
            new(156, 224, 255), new(244, 252, 255), new(193, 238, 255))
    };

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

    private static double Gaussian(
        double x,
        double y,
        double horizontalRadius,
        double verticalRadius) => Math.Exp(-0.5d * (
            x * x / (horizontalRadius * horizontalRadius) +
            y * y / (verticalRadius * verticalRadius)));

    private static byte ToByte(double value) =>
        (byte)Math.Clamp(Math.Round(value), 0d, 255d);

    private readonly record struct SequenceKey(
        CompactPresenceVisualMode Mode,
        bool IsHovered);

    private readonly record struct OrbColor(byte Red, byte Green, byte Blue);

    private sealed record OrbProfile(
        int FrameCount,
        bool IsLooping,
        double BreathScale,
        double BreathIntensity,
        double Drift,
        double OuterAlpha,
        double EnergyAlpha,
        double CoreAlpha,
        double HotAlpha,
        double ArcAlpha,
        OrbColor Outer,
        OrbColor Energy,
        OrbColor Accent,
        OrbColor Core,
        OrbColor Hot,
        OrbColor Arc,
        bool HasGeneratingSweep = false,
        bool IsBloom = false);
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
            throw new ArgumentException("Pixels must be premultiplied BGRA.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public byte GetAlpha(int x, int y) => Pixels[(y * Width + x) * 4 + 3];

    public (byte Blue, byte Green, byte Red, byte Alpha) GetPixel(int x, int y)
    {
        var offset = (y * Width + x) * 4;
        return (Pixels[offset], Pixels[offset + 1], Pixels[offset + 2], Pixels[offset + 3]);
    }
}
