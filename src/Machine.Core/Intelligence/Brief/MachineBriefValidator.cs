using System.Globalization;
using System.Text.RegularExpressions;

namespace Machine.Core;

public static partial class MachineBriefValidator
{
    private const int MaximumOverallCharacters = 240;
    private const int MaximumPointCharacters = 240;
    private const int MaximumOutlookCharacters = 240;
    private const int MaximumTotalWords = 120;
    private const int MaximumEvidenceIdsPerStatement = 5;

    private static readonly string[] CausalPhrases =
    [
        "because",
        "caused",
        "causes",
        "causing",
        "due to",
        "led to",
        "leads to",
        "resulted in",
        "results in",
        "responsible for",
        "therefore",
        "drove",
        "driven by",
        "blocking",
        "blocks",
        "preventing",
        "prevents",
        "requires a restart"
    ];

    private static readonly string[] MutationPhrases =
    [
        "disable",
        "enable",
        "delete",
        "uninstall",
        "terminate",
        "kill the",
        "stop the",
        "remove the",
        "edit the registry",
        "change the registry",
        "run powershell",
        "run command",
        "execute command",
        "turn off",
        "shut down",
        "restart the",
        "close the application"
    ];

    private static readonly string[] NonEnglishMarkers =
    [
        " ang ",
        " mga ",
        " iyong ",
        " ngayon ",
        " ngunit ",
        " walang ",
        " mayroon "
    ];

    private static readonly HashSet<string> CommonCapitalizedWords = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "A", "AI", "Active", "Application", "Asleep", "Attention",
        "Available", "Brief", "CPU", "Critical", "Current", "Deterministic",
        "Early", "End", "Established", "Everything", "Forward", "Future", "GPU",
        "Forecast", "Forecasting", "Global", "Health", "History", "I",
        "Idle", "Important", "Learning", "Local",
        "Machine", "Matasuri", "Memory", "No", "Normal", "Not", "Nothing",
        "Observed", "Pattern", "PC", "Pending", "Power", "Provisional", "Qwen", "RAM", "Ready",
        "Recently", "Reliability", "Routine", "Self", "Stable", "Storage",
        "System", "The", "There", "This", "Today", "Unavailable", "Unknown",
        "VRAM", "Warning", "Windows", "Within", "Your"
    };

    [GeneratedRegex(@"(?<![\p{L}\d])[-+]?\d+(?:[,.]\d+)*(?:\.\d+)?")]
    private static partial Regex NumericTokenRegex();

    [GeneratedRegex(@"\b(?:zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|hundred|thousand)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NumericWordRegex();

    [GeneratedRegex(@"\b(?:[A-Z][A-Za-z0-9._-]{1,}|[A-Za-z0-9_-]+\.exe)\b")]
    private static partial Regex EntityLikeTokenRegex();

    [GeneratedRegex(@"[\u0400-\u04FF\u0600-\u06FF\u3040-\u30FF\u3400-\u9FFF\uAC00-\uD7AF]")]
    private static partial Regex NonLatinScriptRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(
        @"\b(?:within|outside|above|below|beyond|exceeds?|exceeding|matches?|matching|aligns?|aligned)\b.*\b(?:learned|normal|typical|usual|expected|baselines?|ranges?)\b|" +
        @"\b(?:normal|typical|usual|expected)\b.*\b(?:resources?|CPU|memory|GPU|power|usage|utilization)\b|" +
        @"\b(?:resources?|CPU|memory|GPU|power|usage|utilization)\b.*\b(?:normal|typical|usual|expected)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ResourceComparisonRegex();

    [GeneratedRegex(@"\b(?:queued|scheduled|planned|automatic|automatically)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RestartScheduleRegex();

    [GeneratedRegex(@"\b(?:shutdowns?|shut-downs?|reboots?|restarts?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SystemLifecycleRegex();

    public static MachineBriefValidationResult Validate(
        MachineBriefDraft? draft,
        MachineSituationSnapshot situation)
    {
        ArgumentNullException.ThrowIfNull(situation);
        if (draft is null ||
            string.IsNullOrWhiteSpace(draft.Overall) ||
            draft.OverallEvidenceIds is null ||
            draft.Points is null ||
            draft.OutlookEvidenceIds is null ||
            draft.Points.Count > MachineBriefPromptPolicy.MaximumPointCount ||
            draft.Points.Any(point => point is null ||
                string.IsNullOrWhiteSpace(point.Text) ||
                point.EvidenceIds is null) ||
            (string.IsNullOrWhiteSpace(draft.Outlook) &&
                draft.OutlookEvidenceIds.Count != 0) ||
            (!string.IsNullOrWhiteSpace(draft.Outlook) &&
                draft.OutlookEvidenceIds.Count == 0))
        {
            return Reject(MachineBriefValidationFailure.Schema,
                "Response schema was incomplete.");
        }

        var overall = Normalize(draft.Overall);
        var outlook = string.IsNullOrWhiteSpace(draft.Outlook)
            ? null
            : Normalize(draft.Outlook);
        var points = draft.Points.Select(point => new MachineBriefPoint(
            Normalize(point.Text!),
            NormalizeIds(point.EvidenceIds!))).ToArray();
        var overallIds = NormalizeIds(draft.OverallEvidenceIds);
        var outlookIds = NormalizeIds(draft.OutlookEvidenceIds);

        if (overall.Length > MaximumOverallCharacters ||
            points.Any(point => point.Text.Length > MaximumPointCharacters) ||
            outlook?.Length > MaximumOutlookCharacters ||
            WordCount(overall) + points.Sum(point => WordCount(point.Text)) +
                (outlook is null ? 0 : WordCount(outlook)) > MaximumTotalWords)
        {
            return Reject(MachineBriefValidationFailure.Length,
                "Brief text exceeded its bounded length.");
        }

        var evidence = situation.Evidence.ToDictionary(
            item => item.Id,
            StringComparer.Ordinal);
        var idResult = ValidateEvidenceIds(overallIds, evidence);
        if (idResult is not null)
        {
            return idResult;
        }
        foreach (var point in points)
        {
            idResult = ValidateEvidenceIds(point.EvidenceIds, evidence);
            if (idResult is not null)
            {
                return idResult;
            }
        }
        if (outlook is not null)
        {
            idResult = ValidateEvidenceIds(outlookIds, evidence);
            if (idResult is not null)
            {
                return idResult;
            }
        }

        var statements = new List<(string Text, IReadOnlyList<string> Ids)>
        {
            (overall, overallIds)
        };
        statements.AddRange(points.Select(point =>
            (point.Text, point.EvidenceIds)));
        if (outlook is not null)
        {
            statements.Add((outlook, outlookIds));
        }

        foreach (var statement in statements)
        {
            var result = ValidateStatement(statement.Text, statement.Ids,
                evidence, situation.Evidence);
            if (result is not null)
            {
                return result;
            }
        }

        return MachineBriefValidationResult.Valid(new(
            overall,
            overallIds,
            points,
            outlook,
            outlookIds));
    }

    private static MachineBriefValidationResult? ValidateStatement(
        string text,
        IReadOnlyList<string> evidenceIds,
        IReadOnlyDictionary<string, MachineSituationEvidenceItem> evidence,
        IReadOnlyList<MachineSituationEvidenceItem> allEvidence)
    {
        if (text.Contains('?', StringComparison.Ordinal) ||
            text.Contains('{', StringComparison.Ordinal) ||
            text.Contains('}', StringComparison.Ordinal) ||
            NonLatinScriptRegex().IsMatch(text) ||
            NonEnglishMarkers.Any(marker => $" {text} ".Contains(
                marker, StringComparison.OrdinalIgnoreCase)))
        {
            return Reject(MachineBriefValidationFailure.EnglishOnly,
                "Brief output was not bounded plain English.");
        }

        if (MutationPhrases.Any(phrase => text.Contains(
            phrase, StringComparison.OrdinalIgnoreCase)) ||
            text.Contains("HKCU", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            return Reject(MachineBriefValidationFailure.ActionBoundary,
                "Brief output crossed the controlled-action boundary.");
        }

        var cited = evidenceIds.Select(id => evidence[id]).ToArray();
        if (CausalPhrases.Any(phrase => text.Contains(
                phrase, StringComparison.OrdinalIgnoreCase)) &&
            !cited.Any(item => item.AllowsCausalLanguage &&
                IsSuppliedStatement(text, item)))
        {
            return Reject(MachineBriefValidationFailure.Causality,
                "A causal claim was not authorized by deterministic evidence. " +
                "Remove causal connectives such as because, due to, requires, " +
                "and blocking from overall and points. State unavailable or " +
                "pending status without explaining a cause.");
        }

        // Numeric/entity token membership alone cannot prove a relationship.
        // Keep these high-risk claims extractive; the model may synthesize the
        // surrounding assessment, but must not invent a comparison or transfer
        // a system event to an application merely by citing both records.
        if (ResourceComparisonRegex().IsMatch(text) &&
            !cited.Any(item => IsSuppliedStatement(text, item)))
        {
            return Reject(MachineBriefValidationFailure.ClaimGrounding,
                "Resource or learned-range comparisons must copy a complete " +
                "cited summary exactly as a separate point, or be omitted. " +
                "Remove comparison wording from overall. Do not add prefixes, " +
                "sample counts, or conclusions to the copied point. Do not derive a " +
                "comparison from current values and learned statistics.");
        }

        if (SystemLifecycleRegex().IsMatch(text))
        {
            if (RestartScheduleRegex().IsMatch(text) &&
                !cited.Any(item => IsSuppliedStatement(text, item)))
            {
                return Reject(MachineBriefValidationFailure.ClaimGrounding,
                    "Pending restart evidence does not establish a queued, " +
                    "scheduled, or automatic restart. Omit that claim.");
            }

            var namesApplication = cited.Any(item =>
                item.EntityNames.Any(name =>
                    !string.Equals(name, "Windows",
                        StringComparison.OrdinalIgnoreCase) &&
                    text.Contains(name, StringComparison.OrdinalIgnoreCase)) &&
                (item.Category == MachineSituationCategory.Recently ||
                    item.Id.StartsWith("finding.", StringComparison.Ordinal)));
            if (namesApplication &&
                !cited.Any(item => IsSuppliedStatement(text, item)))
            {
                return Reject(MachineBriefValidationFailure.ClaimGrounding,
                    "Keep application failures and system shutdown/restart " +
                    "evidence in separate statements; do not merge their " +
                    "subjects or counts.");
            }
        }

        if (MentionsEndOfDay(text) &&
            !evidenceIds.Contains("forward.end_of_day",
                StringComparer.Ordinal) &&
            !(MentionsUnavailableForecast(text) &&
                evidenceIds.Contains("learning.awareness",
                    StringComparer.Ordinal)))
        {
            return Reject(MachineBriefValidationFailure.ForecastBoundary,
                "An end-of-day claim lacked deterministic forecast evidence.");
        }

        if ((text.Contains("forecast", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("projected", StringComparison.OrdinalIgnoreCase)) &&
            NumericTokenRegex().IsMatch(text) &&
            !cited.Any(item => item.Category == MachineSituationCategory.Forward))
        {
            return Reject(MachineBriefValidationFailure.ForecastBoundary,
                "Numeric forecast or forecast-coverage claims require cited " +
                "Forward evidence. Learning sample/day counts are not " +
                "forecast duration or coverage. Omit that forecast claim.");
        }

        var allowedText = string.Join(' ', cited.SelectMany(item =>
            item.DisplayValues.Prepend(item.Summary)));
        var allowedNumbers = NumericTokenRegex().Matches(allowedText)
            .Select(match => NormalizeNumericToken(match.Value))
            .ToHashSet(StringComparer.Ordinal);
        foreach (Match number in NumericTokenRegex().Matches(text))
        {
            if (!allowedNumbers.Contains(NormalizeNumericToken(number.Value)))
            {
                return Reject(MachineBriefValidationFailure.NumericGrounding,
                    "A numeric claim was absent from its cited evidence.");
            }
        }
        foreach (Match numberWord in NumericWordRegex().Matches(text))
        {
            if (!ContainsWholeWord(allowedText, numberWord.Value))
            {
                return Reject(MachineBriefValidationFailure.NumericGrounding,
                    "A spelled-out numeric claim was absent from its cited evidence.");
            }
        }

        var allEntities = allEvidence
            .SelectMany(item => item.EntityNames.Select(name =>
                (Name: name, EvidenceId: item.Id)))
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .ToArray();
        var citedEntityNames = cited
            .SelectMany(item => item.EntityNames)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in allEntities
            .GroupBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(entity => text.Contains(
                entity.Name, StringComparison.OrdinalIgnoreCase)))
        {
            if (!citedEntityNames.Contains(entity.Name))
            {
                return Reject(MachineBriefValidationFailure.EntityGrounding,
                    $"Named entity '{entity.Name}' must cite " +
                    $"'{entity.EvidenceId}' in the same statement or be " +
                    "omitted.");
            }
        }

        var allowedEntityTokens = cited
            .SelectMany(item => item.EntityNames)
            .SelectMany(name => EntityLikeTokenRegex().Matches(name)
                .Cast<Match>())
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // A verbatim deterministic summary is already an identified fact.
        // Sentence/title capitalization (for example Pending File Rename)
        // is not a newly invented named entity in that extractive case.
        var copiesSummary = cited.Any(item => IsSuppliedStatement(text, item));
        foreach (Match token in EntityLikeTokenRegex().Matches(text))
        {
            if (!CommonCapitalizedWords.Contains(token.Value) &&
                !allowedEntityTokens.Contains(token.Value) &&
                !copiesSummary)
            {
                return Reject(MachineBriefValidationFailure.EntityGrounding,
                    $"Named entity '{token.Value}' is absent from supplied " +
                    "evidence and must be omitted.");
            }
        }

        return null;
    }

    private static MachineBriefValidationResult? ValidateEvidenceIds(
        IReadOnlyList<string> ids,
        IReadOnlyDictionary<string, MachineSituationEvidenceItem> evidence)
    {
        if (ids.Count is < 1 or > MaximumEvidenceIdsPerStatement ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Count ||
            ids.Any(id => !evidence.ContainsKey(id)))
        {
            return Reject(MachineBriefValidationFailure.EvidenceIdentity,
                "A factual statement referenced invalid evidence identity.");
        }
        return null;
    }

    private static string Normalize(string text) =>
        WhitespaceRegex().Replace(text.Trim(), " ");

    private static bool IsSuppliedStatement(
        string text,
        MachineSituationEvidenceItem item) => string.Equals(
            Normalize(text).TrimEnd('.', '!'),
            Normalize(item.Summary).TrimEnd('.', '!'),
            StringComparison.OrdinalIgnoreCase);

    private static string[] NormalizeIds(IEnumerable<string> ids) => ids
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Select(id => id.Trim())
        .ToArray();

    private static string NormalizeNumericToken(string value)
    {
        var normalized = value.Replace(",", string.Empty,
            StringComparison.Ordinal);
        return decimal.TryParse(normalized, NumberStyles.Number,
            CultureInfo.InvariantCulture, out var parsed)
                ? parsed.ToString(CultureInfo.InvariantCulture)
                : normalized;
    }

    private static bool ContainsWholeWord(string text, string word) =>
        Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool MentionsEndOfDay(string text) =>
        text.Contains("end of day", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("end-of-day", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("remaining day", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("by tonight", StringComparison.OrdinalIgnoreCase);

    private static bool MentionsUnavailableForecast(string text) =>
        text.Contains("not enough", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("insufficient", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("cannot yet", StringComparison.OrdinalIgnoreCase);

    private static int WordCount(string text) => text.Split(
        (char[]?)null,
        StringSplitOptions.RemoveEmptyEntries).Length;

    private static MachineBriefValidationResult Reject(
        MachineBriefValidationFailure failure,
        string reason) => MachineBriefValidationResult.Rejected(
            failure, reason);
}
