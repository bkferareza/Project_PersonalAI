namespace Machine.Core;

public sealed record MachineNetworkSnapshot(
    IReadOnlyList<MachineNetworkInterfaceSnapshot> Interfaces,
    MachineNetworkAggregateSnapshot Aggregate,
    DateTimeOffset CapturedAt);

public sealed record MachineNetworkInterfaceSnapshot(
    string Name,
    string? Description,
    string InterfaceType,
    string OperationalStatus,
    long? ReceiveLinkSpeedBitsPerSecond,
    long? TransmitLinkSpeedBitsPerSecond,
    ulong? BytesReceived,
    ulong? BytesSent);

public sealed record MachineNetworkAggregateSnapshot(
    int ActiveInterfaceCount,
    ulong TotalBytesReceived,
    ulong TotalBytesSent,
    double? ReceiveBytesPerSecond,
    double? SendBytesPerSecond)
{
    public MachineNetworkActivityClass ActivityClass =>
        MachineNetworkActivityClassifier.Classify(
            ReceiveBytesPerSecond,
            SendBytesPerSecond);
}
