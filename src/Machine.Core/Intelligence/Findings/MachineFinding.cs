namespace Machine.Core;

public sealed record MachineFinding(
    string Code,
    MachineFindingSeverity Severity,
    string Title,
    string Detail);
