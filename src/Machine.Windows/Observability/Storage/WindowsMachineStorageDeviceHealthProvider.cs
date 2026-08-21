using System.Management;
using Machine.Core;

namespace Machine.Windows;

public sealed class WindowsMachineStorageDeviceHealthProvider :
    IMachineStorageDeviceHealthProvider
{
    private const string StorageNamespace = @"root\Microsoft\Windows\Storage";

    public Task<MachineStorageDeviceHealthCollection> GetAsync(
        CancellationToken cancellationToken = default) => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capturedAt = DateTimeOffset.UtcNow;
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    StorageNamespace,
                    "SELECT FriendlyName, Manufacturer, MediaType, BusType, Size, HealthStatus, OperationalStatus FROM MSFT_PhysicalDisk");
                using var results = searcher.Get();
                var devices = results.Cast<ManagementObject>()
                    .Select((item, index) => Project(item, capturedAt, index))
                    .ToArray();
                return new MachineStorageDeviceHealthCollection(capturedAt,
                    devices,
                    devices.Length == 0
                        ? MachineHardwareTelemetryAvailability.Unavailable
                        : MachineHardwareTelemetryAvailability.Partial,
                    devices.Length == 0 ? "storage.no-physical-devices" : null);
            }
            catch (ManagementException)
            {
                return new MachineStorageDeviceHealthCollection(capturedAt, [],
                    MachineHardwareTelemetryAvailability.Unavailable,
                    "storage.wmi-unavailable");
            }
            catch (UnauthorizedAccessException)
            {
                return new MachineStorageDeviceHealthCollection(capturedAt, [],
                    MachineHardwareTelemetryAvailability.Unavailable,
                    "storage.access-denied");
            }
        }, cancellationToken);

    private static MachineStorageDeviceHealthSnapshot Project(
        ManagementBaseObject item,
        DateTimeOffset capturedAt,
        int index) => new(
        capturedAt,
        $"physical-{index + 1}",
        ReadString(item, "FriendlyName") ?? "Windows physical storage device",
        ReadString(item, "Manufacturer"),
        MapMediaType(ReadUInt16(item, "MediaType")),
        MapBusType(ReadUInt16(item, "BusType")),
        ReadUInt64(item, "Size"),
        MapHealthStatus(ReadUInt16(item, "HealthStatus")),
        MapOperationalStatus(item["OperationalStatus"]),
        null, null, null, null, null, null, null, null, null, null, null, null,
        MachineHardwareTelemetryAvailability.Partial,
        "Windows reported device identity and health state; reliability counters were unavailable.");

    private static string? ReadString(ManagementBaseObject item, string name) =>
        item[name] as string;

    private static ushort? ReadUInt16(ManagementBaseObject item, string name) =>
        item[name] is null ? null : Convert.ToUInt16(item[name]);

    private static ulong? ReadUInt64(ManagementBaseObject item, string name) =>
        item[name] is null ? null : Convert.ToUInt64(item[name]);

    private static string? MapMediaType(ushort? value) => value switch
    {
        3 => "HDD",
        4 => "SSD",
        5 => "SCM",
        _ => value is null ? null : "Unspecified"
    };

    private static string? MapBusType(ushort? value) => value switch
    {
        7 => "USB",
        11 => "SATA",
        17 => "NVMe",
        _ => value is null ? null : "Other"
    };

    private static string? MapHealthStatus(ushort? value) => value switch
    {
        0 => "Healthy",
        1 => "Warning",
        2 => "Unhealthy",
        _ => value is null ? null : "Unknown"
    };

    private static string? MapOperationalStatus(object? value) => value is ushort[] values &&
        values.Length > 0 ? values[0] switch
        {
            2 => "OK",
            3 => "Degraded",
            6 => "Error",
            _ => "Windows reported status"
        } : null;
}
