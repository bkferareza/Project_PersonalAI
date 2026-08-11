namespace Machine.Core;

public sealed record MachineNetworkInsightContext(
    MachineNetworkActivityClass ActivityClass,
    double? ReceiveBytesPerSecond,
    double? SendBytesPerSecond);

public sealed record MachineSessionInsightContext(
    TimeSpan SystemUptime,
    TimeSpan MachineUptime);
