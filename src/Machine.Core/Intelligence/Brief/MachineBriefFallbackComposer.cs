namespace Machine.Core;

public static class MachineBriefFallbackComposer
{
    public static MachineBriefValidatedContent Compose(
        MachineSituationSnapshot situation)
    {
        ArgumentNullException.ThrowIfNull(situation);
        var posture = situation.Evidence.FirstOrDefault(item =>
            string.Equals(item.Id, "now.posture", StringComparison.Ordinal));
        var overall = situation.GlobalPosture switch
        {
            MachineOverallState.Stable =>
                "Everything looks normal overall.",
            MachineOverallState.Attention =>
                "The machine is stable overall, with a current condition worth watching.",
            MachineOverallState.Warning =>
                "A verified machine condition currently deserves review.",
            MachineOverallState.Critical =>
                "A serious verified machine condition currently needs attention.",
            _ =>
                "I do not yet have enough verified current evidence for a complete machine assessment."
        };
        var overallIds = posture is null
            ? situation.Evidence.Take(1).Select(item => item.Id).ToArray()
            : [posture.Id];

        var points = new List<MachineBriefPoint>();
        var significant = situation.Evidence
            .Where(item => !string.Equals(item.Id, "now.posture",
                    StringComparison.Ordinal) &&
                item.Importance >= MachineSituationImportance.Notable)
            .Take(2);
        foreach (var item in significant)
        {
            points.Add(new(item.Summary, [item.Id]));
        }

        if (points.Count < MachineBriefPromptPolicy.MaximumPointCount)
        {
            var learned = situation.Evidence.FirstOrDefault(item =>
                string.Equals(item.Id, "learned.current_context",
                    StringComparison.Ordinal)) ??
                situation.Evidence.FirstOrDefault(item =>
                    string.Equals(item.Id, "learning.awareness",
                        StringComparison.Ordinal));
            if (learned is not null &&
                points.All(point => !point.EvidenceIds.Contains(
                    learned.Id, StringComparer.Ordinal)))
            {
                points.Add(new(learned.Summary, [learned.Id]));
            }
        }

        if (points.Count == 0 && posture is not null)
        {
            points.Add(new(
                "No significant new machine-wide finding is currently selected.",
                [posture.Id]));
        }

        var forward = situation.Evidence.FirstOrDefault(item =>
            string.Equals(item.Id, "forward.next_observed_hour",
                StringComparison.Ordinal));
        return new(
            overall,
            overallIds,
            points.Take(MachineBriefPromptPolicy.MaximumPointCount).ToArray(),
            forward?.Summary,
            forward is null ? [] : [forward.Id]);
    }
}
