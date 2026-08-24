namespace Machine.Core;

public static class MachineLearningPowerEvidencePolicy
{
    public static TimeSpan MaximumEstimateAge =>
        MachineLearningService.ObservationInterval;

    public static double? SelectEligibleEstimatedWallPowerWatts(
        MachinePowerEstimate? estimate,
        DateTimeOffset observationTimestamp)
    {
        if (estimate?.EstimatedWallWatts is not { } watts ||
            !double.IsFinite(watts) ||
            watts < 0d ||
            estimate.CapturedAt > observationTimestamp ||
            observationTimestamp - estimate.CapturedAt > MaximumEstimateAge)
        {
            return null;
        }

        return estimate.Confidence is
            MachinePowerEstimateConfidence.Measured or
            MachinePowerEstimateConfidence.HighEstimate or
            MachinePowerEstimateConfidence.ModerateEstimate
                ? watts
                : null;
    }
}
