namespace Machine.Core;

public interface IMachineUsageOutlookGenerator
{
    Task<MachineUsageOutlook> GenerateAsync(
        MachineUsageOutlookRequest request,
        CancellationToken cancellationToken = default);
}
