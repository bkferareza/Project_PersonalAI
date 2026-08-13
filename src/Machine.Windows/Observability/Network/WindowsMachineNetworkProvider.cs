using System.Diagnostics;
using System.Net.NetworkInformation;
using Machine.Core;

namespace Machine.Windows;

public sealed class WindowsMachineNetworkProvider : IMachineNetworkProvider
{
    private readonly object _sampleGate = new();
    private IReadOnlyList<MachineNetworkCounterSample>? _previousCounters;
    private long? _previousTimestamp;

    public Task<MachineNetworkSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(
            () => CaptureSnapshot(cancellationToken),
            cancellationToken);
    }

    public static bool ShouldIncludeInterface(
        NetworkInterfaceType interfaceType,
        OperationalStatus operationalStatus) =>
        operationalStatus == OperationalStatus.Up &&
        interfaceType is not NetworkInterfaceType.Loopback and
            not NetworkInterfaceType.Tunnel;

    public static bool IsObviousFilterInterface(
        string? name,
        string? description)
    {
        var values = new[] { name, description };
        return values.Any(value => !string.IsNullOrWhiteSpace(value) &&
            (value.Contains(
                "QoS Packet Scheduler",
                StringComparison.OrdinalIgnoreCase) ||
             value.Contains(
                "WFP 802.3 MAC Layer LightWeight Filter",
                StringComparison.OrdinalIgnoreCase) ||
             value.Contains(
                "WFP Native MAC Layer LightWeight Filter",
                StringComparison.OrdinalIgnoreCase) ||
             value.Contains(
                "Native WiFi Filter",
                StringComparison.OrdinalIgnoreCase) ||
             value.StartsWith(
                "WAN Miniport (",
                StringComparison.OrdinalIgnoreCase) ||
             value.Contains(
                "Hyper-V Virtual Switch Extension Adapter",
                StringComparison.OrdinalIgnoreCase) ||
             value.Contains(
                "Hyper-V Virtual Switch Extension Filter",
                StringComparison.OrdinalIgnoreCase)));
    }

    private MachineNetworkSnapshot CaptureSnapshot(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var interfaces = new List<MachineNetworkInterfaceSnapshot>();
        var counters = new List<MachineNetworkCounterSample>();

        foreach (var networkInterface in
            NetworkInterface.GetAllNetworkInterfaces())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ShouldIncludeInterface(
                    networkInterface.NetworkInterfaceType,
                    networkInterface.OperationalStatus) ||
                IsObviousFilterInterface(
                    networkInterface.Name,
                    networkInterface.Description))
            {
                continue;
            }

            var name = networkInterface.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var description = networkInterface.Description?.Trim();
            if (string.IsNullOrWhiteSpace(description) ||
                string.Equals(
                    description,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                description = null;
            }

            var linkSpeed = TryReadLinkSpeed(networkInterface);
            var statistics = TryReadStatistics(networkInterface);
            ulong? bytesReceived = statistics is null
                ? null
                : checked((ulong)statistics.BytesReceived);
            ulong? bytesSent = statistics is null
                ? null
                : checked((ulong)statistics.BytesSent);

            interfaces.Add(new MachineNetworkInterfaceSnapshot(
                Name: name,
                Description: description,
                InterfaceType:
                    networkInterface.NetworkInterfaceType.ToString(),
                OperationalStatus:
                    networkInterface.OperationalStatus.ToString(),
                ReceiveLinkSpeedBitsPerSecond: linkSpeed,
                TransmitLinkSpeedBitsPerSecond: linkSpeed,
                BytesReceived: bytesReceived,
                BytesSent: bytesSent));

            var interfaceId = networkInterface.Id;
            if (!string.IsNullOrWhiteSpace(interfaceId) &&
                bytesReceived is not null && bytesSent is not null)
            {
                counters.Add(new MachineNetworkCounterSample(
                    interfaceId,
                    bytesReceived.Value,
                    bytesSent.Value));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var timestamp = Stopwatch.GetTimestamp();
        MachineNetworkThroughputSample throughput;
        lock (_sampleGate)
        {
            var elapsed = _previousTimestamp is null
                ? TimeSpan.Zero
                : MachineNetworkElapsedTime.Calculate(
                    _previousTimestamp.Value,
                    timestamp,
                    Stopwatch.Frequency);
            throughput = MachineNetworkThroughputCalculator.Calculate(
                _previousCounters,
                counters,
                elapsed);
            _previousCounters = counters.ToArray();
            _previousTimestamp = timestamp;
        }

        var orderedInterfaces = interfaces
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        var totalBytesReceived = SumCounters(
            orderedInterfaces.Select(item => item.BytesReceived));
        var totalBytesSent = SumCounters(
            orderedInterfaces.Select(item => item.BytesSent));

        return new MachineNetworkSnapshot(
            Interfaces: orderedInterfaces,
            Aggregate: new MachineNetworkAggregateSnapshot(
                ActiveInterfaceCount: orderedInterfaces.Length,
                TotalBytesReceived: totalBytesReceived,
                TotalBytesSent: totalBytesSent,
                ReceiveBytesPerSecond:
                    throughput.ReceiveBytesPerSecond,
                SendBytesPerSecond:
                    throughput.SendBytesPerSecond),
            CapturedAt: DateTimeOffset.UtcNow);
    }

    private static long? TryReadLinkSpeed(
        NetworkInterface networkInterface)
    {
        try
        {
            var speed = networkInterface.Speed;
            return speed > 0 ? speed : null;
        }
        catch (Exception exception)
            when (IsOptionalNetworkValueException(exception))
        {
            return null;
        }
    }

    private static IPv4InterfaceStatistics? TryReadStatistics(
        NetworkInterface networkInterface)
    {
        try
        {
            var statistics = networkInterface.GetIPv4Statistics();
            return statistics.BytesReceived >= 0 && statistics.BytesSent >= 0
                ? statistics
                : null;
        }
        catch (Exception exception)
            when (IsOptionalNetworkValueException(exception))
        {
            return null;
        }
    }

    private static bool IsOptionalNetworkValueException(
        Exception exception) =>
        exception is NetworkInformationException or
            PlatformNotSupportedException or
            NotSupportedException or
            ObjectDisposedException or
            InvalidOperationException;

    private static ulong SumCounters(IEnumerable<ulong?> values)
    {
        var total = 0UL;
        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            total = total > ulong.MaxValue - value.Value
                ? ulong.MaxValue
                : total + value.Value;
        }

        return total;
    }
}

public static class MachineNetworkElapsedTime
{
    public static TimeSpan Calculate(
        long previousTimestamp,
        long currentTimestamp,
        long timestampFrequency)
    {
        if (timestampFrequency <= 0 || currentTimestamp <= previousTimestamp)
        {
            return TimeSpan.Zero;
        }

        var seconds = (currentTimestamp - previousTimestamp) /
            (double)timestampFrequency;
        return !double.IsFinite(seconds) || seconds <= 0d
            ? TimeSpan.Zero
            : seconds >= TimeSpan.MaxValue.TotalSeconds
                ? TimeSpan.MaxValue
                : TimeSpan.FromSeconds(seconds);
    }
}
