using System.Globalization;
using Machine.Core;

namespace Machine.App.Features;

internal sealed record OverviewTodayStatusPresentation(
    string Title,
    string PrimaryText,
    string EnergyText,
    string EvidenceText);

internal static class OverviewTodayStatusPresenter
{
    internal static OverviewTodayStatusPresentation Present(
        MachineTodayStatusProjection status)
    {
        ArgumentNullException.ThrowIfNull(status);

        var rateEvidence = status.Rate is { } rate
            ? $"{rate.ProviderName} residential reference · " +
              $"{FormatCurrency(rate.CurrencyCode)}" +
              $"{rate.RatePerKWh:F4}/kWh · " +
              rate.EffectiveMonth.ToString(
                  "MMMM yyyy",
                  CultureInfo.CurrentCulture)
            : "Published residential reference unavailable";

        if (!status.HasObservedEnergy)
        {
            return new(
                status.Title,
                "Still observing",
                "Valid observed PC energy will appear here.",
                $"Estimated PC electricity cost · {rateEvidence}");
        }

        return new(
            status.Title,
            status.EstimatedPcElectricityCost is { } cost &&
                status.Rate is { } costRate
                    ? $"~{FormatCurrency(costRate.CurrencyCode)}" +
                      $"{cost:F2} estimated"
                    : "Cost unavailable",
            $"{status.ObservedEnergyKilowattHours:F3} kWh observed PC energy",
            $"Estimated PC electricity cost · {rateEvidence}");
    }

    private static string FormatCurrency(string currencyCode) =>
        string.Equals(
            currencyCode,
            "PHP",
            StringComparison.OrdinalIgnoreCase)
                ? "₱"
                : $"{currencyCode} ";
}
