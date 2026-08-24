namespace Machine.Core;

public sealed record MachineActionMutationResult(
    bool ProviderReportedSuccess,
    string? FailureCode = null)
{
    public static MachineActionMutationResult Completed() => new(true);

    public static MachineActionMutationResult Failed(string failureCode) =>
        new(false, failureCode);
}

public interface IMachineActionExecutor
{
    MachineActionCapability Capability { get; }

    MachineActionTargetKind TargetKind { get; }

    Task<MachineActionTargetState> ReadStateAsync(
        MachineActionTarget target,
        MachineActionRecoveryPayload? recoveryPayload,
        CancellationToken cancellationToken = default);

    Task<MachineActionMutationResult> ExecuteAsync(
        MachineActionPlan plan,
        CancellationToken cancellationToken = default);

    Task<MachineActionMutationResult> UndoAsync(
        MachineActionUndoPlan plan,
        CancellationToken cancellationToken = default);
}

public sealed class MachineActionExecutorRegistry
{
    private readonly IReadOnlyDictionary<
        (MachineActionCapability Capability,
         MachineActionTargetKind TargetKind),
        IMachineActionExecutor> _executors;

    public MachineActionExecutorRegistry(
        IEnumerable<IMachineActionExecutor> executors)
    {
        ArgumentNullException.ThrowIfNull(executors);
        var registered = new Dictionary<
            (MachineActionCapability, MachineActionTargetKind),
            IMachineActionExecutor>();
        foreach (var executor in executors)
        {
            ArgumentNullException.ThrowIfNull(executor);
            MachineActionGuard.RequireAllowlisted(
                executor.Capability,
                executor.TargetKind);
            if (!registered.TryAdd(
                (executor.Capability, executor.TargetKind), executor))
            {
                throw new ArgumentException(
                    "Only one explicit executor may own a capability and " +
                    "target-kind pair.", nameof(executors));
            }
        }

        _executors = registered;
    }

    public bool TryGet(
        MachineActionCapability capability,
        MachineActionTargetKind targetKind,
        out IMachineActionExecutor? executor)
    {
        if (!MachineActionGuard.IsAllowlisted(capability, targetKind))
        {
            executor = null;
            return false;
        }

        return _executors.TryGetValue(
            (capability, targetKind), out executor);
    }
}
