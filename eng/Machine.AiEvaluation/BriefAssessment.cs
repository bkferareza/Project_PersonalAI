using System.Text.Json;
using Machine.Core;

namespace Machine.AiEvaluation;

internal static class BriefAssessment
{
    public static (bool Structured, MachineBriefValidationResult Validation,
        IReadOnlyList<string> EvidenceIds) Assess(
        LocalInferenceResult result,
        MachineSituationSnapshot situation)
    {
        if (!result.IsSuccess || result.ContainsToolCalls ||
            string.IsNullOrWhiteSpace(result.Text))
        {
            return (false, MachineBriefValidationResult.Rejected(
                result.ContainsToolCalls
                    ? MachineBriefValidationFailure.ActionBoundary
                    : MachineBriefValidationFailure.Schema,
                result.Failure?.SafeMessage ?? "No usable response."), []);
        }

        if (!TryParse(result.Text, out var draft))
        {
            return (false, MachineBriefValidationResult.Rejected(
                MachineBriefValidationFailure.Schema,
                "Response was not the exact Brief JSON contract."), []);
        }

        var validation = MachineBriefValidator.Validate(draft, situation);
        var ids = (draft!.OverallEvidenceIds ?? [])
            .Concat(draft.Points?.SelectMany(point =>
                point.EvidenceIds ?? []) ?? [])
            .Concat(draft.OutlookEvidenceIds ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return (true, validation, ids);
    }

    private static bool TryParse(string json, out MachineBriefDraft? draft)
    {
        draft = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasExactProperties(root, "overall", "overall_evidence_ids",
                    "points", "outlook", "outlook_evidence_ids") ||
                root.GetProperty("points").ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var points = new List<MachineBriefDraftPoint>();
            foreach (var point in root.GetProperty("points").EnumerateArray())
            {
                if (point.ValueKind != JsonValueKind.Object ||
                    !HasExactProperties(point, "text", "evidence_ids"))
                {
                    return false;
                }
                points.Add(new(
                    OptionalString(point.GetProperty("text")),
                    Strings(point.GetProperty("evidence_ids"))));
            }

            draft = new(
                OptionalString(root.GetProperty("overall")),
                Strings(root.GetProperty("overall_evidence_ids")),
                points,
                OptionalString(root.GetProperty("outlook")),
                Strings(root.GetProperty("outlook_evidence_ids")));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? OptionalString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static string[]? Strings(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array &&
        element.EnumerateArray().All(item =>
            item.ValueKind == JsonValueKind.String)
            ? element.EnumerateArray().Select(item => item.GetString()!)
                .ToArray()
            : null;

    private static bool HasExactProperties(
        JsonElement element,
        params string[] expected)
    {
        var names = element.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        return names.Length == expected.Length &&
            names.ToHashSet(StringComparer.Ordinal).SetEquals(expected);
    }
}

internal sealed class RecordingInferenceRuntime(ILocalInferenceRuntime inner)
    : ILocalInferenceRuntime
{
    public List<LocalInferenceResult> Results { get; } = [];

    public Task<LocalInferenceStartResult> EnsureAvailableAsync(
        CancellationToken cancellationToken = default) =>
        inner.EnsureAvailableAsync(cancellationToken);

    public Task<LocalInferenceStatus> GetStatusAsync(
        CancellationToken cancellationToken = default) =>
        inner.GetStatusAsync(cancellationToken);

    public async Task<LocalInferenceResult> GenerateAsync(
        LocalInferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.GenerateAsync(request, cancellationToken);
        Results.Add(result);
        return result;
    }

    public Task RequestUnloadAsync(
        CancellationToken cancellationToken = default) =>
        inner.RequestUnloadAsync(cancellationToken);

    public Task ShutdownAsync(
        CancellationToken cancellationToken = default) =>
        inner.ShutdownAsync(cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
