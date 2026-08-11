using System.Globalization;
using System.Text.RegularExpressions;

namespace Machine.Core;

public static class MachineExplanationValidator
{
    public const int MaximumWordCount = 55;
    public const int MaximumSentenceCount = 2;

    private static readonly string[] ProhibitedLanguage =
    [
        "wala akong right",
        "wala akong karapatan",
        "hindi ko kayang",
        "hindi ako pwedeng",
        "hindi ako puwedeng",
        "hindi ako allowed",
        "i cannot",
        "i can't",
        "not allowed",
        "permission",
        "permiso",
        "authority",
        "authorized",
        "right to",
        "sabihin mo lang",
        "just say",
        "let me know",
        "ask me",
        "allow me",
        "pwede kong",
        "puwede kong",
        "maaari kong",
        "kaya kong",
        "pwede mong",
        "puwede mong",
        "you can",
        "you should",
        "consider ",
        "try ",
        "i can fix",
        "i can stop",
        "i can optimize",
        "i can clean",
        "i can delete",
        "i can uninstall",
        "i can disable",
        "kung gusto mo",
        "gusto mo bang",
        "would you like",
        "i-fix",
        "aayusin",
        "maaayos ko",
        "ayusin",
        "i-stop",
        "ihihinto",
        "itigil",
        "i-optimize",
        "optimize",
        "lilinisin",
        "linisin",
        "clean up",
        "cleanup",
        "tatanggalin",
        "tanggalin",
        "uninstall",
        "disable",
        "delete",
        "burahin",
        "recommend",
        "suggest",
        "dapat mong",
        "subukan mong",
        "masamang resources",
        "nakakabahala",
        "nag-aalala",
        "language model",
        "artificial intelligence",
        "ollama",
        "bilang ai",
        "isang ai"
    ];

    private static readonly string[] CausalLanguage =
    [
        " kasi",
        "dahil",
        "because",
        "due to",
        "caused by",
        "causes",
        "triggered by",
        "driven by",
        "contributes to",
        "leading to",
        "sanhi",
        "dahilan",
        "dulot ng",
        "gawa ng",
        "resulta ng",
        "responsible",
        "nag-o-occupy",
        "sila ang",
        "kumakain ng",
        "nagpapataas",
        "nagpapabagal",
        "taking up",
        " para hindi",
        "para maiwasan",
        " kaya "
    ];

    private static readonly string[] StableStateClaims =
    [
        "stable",
        "all good",
        "walang issue",
        "no issue",
        "okay ang",
        "ok ang",
        "normal ang",
        "maayos ang takbo",
        "kalma ang takbo"
    ];

    private static readonly string[] AttentionStateClaims =
    [
        "attention",
        "medyo busy",
        "busy ako",
        "busy ang",
        "alerto",
        "needs attention",
        "kailangan ng pansin"
    ];

    private static readonly string[] WarningStateClaims =
    [
        "warning",
        "under pressure",
        "babala"
    ];

    private static readonly string[] CriticalStateClaims =
    [
        "critical",
        "kritikal",
        "malubha",
        "severe",
        "seryoso"
    ];

    private static readonly string[] CpuPressureClaims =
    [
        "high cpu",
        "cpu is high",
        "cpu usage is high",
        "mataas ang cpu",
        "cpu pressure"
    ];

    private static readonly string[] MemoryPressureClaims =
    [
        "high memory",
        "memory is high",
        "memory usage is high",
        "mataas ang memory",
        "memory pressure"
    ];

    private static readonly string[] StoragePressureClaims =
    [
        "low storage",
        "storage is low",
        "mababa ang storage",
        "low free space",
        "kulang ang storage",
        "limited storage"
    ];

    private static readonly string[] UnsupportedNetworkLanguage =
    [
        "download",
        "upload",
        "streaming",
        "gaming",
        "suspicious",
        "kahina-hinala",
        "disconnect",
        "mag-disconnect",
        "network problem",
        "network issue",
        "network pressure",
        "traffic problem",
        "traffic issue",
        "traffic warning",
        "application traffic",
        "app traffic",
        "browser traffic",
        "using the network",
        "uses the network",
        "using the internet",
        "uses the internet",
        "network usage by",
        "internet usage by"
    ];

    private static readonly Regex CpuThenPercentage = new(
        @"\bcpu\b[^.!?%]{0,60}?(?<value>\d{1,3}(?:\.\d+)?)\s*(?:%|percent|porsyento)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex PercentageThenCpu = new(
        @"(?<value>\d{1,3}(?:\.\d+)?)\s*(?:%|percent|porsyento)[^.!?%]{0,30}?\bcpu\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex MemoryThenPercentage = new(
        @"\b(?:memory|ram)\b[^.!?%]{0,60}?(?<value>\d{1,3}(?:\.\d+)?)\s*(?:%|percent|porsyento)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex PercentageThenMemory = new(
        @"(?<value>\d{1,3}(?:\.\d+)?)\s*(?:%|percent|porsyento)[^.!?%]{0,30}?\b(?:memory|ram)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex AiReference = new(
        @"\bai\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex QuestionLanguage = new(
        @"\b(?:ba|kumusta)\b|(?:^|[.!]\s*)(?:ano|alin|bakit|paano|kailan|saan|sino|who|what|when|where|why|how|would|could|can|should|is|are|do|does|did)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex CausalParticle = new(
        @"\b(?:kasi|kaya|therefore|thus)\b|\bpara\s+(?:hindi|maiwasan)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex PersonalizedComparisonLanguage = new(
        @"\b(?:usual|normal\s+for\s+me|typically|karaniwan|kumpara|compared|observed\s+pattern)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static bool IsValid(
        string? text,
        IReadOnlyList<string> currentProcessNames,
        MachineFindingsSnapshot? findings,
        MachineStorageExplanationContext? storage = null,
        MachineResourceSnapshot? resources = null,
        MachineLearnedContext? learnedContext = null,
        MachineNetworkInsightContext? network = null)
    {
        ArgumentNullException.ThrowIfNull(currentProcessNames);

        if (string.IsNullOrWhiteSpace(text) ||
            text.Any(char.IsControl) ||
            CountWords(text) > MaximumWordCount ||
            CountSentences(text) > MaximumSentenceCount ||
            text.Contains('?') ||
            QuestionLanguage.IsMatch(text) ||
            ContainsAny(text, ProhibitedLanguage) ||
            AiReference.IsMatch(text) ||
            ContainsProcessName(text, currentProcessNames) ||
            ContradictsVerifiedContext(text, findings) ||
            ConflatesMemoryAndStorage(text) ||
            InventsFolderScanResult(text, storage) ||
            ContainsIncorrectResourcePercentage(text, resources) ||
            ContainsUnsupportedNetworkClaim(text, network) ||
            ContainsUnsupportedPersonalizedComparison(
                text,
                learnedContext,
                network))
        {
            return false;
        }

        if (!ContainsCausalLanguage(text))
        {
            return true;
        }

        var textWithoutVerifiedDetails = text;

        foreach (var finding in findings?.Findings ?? [])
        {
            if (!string.IsNullOrWhiteSpace(finding.Detail))
            {
                textWithoutVerifiedDetails =
                    textWithoutVerifiedDetails.Replace(
                        finding.Detail,
                        string.Empty,
                        StringComparison.OrdinalIgnoreCase);
            }
        }

        return !ContainsCausalLanguage(textWithoutVerifiedDetails);
    }

    private static bool ContainsUnsupportedPersonalizedComparison(
        string text,
        MachineLearnedContext? learnedContext,
        MachineNetworkInsightContext? network)
    {
        if (!PersonalizedComparisonLanguage.IsMatch(text))
        {
            return false;
        }

        if (learnedContext is null ||
            learnedContext.Confidence != MachineLearningConfidence.Established ||
            learnedContext.SampleCount <= 0)
        {
            return true;
        }

        if (!MentionsNetwork(text))
        {
            return false;
        }

        return network is null ||
            learnedContext.DominantNetworkActivityClass is not
                (MachineNetworkActivityClass.Quiet or
                 MachineNetworkActivityClass.Light or
                 MachineNetworkActivityClass.Active) ||
            learnedContext.DominantNetworkActivityCount <
                MachineNetworkActivityClassifier.MinimumDominantObservationCount ||
            learnedContext.NetworkObservationCount <
                learnedContext.DominantNetworkActivityCount;
    }

    private static bool ContainsUnsupportedNetworkClaim(
        string text,
        MachineNetworkInsightContext? network)
    {
        if (ContainsAny(text, UnsupportedNetworkLanguage))
        {
            return true;
        }

        if (!MentionsNetwork(text))
        {
            return false;
        }

        if (network is null)
        {
            return true;
        }

        var sentences = text.Split(
            ['.', '!', '?'],
            StringSplitOptions.RemoveEmptyEntries);
        return sentences.Any(sentence =>
            MentionsNetwork(sentence) && ContainsAny(
                sentence,
                [
                    "attention",
                    "warning",
                    "critical",
                    "anomal",
                    "problem",
                    "issue",
                    "pressure",
                    "productive",
                    "good",
                    "bad",
                    "masama",
                    "maganda"
                ]));
    }

    private static bool MentionsNetwork(string text) =>
        ContainsAny(
            text,
            ["network", "traffic", "internet", "throughput"]);

    private static bool ContradictsVerifiedContext(
        string text,
        MachineFindingsSnapshot? findings)
    {
        var state = findings?.OverallState ??
            MachineOverallState.Unknown;

        var contradictsState = state switch
        {
            MachineOverallState.Stable =>
                ContainsAny(text, AttentionStateClaims) ||
                ContainsAny(text, WarningStateClaims) ||
                ContainsAny(text, CriticalStateClaims),
            MachineOverallState.Attention =>
                ContainsAny(text, StableStateClaims) ||
                ContainsAny(text, WarningStateClaims) ||
                ContainsAny(text, CriticalStateClaims),
            MachineOverallState.Warning =>
                ContainsAny(text, StableStateClaims) ||
                ContainsAny(text, AttentionStateClaims) ||
                ContainsAny(text, CriticalStateClaims),
            MachineOverallState.Critical =>
                ContainsAny(text, StableStateClaims) ||
                ContainsAny(text, AttentionStateClaims) ||
                ContainsAny(text, WarningStateClaims),
            _ =>
                ContainsAny(text, StableStateClaims) ||
                ContainsAny(text, AttentionStateClaims) ||
                ContainsAny(text, WarningStateClaims) ||
                ContainsAny(text, CriticalStateClaims)
        };

        if (contradictsState)
        {
            return true;
        }

        var findingCodes = (findings?.Findings ?? [])
            .Select(finding => finding.Code)
            .ToHashSet(StringComparer.Ordinal);

        if (ContradictsPartialFindings(text, findingCodes))
        {
            return true;
        }

        return (!findingCodes.Contains("cpu.usage.high") &&
                ContainsAny(text, CpuPressureClaims)) ||
            (!findingCodes.Contains("memory.usage.high") &&
                ContainsAny(text, MemoryPressureClaims)) ||
            (!findingCodes.Contains(
                    "storage.system-volume.low-free-space") &&
                ContainsAny(text, StoragePressureClaims));
    }

    private static bool ContradictsPartialFindings(
        string text,
        IReadOnlySet<string> findingCodes)
    {
        var hasFolderPartial = findingCodes.Contains(
            "data.folder-scan.partial");
        var hasClassicSoftwarePartial = findingCodes.Contains(
            "data.software.classic.partial");
        var hasPackagedSoftwarePartial = findingCodes.Contains(
            "data.software.packaged.partial");
        var hasStartupPartial = findingCodes.Contains(
            "data.startup.partial");

        if (!hasFolderPartial &&
            !hasClassicSoftwarePartial &&
            !hasPackagedSoftwarePartial &&
            !hasStartupPartial)
        {
            return false;
        }

        if (ClaimsCompleteData(
            text,
            [
                "current inventory",
                "latest inventory",
                "inventory data",
                "all inventory"
            ]))
        {
            return true;
        }

        return (hasFolderPartial && ClaimsCompleteData(
                text,
                ["folder", "scan", "storage inspection"])) ||
            (hasClassicSoftwarePartial && ClaimsCompleteData(
                text,
                ["classic software", "classic inventory"])) ||
            (hasPackagedSoftwarePartial && ClaimsCompleteData(
                text,
                ["packaged", "package inventory"])) ||
            ((hasClassicSoftwarePartial ||
              hasPackagedSoftwarePartial) && ClaimsCompleteData(
                text,
                ["software inventory"])) ||
            (hasStartupPartial && ClaimsCompleteData(
                text,
                ["startup"]));
    }

    private static bool ClaimsCompleteData(
        string text,
        IReadOnlyList<string> subjects)
    {
        var sentences = text.Split(
            ['.', '!', '?'],
            StringSplitOptions.RemoveEmptyEntries);

        return sentences.Any(sentence =>
            ContainsAny(sentence, subjects) &&
            ContainsAny(
                sentence,
                ["complete", "kumpleto", "fully scanned", "buo ang"]) &&
            !ContainsAny(
                sentence,
                [
                    "incomplete",
                    "not complete",
                    "not yet complete",
                    "hindi kumpleto",
                    "hindi pa kumpleto",
                    "di kumpleto",
                    "hindi buo"
                ]));
    }

    private static bool ConflatesMemoryAndStorage(string text)
    {
        var sentences = text.Split(
            ['.', '!', '?'],
            StringSplitOptions.RemoveEmptyEntries);

        return sentences.Any(sentence =>
            (sentence.Contains(
                "memory",
                StringComparison.OrdinalIgnoreCase) ||
             sentence.Contains(
                "ram",
                StringComparison.OrdinalIgnoreCase)) &&
            (sentence.Contains(
                "drive",
                StringComparison.OrdinalIgnoreCase) ||
             sentence.Contains(
                "disk",
                StringComparison.OrdinalIgnoreCase) ||
             sentence.Contains(
                "storage",
                StringComparison.OrdinalIgnoreCase) ||
             sentence.Contains(
                "free space",
                StringComparison.OrdinalIgnoreCase) ||
             sentence.Contains(
                "available space",
                StringComparison.OrdinalIgnoreCase)));
    }

    private static bool InventsFolderScanResult(
        string text,
        MachineStorageExplanationContext? storage)
    {
        if (storage?.LargeFolderScan is not null)
        {
            return false;
        }

        var mentionsFolderScan = text.Contains(
            "scan",
            StringComparison.OrdinalIgnoreCase) ||
            text.Contains(
                "folder",
                StringComparison.OrdinalIgnoreCase);

        if (!mentionsFolderScan)
        {
            return false;
        }

        return ContainsAny(
            text,
            [
                "scan found",
                "scan did not find",
                "no folders found",
                "no large folder",
                "walang nakita",
                "walang malaking folder"
            ]);
    }

    private static bool ContainsIncorrectResourcePercentage(
        string text,
        MachineResourceSnapshot? resources)
    {
        var cpuClaims = GetPercentageClaims(
            text,
            CpuThenPercentage,
            PercentageThenCpu);
        var memoryClaims = GetPercentageClaims(
            text,
            MemoryThenPercentage,
            PercentageThenMemory);

        if (cpuClaims.Count == 0 && memoryClaims.Count == 0)
        {
            return false;
        }

        if (resources is null ||
            !double.IsFinite(resources.CpuUsagePercent) ||
            resources.TotalMemoryBytes == 0 ||
            resources.UsedMemoryBytes > resources.TotalMemoryBytes)
        {
            return true;
        }

        if (cpuClaims.Any(claim =>
            !ApproximatelyEquals(
                claim.Value,
                resources.CpuUsagePercent)))
        {
            return true;
        }

        var usedMemoryPercent = resources.UsedMemoryBytes /
            (double)resources.TotalMemoryBytes * 100d;
        var availableMemoryPercent = 100d - usedMemoryPercent;

        return memoryClaims.Any(claim =>
        {
            var describesUsedMemory = ContainsAny(
                claim.Text,
                ["used", "using", "usage", "gumagamit", "gamit"]);
            var describesAvailableMemory = ContainsAny(
                claim.Text,
                ["available", "free", "bakante"]);

            if (describesUsedMemory)
            {
                return !ApproximatelyEquals(
                    claim.Value,
                    usedMemoryPercent);
            }

            if (describesAvailableMemory)
            {
                return !ApproximatelyEquals(
                    claim.Value,
                    availableMemoryPercent);
            }

            return !ApproximatelyEquals(
                    claim.Value,
                    usedMemoryPercent) &&
                !ApproximatelyEquals(
                    claim.Value,
                    availableMemoryPercent);
        });
    }

    private static IReadOnlyList<PercentageClaim>
        GetPercentageClaims(
            string text,
            Regex metricThenPercentage,
            Regex percentageThenMetric) =>
        metricThenPercentage.Matches(text)
            .Concat(percentageThenMetric.Matches(text))
            .Select(match => new PercentageClaim(
                double.Parse(
                    match.Groups["value"].Value,
                    CultureInfo.InvariantCulture),
                match.Value))
            .ToArray();

    private static bool ApproximatelyEquals(
        double actual,
        double expected) =>
        Math.Abs(actual - expected) <= 1d;

    private static int CountWords(string text) =>
        text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries).Length;

    private static int CountSentences(string text)
    {
        var count = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '!')
            {
                count++;
                continue;
            }

            if (text[index] != '.')
            {
                continue;
            }

            var isDecimalPoint = index > 0 &&
                index < text.Length - 1 &&
                char.IsDigit(text[index - 1]) &&
                char.IsDigit(text[index + 1]);

            if (!isDecimalPoint)
            {
                count++;
            }
        }

        return count;
    }

    private static bool ContainsAny(
        string text,
        IReadOnlyList<string> values) =>
        values.Any(value => text.Contains(
            value,
            StringComparison.OrdinalIgnoreCase));

    private static bool ContainsCausalLanguage(string text) =>
        ContainsAny(text, CausalLanguage) ||
        CausalParticle.IsMatch(text);

    private static bool ContainsProcessName(
        string text,
        IReadOnlyList<string> currentProcessNames) =>
        currentProcessNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Any(name => text.Contains(
                name,
                StringComparison.OrdinalIgnoreCase));

    private sealed record PercentageClaim(double Value, string Text);
}
