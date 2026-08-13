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
        "isang ai",
        "palagi",
        "always"
    ];

    private static readonly string[] CausalLanguage =
    [
        " kasi",
        "dahil",
        "because",
        "due to",
        "caused by",
        "caused ",
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

    private static readonly string[] UnsupportedHealthLanguage =
    [
        "brownout",
        "power outage",
        "power loss",
        "power supply",
        "psu",
        "driver blame",
        "driver failure caused",
        "faulty driver",
        "driver is faulty",
        "driver problem",
        "driver issue",
        "update caused",
        "caused the crash",
        "broke the system",
        " is broken",
        " is unstable",
        "sinira ang",
        "nasira ang",
        "sirang ",
        "restart now",
        "reboot now",
        "restart immediately",
        "reboot immediately",
        "please restart",
        "please reboot",
        "restart your",
        "reboot your",
        "restart the computer",
        "reboot the computer",
        "restart windows",
        "reboot windows",
        "restart will fix",
        "restarting will fix",
        "rebooting will fix",
        "repair windows",
        "repair the app",
        "repair the application",
        "run repair",
        "needs repair",
        "fix windows",
        "fix the app",
        "fix the application",
        "fix the crash",
        "mag-restart",
        "mag-reboot",
        "i-restart",
        "i-reboot",
        "install updates",
        "update windows now"
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

    private static readonly Regex CpuThenPercentageRange = new(
        @"\bcpu\b[^.!?%]{0,60}?(?<low>\d{1,3}(?:\.\d+)?)\s*(?:-|to|\u2013|\u2014)\s*(?<high>\d{1,3}(?:\.\d+)?)\s*(?:%|percent|porsyento)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex PercentageRangeThenCpu = new(
        @"(?<low>\d{1,3}(?:\.\d+)?)\s*(?:-|to|\u2013|\u2014)\s*(?<high>\d{1,3}(?:\.\d+)?)\s*(?:%|percent|porsyento)[^.!?%]{0,30}?\bcpu\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex MemoryThenPercentageRange = new(
        @"\b(?:memory|ram)\b[^.!?%]{0,60}?(?<low>\d{1,3}(?:\.\d+)?)\s*(?:-|to|\u2013|\u2014)\s*(?<high>\d{1,3}(?:\.\d+)?)\s*(?:%|percent|porsyento)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex PercentageRangeThenMemory = new(
        @"(?<low>\d{1,3}(?:\.\d+)?)\s*(?:-|to|\u2013|\u2014)\s*(?<high>\d{1,3}(?:\.\d+)?)\s*(?:%|percent|porsyento)[^.!?%]{0,30}?\b(?:memory|ram)\b",
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
        @"\b(?:usual|usually|normal\s+(?:for\s+me|ko)|normally|typical|typically|karaniwan|kumpara|compared|learned\s+pattern|observed\s+pattern|over\s+time|magkakahawig|similar\s+(?:learned\s+)?behavior)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex PatternLanguage = new(
        @"\b(?:learned\s+pattern|observed\s+pattern|broader\s+pattern|magkakahawig|similar\s+(?:learned\s+)?behavior)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex HistoricalLanguage = new(
        @"\b(?:historical|historically|stale|dati|noon|previously)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex RecentComparisonLanguage = new(
        @"\b(?:recent|recently|recent average|historical average|last\s+(?:7|seven)\s+days?|kumpara sa recent|recent na average)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex HealthCountClaim = new(
        @"\b(?<value>\d+|zero|one|two|three|four|five|six|seven|eight|nine|ten)\s+(?:application\s+|app\s+)?(?<kind>pending\s+updates?|updates?\s+available|crashes?\s+or\s+hangs?|crashes?|hangs?|unexpected\s+shutdowns?|update\s+failures?|hardware\s+(?:errors?|failures?))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex GpuUtilizationClaim = new(
        @"\bgpu(?:\s+(?:utilization|usage|load))?\b[^.!?%]{0,40}?(?<value>\d{1,3}(?:\.\d+)?)\s*(?:%|percent)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex GpuMemoryClaim = new(
        @"\b(?:vram|gpu\s+memory)\b[^.!?%]{0,40}?(?<value>\d{1,3}(?:\.\d+)?)\s*(?:%|percent)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex GpuTemperatureClaim = new(
        @"\b(?:gpu\s+temperature|temperature)\b[^.!?\d]{0,24}(?<value>\d{1,3}(?:\.\d+)?)\s*(?:°\s*)?(?:c|celsius)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex GpuPowerClaim = new(
        @"\b(?:gpu\s+board\s+power|board\s+power|gpu\s+power)\b[^.!?\d]{0,24}(?<value>\d{1,4}(?:\.\d+)?)\s*(?:w|watts?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex GpuReference = new(
        @"\b(?:gpu|vram)\b|\bboard\s+power\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static bool IsValid(
        string? text,
        IReadOnlyList<string> currentProcessNames,
        MachineFindingsSnapshot? findings,
        MachineStorageExplanationContext? storage = null,
        MachineResourceSnapshot? resources = null,
        MachineLearnedContext? learnedContext = null,
        MachineNetworkInsightContext? network = null,
        MachineHealthInsightContext? health = null,
        MachineHistoryInsightContext? history = null,
        MachineGpuInsightContext? gpu = null)
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
            ContainsProcessName(text, currentProcessNames, health) ||
            ContradictsVerifiedContext(text, findings) ||
            ConflatesMemoryAndStorage(text) ||
            InventsFolderScanResult(text, storage) ||
            ContainsIncorrectResourcePercentage(
                text,
                resources,
                learnedContext) ||
            ContainsUnsupportedNetworkClaim(text, network) ||
            ContainsUnsupportedHealthClaim(text, health) ||
            ContainsIncorrectHealthCount(text, health) ||
            ContainsUnsupportedHistoryClaim(text, history) ||
            ContainsUnsupportedGpuClaim(text, gpu, history) ||
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

    private static bool ContainsUnsupportedHistoryClaim(
        string text,
        MachineHistoryInsightContext? history)
    {
        if (!RecentComparisonLanguage.IsMatch(text))
        {
            return false;
        }

        return history?.RecentComparable is null;
    }

    private static bool ContainsUnsupportedGpuClaim(
        string text,
        MachineGpuInsightContext? gpu,
        MachineHistoryInsightContext? history)
    {
        if (!GpuReference.IsMatch(text))
        {
            return false;
        }

        if (ContainsAny(text,
            ["gpu pressure", "gpu warning", "gpu critical",
             "gpu is stable", "stable gpu", "gpu problem", "gpu issue"]))
        {
            return true;
        }

        var utilization = AllowedGpuValues(
            gpu?.UtilizationPercent,
            history?.CurrentPeriod.GpuMeanPercent,
            history?.RecentComparable?.GpuMeanPercent);
        var memory = AllowedGpuValues(
            gpu?.MemoryUtilizationPercent,
            history?.CurrentPeriod.GpuMemoryMeanPercent,
            history?.RecentComparable?.GpuMemoryMeanPercent);
        var temperature = AllowedGpuValues(
            gpu?.TemperatureCelsius,
            history?.CurrentPeriod.GpuTemperatureMeanCelsius,
            history?.RecentComparable?.GpuTemperatureMeanCelsius);
        var power = AllowedGpuValues(
            gpu?.BoardPowerWatts,
            history?.CurrentPeriod.GpuBoardPowerMeanWatts,
            history?.RecentComparable?.GpuBoardPowerMeanWatts);

        if (utilization.Count == 0 && memory.Count == 0 &&
            temperature.Count == 0 && power.Count == 0)
        {
            return true;
        }

        return HasUnsupportedGpuValue(text, GpuUtilizationClaim, utilization) ||
            HasUnsupportedGpuValue(text, GpuMemoryClaim, memory) ||
            HasUnsupportedGpuValue(text, GpuTemperatureClaim, temperature) ||
            HasUnsupportedGpuValue(text, GpuPowerClaim, power);
    }

    private static IReadOnlyList<double> AllowedGpuValues(
        params double?[] values) => values
        .Where(value => value is not null && double.IsFinite(value.Value))
        .Select(value => value!.Value)
        .ToArray();

    private static bool HasUnsupportedGpuValue(
        string text,
        Regex pattern,
        IReadOnlyList<double> allowedValues) => pattern.Matches(text)
        .Select(match => double.TryParse(
            match.Groups["value"].Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
                ? value
                : double.NaN)
        .Any(claim => !double.IsFinite(claim) ||
            !allowedValues.Any(value => ApproximatelyEquals(claim, value)));

    private static bool ContainsUnsupportedPersonalizedComparison(
        string text,
        MachineLearnedContext? learnedContext,
        MachineNetworkInsightContext? network)
    {
        if (!PersonalizedComparisonLanguage.IsMatch(text))
        {
            return false;
        }

        if (ContainsAny(text,
        [
            "anomal",
            "unusual",
            "abnormal",
            "problema",
            "problem",
            "issue"
        ]))
        {
            return true;
        }

        if (learnedContext is null)
        {
            return true;
        }

        MachineNetworkActivityClass? learnedNetworkClass;
        long learnedDominantNetworkCount;
        long learnedNetworkObservationCount;
        if (PatternLanguage.IsMatch(text))
        {
            var pattern = learnedContext.MatchingBroaderPattern;
            if (pattern is null ||
                pattern.Confidence != MachineLearningConfidence.Established ||
                pattern.Freshness == MachineLearningFreshness.Stale)
            {
                return true;
            }

            learnedNetworkClass = pattern.DominantNetworkActivityClass;
            learnedDominantNetworkCount =
                pattern.DominantNetworkActivityCount;
            learnedNetworkObservationCount =
                pattern.NetworkObservationCount;
        }
        else
        {
            var profile = learnedContext.MatchingProfile;
            if (profile is null ||
                profile.Confidence != MachineLearningConfidence.Established ||
                profile.LifetimeSampleCount <= 0 ||
                profile.Freshness == MachineLearningFreshness.Stale &&
                    !HistoricalLanguage.IsMatch(text))
            {
                return true;
            }

            learnedNetworkClass = profile.DominantNetworkActivityClass;
            learnedDominantNetworkCount =
                profile.DominantNetworkActivityCount;
            learnedNetworkObservationCount =
                profile.NetworkObservationCount;
        }

        if (!MentionsNetwork(text))
        {
            return false;
        }

        return network is null ||
            learnedNetworkClass is not
                (MachineNetworkActivityClass.Quiet or
                 MachineNetworkActivityClass.Light or
                 MachineNetworkActivityClass.Active) ||
            learnedDominantNetworkCount <
                MachineNetworkActivityClassifier.MinimumDominantObservationCount ||
            learnedNetworkObservationCount < learnedDominantNetworkCount;
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

    private static bool ContainsUnsupportedHealthClaim(
        string text,
        MachineHealthInsightContext? health)
    {
        if (ContainsAny(text, UnsupportedHealthLanguage))
        {
            return true;
        }

        var mentionsRestart = ContainsAny(
            text,
            ["restart", "reboot", "pending restart"]);
        var mentionsUpdate = ContainsAny(
            text,
            ["windows update", "updates available", "pending update",
             "update failure", "update failures", "up to date"]);
        var mentionsReliability = ContainsAny(
            text,
            ["crash", "hang", "unexpected shutdown", "hardware error",
             "hardware failure", "reliability"]);
        var mentionsCombinedApplicationFailure = ContainsAny(
            text,
            ["crash or hang", "crashes or hangs"]);
        if (health is null)
        {
            return mentionsRestart || mentionsUpdate || mentionsReliability;
        }

        if (mentionsRestart)
        {
            var claimsPending = ContainsAny(
                text,
                ["restart pending", "pending restart", "reboot pending",
                 "pending windows restart",
                 "requires restart", "restart required"]);
            var claimsNoPending = ContainsAny(
                text,
                ["no restart pending", "no pending restart",
                 "walang pending restart"]);
            if (claimsPending && health.IsRebootPending != true ||
                claimsNoPending && health.IsRebootPending != false)
            {
                return true;
            }
        }

        if (text.Contains(
                "up to date",
                StringComparison.OrdinalIgnoreCase) &&
            health.UpdateState != MachineWindowsUpdateState.UpToDate)
        {
            return true;
        }

        var claimsNoPendingUpdates = ContainsAny(
            text,
            ["no pending updates", "no updates available",
             "walang pending update"]);
        if (claimsNoPendingUpdates &&
            (health.UpdateVerifiedAt is null ||
             health.PendingUpdateCount != 0) ||
            !claimsNoPendingUpdates &&
            ContainsAny(text, ["updates available", "pending update"]) &&
            (health.PendingUpdateCount is null or <= 0))
        {
            return true;
        }

        var counts = health.ReliabilityLast7Days;
        var claimsNoCrashes = ContainsAny(
            text,
            ["no application crashes", "no app crashes", "no crashes",
             "walang crash"]);
        var claimsNoHangs = ContainsAny(
            text,
            ["no application hangs", "no app hangs", "no hangs",
             "walang hang"]);
        var claimsNoCombinedApplicationFailure = ContainsAny(
            text,
            ["no crashes or hangs", "no crash or hang",
             "walang crash o hang"]);
        var claimsNoUnexpectedShutdown = ContainsAny(
            text,
            ["no unexpected shutdown", "walang unexpected shutdown"]);
        var claimsNoHardwareFailure = ContainsAny(
            text,
            ["no hardware errors", "no hardware failures",
             "walang hardware error"]);
        var claimsNoUpdateFailure = ContainsAny(
            text,
            ["no update failures", "walang update failure"]);
        var claimsNoReliabilityIncident = ContainsAny(
            text,
            ["no reliability incidents"]);
        var hasPositiveAbsenceClaim = claimsNoCrashes || claimsNoHangs ||
            claimsNoCombinedApplicationFailure ||
            claimsNoUnexpectedShutdown || claimsNoHardwareFailure ||
            claimsNoUpdateFailure || claimsNoReliabilityIncident;
        if (hasPositiveAbsenceClaim &&
            health.ReliabilityDataStatus !=
                MachineHealthDataStatus.Complete ||
            claimsNoCrashes &&
                (counts?.ApplicationCrashCount ?? -1) != 0 ||
            claimsNoHangs &&
                (counts?.ApplicationHangCount ?? -1) != 0 ||
            claimsNoCombinedApplicationFailure &&
                ((counts?.ApplicationCrashCount ?? -1) != 0 ||
                 (counts?.ApplicationHangCount ?? -1) != 0) ||
            claimsNoUnexpectedShutdown &&
                (counts?.UnexpectedShutdownCount ?? -1) != 0 ||
            claimsNoHardwareFailure &&
                (counts?.HardwareFailureCount ?? -1) != 0 ||
            claimsNoUpdateFailure &&
                (counts?.UpdateFailureCount ?? -1) != 0 ||
            claimsNoReliabilityIncident &&
                (counts?.TotalIncidentCount ?? -1) != 0)
        {
            return true;
        }

        if (!claimsNoUnexpectedShutdown &&
            ContainsAny(text, ["unexpected shutdown"]) &&
            (counts?.UnexpectedShutdownCount ?? 0) == 0 &&
            health.MostRecentSignificantIncident?.Category !=
                MachineReliabilityIncidentCategory.UnexpectedShutdown)
        {
            return true;
        }

        if (!claimsNoHardwareFailure &&
            ContainsAny(text, ["hardware error", "hardware failure"]) &&
            (counts?.HardwareFailureCount ?? 0) == 0 &&
            health.MostRecentSignificantIncident?.Category !=
                MachineReliabilityIncidentCategory.HardwareFailure)
        {
            return true;
        }

        if (!claimsNoUpdateFailure &&
            ContainsAny(text, ["update failure", "update failures"]) &&
            (counts?.UpdateFailureCount ?? 0) == 0)
        {
            return true;
        }

        if (ContainsAny(text, ["crash", "crashes"]) &&
            !claimsNoCrashes &&
            !claimsNoCombinedApplicationFailure &&
            !mentionsCombinedApplicationFailure &&
            (counts?.ApplicationCrashCount ?? 0) == 0 &&
            health.MostRecentSignificantIncident?.Category !=
                MachineReliabilityIncidentCategory.ApplicationCrash)
        {
            return true;
        }

        if (ContainsAny(text, ["hang", "hangs"]) &&
            !claimsNoHangs &&
            !claimsNoCombinedApplicationFailure &&
            !mentionsCombinedApplicationFailure &&
            (counts?.ApplicationHangCount ?? 0) == 0 &&
            health.MostRecentSignificantIncident?.Category !=
                MachineReliabilityIncidentCategory.ApplicationHang)
        {
            return true;
        }

        if (mentionsCombinedApplicationFailure &&
            !claimsNoCombinedApplicationFailure &&
            (counts?.ApplicationCrashCount ?? 0) +
                (counts?.ApplicationHangCount ?? 0) == 0 &&
            health.MostRecentSignificantIncident?.Category is not
                (MachineReliabilityIncidentCategory.ApplicationCrash or
                 MachineReliabilityIncidentCategory.ApplicationHang) &&
            health.RecurringApplicationFailure is null)
        {
            return true;
        }

        return false;
    }

    private static bool ContainsIncorrectHealthCount(
        string text,
        MachineHealthInsightContext? health)
    {
        var matches = HealthCountClaim.Matches(text);
        if (matches.Count == 0)
        {
            return false;
        }

        if (health is not { } verifiedHealth)
        {
            return true;
        }
        var counts = verifiedHealth.ReliabilityLast7Days;

        foreach (Match match in matches)
        {
            if (!TryParseHealthCount(
                    match.Groups["value"].Value,
                    out var claimed))
            {
                return true;
            }

            var kind = match.Groups["kind"].Value;
            if (kind.Contains("pending", StringComparison.OrdinalIgnoreCase) ||
                kind.Contains("available", StringComparison.OrdinalIgnoreCase))
            {
                if (verifiedHealth.PendingUpdateCount is not
                        { } pendingCount ||
                    claimed != pendingCount)
                {
                    return true;
                }
                continue;
            }

            if (counts is null)
            {
                return true;
            }

            if (kind.Contains(" or ", StringComparison.OrdinalIgnoreCase))
            {
                var combined = counts.ApplicationCrashCount +
                    counts.ApplicationHangCount;
                var recurringApplication =
                    verifiedHealth.RecurringApplicationFailure;
                var mentionsRecurringApplication = recurringApplication is
                        not null &&
                    MentionsApplication(
                        text,
                        recurringApplication.ApplicationName);
                var verifiedCombined = mentionsRecurringApplication
                    ? recurringApplication!.IncidentCountLast7Days
                    : combined;
                if (claimed != verifiedCombined)
                {
                    return true;
                }
                continue;
            }

            var verified = kind.Contains(
                    "unexpected",
                    StringComparison.OrdinalIgnoreCase)
                ? counts.UnexpectedShutdownCount
                : kind.Contains(
                    "update",
                    StringComparison.OrdinalIgnoreCase)
                    ? counts.UpdateFailureCount
                    : kind.Contains(
                        "hardware",
                        StringComparison.OrdinalIgnoreCase)
                        ? counts.HardwareFailureCount
                        : kind.StartsWith(
                            "hang",
                            StringComparison.OrdinalIgnoreCase)
                            ? counts.ApplicationHangCount
                            : counts.ApplicationCrashCount;
            if (claimed != verified)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseHealthCount(
        string value,
        out int count)
    {
        if (int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out count))
        {
            return true;
        }

        count = value.ToLowerInvariant() switch
        {
            "zero" => 0,
            "one" => 1,
            "two" => 2,
            "three" => 3,
            "four" => 4,
            "five" => 5,
            "six" => 6,
            "seven" => 7,
            "eight" => 8,
            "nine" => 9,
            "ten" => 10,
            _ => -1
        };
        return count >= 0;
    }

    private static bool MentionsApplication(
        string text,
        string applicationName)
    {
        if (text.Contains(
                applicationName,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var withoutExtension = Path.GetFileNameWithoutExtension(
            applicationName);
        return !string.IsNullOrWhiteSpace(withoutExtension) &&
            text.Contains(
                withoutExtension,
                StringComparison.OrdinalIgnoreCase);
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
        MachineResourceSnapshot? resources,
        MachineLearnedContext? learnedContext)
    {
        var cpuClaims = GetPercentageClaims(
            text,
            CpuThenPercentage,
            PercentageThenCpu);
        var memoryClaims = GetPercentageClaims(
            text,
            MemoryThenPercentage,
            PercentageThenMemory);
        var cpuRangeClaims = GetPercentageRangeClaims(
            text,
            CpuThenPercentageRange,
            PercentageRangeThenCpu);
        var memoryRangeClaims = GetPercentageRangeClaims(
            text,
            MemoryThenPercentageRange,
            PercentageRangeThenMemory);

        if (cpuClaims.Count == 0 && memoryClaims.Count == 0 &&
            cpuRangeClaims.Count == 0 && memoryRangeClaims.Count == 0)
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

        var usesLearnedLanguage =
            PersonalizedComparisonLanguage.IsMatch(text);
        var learnedCpu = GetLearnedCpuEvidence(
            text,
            learnedContext,
            usesLearnedLanguage);
        var learnedMemory = GetLearnedMemoryEvidence(
            text,
            learnedContext,
            usesLearnedLanguage);
        if (cpuRangeClaims.Any(claim =>
                learnedCpu.Range is null ||
                !ApproximatelyEquals(claim.Low, learnedCpu.Range.Low) ||
                !ApproximatelyEquals(claim.High, learnedCpu.Range.High)) ||
            memoryRangeClaims.Any(claim =>
                learnedMemory.Range is null ||
                !ApproximatelyEquals(claim.Low, learnedMemory.Range.Low) ||
                !ApproximatelyEquals(claim.High, learnedMemory.Range.High)))
        {
            return true;
        }

        var allowedCpuValues = new List<double>
        {
            resources.CpuUsagePercent
        };
        AddLearnedValues(allowedCpuValues, learnedCpu);
        if (cpuClaims.Any(claim => !allowedCpuValues.Any(value =>
            ApproximatelyEquals(claim.Value, value))))
        {
            return true;
        }

        var usedMemoryPercent = resources.UsedMemoryBytes /
            (double)resources.TotalMemoryBytes * 100d;
        var availableMemoryPercent = 100d - usedMemoryPercent;

        var allowedMemoryValues = new List<double>
        {
            usedMemoryPercent,
            availableMemoryPercent
        };
        AddLearnedValues(allowedMemoryValues, learnedMemory);
        var allowedUsedMemoryValues = new List<double>
        {
            usedMemoryPercent
        };
        AddLearnedValues(allowedUsedMemoryValues, learnedMemory);

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
                return !allowedUsedMemoryValues.Any(value =>
                    ApproximatelyEquals(claim.Value, value));
            }

            if (describesAvailableMemory)
            {
                return !ApproximatelyEquals(
                    claim.Value,
                    availableMemoryPercent);
            }

            return !allowedMemoryValues.Any(value =>
                ApproximatelyEquals(claim.Value, value));
        });
    }

    private static LearnedMetricEvidence GetLearnedCpuEvidence(
        string text,
        MachineLearnedContext? context,
        bool usesLearnedLanguage)
    {
        if (!usesLearnedLanguage || context is null)
        {
            return default;
        }

        if (PatternLanguage.IsMatch(text) &&
            context.MatchingBroaderPattern is { } pattern)
        {
            return new LearnedMetricEvidence(
                pattern.CpuTypicalRange,
                null);
        }

        return context.MatchingProfile is { } profile
            ? new LearnedMetricEvidence(
                profile.Cpu.TypicalRange,
                profile.Cpu.AdaptiveMean)
            : default;
    }

    private static LearnedMetricEvidence GetLearnedMemoryEvidence(
        string text,
        MachineLearnedContext? context,
        bool usesLearnedLanguage)
    {
        if (!usesLearnedLanguage || context is null)
        {
            return default;
        }

        if (PatternLanguage.IsMatch(text) &&
            context.MatchingBroaderPattern is { } pattern)
        {
            return new LearnedMetricEvidence(
                pattern.MemoryTypicalRange,
                null);
        }

        return context.MatchingProfile is { } profile
            ? new LearnedMetricEvidence(
                profile.Memory.TypicalRange,
                profile.Memory.AdaptiveMean)
            : default;
    }

    private static void AddLearnedValues(
        ICollection<double> values,
        LearnedMetricEvidence evidence)
    {
        if (evidence.Mean is { } mean)
        {
            values.Add(mean);
        }
        if (evidence.Range is { } range)
        {
            values.Add(range.Low);
            values.Add(range.High);
        }
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

    private static IReadOnlyList<PercentageRangeClaim>
        GetPercentageRangeClaims(
            string text,
            Regex metricThenRange,
            Regex rangeThenMetric) =>
        metricThenRange.Matches(text)
            .Concat(rangeThenMetric.Matches(text))
            .Select(match => new PercentageRangeClaim(
                double.Parse(
                    match.Groups["low"].Value,
                    CultureInfo.InvariantCulture),
                double.Parse(
                    match.Groups["high"].Value,
                    CultureInfo.InvariantCulture)))
            .ToArray();

    private static bool ApproximatelyEquals(
        double actual,
        double expected) =>
        Math.Abs(actual - expected) <= 1d;

    private readonly record struct LearnedMetricEvidence(
        MachineLearningRange? Range,
        double? Mean);

    private readonly record struct PercentageRangeClaim(
        double Low,
        double High);

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
        IReadOnlyList<string> currentProcessNames,
        MachineHealthInsightContext? health)
    {
        var allowedApplicationNames = new[]
        {
            health?.MostRecentSignificantIncident?.ApplicationName,
            health?.RecurringApplicationFailure?.ApplicationName
        }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .ToArray();

        return
        currentProcessNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Where(name => !allowedApplicationNames.Any(allowed =>
                string.Equals(
                    allowed,
                    name,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    Path.GetFileNameWithoutExtension(allowed),
                    name,
                    StringComparison.OrdinalIgnoreCase)))
            .Any(name => text.Contains(
                name,
                StringComparison.OrdinalIgnoreCase));
    }

    private sealed record PercentageClaim(double Value, string Text);
}
