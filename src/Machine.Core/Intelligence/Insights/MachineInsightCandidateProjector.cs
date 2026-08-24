using System.Globalization;

namespace Machine.Core;

public static class MachineInsightCandidateProjector
{
    public const string LearnedEnergyAboveId =
        "learned-energy-today-above";
    public const string LearnedEnergyBelowId =
        "learned-energy-today-below";
    public static readonly TimeSpan MinimumLearnedDeviationDuration =
        TimeSpan.FromHours(1);
    public static readonly TimeSpan CandidateFreshness =
        TimeSpan.FromMinutes(10);
    public const double MinimumAbsoluteDeviationKilowattHours = 0.01d;
    public const double MinimumRelativeDeviation = 0.05d;

    public static MachineInsightCandidate? ProjectLearnedEnergyDeviation(
        MachineTodayLearnedEnergyComparison? comparison,
        DateTimeOffset now,
        TimeZoneInfo? timeZone = null)
    {
        if (comparison is null ||
            comparison.ComparisonMaturity !=
                MachineLearningEvidenceMaturity.Established ||
            comparison.ObservedDuration < MinimumLearnedDeviationDuration ||
            !comparison.HasCompleteLearnedCoverage ||
            comparison.ExpectedObservedEnergyKilowattHours is not
                { } expected ||
            comparison.ExpectedLowerEnergyKilowattHours is not { } lower ||
            comparison.ExpectedUpperEnergyKilowattHours is not { } upper ||
            !IsFiniteNonnegative(comparison.ActualObservedEnergyKilowattHours) ||
            !IsFinitePositive(expected) ||
            !IsFiniteNonnegative(lower) ||
            !IsFiniteNonnegative(upper) ||
            lower > upper ||
            !IsCurrent(comparison.LocalDate, now, timeZone))
        {
            return null;
        }

        var isAbove = comparison.ComparisonState ==
            MachineTodayLearnedEnergyComparisonState.AboveLearnedRange;
        var isBelow = comparison.ComparisonState ==
            MachineTodayLearnedEnergyComparisonState.BelowLearnedRange;
        if (!isAbove && !isBelow)
        {
            return null;
        }

        var nearestBound = isAbove ? upper : lower;
        var beyondBound = isAbove
            ? comparison.ActualObservedEnergyKilowattHours - upper
            : lower - comparison.ActualObservedEnergyKilowattHours;
        var relativeDenominator = Math.Max(
            nearestBound,
            MachineTodayLearnedEnergyProjector.
                MinimumMeaningfulExpectedEnergyKilowattHours);
        if (!double.IsFinite(beyondBound) ||
            beyondBound < MinimumAbsoluteDeviationKilowattHours ||
            beyondBound / relativeDenominator < MinimumRelativeDeviation)
        {
            return null;
        }

        var id = isAbove ? LearnedEnergyAboveId : LearnedEnergyBelowId;
        var title = isAbove
            ? "Running heavier than usual"
            : "Running lighter than usual";
        var primary =
            $"~{comparison.ActualObservedEnergyKilowattHours:F3} kWh observed today";
        var secondary =
            $"For the same {FormatDuration(comparison.ObservedDuration)} " +
            "observed, your established range is around " +
            $"{lower:F3}–{upper:F3} kWh.";
        var evidence = CreateLearnedEnergyEvidence(
            comparison,
            beyondBound,
            isAbove,
            now);
        var explainContext = new MachineInsightExplainContext(
            id,
            MachineInsightKind.LearnedEnergyDeviation,
            title,
            primary,
            secondary,
            evidence,
            comparison.ActualObservedEnergyKilowattHours,
            ToWholeSeconds(comparison.ObservedDuration),
            expected,
            lower,
            upper,
            comparison.DifferenceKilowattHours,
            comparison.DifferencePercent,
            comparison.LearnedCoverage,
            comparison.ComparisonMaturity,
            comparison.ActualEstimatedCost,
            comparison.ExpectedEstimatedCost,
            comparison.ExpectedLowerCost,
            comparison.ExpectedUpperCost,
            comparison.Rate?.ProviderName,
            comparison.Rate?.CurrencyCode,
            comparison.Rate?.RatePerKWh,
            comparison.Rate?.EffectiveMonth);

        return new(
            id,
            MachineInsightKind.LearnedEnergyDeviation,
            title,
            primary,
            secondary,
            evidence,
            MachineInsightImportance.Notable,
            now,
            now + CandidateFreshness,
            MachineLearningEvidenceMaturity.Established,
            CanSignalNew: true,
            explainContext);
    }

    public static MachineInsightCandidate? ProjectMachineFinding(
        MachineFindingsSnapshot? snapshot,
        DateTimeOffset now)
    {
        var finding = snapshot?.Findings
            .Where(IsMeaningfulMachineFinding)
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .FirstOrDefault();
        if (finding is null)
        {
            return null;
        }

        var id = $"machine-finding:{finding.Code}";
        var primary = finding.Severity switch
        {
            MachineFindingSeverity.Critical => "Needs prompt review",
            MachineFindingSeverity.Warning => "Worth reviewing soon",
            MachineFindingSeverity.Attention => "Worth a closer look",
            _ => "Windows reports a current condition"
        };
        const string evidence = "Verified deterministic finding · current";
        var explainContext = new MachineInsightExplainContext(
            id,
            MachineInsightKind.MachineFinding,
            finding.Title,
            primary,
            finding.Detail,
            evidence);

        return new(
            id,
            MachineInsightKind.MachineFinding,
            finding.Title,
            primary,
            finding.Detail,
            evidence,
            finding.Severity is MachineFindingSeverity.Warning or
                MachineFindingSeverity.Critical
                    ? MachineInsightImportance.Important
                    : MachineInsightImportance.Notable,
            now,
            now + CandidateFreshness,
            EvidenceMaturity: null,
            CanSignalNew: true,
            explainContext);
    }

    private static bool IsMeaningfulMachineFinding(MachineFinding finding) =>
        !finding.Code.StartsWith("data.", StringComparison.Ordinal) &&
        (finding.Severity != MachineFindingSeverity.Info ||
            finding.Code == "health.restart.pending");

    private static string CreateLearnedEnergyEvidence(
        MachineTodayLearnedEnergyComparison comparison,
        double beyondBound,
        bool isAbove,
        DateTimeOffset now)
    {
        var direction = isAbove ? "+" : "−";
        var evidence =
            $"Established · {comparison.LearnedCoverage:P0} learned coverage · " +
            $"{direction}{beyondBound:F3} kWh beyond range";
        if (comparison.ActualEstimatedCost is { } actualCost &&
            comparison.ExpectedLowerCost is { } lowerCost &&
            comparison.ExpectedUpperCost is { } upperCost &&
            comparison.Rate is { } rate)
        {
            var currency = FormatCurrency(rate.CurrencyCode);
            evidence += $" · ~{currency}{actualCost:F2} observed vs " +
                $"~{currency}{lowerCost:F2}–{currency}{upperCost:F2} " +
                "learned estimate";
        }

        return evidence + " · updated " +
            now.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
    }

    private static bool IsCurrent(
        DateOnly localDate,
        DateTimeOffset now,
        TimeZoneInfo? timeZone)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        return localDate == DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, zone).Date);
    }

    private static bool IsFiniteNonnegative(double value) =>
        double.IsFinite(value) && value >= 0d;

    private static bool IsFinitePositive(double value) =>
        double.IsFinite(value) &&
        value > MachineTodayLearnedEnergyProjector.
            MinimumMeaningfulExpectedEnergyKilowattHours;

    private static string FormatDuration(TimeSpan duration)
    {
        var bounded = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        if (bounded.TotalHours >= 1d)
        {
            return $"{(int)bounded.TotalHours}h {bounded.Minutes}m";
        }

        return $"{Math.Max(1, bounded.Minutes)}m";
    }

    private static long ToWholeSeconds(TimeSpan duration) =>
        duration <= TimeSpan.Zero
            ? 0
            : duration.TotalSeconds >= long.MaxValue
                ? long.MaxValue
                : (long)duration.TotalSeconds;

    private static string FormatCurrency(string currencyCode) =>
        string.Equals(currencyCode, "PHP", StringComparison.OrdinalIgnoreCase)
            ? "₱"
            : $"{currencyCode} ";
}
