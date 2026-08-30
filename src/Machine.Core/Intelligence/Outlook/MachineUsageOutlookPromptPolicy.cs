namespace Machine.Core;

public static class MachineUsageOutlookPromptPolicy
{
    public const string CurrentVersion = "english-concise-v2";

    public static bool CanExposeEndOfDayProjection(
        MachineUsageForecast forecast)
    {
        ArgumentNullException.ThrowIfNull(forecast);

        return forecast.HasEndOfDayForecast &&
            forecast.AvailabilityReason ==
                MachineUsageForecastAvailabilityReason.Available;
    }
}
