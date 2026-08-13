using System.Globalization;

namespace Machine.Core;

public static class MachineHealthLearnedItemProjector
{
    public const int MaximumItemCount = 4;

    public static IReadOnlyList<MachineLearnedItem> Project(
        MachineHealthHistorySnapshot history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var reliability = history.Reliability;
        if (reliability is null)
        {
            return [];
        }

        var items = new List<MachineLearnedItem>(MaximumItemCount);
        var recurring = reliability.Summary.RecurringApplications
            .OrderByDescending(item => item.IncidentCountLast7Days)
            .ThenByDescending(item => item.IncidentCountLast30Days)
            .FirstOrDefault();
        if (recurring is not null)
        {
            var count = recurring.IncidentCountLast7Days > 0
                ? recurring.IncidentCountLast7Days
                : recurring.IncidentCountLast30Days;
            var window = recurring.IncidentCountLast7Days > 0
                ? "7 days"
                : "30 days";
            items.Add(new MachineLearnedItem(
                $"Windows recorded " +
                $"{FormatCount(count, "crash or hang", "crashes or hangs")} " +
                $"of {recurring.ApplicationName} during the last {window}.",
                count,
                null,
                false,
                MachineLearningMemoryLayer.HealthHistory));
        }

        if (reliability.LastUnexpectedShutdown is { } shutdown)
        {
            items.Add(new MachineLearnedItem(
                "Windows recorded an unexpected shutdown on " +
                shutdown.ToLocalTime().ToString(
                    "MMM d 'at' h:mm tt",
                    CultureInfo.CurrentCulture) + ".",
                1,
                null,
                false,
                MachineLearningMemoryLayer.HealthHistory));
        }

        if (reliability.DataStatus == MachineHealthDataStatus.Complete &&
            reliability.Summary.Last30Days.UpdateFailureCount == 0)
        {
            items.Add(new MachineLearnedItem(
                "No update failures were recorded in the verified " +
                "30-day reliability window.",
                1,
                null,
                false,
                MachineLearningMemoryLayer.HealthHistory));
        }
        else if (reliability.DataStatus != MachineHealthDataStatus.Complete)
        {
            items.Add(new MachineLearnedItem(
                "Update failure history is partially available.",
                1,
                null,
                false,
                MachineLearningMemoryLayer.HealthHistory));
        }

        return items.Take(MaximumItemCount).ToArray();
    }

    private static string FormatCount(
        int count,
        string singular,
        string plural) =>
        $"{count.ToString("N0", CultureInfo.InvariantCulture)} " +
        (count == 1 ? singular : plural);
}
