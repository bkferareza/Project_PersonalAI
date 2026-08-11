namespace Machine.Core;

public sealed record MachineNetworkCounterSample(
    string InterfaceId,
    ulong BytesReceived,
    ulong BytesSent);

public sealed record MachineNetworkThroughputSample(
    double? ReceiveBytesPerSecond,
    double? SendBytesPerSecond,
    int ReceiveContributingInterfaceCount,
    int SendContributingInterfaceCount);

public static class MachineNetworkThroughputCalculator
{
    public static MachineNetworkThroughputSample Calculate(
        IReadOnlyList<MachineNetworkCounterSample>? previous,
        IReadOnlyList<MachineNetworkCounterSample> current,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (previous is null || elapsed <= TimeSpan.Zero ||
            !double.IsFinite(elapsed.TotalSeconds))
        {
            return Unavailable();
        }

        var previousById = previous
            .Where(sample => !string.IsNullOrWhiteSpace(sample.InterfaceId))
            .GroupBy(sample => sample.InterfaceId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.Ordinal);
        var seenCurrentIds = new HashSet<string>(StringComparer.Ordinal);
        var receivedDelta = 0d;
        var sentDelta = 0d;
        var receivedContributors = 0;
        var sentContributors = 0;

        foreach (var sample in current)
        {
            if (string.IsNullOrWhiteSpace(sample.InterfaceId) ||
                !seenCurrentIds.Add(sample.InterfaceId) ||
                !previousById.TryGetValue(sample.InterfaceId, out var prior))
            {
                continue;
            }

            if (sample.BytesReceived >= prior.BytesReceived)
            {
                receivedDelta += sample.BytesReceived - prior.BytesReceived;
                receivedContributors++;
            }

            if (sample.BytesSent >= prior.BytesSent)
            {
                sentDelta += sample.BytesSent - prior.BytesSent;
                sentContributors++;
            }
        }

        var elapsedSeconds = elapsed.TotalSeconds;
        return new MachineNetworkThroughputSample(
            receivedContributors == 0
                ? null
                : receivedDelta / elapsedSeconds,
            sentContributors == 0
                ? null
                : sentDelta / elapsedSeconds,
            receivedContributors,
            sentContributors);
    }

    private static MachineNetworkThroughputSample Unavailable() =>
        new(null, null, 0, 0);
}
