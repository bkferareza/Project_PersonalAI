namespace Machine.Core;

public static class MachineExplanationValidator
{
    public const int MaximumWordCount = 45;

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
        " kaya "
    ];

    public static bool IsValid(
        string? text,
        string requiredOpening,
        IReadOnlyList<string> currentProcessNames,
        MachineFindingsSnapshot? findings)
    {
        ArgumentNullException.ThrowIfNull(currentProcessNames);

        if (string.IsNullOrWhiteSpace(text) ||
            string.IsNullOrWhiteSpace(requiredOpening) ||
            text.Any(char.IsControl) ||
            !text.StartsWith(requiredOpening, StringComparison.Ordinal) ||
            !HasOpeningBoundary(text, requiredOpening) ||
            CountWords(text) > MaximumWordCount ||
            text.Contains('?') ||
            ContainsAny(text, ProhibitedLanguage) ||
            ContainsProcessName(text, currentProcessNames))
        {
            return false;
        }

        if (!ContainsAny(text, CausalLanguage))
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

        return !ContainsAny(
            textWithoutVerifiedDetails,
            CausalLanguage);
    }

    private static bool HasOpeningBoundary(
        string text,
        string requiredOpening) =>
        text.Length == requiredOpening.Length ||
        char.IsWhiteSpace(text[requiredOpening.Length]);

    private static int CountWords(string text) =>
        text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool ContainsAny(
        string text,
        IReadOnlyList<string> values) =>
        values.Any(value => text.Contains(
            value,
            StringComparison.OrdinalIgnoreCase));

    private static bool ContainsProcessName(
        string text,
        IReadOnlyList<string> currentProcessNames) =>
        currentProcessNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Any(name => text.Contains(
                name,
                StringComparison.OrdinalIgnoreCase));
}
