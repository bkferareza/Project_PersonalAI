namespace Machine.Core;

public static class MachineRebootPendingAggregator
{
    public const int MaximumReasonCount = 8;

    public static MachineRebootPendingSnapshot Aggregate(
        IEnumerable<MachineRebootPendingIndicator> indicators,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(indicators);

        var normalized = indicators
            .Where(indicator =>
                indicator is not null &&
                Enum.IsDefined(indicator.Reason) &&
                indicator.Reason != MachineRebootPendingReason.Unknown)
            .GroupBy(indicator => indicator.Reason)
            .Select(group => new MachineRebootPendingIndicator(
                group.Key,
                Combine(group.Select(indicator => indicator.IsPresent))))
            .OrderBy(indicator => indicator.Reason)
            .Take(MaximumReasonCount)
            .ToArray();
        var reasons = normalized
            .Where(indicator => indicator.IsPresent == true)
            .Select(indicator => indicator.Reason)
            .ToArray();
        var knownCount = normalized.Count(indicator =>
            indicator.IsPresent is not null);
        var isPartial = knownCount < normalized.Length;
        bool? isPending = reasons.Length > 0
            ? true
            : knownCount == 0
                ? null
                : isPartial
                    ? null
                    : false;
        var confidence = reasons.Length > 0 && !isPartial ||
            isPending == false
                ? MachineRebootPendingConfidence.Verified
                : reasons.Length > 0 || knownCount > 0
                    ? MachineRebootPendingConfidence.Partial
                    : MachineRebootPendingConfidence.Unknown;

        return new MachineRebootPendingSnapshot(
            CapturedAt: capturedAt,
            IsPending: isPending,
            Confidence: confidence,
            Reasons: reasons,
            Indicators: normalized,
            IsPartial: isPartial || normalized.Length == 0);
    }

    private static bool? Combine(IEnumerable<bool?> values)
    {
        var materialized = values.ToArray();
        if (materialized.Any(value => value == true))
        {
            return true;
        }

        return materialized.Any(value => value is null)
            ? null
            : false;
    }
}
