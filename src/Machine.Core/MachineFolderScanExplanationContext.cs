namespace Machine.Core;

public sealed record MachineFolderScanExplanationContext(
    IReadOnlyList<MachineFolderMeasurementExplanationContext> Folders,
    bool IsComplete);
