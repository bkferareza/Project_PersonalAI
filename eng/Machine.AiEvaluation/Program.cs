using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Machine.AiEvaluation;
using Machine.Core;
using Machine.Inference;

var options = EvaluationOptions.Parse(args);
Directory.CreateDirectory(options.OutputDirectory);

if (options.ExportDataset)
{
    var dataset = await DatasetExporter.ExportAsync(Path.Combine(
        options.OutputDirectory, "dataset"));
    Console.WriteLine($"Dataset: {dataset.TrainExamples} train, " +
        $"{dataset.ValidationExamples} validation, " +
        $"{dataset.HeldOutExamples} held-out; SHA-256 {dataset.Sha256}");
    if (options.DatasetOnly)
    {
        return;
    }
}

var configuration = BundledInferenceConfiguration.LoadFromManifests(
    options.RuntimeManifestPath,
    options.ModelManifestPath,
    options.RuntimeDirectory,
    options.ModelDirectory);
await using var runtime = new BundledLlamaInferenceRuntime(configuration);
var recording = new RecordingInferenceRuntime(runtime);
var generator = new LocalMachineIntelligenceGenerator(
    recording, configuration.ModelAlias);
var scenarioResults = new List<MatasuriScenarioResult>();

foreach (var scenario in MatasuriScenarioCorpus.Create())
{
    var firstResultIndex = recording.Results.Count;
    var stopwatch = Stopwatch.StartNew();
    var brief = await generator.GenerateAsync(new(
        scenario.Situation,
        $"{configuration.ModelName}:{configuration.Quantization}:" +
            configuration.ModelSha256,
        $"{configuration.RuntimeVersion}:{configuration.RuntimeCommit}"));
    stopwatch.Stop();

    var raw = recording.Results.Skip(firstResultIndex).ToArray();
    var first = raw.FirstOrDefault() ?? new LocalInferenceResult(
        null, null, Failure: new(LocalInferenceFailureKind.InvalidResponse,
            "No response was recorded."));
    var firstAssessment = BriefAssessment.Assess(first, scenario.Situation);
    var finalIds = brief.OverallEvidenceIds
        .Concat(brief.Points.SelectMany(point => point.EvidenceIds))
        .Concat(brief.OutlookEvidenceIds)
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    var expectation = scenario.Expectation;
    var requiredCovered = expectation.RequiredEvidenceIds.All(id =>
        finalIds.Contains(id, StringComparer.Ordinal));
    var forbiddenSelected = expectation.ForbiddenEvidenceIds.Any(id =>
        finalIds.Contains(id, StringComparer.Ordinal)) ||
        finalIds.Any(id => !expectation.AllowedEvidenceIds.Contains(
            id, StringComparer.Ordinal));
    var outlookMet = expectation.OutlookAvailable
        ? brief.Outlook is not null
        : brief.Outlook is null;
    var repairSucceeded = brief.Diagnostics.RepairAttempted &&
        brief.Source == MachineExplanationSource.LocalModel;
    scenarioResults.Add(new(
        scenario.Id,
        scenario.Category,
        firstAssessment.Structured,
        firstAssessment.Validation.IsValid,
        firstAssessment.Validation.Failure,
        brief.Diagnostics.RepairAttempted,
        repairSucceeded,
        brief.Source == MachineExplanationSource.DeterministicFallback,
        requiredCovered,
        forbiddenSelected,
        outlookMet,
        stopwatch.ElapsedMilliseconds,
        first.LoadDuration is { } load ? (long)load.TotalMilliseconds : null,
        first.PromptEvaluationDuration is { } prompt
            ? (long)prompt.TotalMilliseconds : null,
        raw.Sum(result => result.GenerationDuration?.TotalMilliseconds ?? 0d)
            is var generation && generation > 0d ? (long)generation : null,
        raw.Sum(result => result.PromptTokenCount ?? 0) is var promptTokens &&
            promptTokens > 0 ? promptTokens : null,
        raw.Sum(result => result.OutputTokenCount ?? 0) is var outputTokens &&
            outputTokens > 0 ? outputTokens : null,
        raw.Where(result => result.GenerationTokensPerSecond is not null)
            .Select(result => result.GenerationTokensPerSecond!.Value)
            .DefaultIfEmpty().Average() is var rate && rate > 0d ? rate : null,
        brief.Overall,
        brief.Points.Select(point => point.Text).ToArray(),
        brief.Outlook,
        finalIds));
    Console.WriteLine($"{scenario.Id}: {brief.Diagnostics.ValidationState}, " +
        $"{stopwatch.Elapsed.TotalSeconds:F1}s");
}

var status = await runtime.GetStatusAsync();
var latencies = scenarioResults.Select(result =>
    (double)result.TotalLatencyMilliseconds).Order().ToArray();
var report = new MatasuriModelEvaluationReport(
    1,
    DateTimeOffset.UtcNow,
    configuration.ModelName,
    configuration.Quantization,
    configuration.ModelSizeBytes,
    configuration.ModelSha256,
    configuration.RuntimeVersion,
    configuration.RuntimeCommit,
    scenarioResults.Count,
    scenarioResults.Count(result => result.FirstPassStructurallyValid),
    scenarioResults.Count(result => result.FirstPassGroundingValid),
    scenarioResults.Count(result => result.RepairRequired),
    scenarioResults.Count(result => result.RepairSucceeded),
    scenarioResults.Count(result => result.FallbackRequired),
    Attempts(MachineBriefValidationFailure.NumericGrounding),
    Attempts(MachineBriefValidationFailure.EntityGrounding),
    Attempts(MachineBriefValidationFailure.Causality),
    Attempts(MachineBriefValidationFailure.ActionBoundary),
    scenarioResults.Count(result => result.RequiredEvidenceCovered),
    scenarioResults.Count(result => result.ForbiddenEvidenceSelected),
    latencies.Average(),
    Percentile(latencies, 0.5d),
    Percentile(latencies, 0.95d),
    scenarioResults.Select(result => result.ColdLoadMilliseconds)
        .FirstOrDefault(value => value is not null),
    status.LoadedModels.FirstOrDefault()?.ResidentBytes,
    scenarioResults);

var stem = Slug(configuration.ModelName) + "-" +
    Slug(configuration.Quantization);
var jsonPath = Path.Combine(options.OutputDirectory, stem + ".json");
var markdownPath = Path.Combine(options.OutputDirectory, stem + ".md");
await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report,
    new JsonSerializerOptions { WriteIndented = true }));
await File.WriteAllTextAsync(markdownPath, Markdown(report));
Console.WriteLine($"Report: {jsonPath}");
await runtime.RequestUnloadAsync();

int Attempts(MachineBriefValidationFailure failure) =>
    scenarioResults.Count(result => result.FirstPassFailure == failure);

static double Percentile(double[] sorted, double percentile)
{
    if (sorted.Length == 0)
    {
        return 0d;
    }
    var rank = (int)Math.Ceiling(percentile * sorted.Length) - 1;
    return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
}

static string Slug(string value) => new(value.ToLowerInvariant()
    .Select(character => char.IsLetterOrDigit(character) ? character : '-')
    .ToArray());

static string Markdown(MatasuriModelEvaluationReport report)
{
    var builder = new StringBuilder();
    builder.AppendLine($"# {report.ModelName} Matasuri evaluation");
    builder.AppendLine();
    builder.AppendLine($"- Quantization: {report.Quantization}");
    builder.AppendLine($"- Scenarios: {report.TotalScenarios}");
    builder.AppendLine($"- First-pass grounded: " +
        $"{Percent(report.FirstPassGroundingValid, report.TotalScenarios)}");
    builder.AppendLine($"- Repair required: " +
        $"{Percent(report.RepairRequired, report.TotalScenarios)}");
    builder.AppendLine($"- Fallback required: " +
        $"{Percent(report.FallbackRequired, report.TotalScenarios)}");
    builder.AppendLine($"- Required evidence covered: " +
        $"{Percent(report.RequiredEvidenceCovered, report.TotalScenarios)}");
    builder.AppendLine($"- Median latency: " +
        $"{report.MedianLatencyMilliseconds / 1000d:F2}s");
    builder.AppendLine($"- P95 latency: " +
        $"{report.P95LatencyMilliseconds / 1000d:F2}s");
    builder.AppendLine();
    builder.AppendLine("| Scenario | First pass | Repair | Fallback | Evidence | Latency |");
    builder.AppendLine("| --- | --- | --- | --- | --- | ---: |");
    foreach (var result in report.Scenarios)
    {
        builder.AppendLine($"| {result.Category} | " +
            $"{(result.FirstPassGroundingValid ? "Pass" : result.FirstPassFailure)} | " +
            $"{(result.RepairRequired ? result.RepairSucceeded ? "Pass" : "Failed" : "No")} | " +
            $"{(result.FallbackRequired ? "Yes" : "No")} | " +
            $"{(result.RequiredEvidenceCovered && !result.ForbiddenEvidenceSelected ? "Pass" : "Failed")} | " +
            $"{result.TotalLatencyMilliseconds / 1000d:F2}s |");
    }
    return builder.ToString();
}

static string Percent(int count, int total) => total == 0 ? "n/a" :
    (100d * count / total).ToString("F1", CultureInfo.InvariantCulture) + "%";

internal sealed record EvaluationOptions(
    string RuntimeManifestPath,
    string ModelManifestPath,
    string RuntimeDirectory,
    string ModelDirectory,
    string OutputDirectory,
    bool ExportDataset,
    bool DatasetOnly)
{
    public static EvaluationOptions Parse(string[] args)
    {
        var root = FindRepositoryRoot();
        string Value(string name, string fallback)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length
                ? Path.GetFullPath(args[index + 1])
                : fallback;
        }
        return new(
            Value("--runtime-manifest", Path.Combine(root, "eng", "inference",
                "runtime-manifest.json")),
            ResolveModelManifest(args, root, Value),
            Value("--runtime-directory", Path.Combine(root, "artifacts",
                "local-inference", "runtime", "b10724", "cuda12-x64")),
            Value("--model-directory", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Matasuri", "Inference", "Models")),
            Value("--output", Path.Combine(root, "artifacts", "ai-evaluation")),
            args.Contains("--export-dataset", StringComparer.Ordinal),
            args.Contains("--dataset-only", StringComparer.Ordinal));
    }

    private static string ResolveModelManifest(
        string[] args,
        string root,
        Func<string, string, string> value)
    {
        var explicitManifest = Array.IndexOf(args, "--model-manifest");
        if (explicitManifest >= 0)
        {
            return value("--model-manifest", string.Empty);
        }

        var modelIndex = Array.IndexOf(args, "--model");
        if (modelIndex < 0 || modelIndex + 1 >= args.Length)
        {
            return Path.Combine(root, "eng", "inference",
                "model-manifest.json");
        }

        var catalogPath = Path.Combine(root, "eng", "inference",
            "model-catalog.json");
        using var document = JsonDocument.Parse(File.ReadAllText(catalogPath));
        var selected = args[modelIndex + 1];
        var entry = document.RootElement.GetProperty("models")
            .EnumerateArray().FirstOrDefault(model => string.Equals(
                model.GetProperty("id").GetString(), selected,
                StringComparison.Ordinal));
        if (entry.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException($"Unknown model catalog ID '{selected}'.");
        }
        return Path.Combine(Path.GetDirectoryName(catalogPath)!,
            entry.GetProperty("manifestFile").GetString()!);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "Machine.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException(
            "Could not locate the repository root.");
    }
}
