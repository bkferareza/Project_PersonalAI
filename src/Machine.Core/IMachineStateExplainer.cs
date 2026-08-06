namespace Machine.Core;

public interface IMachineStateExplainer
{
    Task<MachineStateExplanation> ExplainAsync(
        MachineStateExplanationRequest request,
        CancellationToken cancellationToken = default);
}
