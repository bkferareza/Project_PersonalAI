using System.Text.Json;
using Machine.AiEvaluation;

namespace Machine.Tests;

public sealed class MatasuriEvaluationHarnessTests
{
    [Fact]
    public void CorpusCoversEveryRequiredProductScenario()
    {
        var scenarios = MatasuriScenarioCorpus.Create();
        var categories = scenarios.Select(scenario => scenario.Category)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(14, scenarios.Count);
        Assert.Equal(14, scenarios.Select(scenario => scenario.Id)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.All(scenarios, scenario =>
        {
            Assert.True(scenario.HeldOut);
            Assert.NotEmpty(scenario.Situation.Evidence);
            Assert.All(scenario.Expectation.RequiredEvidenceIds, id =>
                Assert.Contains(scenario.Situation.Evidence,
                    evidence => evidence.Id == id));
        });
        Assert.Contains("Normal machine", categories);
        Assert.Contains("Single localized reliability issue", categories);
        Assert.Contains("Broader system issue", categories);
        Assert.Contains("Learned deviation", categories);
        Assert.Contains("Early learning", categories);
        Assert.Contains("Established learning", categories);
        Assert.Contains("Recurring pattern", categories);
        Assert.Contains("Forecast available", categories);
        Assert.Contains("Forecast unavailable", categories);
        Assert.Contains("High power but normal", categories);
        Assert.Contains("Low power but abnormal", categories);
        Assert.Contains("Self-health incident", categories);
        Assert.Contains("Recent verified action", categories);
        Assert.Contains("Conflicting and complex signals", categories);
    }

    [Fact]
    public async Task DatasetIsBoundedSanitizedAndDeterministicallySplit()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "matasuri-ai-dataset-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await DatasetExporter.ExportAsync(directory, 560);
            Assert.Equal(448, result.TrainExamples);
            Assert.Equal(56, result.ValidationExamples);
            Assert.Equal(56, result.HeldOutExamples);
            Assert.Equal(64, result.Sha256.Length);

            var allText = string.Join('\n', Directory.GetFiles(directory)
                .SelectMany(File.ReadAllLines));
            Assert.DoesNotContain("ip_address", allText,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("latitude", allText,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("longitude", allText,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("api_key", allText,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("raw_history", allText,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("raw_learning", allText,
                StringComparison.OrdinalIgnoreCase);

            var heldOutIds = File.ReadLines(Path.Combine(directory,
                    "held-out.jsonl"))
                .Select(line => JsonDocument.Parse(line))
                .Select(document => document.RootElement
                    .GetProperty("ScenarioId").GetString())
                .ToHashSet(StringComparer.Ordinal);
            var trainingIds = File.ReadLines(Path.Combine(directory,
                    "train.jsonl"))
                .Select(line => JsonDocument.Parse(line))
                .Select(document => document.RootElement
                    .GetProperty("ScenarioId").GetString())
                .ToHashSet(StringComparer.Ordinal);
            Assert.Empty(heldOutIds.Intersect(trainingIds,
                StringComparer.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
