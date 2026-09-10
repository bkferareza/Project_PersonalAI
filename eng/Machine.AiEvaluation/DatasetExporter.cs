using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Machine.Core;

namespace Machine.AiEvaluation;

public static class DatasetExporter
{
    public const int DefaultExampleCount = 560;

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
        var fallback = MachineBriefFallbackComposer.Compose(scenario.Situation);
        var evidence = scenario.Situation.Evidence.Select(item => new
        {
            item.Id,
            Category = item.Category.ToString(),
            TimeScope = item.TimeScope.ToString(),
            Importance = item.Importance.ToString(),
            Freshness = item.Freshness.ToString(),
            Maturity = item.Maturity.ToString(),
            item.Summary,
            DisplayValues = item.DisplayValues,
            EntityNames = item.EntityNames,
            item.AllowsCausalLanguage
        }).ToArray();
        return new(
            variantId,
            split,
            MachineSituationSnapshot.CurrentSchemaVersion,
            scenario.Category,
            evidence,
            "matasuri-brief-v4/schema-v1",
            new
            {
                fallback.Overall,
                fallback.OverallEvidenceIds,
                Points = fallback.Points.Select(point => new
                {
                    point.Text,
                    point.EvidenceIds
                }).ToArray(),
                fallback.Outlook,
                fallback.OutlookEvidenceIds
            },
            fallback.OverallEvidenceIds
                .Concat(fallback.Points.SelectMany(point => point.EvidenceIds))
                .Concat(fallback.OutlookEvidenceIds)
                .Distinct(StringComparer.Ordinal).ToArray(),
            scenario.Situation.LearningAwareness.CurrentContextMaturity.ToString(),
            scenario.Situation.LearningAwareness.ForecastAvailability.ToString(),
            "deterministic-fallback",
            "accepted-by-construction");
    }

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
