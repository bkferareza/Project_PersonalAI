namespace Machine.Core;

public enum MachineNetworkActivityClass
{
    Unavailable,
    Quiet,
    Light,
    Active
}

public static class MachineNetworkActivityClassifier
{
    public const double QuietUpperBoundBytesPerSecond = 16d * 1024d;
    public const double LightUpperBoundBytesPerSecond = 1024d * 1024d;
    public const int MinimumDominantObservationCount = 12;

    public static MachineNetworkActivityClass Classify(
        double? receiveBytesPerSecond,
        double? sendBytesPerSecond)
    {
        if (!IsAvailableRate(receiveBytesPerSecond) ||
            !IsAvailableRate(sendBytesPerSecond))
        {
            return MachineNetworkActivityClass.Unavailable;
        }

        var combinedBytesPerSecond =
            receiveBytesPerSecond.GetValueOrDefault() +
            sendBytesPerSecond.GetValueOrDefault();
        if (!double.IsFinite(combinedBytesPerSecond))
        {
            return MachineNetworkActivityClass.Unavailable;
        }

        return combinedBytesPerSecond < QuietUpperBoundBytesPerSecond
            ? MachineNetworkActivityClass.Quiet
            : combinedBytesPerSecond < LightUpperBoundBytesPerSecond
                ? MachineNetworkActivityClass.Light
                : MachineNetworkActivityClass.Active;
    }

    public static MachineNetworkActivityClass? SelectDominant(
        long quietCount,
        long lightCount,
        long activeCount,
        int minimumObservationCount = MinimumDominantObservationCount)
    {
        if (quietCount < 0 || lightCount < 0 || activeCount < 0 ||
            minimumObservationCount <= 0)
        {
            return null;
        }

        var total = SaturatingAdd(
            SaturatingAdd(quietCount, lightCount),
            activeCount);
        if (total < minimumObservationCount)
        {
            return null;
        }

        var maximum = Math.Max(quietCount, Math.Max(lightCount, activeCount));
        var maximumCount = (quietCount == maximum ? 1 : 0) +
            (lightCount == maximum ? 1 : 0) +
            (activeCount == maximum ? 1 : 0);
        if (maximumCount != 1)
        {
            return null;
        }

        return maximum == quietCount
            ? MachineNetworkActivityClass.Quiet
            : maximum == lightCount
                ? MachineNetworkActivityClass.Light
                : MachineNetworkActivityClass.Active;
    }

    public static long GetCount(
        MachineNetworkActivityClass activityClass,
        long quietCount,
        long lightCount,
        long activeCount) => activityClass switch
        {
            MachineNetworkActivityClass.Quiet => quietCount,
            MachineNetworkActivityClass.Light => lightCount,
            MachineNetworkActivityClass.Active => activeCount,
            _ => 0
        };

    private static bool IsAvailableRate(double? value) =>
        value is not null && double.IsFinite(value.Value) && value.Value >= 0d;

    private static long SaturatingAdd(long left, long right) =>
        left >= long.MaxValue - right ? long.MaxValue : left + right;
}
