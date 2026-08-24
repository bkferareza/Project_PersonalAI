namespace Machine.Core;

public static class MachineLearningPolicy
{
    // Current CPU and memory behavior gives half of its weight to evidence
    // newer than this elapsed wall-clock interval. Lifetime Welford evidence
    // is retained separately and is never decayed.
    public static readonly TimeSpan AdaptiveHalfLife =
        TimeSpan.FromDays(21);

    // Freshness describes recency only; it never changes finding severity.
    public static readonly TimeSpan FreshMaximumAge =
        TimeSpan.FromDays(7);
    public static readonly TimeSpan AgingMaximumAge =
        TimeSpan.FromDays(30);

    // A typical range is adaptive mean +/- two standard deviations, clamped
    // to the valid percentage domain. It is descriptive, not anomalous.
    public const double TypicalRangeStandardDeviationMultiplier = 2d;

    // These thresholds avoid durable rewrites for floating-point noise while
    // still reinforcing a profile after six minutes at full cadence.
    public const double MaterialMeanShiftPercentagePoints = 0.25d;
    public const double MaterialRangeBoundShiftPercentagePoints = 0.5d;
    public const double MaterialEstimatedWallPowerMeanShiftWatts = 1d;
    public const double MaterialEstimatedWallPowerRangeShiftWatts = 2d;
    public const int ProfileReinforcementSampleInterval = 12;
    // Adjacent profiles must overlap by at least half of the narrower range.
    public const double MinimumRangeOverlapRatio = 0.5d;
    public const double ZeroVarianceSimilarityTolerancePercentagePoints = 1d;
    public const int MinimumPatternProfileCount = 2;
    public const int EstablishedPatternProfileCount = 3;
    public const int MaximumPatternCount = 24;

    public static MachineLearningFreshness GetFreshness(
        DateTimeOffset lastObservedAt,
        DateTimeOffset now)
    {
        var age = now <= lastObservedAt
            ? TimeSpan.Zero
            : now - lastObservedAt;
        return age <= FreshMaximumAge
            ? MachineLearningFreshness.Fresh
            : age <= AgingMaximumAge
                ? MachineLearningFreshness.Aging
                : MachineLearningFreshness.Stale;
    }

    public static MachineLearningRange? CreateTypicalRange(
        double mean,
        double standardDeviation,
        long evidenceCount)
    {
        if (evidenceCount < 2 ||
            !double.IsFinite(mean) ||
            !double.IsFinite(standardDeviation) ||
            standardDeviation < 0d)
        {
            return null;
        }

        var spread = standardDeviation *
            TypicalRangeStandardDeviationMultiplier;
        return new MachineLearningRange(
            Math.Clamp(mean - spread, 0d, 100d),
            Math.Clamp(mean + spread, 0d, 100d));
    }

    public static MachineLearningRange? CreateNonnegativeTypicalRange(
        double mean,
        double standardDeviation,
        long evidenceCount)
    {
        if (evidenceCount < MachineLearningService.ProvisionalSampleCount ||
            !double.IsFinite(mean) ||
            !double.IsFinite(standardDeviation) ||
            mean < 0d ||
            standardDeviation < 0d)
        {
            return null;
        }

        var spread = standardDeviation *
            TypicalRangeStandardDeviationMultiplier;
        return new MachineLearningRange(
            Math.Max(0d, mean - spread),
            mean + spread);
    }

    public static MachineLearningEvidenceMaturity GetEvidenceMaturity(
        long evidenceCount,
        int distinctObservedDayCount) =>
        evidenceCount >= MachineLearningService.EstablishedSampleCount &&
        distinctObservedDayCount >=
            MachineLearningService.EstablishedObservedDayCount
            ? MachineLearningEvidenceMaturity.Established
            : evidenceCount >= MachineLearningService.ProvisionalSampleCount
                ? MachineLearningEvidenceMaturity.Provisional
                : MachineLearningEvidenceMaturity.Insufficient;

    public static bool AreRangesCompatible(
        MachineLearningRange left,
        MachineLearningRange right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftWidth = Math.Max(0d, left.High - left.Low);
        var rightWidth = Math.Max(0d, right.High - right.Low);
        var narrowerWidth = Math.Min(leftWidth, rightWidth);
        if (narrowerWidth <= double.Epsilon)
        {
            var leftCenter = (left.Low + left.High) / 2d;
            var rightCenter = (right.Low + right.High) / 2d;
            return Math.Abs(leftCenter - rightCenter) <=
                ZeroVarianceSimilarityTolerancePercentagePoints;
        }

        var overlap = Math.Max(0d,
            Math.Min(left.High, right.High) -
            Math.Max(left.Low, right.Low));
        return overlap / narrowerWidth >= MinimumRangeOverlapRatio;
    }
}
