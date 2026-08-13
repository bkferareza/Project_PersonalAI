using System.Text.RegularExpressions;

namespace Machine.Core;

public static partial class MachineReliabilityAggregator
{
    public const int MaximumIncidentCount = 100;
    public const int MaximumRecurringApplicationCount = 8;
    public const int RecurringApplicationMinimumIncidentCount = 2;
    public static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(30);
    public static readonly TimeSpan UnexpectedShutdownDeduplicationWindow =
        TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ApplicationFailureDeduplicationWindow =
        TimeSpan.FromMinutes(2);
    public static readonly TimeSpan UpdateFailureDeduplicationWindow =
        TimeSpan.FromMinutes(5);

    public static MachineReliabilitySnapshot Aggregate(
        IEnumerable<MachineReliabilityIncident> candidates,
        DateTimeOffset capturedAt,
        MachineHealthDataStatus dataStatus = MachineHealthDataStatus.Complete,
        int readFailureCount = 0,
        DateTimeOffset? verifiedAt = null,
        string? failureCode = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (!Enum.IsDefined(dataStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(dataStatus));
        }

        var windowStart = capturedAt - HistoryWindow;
        var normalized = candidates
            .Select(NormalizeIncident)
            .Where(incident => incident is not null)
            .Select(incident => incident!)
            .Where(incident =>
                incident.OccurredAt >= windowStart &&
                incident.OccurredAt <= capturedAt + TimeSpan.FromMinutes(5))
            .OrderBy(incident => incident.OccurredAt)
            .ThenBy(incident => incident.Category)
            .ThenBy(incident => incident.Source, StringComparer.Ordinal)
            .ToArray();
        var deduplicated = Deduplicate(normalized);
        var retained = deduplicated
            .OrderByDescending(incident => incident.OccurredAt)
            .ThenBy(incident => incident.Category)
            .ThenBy(incident => incident.Source, StringComparer.Ordinal)
            .Take(MaximumIncidentCount)
            .ToArray();
        var summary = new MachineReliabilitySummary(
            Last24Hours: CountWindow(
                deduplicated,
                capturedAt - TimeSpan.FromHours(24)),
            Last7Days: CountWindow(
                deduplicated,
                capturedAt - TimeSpan.FromDays(7)),
            Last30Days: CountWindow(deduplicated, windowStart),
            MostRecentIncident: retained.FirstOrDefault(),
            RecurringApplications: CreateRecurringApplications(
                deduplicated,
                capturedAt));

        return new MachineReliabilitySnapshot(
            CapturedAt: capturedAt,
            VerifiedAt: dataStatus == MachineHealthDataStatus.Unavailable
                ? verifiedAt
                : verifiedAt ?? capturedAt,
            WindowStart: windowStart,
            DataStatus: dataStatus,
            ReadFailureCount: Math.Max(0, readFailureCount),
            Incidents: retained,
            Summary: summary,
            LastUnexpectedShutdownAt: FindMostRecentOccurrence(
                deduplicated,
                MachineReliabilityIncidentCategory.UnexpectedShutdown),
            LastVerifiedHardwareFailureAt: FindMostRecentOccurrence(
                deduplicated,
                MachineReliabilityIncidentCategory.HardwareFailure),
            FailureCode: NormalizeCode(failureCode, 80));
    }

    public static MachineReliabilityIncident? NormalizeIncident(
        MachineReliabilityIncident? incident)
    {
        if (incident is null ||
            incident.OccurredAt == default ||
            !Enum.IsDefined(incident.Category) ||
            !Enum.IsDefined(incident.Severity))
        {
            return null;
        }

        var source = NormalizeCode(incident.Source, 96);
        var summaryCode = NormalizeCode(incident.SummaryCode, 80);
        if (source is null || summaryCode is null)
        {
            return null;
        }

        return incident with
        {
            Source = source,
            SummaryCode = summaryCode,
            ApplicationName = NormalizeApplicationIdentity(
                incident.ApplicationName),
            FaultModule = NormalizeApplicationIdentity(
                incident.FaultModule),
            UpdateIdentifier =
                MachineWindowsUpdatePolicy.NormalizeKnowledgeBaseId(
                    incident.UpdateIdentifier),
            FailureCode = NormalizeCode(incident.FailureCode, 48),
            CorrelationId = NormalizeCode(incident.CorrelationId, 96)
        };
    }

    public static string? NormalizeApplicationIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Trim('"', '\'');
        if (normalized.Contains("://", StringComparison.Ordinal) ||
            ContainsExecutableArguments(normalized))
        {
            return null;
        }
        var finalSeparator = Math.Max(
            normalized.LastIndexOf('\\'),
            normalized.LastIndexOf('/'));
        if (finalSeparator >= 0 && finalSeparator < normalized.Length - 1)
        {
            normalized = normalized[(finalSeparator + 1)..];
        }

        normalized = normalized.Trim().Trim('"', '\'');
        if (normalized.Length is 0 or > 128 ||
            normalized.Any(char.IsControl) ||
            !SafeApplicationIdentityPattern().IsMatch(normalized))
        {
            return null;
        }

        return normalized;
    }

    private static bool ContainsExecutableArguments(string value) =>
        value.Contains(".exe ", StringComparison.OrdinalIgnoreCase) ||
        value.Contains(".com ", StringComparison.OrdinalIgnoreCase) ||
        value.Contains(".dll ", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<MachineReliabilityIncident> Deduplicate(
        IReadOnlyList<MachineReliabilityIncident> ordered)
    {
        var result = new List<MachineReliabilityIncident>(ordered.Count);

        foreach (var candidate in ordered)
        {
            var duplicateIndex = FindDuplicateIndex(result, candidate);
            if (duplicateIndex < 0)
            {
                result.Add(candidate);
                continue;
            }

            result[duplicateIndex] = Merge(result[duplicateIndex], candidate);
        }

        return result;
    }

    private static int FindDuplicateIndex(
        IReadOnlyList<MachineReliabilityIncident> existing,
        MachineReliabilityIncident candidate)
    {
        for (var index = existing.Count - 1; index >= 0; index--)
        {
            var current = existing[index];
            var elapsed = candidate.OccurredAt - current.OccurredAt;
            if (elapsed > UnexpectedShutdownDeduplicationWindow)
            {
                break;
            }

            if (IsDuplicate(current, candidate, elapsed))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsDuplicate(
        MachineReliabilityIncident left,
        MachineReliabilityIncident right,
        TimeSpan elapsed)
    {
        if (left.Category != right.Category)
        {
            return false;
        }

        if (left.Category ==
            MachineReliabilityIncidentCategory.UnexpectedShutdown)
        {
            return elapsed <= UnexpectedShutdownDeduplicationWindow &&
                AreComplementaryUnexpectedShutdownSources(left, right);
        }

        if (left.Category is
            MachineReliabilityIncidentCategory.ApplicationCrash or
            MachineReliabilityIncidentCategory.ApplicationHang)
        {
            if (!string.IsNullOrWhiteSpace(left.CorrelationId) &&
                string.Equals(
                    left.CorrelationId,
                    right.CorrelationId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return elapsed <= ApplicationFailureDeduplicationWindow &&
                !string.IsNullOrWhiteSpace(left.ApplicationName) &&
                string.Equals(
                    left.ApplicationName,
                    right.ApplicationName,
                    StringComparison.OrdinalIgnoreCase) &&
                AreComplementaryApplicationSources(left, right);
        }

        if (left.Category is
            MachineReliabilityIncidentCategory.UpdateFailure or
            MachineReliabilityIncidentCategory.InstallFailure)
        {
            return elapsed <= UpdateFailureDeduplicationWindow &&
                !string.IsNullOrWhiteSpace(left.UpdateIdentifier) &&
                string.Equals(
                    left.UpdateIdentifier,
                    right.UpdateIdentifier,
                    StringComparison.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(left.CorrelationId) &&
            string.Equals(
                left.CorrelationId,
                right.CorrelationId,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreComplementaryApplicationSources(
        MachineReliabilityIncident left,
        MachineReliabilityIncident right)
    {
        var leftIsReport = string.Equals(
            left.Source,
            "Windows Error Reporting",
            StringComparison.OrdinalIgnoreCase);
        var rightIsReport = string.Equals(
            right.Source,
            "Windows Error Reporting",
            StringComparison.OrdinalIgnoreCase);
        if (leftIsReport == rightIsReport)
        {
            return false;
        }

        var native = leftIsReport ? right : left;
        return left.Category switch
        {
            MachineReliabilityIncidentCategory.ApplicationCrash =>
                string.Equals(
                    native.Source,
                    "Application Error",
                    StringComparison.OrdinalIgnoreCase) &&
                native.EventId == 1000,
            MachineReliabilityIncidentCategory.ApplicationHang =>
                string.Equals(
                    native.Source,
                    "Application Hang",
                    StringComparison.OrdinalIgnoreCase) &&
                native.EventId == 1002,
            _ => false
        };
    }

    private static bool AreComplementaryUnexpectedShutdownSources(
        MachineReliabilityIncident left,
        MachineReliabilityIncident right)
    {
        static bool IsKernelPower(MachineReliabilityIncident incident) =>
            string.Equals(
                incident.Source,
                "Microsoft-Windows-Kernel-Power",
                StringComparison.OrdinalIgnoreCase) &&
            incident.EventId == 41;
        static bool IsEventLog(MachineReliabilityIncident incident) =>
            (string.Equals(
                 incident.Source,
                 "EventLog",
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 incident.Source,
                 "Microsoft-Windows-Eventlog",
                 StringComparison.OrdinalIgnoreCase)) &&
            incident.EventId == 6008;

        return IsKernelPower(left) && IsEventLog(right) ||
            IsEventLog(left) && IsKernelPower(right);
    }

    private static MachineReliabilityIncident Merge(
        MachineReliabilityIncident left,
        MachineReliabilityIncident right)
    {
        var preferred = Score(right) > Score(left) ? right : left;
        var other = ReferenceEquals(preferred, left) ? right : left;
        return preferred with
        {
            OccurredAt = left.OccurredAt <= right.OccurredAt
                ? left.OccurredAt
                : right.OccurredAt,
            ApplicationName = preferred.ApplicationName ??
                other.ApplicationName,
            FaultModule = preferred.FaultModule ?? other.FaultModule,
            UpdateIdentifier = preferred.UpdateIdentifier ??
                other.UpdateIdentifier,
            FailureCode = preferred.FailureCode ?? other.FailureCode,
            CorrelationId = preferred.CorrelationId ?? other.CorrelationId,
            Severity = left.Severity >= right.Severity
                ? left.Severity
                : right.Severity
        };
    }

    private static int Score(MachineReliabilityIncident incident) =>
        (incident.ApplicationName is null ? 0 : 1) +
        (incident.FaultModule is null ? 0 : 1) +
        (incident.UpdateIdentifier is null ? 0 : 1) +
        (incident.FailureCode is null ? 0 : 1) +
        (incident.CorrelationId is null ? 0 : 1);

    private static MachineReliabilityWindowSummary CountWindow(
        IEnumerable<MachineReliabilityIncident> incidents,
        DateTimeOffset windowStart)
    {
        var counts = incidents
            .Where(incident => incident.OccurredAt >= windowStart)
            .GroupBy(incident => incident.Category)
            .ToDictionary(group => group.Key, group => group.Count());
        int GetCount(MachineReliabilityIncidentCategory category) =>
            counts.GetValueOrDefault(category);

        return new MachineReliabilityWindowSummary(
            ApplicationCrashCount: GetCount(
                MachineReliabilityIncidentCategory.ApplicationCrash),
            ApplicationHangCount: GetCount(
                MachineReliabilityIncidentCategory.ApplicationHang),
            UnexpectedShutdownCount: GetCount(
                MachineReliabilityIncidentCategory.UnexpectedShutdown),
            UpdateFailureCount: GetCount(
                MachineReliabilityIncidentCategory.UpdateFailure) +
                GetCount(MachineReliabilityIncidentCategory.InstallFailure),
            HardwareFailureCount: GetCount(
                MachineReliabilityIncidentCategory.HardwareFailure),
            OtherFailureCount: GetCount(
                MachineReliabilityIncidentCategory.WindowsFailure) +
                GetCount(MachineReliabilityIncidentCategory.Unknown));
    }

    private static DateTimeOffset? FindMostRecentOccurrence(
        IEnumerable<MachineReliabilityIncident> incidents,
        MachineReliabilityIncidentCategory category) => incidents
        .Where(incident => incident.Category == category)
        .Select(incident => (DateTimeOffset?)incident.OccurredAt)
        .Max();

    private static IReadOnlyList<MachineRecurringApplicationFailure>
        CreateRecurringApplications(
            IEnumerable<MachineReliabilityIncident> incidents,
            DateTimeOffset capturedAt)
    {
        var last7Days = capturedAt - TimeSpan.FromDays(7);
        return incidents
            .Where(incident =>
                incident.Category is (
                    MachineReliabilityIncidentCategory.ApplicationCrash or
                    MachineReliabilityIncidentCategory.ApplicationHang) &&
                incident.ApplicationName is not null)
            .GroupBy(
                incident => incident.ApplicationName!,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new MachineRecurringApplicationFailure(
                ApplicationName: group
                    .OrderBy(
                        value => value.ApplicationName,
                        StringComparer.Ordinal)
                    .First().ApplicationName!,
                IncidentCountLast30Days: group.Count(),
                IncidentCountLast7Days: group.Count(incident =>
                    incident.OccurredAt >= last7Days),
                LastOccurredAt: group.Max(incident =>
                    incident.OccurredAt)))
            .Where(recurring => recurring.IncidentCountLast30Days >=
                RecurringApplicationMinimumIncidentCount)
            .OrderByDescending(recurring =>
                recurring.IncidentCountLast7Days)
            .ThenByDescending(recurring =>
                recurring.IncidentCountLast30Days)
            .ThenByDescending(recurring => recurring.LastOccurredAt)
            .ThenBy(recurring =>
                recurring.ApplicationName,
                StringComparer.OrdinalIgnoreCase)
            .Take(MaximumRecurringApplicationCount)
            .ToArray();
    }

    private static string? NormalizeCode(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength ||
            normalized.Any(char.IsControl) ||
            !SafeCodePattern().IsMatch(normalized))
        {
            return null;
        }

        return normalized;
    }

    [GeneratedRegex(@"^[\p{L}\p{N} ._()+\-]+$",
        RegexOptions.CultureInvariant, 100)]
    private static partial Regex SafeApplicationIdentityPattern();

    [GeneratedRegex(@"^[A-Za-z0-9 ._:+\-]+$",
        RegexOptions.CultureInvariant, 100)]
    private static partial Regex SafeCodePattern();
}
