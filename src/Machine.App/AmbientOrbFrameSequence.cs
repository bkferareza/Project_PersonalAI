namespace Machine.App;

public sealed class AmbientOrbFrameSequence
{
    public const int FramesPerSecond = 10;
    public const int FrameCount = 48;
    public const int CanvasSize = 128;
    private const byte HitTestAlphaThreshold = 20;

    private AmbientOrbFrameSequence(AmbientOrbFrame[] frames)
    {
        Frames = frames;
    }

    public IReadOnlyList<AmbientOrbFrame> Frames { get; }

    public TimeSpan FrameInterval =>
        TimeSpan.FromSeconds(1d / FramesPerSecond);

    public static AmbientOrbFrameSequence Create(bool isHovered = false)
    {
        var frames = new AmbientOrbFrame[FrameCount];
        var intensity = isHovered ? 1.14d : 1d;

        for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            var phase = Math.Tau * frameIndex / frames.Length;
            frames[frameIndex] = new AmbientOrbFrame(
                CanvasSize,
                CanvasSize,
                RenderFrame(phase, intensity));
        }

        return new AmbientOrbFrameSequence(frames);
    }

    public bool IsHitTestVisible(int x, int y)
    {
        if (x < 0 || y < 0 || x >= CanvasSize || y >= CanvasSize)
        {
            return false;
        }

        return Frames[0].GetAlpha(x, y) >= HitTestAlphaThreshold;
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

    private static byte[] RenderFrame(double phase, double intensity)
    {
        var pixels = new byte[CanvasSize * CanvasSize * 4];
        var breathing = 0.5d + 0.5d * Math.Sin(phase);
        var driftX = 1.8d * Math.Sin(phase + 0.7d);
        var driftY = 1.2d * Math.Sin(phase * 0.75d - 0.4d);
        var coreScale = 1d + 0.055d * breathing;

        for (var y = 0; y < CanvasSize; y++)
        {
            for (var x = 0; x < CanvasSize; x++)
            {
                var dx = x - 64d;
                var dy = y - 64d;
                if (dx * dx + dy * dy > 58d * 58d)
                {
                    continue;
                }

                var red = 0d;
                var green = 0d;
                var blue = 0d;
                var alpha = 0d;

                AddLayer(
                    ref red,
                    ref green,
                    ref blue,
                    ref alpha,
                    0.31d * intensity * (0.86d + 0.14d * breathing) *
                        Gaussian(dx - driftX, dy - driftY, 31d, 28d),
                    83d,
                    124d,
                    241d);
                AddLayer(
                    ref red,
                    ref green,
                    ref blue,
                    ref alpha,
                    0.28d * intensity * Gaussian(
                        dx + 8d - driftX,
                        dy - 4d - driftY,
                        23d,
                        16d),
                    76d,
                    219d,
                    255d);
                AddLayer(
                    ref red,
                    ref green,
                    ref blue,
                    ref alpha,
                    0.35d * intensity * Gaussian(
                        dx - 7d - driftX,
                        dy + 8d - driftY,
                        18d,
                        24d),
                    108d,
                    76d,
                    222d);
                AddLayer(
                    ref red,
                    ref green,
                    ref blue,
                    ref alpha,
                    0.78d * intensity * Gaussian(
                        dx - driftX * 0.45d,
                        dy - driftY * 0.45d,
                        13d * coreScale,
                        12d * coreScale),
                    156d,
                    224d,
                    255d);
                AddLayer(
                    ref red,
                    ref green,
                    ref blue,
                    ref alpha,
                    0.7d * intensity * Gaussian(
                        dx + 4d - driftX * 0.35d,
                        dy + 5d - driftY * 0.35d,
                        7d * coreScale,
                        6d * coreScale),
                    244d,
                    252d,
                    255d);

                var radius = Math.Sqrt(dx * dx + dy * dy);
                var angle = Math.Atan2(dy, dx);
                if (radius is > 41d and < 45d && angle is > 2.45d and < 4.05d)
                {
                    var arcStrength = 0.16d * intensity *
                        (1d - Math.Abs(radius - 43d) / 2d) *
                        Math.Sin((angle - 2.45d) / 1.6d * Math.PI);
                    AddLayer(
                        ref red,
                        ref green,
                        ref blue,
                        ref alpha,
                        arcStrength,
                        193d,
                        238d,
                        255d);
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

    private static void AddLayer(
        ref double red,
        ref double green,
        ref double blue,
        ref double alpha,
        double layerAlpha,
        double layerRed,
        double layerGreen,
        double layerBlue)
    {
        var sourceAlpha = Math.Clamp(layerAlpha, 0d, 1d);
        var inverseAlpha = 1d - sourceAlpha;
        red = layerRed / 255d * sourceAlpha + red * inverseAlpha;
        green = layerGreen / 255d * sourceAlpha + green * inverseAlpha;
        blue = layerBlue / 255d * sourceAlpha + blue * inverseAlpha;
        alpha = sourceAlpha + alpha * inverseAlpha;
    }

    private static double Gaussian(
        double x,
        double y,
        double horizontalRadius,
        double verticalRadius) =>
        Math.Exp(-0.5d * (
            x * x / (horizontalRadius * horizontalRadius) +
            y * y / (verticalRadius * verticalRadius)));

    private static byte ToByte(double value) =>
        (byte)Math.Clamp(Math.Round(value), 0d, 255d);
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

    public byte GetAlpha(int x, int y) =>
        Pixels[(y * Width + x) * 4 + 3];

    public (byte Blue, byte Green, byte Red, byte Alpha) GetPixel(int x, int y)
    {
        var offset = (y * Width + x) * 4;
        return (
            Pixels[offset],
            Pixels[offset + 1],
            Pixels[offset + 2],
            Pixels[offset + 3]);
    }
}
