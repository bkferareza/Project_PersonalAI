namespace Machine.Core;

public interface IMachineFolderInspectionProvider
{
    Task<MachineFolderInspectionSnapshot> GetLargestTopLevelFoldersAsync(
        string rootPath,
        int count,
        TimeSpan timeBudget,
        CancellationToken cancellationToken = default);
}
