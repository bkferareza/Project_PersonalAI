using System.Text.RegularExpressions;

namespace Machine.Core;

public static partial class MachineWindowsUpdatePolicy
{
    public const int MaximumHistoryCount = 30;

    public static MachineWindowsUpdateState EvaluateState(
        bool serviceAvailable,
        bool searchSucceeded,
        int pendingUpdateCount,
        int downloadedPendingUpdateCount,
        bool restartRequired)
    {
        if (!serviceAvailable || !searchSucceeded ||
            pendingUpdateCount < 0 || downloadedPendingUpdateCount < 0)
        {
            return MachineWindowsUpdateState.Unknown;
        }

        if (restartRequired)
        {
            return MachineWindowsUpdateState.RestartRequired;
        }

        if (pendingUpdateCount == 0)
        {
            return MachineWindowsUpdateState.UpToDate;
        }

        return downloadedPendingUpdateCount > 0
            ? MachineWindowsUpdateState.InstallPending
            : MachineWindowsUpdateState.UpdatesAvailable;
    }

    public static IReadOnlyList<MachineWindowsUpdateHistoryEntry>
        NormalizeHistory(
            IEnumerable<MachineWindowsUpdateHistoryEntry> history,
            int maximumCount = MaximumHistoryCount)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);

        return history
            .Where(IsValidHistoryEntry)
            .Select(entry => entry with
            {
                Title = NormalizeText(entry.Title, 180)!,
                Category = NormalizeText(entry.Category, 80),
                KnowledgeBaseId = NormalizeKnowledgeBaseId(
                    entry.KnowledgeBaseId ?? entry.Title)
            })
            .GroupBy(
                entry => CreateHistoryIdentity(entry),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(entry => entry.OccurredAt)
                .First())
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Min(maximumCount, MaximumHistoryCount))
            .ToArray();
    }

    public static string? NormalizeKnowledgeBaseId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = KnowledgeBasePattern().Match(value);
        return match.Success
            ? match.Value.ToUpperInvariant()
            : null;
    }

    private static bool IsValidHistoryEntry(
        MachineWindowsUpdateHistoryEntry entry) =>
        entry is not null &&
        entry.OccurredAt != default &&
        !string.IsNullOrWhiteSpace(entry.Title) &&
        Enum.IsDefined(entry.Result);

    private static string CreateHistoryIdentity(
        MachineWindowsUpdateHistoryEntry entry)
    {
        var kb = NormalizeKnowledgeBaseId(
            entry.KnowledgeBaseId ?? entry.Title);
        var title = NormalizeText(entry.Title, 180) ?? string.Empty;
        var minute = entry.OccurredAt.UtcTicks /
            TimeSpan.TicksPerMinute;
        return $"{kb ?? title}|{entry.Result}|{minute}";
    }

    private static string? NormalizeText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0 || normalized.Any(char.IsControl))
        {
            return null;
        }

        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    [GeneratedRegex(@"\bKB\d{4,10}\b", RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant, 100)]
    private static partial Regex KnowledgeBasePattern();
}
