using Machine.Core;

namespace Machine.App;

public enum MatasuriPresentationTheme
{
    System,
    Light,
    Dark
}

public sealed record MatasuriPresentationValidationOptions(
    MachineOverallState? State,
    bool IsGenerating,
    MatasuriPresentationTheme Theme,
    bool HasNewInsight = false,
    bool ReducedMotion = false)
{
    public static MatasuriPresentationValidationOptions Parse(
        string? arguments)
    {
        MachineOverallState? state = null;
        var isGenerating = false;
        var hasNewInsight = false;
        var reducedMotion = false;
        var theme = MatasuriPresentationTheme.System;

        foreach (var argument in (arguments ?? string.Empty).Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries))
        {
            const string statePrefix = "--matasuri-state=";
            const string themePrefix = "--matasuri-theme=";
            if (argument.StartsWith(
                    statePrefix,
                    StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<MachineOverallState>(
                    argument[statePrefix.Length..],
                    ignoreCase: true,
                    out var parsedState) &&
                Enum.IsDefined(parsedState))
            {
                state = parsedState;
            }
            else if (argument.Equals(
                "--matasuri-generating",
                StringComparison.OrdinalIgnoreCase))
            {
                isGenerating = true;
            }
            else if (argument.Equals(
                "--matasuri-new-insight",
                StringComparison.OrdinalIgnoreCase))
            {
                hasNewInsight = true;
            }
            else if (argument.Equals(
                "--matasuri-reduced-motion",
                StringComparison.OrdinalIgnoreCase))
            {
                reducedMotion = true;
            }
            else if (argument.StartsWith(
                    themePrefix,
                    StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<MatasuriPresentationTheme>(
                    argument[themePrefix.Length..],
                    ignoreCase: true,
                    out var parsedTheme) &&
                Enum.IsDefined(parsedTheme))
            {
                theme = parsedTheme;
            }
        }

        return new(
            state,
            isGenerating,
            theme,
            hasNewInsight,
            reducedMotion);
    }
}
