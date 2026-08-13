namespace Machine.Core;

public sealed record MachineFolderMeasurementExplanationContext(
    string Name,
    long MeasuredSizeBytes,
    bool IsComplete);
