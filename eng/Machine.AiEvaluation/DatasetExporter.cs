using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Machine.Core;

namespace Machine.AiEvaluation;

public static class DatasetExporter
{
    public const int DefaultExampleCount = 560;
    private static readonly string[] VariantSuffixes =
        ["Alpha", "Bravo", "Cedar", "Delta", "Ember", "Flint", "Grove"];

    public static async Task<DatasetExportResult> ExportAsync(
        string outputDirectory,
        int exampleCount = DefaultExampleCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (exampleCount < 100)
        {
            throw new ArgumentOutOfRangeException(nameof(exampleCount));
        }

        Directory.CreateDirectory(outputDirectory);
        var scenarios = MatasuriScenarioCorpus.Create();
        var records = Enumerable.Range(0, exampleCount)
            .Select(index => CreateRecord(scenarios[index % scenarios.Count],
                index))
            .ToArray();
        var training = records.Where(record => record.Split == "train").ToArray();
        var validation = records.Where(record => record.Split == "validation").ToArray();
        var heldOut = records.Where(record => record.Split == "held-out").ToArray();

        var trainPath = Path.Combine(outputDirectory, "train.jsonl");
        var validationPath = Path.Combine(outputDirectory, "validation.jsonl");
        var heldOutPath = Path.Combine(outputDirectory, "held-out.jsonl");
        await WriteAsync(trainPath, training, cancellationToken);
        await WriteAsync(validationPath, validation, cancellationToken);
        await WriteAsync(heldOutPath, heldOut, cancellationToken);
        var hash = await HashAsync([trainPath, validationPath, heldOutPath],
            cancellationToken);
        return new(training.Length, validation.Length, heldOut.Length, hash);
    }

    private static DatasetRecord CreateRecord(
        MatasuriEvaluationScenario scenario,
        int index)
    {
        var split = (index % 10) switch
        {
            0 => "held-out",
            1 => "validation",
            _ => "train"
        };
        var variantId = $"{scenario.Id}-{index:D4}";
        var situation = CreateSyntheticSituation(scenario.Situation, index);
        var fallback = MachineBriefFallbackComposer.Compose(situation);
        var validation = MachineBriefValidator.Validate(new(
            fallback.Overall,
            fallback.OverallEvidenceIds,
            fallback.Points.Select(point => new MachineBriefDraftPoint(
                point.Text, point.EvidenceIds)).ToArray(),
            fallback.Outlook,
            fallback.OutlookEvidenceIds), situation);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Synthetic target '{variantId}' failed {validation.Failure}.");
        }
        var evidence = situation.Evidence.Select(item =>
            (object)new Dictionary<string, object?>
            {
                ["id"] = item.Id,
                ["category"] = item.Category.ToString(),
                ["time_scope"] = item.TimeScope.ToString(),
                ["importance"] = item.Importance.ToString(),
                ["freshness"] = item.Freshness.ToString(),
                ["maturity"] = item.Maturity.ToString(),
                ["summary"] = item.Summary,
                ["display_values"] = item.DisplayValues,
                ["entity_names"] = item.EntityNames,
                ["allows_causal_language"] = item.AllowsCausalLanguage
            }).ToArray();
        return new(
            variantId,
            split,
            MachineSituationSnapshot.CurrentSchemaVersion,
            scenario.Category,
            evidence,
            "matasuri-brief-v4/schema-v1",
            new Dictionary<string, object?>
            {
                ["overall"] = fallback.Overall,
                ["overall_evidence_ids"] = fallback.OverallEvidenceIds,
                ["points"] = fallback.Points.Select(point =>
                    new Dictionary<string, object?>
                    {
                        ["text"] = point.Text,
                        ["evidence_ids"] = point.EvidenceIds
                    }).ToArray(),
                ["outlook"] = fallback.Outlook,
                ["outlook_evidence_ids"] = fallback.OutlookEvidenceIds
            },
            fallback.OverallEvidenceIds
                .Concat(fallback.Points.SelectMany(point => point.EvidenceIds))
                .Concat(fallback.OutlookEvidenceIds)
                .Distinct(StringComparer.Ordinal).ToArray(),
            situation.LearningAwareness.CurrentContextMaturity.ToString(),
            situation.LearningAwareness.ForecastAvailability.ToString(),
            "deterministic-fallback",
            "accepted-by-construction");
    }

    private static MachineSituationSnapshot CreateSyntheticSituation(
        MachineSituationSnapshot source,
        int index)
    {
        var evidence = source.Evidence.Select(item => item with
        {
            Summary = VaryText(RewriteSummary(item.Summary), index),
            DisplayValues = item.DisplayValues
                .Select(value => VaryText(value, index)).ToArray(),
            EntityNames = item.EntityNames
                .Select(entity => VaryEntity(entity, index)).ToArray()
        }).ToArray();
        var sampleOffset = (index % 5) + 1;
        return source with
        {
            CapturedAt = source.CapturedAt.AddMinutes(index + 1),
            Evidence = evidence,
            LearningAwareness = source.LearningAwareness with
            {
                LifetimeAcceptedObservationCount = source.LearningAwareness
                    .LifetimeAcceptedObservationCount + sampleOffset,
                CurrentContextSampleCount =
                    source.LearningAwareness.CurrentContextSampleCount + sampleOffset
            }
        };
    }

    private static string RewriteSummary(string summary) => summary
        .Replace("Deterministic global posture:",
            "Current deterministic posture is", StringComparison.Ordinal)
        .Replace("Current resource use:",
            "Observed resource use shows", StringComparison.Ordinal)
        .Replace("Matasuri self-health is healthy.",
            "Matasuri runtime self-health currently reports Healthy.",
            StringComparison.Ordinal)
        .Replace("GbtCloudMatrix.exe recorded",
            "RenderWorker.exe has", StringComparison.Ordinal)
        .Replace("The system volume has",
            "Current system-volume evidence reports", StringComparison.Ordinal)
        .Replace("Today observed",
            "For this sampled duration, observed energy is",
            StringComparison.Ordinal)
        .Replace("Current-context evidence is",
            "Evidence for this sampled context is", StringComparison.Ordinal)
        .Replace("I've learned this context from",
            "This sampled context contains", StringComparison.Ordinal)
        .Replace("An Established recurring",
            "A recurring Established", StringComparison.Ordinal)
        .Replace("The next observed hour is projected at",
            "Projection for the next observed hour is", StringComparison.Ordinal)
        .Replace("The deterministic forecast is unavailable while",
            "No deterministic forecast is available because",
            StringComparison.Ordinal)
        .Replace("Current estimated wall power of",
            "Estimated current wall power at", StringComparison.Ordinal)
        .Replace("Matasuri recorded",
            "The Matasuri runtime recorded", StringComparison.Ordinal)
        .Replace("The approved Battle.net startup change was independently verified as",
            "A reviewed SyncClient startup change was independently verified as",
            StringComparison.Ordinal)
        .Replace("Today has",
            "Observed PC energy today totals", StringComparison.Ordinal);

    private static string VaryEntity(string entity, int index) => entity switch
    {
        "GbtCloudMatrix.exe" => $"RenderWorker{VariantSuffix(index)}.exe",
        "Battle.net" => $"SyncClient{VariantSuffix(index)}",
        _ => entity
    };

    private static string VaryText(string text, int index)
    {
        text = text.Replace("GbtCloudMatrix.exe",
                $"RenderWorker{VariantSuffix(index)}.exe", StringComparison.Ordinal)
            .Replace("Battle.net", $"SyncClient{VariantSuffix(index)}",
                StringComparison.Ordinal);
        var increment = (index % 5) + 1;
        return Regex.Replace(text, @"\d+(?:\.\d+)?", match =>
        {
            if (!decimal.TryParse(match.Value, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out var value))
            {
                return match.Value;
            }

            var decimals = match.Value.Contains('.')
                ? match.Value.Length - match.Value.IndexOf('.') - 1
                : 0;
            var varied = decimals == 0
                ? value + increment
                : value + increment / (decimal)Math.Pow(10, decimals);
            return varied.ToString(decimals == 0 ? "0" : $"F{decimals}",
                CultureInfo.InvariantCulture);
        });
    }

    private static string VariantSuffix(int index) =>
        VariantSuffixes[index % VariantSuffixes.Length];

    private static async Task WriteAsync(
        string path,
        IEnumerable<DatasetRecord> records,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create,
            FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await using var writer = new StreamWriter(stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(JsonSerializer.Serialize(record));
        }
    }

    private static async Task<string> HashAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in paths)
        {
            await using var stream = File.OpenRead(path);
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private sealed record DatasetRecord(
        string ScenarioId,
        string Split,
        int SituationSchemaVersion,
        string ScenarioCategory,
        object[] NormalizedEvidence,
        string RequiredOutputSchema,
        object AcceptedTargetOutput,
        IReadOnlyList<string> TargetEvidenceIds,
        string Maturity,
        string ForecastAvailability,
        string TeacherModelIdentity,
        string ValidatorResult);
}

public sealed record DatasetExportResult(
    int TrainExamples,
    int ValidationExamples,
    int HeldOutExamples,
    string Sha256);
