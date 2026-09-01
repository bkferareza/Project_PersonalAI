using Machine.Core;
using Machine.Inference;

namespace Machine.Tests;

public sealed class LocalMachineBriefGeneratorTests
{
    private const string ValidJson = """
        {
          "overall": "Everything looks normal overall.",
          "overall_evidence_ids": ["now.posture"],
          "points": [
            {
              "text": "GbtCloudMatrix.exe remains a recent reliability issue worth watching.",
              "evidence_ids": ["recent.reliability"]
            },
            {
              "text": "I've learned this context from 240 samples across 4 observed days.",
              "evidence_ids": ["learned.current_context"]
            }
          ],
          "outlook": "The next observed hour is projected at 0.150 kWh.",
          "outlook_evidence_ids": ["forward.next_observed_hour"]
        }
        """;

    [Fact]
    public async Task ValidStructuredResponseCreatesGroundedBrief()
    {
        var runtime = new QueueRuntime(Result(ValidJson));
        var generator = new LocalMachineIntelligenceGenerator(
            runtime, "qwen3.5-4b");

        var brief = await generator.GenerateAsync(Request());

        Assert.Equal(MachineExplanationSource.LocalModel, brief.Source);
        Assert.Equal(MachineBriefValidationState.Valid,
            brief.Diagnostics.ValidationState);
        Assert.False(brief.Diagnostics.RepairAttempted);
        Assert.Single(runtime.Requests);
        Assert.Equal(8192, runtime.Requests[0].ContextLength);
        Assert.Equal(320, runtime.Requests[0].MaximumOutputTokens);
        Assert.Equal("recent.reliability",
            brief.Points[0].EvidenceIds.Single());
        Assert.Equal("forward.next_observed_hour",
            brief.OutlookEvidenceIds.Single());
    }

    [Fact]
    public async Task PayloadIsBoundedNormalizedSituationOnly()
    {
        var runtime = new QueueRuntime(Result(ValidJson));
        var generator = new LocalMachineIntelligenceGenerator(
            runtime, "qwen3.5-4b");

        await generator.GenerateAsync(Request());

        var user = runtime.Requests[0].Messages.Single(message =>
            message.Role == LocalInferenceMessageRole.User).Content;
        Assert.Contains("\"selected_evidence\"", user,
            StringComparison.Ordinal);
        Assert.Contains("\"learning_awareness\"", user,
            StringComparison.Ordinal);
        Assert.Contains("\"current_context_samples\":240", user,
            StringComparison.Ordinal);
        Assert.Contains("\"allows_causal_language\":false", user,
            StringComparison.Ordinal);
        Assert.DoesNotContain("raw_observation", user,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("history_rollup", user,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ip_address", user,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recovery_payload", user,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", user,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidFirstResponseGetsOneBoundedRepair()
    {
        var invalid = ValidJson.Replace("0.150", "9.999",
            StringComparison.Ordinal);
        var runtime = new QueueRuntime(Result(invalid), Result(ValidJson));
        var generator = new LocalMachineIntelligenceGenerator(
            runtime, "qwen3.5-4b");

        var brief = await generator.GenerateAsync(Request());

        Assert.Equal(MachineExplanationSource.LocalModel, brief.Source);
        Assert.Equal(MachineBriefValidationState.Repaired,
            brief.Diagnostics.ValidationState);
        Assert.True(brief.Diagnostics.RepairAttempted);
        Assert.Equal(2, brief.Diagnostics.RequestCount);
        Assert.Equal(2, runtime.Requests.Count);
        var repair = runtime.Requests[1].Messages.Single(message =>
            message.Role == LocalInferenceMessageRole.User).Content;
        Assert.Contains("numeric claim", repair,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9.999", repair, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecondInvalidResponseUsesDeterministicFallback()
    {
        var invalidEntity = ValidJson.Replace(
            "GbtCloudMatrix.exe", "InventedApp.exe",
            StringComparison.Ordinal);
        var runtime = new QueueRuntime(
            Result(invalidEntity), Result(invalidEntity));
        var generator = new LocalMachineIntelligenceGenerator(
            runtime, "qwen3.5-4b");

        var brief = await generator.GenerateAsync(Request());

        Assert.Equal(MachineExplanationSource.DeterministicFallback,
            brief.Source);
        Assert.Equal(MachineBriefValidationState.RejectedFallback,
            brief.Diagnostics.ValidationState);
        Assert.Equal(2, runtime.Requests.Count);
        Assert.DoesNotContain("InventedApp", brief.Overall,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(brief.Points,
            point => point.Text.Contains("InventedApp",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains("deterministic fallback",
            brief.Diagnostics.ValidationReason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToolCallsNeverReachBriefAndFallBackAfterRepair()
    {
        var runtime = new QueueRuntime(
            Result(ValidJson) with { ContainsToolCalls = true },
            Result(ValidJson) with { ContainsToolCalls = true });
        var generator = new LocalMachineIntelligenceGenerator(
            runtime, "qwen3.5-4b");

        var brief = await generator.GenerateAsync(Request());

        Assert.Equal(MachineExplanationSource.DeterministicFallback,
            brief.Source);
        Assert.Equal(MachineBriefValidationState.RejectedFallback,
            brief.Diagnostics.ValidationState);
        Assert.Equal(2, runtime.Requests.Count);
    }

    [Fact]
    public async Task RuntimeTransportFailureStillReturnsDeterministicBrief()
    {
        var runtime = new QueueRuntime
        {
            ThrowOnGenerate = true
        };
        var generator = new LocalMachineIntelligenceGenerator(
            runtime, "qwen3.5-4b");

        var brief = await generator.GenerateAsync(Request());

        Assert.Equal(MachineExplanationSource.DeterministicFallback,
            brief.Source);
        Assert.Equal(MachineBriefValidationState.RejectedFallback,
            brief.Diagnostics.ValidationState);
        Assert.Equal(2, runtime.Requests.Count);
    }

    [Fact]
    public async Task PromptRemovesFactAndActionAuthority()
    {
        var runtime = new QueueRuntime(Result(ValidJson));
        var generator = new LocalMachineIntelligenceGenerator(
            runtime, "qwen3.5-4b");

        await generator.GenerateAsync(Request());

        var system = runtime.Requests[0].Messages.Single(message =>
            message.Role == LocalInferenceMessageRole.System).Content;
        Assert.Contains("English only", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Every factual statement", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never calculate", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allows_causal_language", system,
            StringComparison.Ordinal);
        Assert.Contains("Do not produce mutation advice", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not change posture", system,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not mechanically recite Task Manager metrics",
            system, StringComparison.OrdinalIgnoreCase);
    }

    private static MachineBriefRequest Request() => new(
        MachineBriefTestData.Situation(), "qwen3.5-4b", "b10724");

    private static LocalInferenceResult Result(string text) => new(
        text,
        "qwen3.5-4b",
        PromptTokenCount: 1_200,
        OutputTokenCount: 100,
        LoadDuration: TimeSpan.FromSeconds(3),
        GenerationDuration: TimeSpan.FromSeconds(2));

    private sealed class QueueRuntime(
        params LocalInferenceResult[] responses) : ILocalInferenceRuntime
    {
        private readonly Queue<LocalInferenceResult> _responses =
            new(responses);

        public List<LocalInferenceRequest> Requests { get; } = [];

        public bool ThrowOnGenerate { get; init; }

        public Task<LocalInferenceStartResult> EnsureAvailableAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalInferenceStartResult(true, false, true));

        public Task<LocalInferenceStatus> GetStatusAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalInferenceStatus(
                true,
                "Matasuri-owned llama.cpp",
                "b10724",
                LocalInferenceModelState.Ready,
                [],
                42,
                true,
                MachineBriefTestData.Now));

        public Task<LocalInferenceResult> GenerateAsync(
            LocalInferenceRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (ThrowOnGenerate)
            {
                throw new HttpRequestException("Synthetic transport failure.");
            }
            return Task.FromResult(_responses.Dequeue());
        }

        public Task RequestUnloadAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ShutdownAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
