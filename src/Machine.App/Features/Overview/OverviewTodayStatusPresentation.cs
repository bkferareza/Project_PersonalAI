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
        const string context =
            "Observed PC energy and estimated electricity cost, not a household bill.";

        if (!status.HasObservedEnergy)
        {
            return new(status.Title, "Still observing",
                "Valid observed PC energy will appear here.", context);
        }

        return new(
            status.Title,
            status.EstimatedPcElectricityCost is { } cost &&
                status.Rate is { } costRate
                    ? $"~{FormatCurrency(costRate.CurrencyCode)}" +
                      $"{cost:F2} estimated"
                    : "Cost unavailable",
            $"{status.ObservedEnergyKilowattHours:F3} kWh",
            context);
    }

    private static string FormatCurrency(string currencyCode) =>
        string.Equals(currencyCode, "PHP", StringComparison.OrdinalIgnoreCase)
            ? "₱"
            : $"{currencyCode} ";
}

internal static class OverviewTodayComparisonPresenter
{
    internal static string Present(
        MachineTodayLearnedEnergyComparison comparison) =>
        comparison.ComparisonState switch
        {
            MachineTodayLearnedEnergyComparisonState.WithinLearnedRange =>
                "Within the range Matasuri has learned for this observed period.",
            MachineTodayLearnedEnergyComparisonState.AboveLearnedRange =>
                "Above the range Matasuri has learned for this observed period.",
            MachineTodayLearnedEnergyComparisonState.BelowLearnedRange =>
                "Below the range Matasuri has learned for this observed period.",
            MachineTodayLearnedEnergyComparisonState.StillLearning =>
                "Matasuri is still learning a comparable pattern for today.",
            _ =>
                "A learned comparison will appear after enough matched observed time."
        };
}
