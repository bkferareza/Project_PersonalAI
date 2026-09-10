using System.Security.Cryptography;
using System.Text;
using Machine.Core;

namespace Machine.Inference;

public sealed partial class LocalMachineIntelligenceGenerator
{
    private async Task RecordBriefMetricAsync(
        MachineSituationSnapshot situation,
        string serializedPayload,
        LocalInferenceResult first,
        MachineBriefValidationResult firstValidation,
        LocalInferenceResult? repair,
        MachineBriefValidationResult? repairValidation,
        TimeSpan totalLatency,
        CancellationToken cancellationToken)
    {
        if (_metricRecorder is null)
        {
            return;
        }
        var results = repair is null ? [first] : new[] { first, repair };
        var fallback = repairValidation is { IsValid: false };
        var metric = new MachineAiGenerationMetric(
            DateTimeOffset.UtcNow,
            first.Model ?? repair?.Model ?? _modelName,
            _quantization,
            MachineAiRequestType.Brief,
            Fingerprint(serializedPayload),
            situation.Evidence.Count,
            Sum(results.Select(result => result.PromptTokenCount)),
            Sum(results.Select(result => result.OutputTokenCount)),
            results.Any(result => result.LoadDuration is not null),
            SumDuration(results.Select(result => result.LoadDuration)),
            SumDuration(results.Select(result =>
                result.PromptEvaluationDuration)),
            SumDuration(results.Select(result => result.GenerationDuration)),
            totalLatency,
            Average(results.Select(result =>
                result.GenerationTokensPerSecond)),
            firstValidation.IsValid,
            repair is not null,
            repairValidation?.IsValid == true,
            fallback,
            firstValidation.Failure);
        await _metricRecorder.RecordAsync(metric, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RecordExplainMetricAsync(
        MachineStateExplanationRequest request,
        LocalInferenceResult result,
        bool isValid,
        TimeSpan totalLatency,
        CancellationToken cancellationToken)
    {
        if (_metricRecorder is null)
        {
            return;
        }
        var metric = new MachineAiGenerationMetric(
            DateTimeOffset.UtcNow,
            result.Model ?? _modelName,
            _quantization,
            MachineAiRequestType.Explain,
            Fingerprint(CreateUserMessage(request)),
            CountExplanationEvidence(request),
            result.PromptTokenCount,
            result.OutputTokenCount,
            result.LoadDuration is not null,
            result.LoadDuration,
            result.PromptEvaluationDuration,
            result.GenerationDuration,
            totalLatency,
            result.GenerationTokensPerSecond,
            isValid,
            RepairAttempted: false,
            RepairSucceeded: false,
            FallbackUsed: !isValid,
            isValid ? MachineBriefValidationFailure.None :
                result.ContainsToolCalls
                    ? MachineBriefValidationFailure.ActionBoundary
                    : MachineBriefValidationFailure.ClaimGrounding);
        await _metricRecorder.RecordAsync(metric, cancellationToken)
            .ConfigureAwait(false);
    }

    private static int CountExplanationEvidence(
        MachineStateExplanationRequest request) => new object?[]
        {
            request.Storage, request.Software, request.Startup,
            request.Findings, request.LearnedContext, request.Network,
            request.Session, request.Health, request.History, request.Gpu,
            request.EnergyCost, request.CurrentInsight
        }.Count(item => item is not null) + 2;

    private static string Fingerprint(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(value)))[..16];

    private static int? Sum(IEnumerable<int?> values)
    {
        var present = values.Where(value => value is not null)
            .Select(value => value!.Value).ToArray();
        return present.Length == 0 ? null : present.Sum();
    }

    private static TimeSpan? SumDuration(IEnumerable<TimeSpan?> values)
    {
        var present = values.Where(value => value is not null)
            .Select(value => value!.Value).ToArray();
        return present.Length == 0 ? null :
            TimeSpan.FromTicks(present.Sum(value => value.Ticks));
    }

    private static double? Average(IEnumerable<double?> values)
    {
        var present = values.Where(value => value is not null)
            .Select(value => value!.Value).ToArray();
        return present.Length == 0 ? null : present.Average();
    }
}
