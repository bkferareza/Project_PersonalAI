namespace Machine.Core;

public static class MachineSituationEvidenceSelector
{
    public const int MaximumEvidenceItemCount = 24;

    private static readonly MachineSituationCategory[] CategoryOrder =
    [
        MachineSituationCategory.Now,
        MachineSituationCategory.Recently,
        MachineSituationCategory.LearnedNormal,
        MachineSituationCategory.Today,
        MachineSituationCategory.Forward,
        MachineSituationCategory.ActionOutcome,
        MachineSituationCategory.LearningConfidence,
        MachineSituationCategory.SelfHealth
    ];

    public static IReadOnlyList<MachineSituationEvidenceItem> Select(
        IEnumerable<MachineSituationEvidenceItem> candidates,
        int maximumItemCount = MaximumEvidenceItemCount)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (maximumItemCount is < 1 or > MaximumEvidenceItemCount)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumItemCount));
        }

        var ordered = candidates
            .Where(IsUsable)
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Importance)
                .ThenBy(item => FreshnessRank(item.Freshness))
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .First())
            .OrderByDescending(item => item.Importance)
            .ThenBy(item => FreshnessRank(item.Freshness))
            .ThenBy(item => CategoryRank(item.Category))
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var selected = new List<MachineSituationEvidenceItem>(
            maximumItemCount);

        foreach (var item in ordered.Where(item =>
            item.Importance >= MachineSituationImportance.Important))
        {
            AddIfAvailable(selected, item, maximumItemCount);
        }

        foreach (var category in CategoryOrder)
        {
            if (selected.Count >= maximumItemCount ||
                selected.Any(item => item.Category == category))
            {
                continue;
            }
            var representative = ordered.FirstOrDefault(item =>
                item.Category == category);
            if (representative is not null)
            {
                AddIfAvailable(selected, representative, maximumItemCount);
            }
        }

        foreach (var item in ordered)
        {
            AddIfAvailable(selected, item, maximumItemCount);
        }

        return selected
            .OrderByDescending(item => item.Importance)
            .ThenBy(item => FreshnessRank(item.Freshness))
            .ThenBy(item => CategoryRank(item.Category))
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddIfAvailable(
        ICollection<MachineSituationEvidenceItem> selected,
        MachineSituationEvidenceItem item,
        int maximumItemCount)
    {
        if (selected.Count >= maximumItemCount ||
            selected.Any(existing => string.Equals(
                existing.Id,
                item.Id,
                StringComparison.Ordinal)))
        {
            return;
        }
        selected.Add(item);
    }

    private static bool IsUsable(MachineSituationEvidenceItem? item) =>
        item is not null &&
        !string.IsNullOrWhiteSpace(item.Id) &&
        !string.IsNullOrWhiteSpace(item.Summary) &&
        item.DisplayValues is not null &&
        item.EntityNames is not null &&
        Enum.IsDefined(item.Category) &&
        Enum.IsDefined(item.TimeScope) &&
        Enum.IsDefined(item.Importance) &&
        Enum.IsDefined(item.Freshness) &&
        Enum.IsDefined(item.Maturity);

    private static int FreshnessRank(MachineSituationFreshness freshness) =>
        freshness switch
        {
            MachineSituationFreshness.Current => 0,
            MachineSituationFreshness.Recent => 1,
            MachineSituationFreshness.Historical => 2,
            MachineSituationFreshness.Stale => 3,
            _ => 4
        };

    private static int CategoryRank(MachineSituationCategory category)
    {
        var index = Array.IndexOf(CategoryOrder, category);
        return index < 0 ? int.MaxValue : index;
    }
}
