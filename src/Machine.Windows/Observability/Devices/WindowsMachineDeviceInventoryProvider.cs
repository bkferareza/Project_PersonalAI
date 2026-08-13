using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Machine.Core;
using Microsoft.Win32.SafeHandles;

namespace Machine.Windows;

public sealed class WindowsMachineDeviceInventoryProvider
    : IMachineDeviceInventoryProvider
{
    public const int MaximumDeviceCount = 4_096;

    private readonly IWindowsDeviceInventorySource _source;

    public WindowsMachineDeviceInventoryProvider()
        : this(new WindowsDeviceInventorySource())
    {
    }

    internal WindowsMachineDeviceInventoryProvider(
        IWindowsDeviceInventorySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public Task<MachineDeviceInventorySnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(
            () => Capture(cancellationToken),
            cancellationToken);
    }

    private MachineDeviceInventorySnapshot Capture(
        CancellationToken cancellationToken)
    {
        NativeDeviceCapture capture;
        try
        {
            capture = _source.Capture(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return new([], false, 1, 0, DateTimeOffset.UtcNow);
        }

        var items = capture.Devices
            .Select(Map)
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.DeviceClass,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DeviceClass, StringComparer.Ordinal)
            .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
            .ToArray();
        var rejected = capture.Devices.Count - items.Length;
        var truncated = Math.Max(0, items.Length - MaximumDeviceCount);
        return new(
            items.Take(MaximumDeviceCount).ToArray(),
            capture.IsComplete && rejected == 0 && truncated == 0,
            capture.ReadFailureCount + rejected,
            truncated,
            capture.CapturedAt.ToUniversalTime());
    }

    internal static MachineDeviceSnapshot? Map(NativeDeviceRecord value)
    {
        var displayName = Normalize(value.DisplayName, 512);
        var deviceClass = Normalize(value.DeviceClass, 256);
        if (displayName is null || deviceClass is null)
        {
            return null;
        }
        return new(
            displayName,
            deviceClass,
            Normalize(value.Manufacturer, 256),
            value.IsPresent,
            value.IsEnabled,
            value.ProblemCode is > 0 and <= int.MaxValue
                ? (int)value.ProblemCode
                : null,
            Normalize(value.DriverProvider, 256),
            Normalize(value.DriverVersion, 128),
            value.DriverDate);
    }

    private static string? Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength
            ? trimmed
            : trimmed[..maximumLength];
    }

    private static bool IsReadFailure(Exception exception) =>
        exception is Win32Exception or
            InvalidOperationException or
            UnauthorizedAccessException;
}
