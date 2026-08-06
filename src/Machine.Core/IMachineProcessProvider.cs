namespace Machine.Core;

public interface IMachineProcessProvider
{
    Task<IReadOnlyList<MachineProcessSnapshot>> GetTopAsync(
        int count,
        CancellationToken cancellationToken = default);
}
