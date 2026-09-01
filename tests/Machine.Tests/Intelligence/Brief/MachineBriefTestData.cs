using Machine.Core;

namespace Machine.Tests;

internal static class MachineBriefTestData
{
    public static readonly DateTimeOffset Now =
        new(2026, 9, 1, 2, 0, 0, TimeSpan.Zero);

    public static MachineSituationSnapshot Situation(
        IReadOnlyList<MachineSituationEvidenceItem>? evidence = null,
        MachineLearningConfidence maturity =
            MachineLearningConfidence.Provisional) => new(
                MachineSituationSnapshot.CurrentSchemaVersion,
                Now,
                MachineOverallState.Stable,
                5,
                evidence ?? Evidence(),
                Awareness(maturity));

    public static IReadOnlyList<MachineSituationEvidenceItem> Evidence() =>
    [
        new(
            "now.posture",
            MachineSituationCategory.Now,
            MachineSituationTimeScope.Current,
            MachineSituationImportance.Routine,
            MachineSituationFreshness.Current,
            MachineSituationEvidenceMaturity.Verified,
            "Deterministic global posture: Stable.",
            ["Stable"],
            []),
        new(
            "now.resources",
            MachineSituationCategory.Now,
            MachineSituationTimeScope.Current,
            MachineSituationImportance.Context,
            MachineSituationFreshness.Current,
            MachineSituationEvidenceMaturity.Verified,
            "Current resource use: CPU 13.0%; memory 43.0%.",
            ["13.0%", "43.0%"],
            []),
        new(
            "recent.reliability",
            MachineSituationCategory.Recently,
            MachineSituationTimeScope.Last7Days,
            MachineSituationImportance.Notable,
            MachineSituationFreshness.Recent,
            MachineSituationEvidenceMaturity.Verified,
            "GbtCloudMatrix.exe recorded 2 recent application failures.",
            ["2 failures"],
            ["GbtCloudMatrix.exe"]),
        new(
            "learned.current_context",
            MachineSituationCategory.LearnedNormal,
            MachineSituationTimeScope.CurrentContext,
            MachineSituationImportance.Context,
            MachineSituationFreshness.Current,
            MachineSituationEvidenceMaturity.Provisional,
            "Learned current context has 240 samples across 4 observed days.",
            ["240 samples", "4 observed days", "Provisional"],
            []),
        new(
            "learning.awareness",
            MachineSituationCategory.LearningConfidence,
            MachineSituationTimeScope.CurrentContext,
            MachineSituationImportance.Context,
            MachineSituationFreshness.Current,
            MachineSituationEvidenceMaturity.Provisional,
            "Learning is active with Provisional current-context evidence.",
            ["Active", "Provisional", "240 current-context samples",
                "4 current-context observed days"],
            []),
        new(
            "forward.next_observed_hour",
            MachineSituationCategory.Forward,
            MachineSituationTimeScope.NextObservedHour,
            MachineSituationImportance.Context,
            MachineSituationFreshness.Current,
            MachineSituationEvidenceMaturity.Provisional,
            "Deterministic next observed hour forecast: 0.150 kWh; ₱2.22.",
            ["0.150 kWh", "₱2.22", "Provisional"],
            [])
    ];

    public static MachineLearningAwareness Awareness(
        MachineLearningConfidence maturity =
            MachineLearningConfidence.Provisional) => new(
                MachineLearningMemoryState.Active,
                2_400,
                2_000,
                18,
                12,
                4,
                null,
                240,
                4,
                maturity,
                MachineLearningFreshness.Fresh,
                MachineLearningEvidenceMaturity.Provisional,
                null,
                MachineLearningPatternReadinessBlocker.NoAdjacentContexts,
                MachineUsageForecastAvailabilityReason.Available,
                1d);

    public static MachineBriefDraft ValidDraft() => new(
        "Everything looks normal overall.",
        ["now.posture"],
        [
            new(
                "GbtCloudMatrix.exe remains a recent reliability issue worth watching.",
                ["recent.reliability"]),
            new(
                "I've learned this context from 240 samples across 4 observed days.",
                ["learned.current_context"])
        ],
        "The next observed hour is projected at 0.150 kWh.",
        ["forward.next_observed_hour"]);
}
