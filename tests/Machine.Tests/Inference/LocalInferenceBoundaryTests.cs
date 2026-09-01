using Machine.Core;
using Machine.Inference;

namespace Machine.Tests;

public sealed class LocalInferenceBoundaryTests
{
    [Fact]
    public async Task GeneratorUsesRuntimeNeutralRequest()
    {
        var runtime = new RecordingRuntime(new LocalInferenceResult(
            "No deterministic issue is visible in the current snapshot.",
            "qwen3.5:4b-runtime"));
        var generator = new LocalMachineIntelligenceGenerator(
            runtime,
            "qwen3.5:4b");

        var result = await generator.ExplainAsync(new(
            new MachineIdentity("Matasuri", "Windows 11", "X64"),
            new MachineResourceSnapshot(
                25d,
                16_000_000_000,
                8_000_000_000,
                DateTimeOffset.UnixEpoch),
            [],
            Findings: new MachineFindingsSnapshot(
                MachineOverallState.Stable,
                [])));

        Assert.Equal(MachineExplanationSource.LocalModel, result.Source);
        var request = Assert.IsType<LocalInferenceRequest>(
            runtime.LastRequest);
        Assert.Equal("qwen3.5:4b", request.Model);
        Assert.Equal(4096, request.ContextLength);
        Assert.Equal(96, request.MaximumOutputTokens);
        Assert.Equal(0.1d, request.Temperature);
        Assert.True(request.DisableReasoning);
        Assert.Equal(
            [LocalInferenceMessageRole.System, LocalInferenceMessageRole.User],
            request.Messages.Select(message => message.Role));
    }

    private sealed class RecordingRuntime(LocalInferenceResult result)
        : ILocalInferenceRuntime
    {
        public LocalInferenceRequest? LastRequest { get; private set; }

        public Task<LocalInferenceStartResult> EnsureAvailableAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalInferenceStartResult(true, false, true));

        public Task<LocalInferenceStatus> GetStatusAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LocalInferenceResult> GenerateAsync(
            LocalInferenceRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
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
