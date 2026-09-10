using Machine.Core;

namespace Machine.AiEvaluation;

public static class MatasuriScenarioCorpus
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<MatasuriEvaluationScenario> Create() =>
    [
        Scenario("normal-machine", "Normal machine",
        [
            Evidence("now.posture", MachineSituationCategory.Now,
                MachineSituationImportance.Routine,
                "Deterministic global posture: Stable.", ["Stable"]),
            Evidence("now.resources", MachineSituationCategory.Now,
                MachineSituationImportance.Context,
                "Current resource use: CPU 12.0%; memory 44.0%.",
                ["12.0%", "44.0%"]),
            Evidence("self.health", MachineSituationCategory.SelfHealth,
                MachineSituationImportance.Routine,
                "Matasuri self-health is healthy.", ["Healthy"])
        ], required: ["now.posture"]),

        Scenario("localized-reliability", "Single localized reliability issue",
        [
            Posture(),
            Evidence("recent.reliability", MachineSituationCategory.Recently,
                MachineSituationImportance.Important,
                "GbtCloudMatrix.exe recorded 4 application failures in the last 7 days.",
                ["4 application failures", "7 days"], ["GbtCloudMatrix.exe"]),
            Evidence("now.resources", MachineSituationCategory.Now,
                MachineSituationImportance.Context,
                "Current resource use: CPU 9.0%; memory 41.0%.",
                ["9.0%", "41.0%"])
        ], required: ["now.posture", "recent.reliability"]),

        Scenario("broader-system-issue", "Broader system issue",
        [
            Evidence("now.posture", MachineSituationCategory.Now,
                MachineSituationImportance.Critical,
                "Deterministic global posture: Warning.", ["Warning"]),
            Evidence("finding.storage", MachineSituationCategory.Now,
                MachineSituationImportance.Critical,
                "The system volume has 3.2% free space remaining.", ["3.2%"])
        ], MachineOverallState.Warning,
            required: ["now.posture", "finding.storage"]),

        Scenario("learned-deviation", "Learned deviation",
        [
            Posture(),
            Evidence("learned.power-deviation",
                MachineSituationCategory.LearnedNormal,
                MachineSituationImportance.Notable,
                "Today observed 0.920 kWh, above the Established learned range of 0.610-0.740 kWh for the same observed duration.",
                ["0.920 kWh", "0.610-0.740 kWh", "Established"])
        ], required: ["now.posture", "learned.power-deviation"]),

        Scenario("early-learning", "Early learning",
        [
            Posture(),
            Evidence("learning.awareness",
                MachineSituationCategory.LearningConfidence,
                MachineSituationImportance.Notable,
                "Current-context evidence is early with 8 samples across 1 observed day.",
                ["Early", "8 samples", "1 observed day"], maturity:
                    MachineSituationEvidenceMaturity.Early)
        ], required: ["now.posture", "learning.awareness"],
            uncertaintyRequired: true, early: true),

        Scenario("established-learning", "Established learning",
        [
            Posture(),
            Evidence("learned.current-context",
                MachineSituationCategory.LearnedNormal,
                MachineSituationImportance.Context,
                "I've learned this context from 920 samples across 18 observed days.",
                ["920 samples", "18 observed days", "Established"], maturity:
                    MachineSituationEvidenceMaturity.Established)
        ], required: ["now.posture", "learned.current-context"], established: true),

        Scenario("recurring-pattern", "Recurring pattern",
        [
            Posture(),
            Evidence("learned.recurring-pattern",
                MachineSituationCategory.LearnedNormal,
                MachineSituationImportance.Notable,
                "An Established recurring Active pattern spans 09:00-12:00 across 9 observed days.",
                ["Established", "09:00-12:00", "9 observed days"], maturity:
                    MachineSituationEvidenceMaturity.Established)
        ], required: ["now.posture", "learned.recurring-pattern"], established: true),

        Scenario("forecast-available", "Forecast available",
        [
            Posture(),
            Evidence("forward.next-observed-hour", MachineSituationCategory.Forward,
                MachineSituationImportance.Context,
                "The next observed hour is projected at 0.118 kWh and ~₱1.74.",
                ["0.118 kWh", "~₱1.74", "Provisional"], maturity:
                    MachineSituationEvidenceMaturity.Provisional)
        ], required: ["now.posture", "forward.next-observed-hour"],
            outlookAvailable: true),

        Scenario("forecast-unavailable", "Forecast unavailable",
        [
            Posture(),
            Evidence("forward.unavailable", MachineSituationCategory.Forward,
                MachineSituationImportance.Context,
                "The deterministic forecast is unavailable while future power evidence is missing.",
                ["Unavailable", "Missing future power evidence"])
        ], required: ["now.posture"], forbidden: ["forward.next-observed-hour"],
            forecastUnavailable: true, uncertaintyRequired: true),

        Scenario("high-power-normal", "High power but normal",
        [
            Posture(),
            Evidence("learned.power-comparison",
                MachineSituationCategory.LearnedNormal,
                MachineSituationImportance.Context,
                "Current estimated wall power of 312 W is within the Established learned range of 280-340 W for this context.",
                ["312 W", "280-340 W", "Established"], maturity:
                    MachineSituationEvidenceMaturity.Established)
        ], required: ["now.posture", "learned.power-comparison"], established: true),

        Scenario("low-power-abnormal", "Low power but abnormal",
        [
            Posture(),
            Evidence("learned.power-comparison",
                MachineSituationCategory.LearnedNormal,
                MachineSituationImportance.Notable,
                "Current estimated wall power of 92 W is above the Established learned range of 58-76 W for this context.",
                ["92 W", "58-76 W", "Established"], maturity:
                    MachineSituationEvidenceMaturity.Established)
        ], required: ["now.posture", "learned.power-comparison"], established: true),

        Scenario("self-health", "Self-health incident",
        [
            Posture(),
            Evidence("self.health", MachineSituationCategory.SelfHealth,
                MachineSituationImportance.Notable,
                "Matasuri recorded 1 self-health incident; global machine posture remains Stable.",
                ["1 self-health incident", "Stable"], ["Matasuri"])
        ], required: ["now.posture", "self.health"],
            forbidden: ["recent.reliability"]),

        Scenario("recent-action", "Recent verified action",
        [
            Posture(),
            Evidence("action.outcome", MachineSituationCategory.ActionOutcome,
                MachineSituationImportance.Context,
                "The approved Battle.net startup change was independently verified as disabled.",
                ["disabled"], ["Battle.net"])
        ], required: ["now.posture", "action.outcome"]),

        Scenario("complex-signals", "Conflicting and complex signals",
        [
            Posture(),
            Evidence("recent.reliability", MachineSituationCategory.Recently,
                MachineSituationImportance.Important,
                "GbtCloudMatrix.exe recorded 6 application failures in the last 7 days.",
                ["6 application failures", "7 days"], ["GbtCloudMatrix.exe"]),
            Evidence("today.energy", MachineSituationCategory.Today,
                MachineSituationImportance.Context,
                "Today has 1.140 kWh of observed PC energy and ~₱16.85 estimated cost.",
                ["1.140 kWh", "~₱16.85"]),
            Evidence("forward.next-observed-hour", MachineSituationCategory.Forward,
                MachineSituationImportance.Context,
                "The next observed hour is projected at 0.105 kWh.",
                ["0.105 kWh", "Provisional"], maturity:
                    MachineSituationEvidenceMaturity.Provisional),
            Evidence("self.health", MachineSituationCategory.SelfHealth,
                MachineSituationImportance.Routine,
                "Matasuri self-health is healthy.", ["Healthy"])
        ], required: ["now.posture", "recent.reliability"],
            outlookAvailable: true)
    ];

    private static MatasuriEvaluationScenario Scenario(
        string id,
        string category,
        IReadOnlyList<MachineSituationEvidenceItem> evidence,
        MachineOverallState posture = MachineOverallState.Stable,
        IReadOnlyList<string>? required = null,
        IReadOnlyList<string>? forbidden = null,
        bool outlookAvailable = false,
        bool uncertaintyRequired = false,
        bool early = false,
        bool established = false,
        bool forecastUnavailable = false)
    {
        var allowed = evidence.Select(item => item.Id).ToArray();
        return new(id, category,
            new(MachineSituationSnapshot.CurrentSchemaVersion, CapturedAt,
                posture, evidence.Count, evidence,
                Awareness(early, established, forecastUnavailable)),
            new(required ?? [], allowed, forbidden ?? [], outlookAvailable,
                uncertaintyRequired));
    }

    private static MachineSituationEvidenceItem Posture() => Evidence(
        "now.posture", MachineSituationCategory.Now,
        MachineSituationImportance.Routine,
        "Deterministic global posture: Stable.", ["Stable"]);

    private static MachineSituationEvidenceItem Evidence(
        string id,
        MachineSituationCategory category,
        MachineSituationImportance importance,
        string summary,
        IReadOnlyList<string> values,
        IReadOnlyList<string>? entities = null,
        MachineSituationEvidenceMaturity maturity =
            MachineSituationEvidenceMaturity.Verified) => new(
        id, category, Scope(category), importance,
        category == MachineSituationCategory.Recently
            ? MachineSituationFreshness.Recent
            : MachineSituationFreshness.Current,
        maturity, summary, values, entities ?? [],
        AllowsCausalLanguage: false);

    private static MachineSituationTimeScope Scope(
        MachineSituationCategory category) => category switch
        {
            MachineSituationCategory.Recently =>
                MachineSituationTimeScope.Last7Days,
            MachineSituationCategory.LearnedNormal or
                MachineSituationCategory.LearningConfidence =>
                    MachineSituationTimeScope.CurrentContext,
            MachineSituationCategory.Today => MachineSituationTimeScope.Today,
            MachineSituationCategory.Forward =>
                MachineSituationTimeScope.NextObservedHour,
            _ => MachineSituationTimeScope.Current
        };

    private static MachineLearningAwareness Awareness(
        bool early,
        bool established,
        bool forecastUnavailable) => new(
        MachineLearningMemoryState.Active,
        early ? 8 : 18_000,
        early ? 8 : 2_880,
        early ? 1 : 40,
        early ? 0 : 40,
        established ? 18 : 5,
        new(8, MachineUserActivityState.Active),
        early ? 8 : established ? 920 : 410,
        early ? 1 : established ? 18 : 5,
        early ? MachineLearningConfidence.Calibrating :
            established ? MachineLearningConfidence.Established :
                MachineLearningConfidence.Provisional,
        MachineLearningFreshness.Fresh,
        early ? MachineLearningEvidenceMaturity.Insufficient :
            established ? MachineLearningEvidenceMaturity.Established :
                MachineLearningEvidenceMaturity.Provisional,
        null,
        early ? MachineLearningPatternReadinessBlocker.InsufficientDistinctDays :
            MachineLearningPatternReadinessBlocker.NoEstablishedAdjacentContexts,
        forecastUnavailable
            ? MachineUsageForecastAvailabilityReason.MissingFuturePowerEvidence
            : MachineUsageForecastAvailabilityReason.Available,
        forecastUnavailable ? 0d : 1d);
}
