using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Machine.Core;
using Machine.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace Machine.App.Features;

public sealed partial class NetworkView
{
    private const int MaximumNetworkInterfaceCount = 12;
    private const string UnavailableValue = "Unavailable";
    private const double BytesPerMebibyte = 1024d * 1024d;
    private const double BytesPerGibibyte = 1024d * 1024d * 1024d;
    private const double BytesPerTebibyte =
        1024d * 1024d * 1024d * 1024d;

    internal void UpdateNetwork(
        MachineNetworkSnapshot? snapshot,
        bool hasPreviousSnapshot,
        OverviewView overview)
    {
        if (snapshot is null)
        {
            NetworkStatusText.Text =
                "Network telemetry is temporarily unavailable.";
            NetworkStatusText.Visibility = Visibility.Visible;
            if (!hasPreviousSnapshot)
            {
                overview.OverviewNetworkActivityText.Text = UnavailableValue;
                overview.OverviewNetworkReceiveText.Text = UnavailableValue;
                overview.OverviewNetworkSendText.Text = UnavailableValue;
                overview.OverviewNetworkInterfaceText.Text =
                    "Interface status unavailable";
                NetworkReceiveRateText.Text = UnavailableValue;
                NetworkSendRateText.Text = UnavailableValue;
                NetworkActivityClassText.Text = UnavailableValue;
                NetworkInterfacesList.ItemsSource =
                    Array.Empty<NetworkInterfaceDisplayItem>();
                NetworkInterfacesEmptyText.Visibility = Visibility.Visible;
            }
            return;
        }

        var aggregate = snapshot.Aggregate;
        overview.OverviewNetworkActivityText.Text = aggregate.ActivityClass.ToString();
        overview.OverviewNetworkReceiveText.Text =
            $"Receive {FormatByteRate(aggregate.ReceiveBytesPerSecond)}";
        overview.OverviewNetworkSendText.Text =
            $"Send {FormatByteRate(aggregate.SendBytesPerSecond)}";
        overview.OverviewNetworkInterfaceText.Text =
            FormatOnlineInterfaceCount(aggregate.ActiveInterfaceCount);

        NetworkReceiveRateText.Text =
            FormatByteRate(aggregate.ReceiveBytesPerSecond);
        NetworkSendRateText.Text =
            FormatByteRate(aggregate.SendBytesPerSecond);
        NetworkActivityClassText.Text = aggregate.ActivityClass.ToString();
        var interfaceItems = snapshot.Interfaces
            .Take(MaximumNetworkInterfaceCount)
            .Select(CreateNetworkInterfaceDisplayItem)
            .ToArray();
        NetworkInterfacesList.ItemsSource = interfaceItems;
        NetworkInterfacesEmptyText.Visibility = interfaceItems.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        NetworkStatusText.Text = snapshot.Interfaces.Count >
                MaximumNetworkInterfaceCount
            ? $"Showing {MaximumNetworkInterfaceCount:N0} of " +
                $"{snapshot.Interfaces.Count:N0} active interfaces."
            : string.Empty;
        NetworkStatusText.Visibility = string.IsNullOrEmpty(
            NetworkStatusText.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    internal void UpdateSession(
        MachineSessionSnapshot? snapshot,
        bool hasPreviousSnapshot,
        OverviewView overview)
    {
        if (snapshot is null)
        {
            if (!hasPreviousSnapshot)
            {
                overview.OverviewSessionUptimeText.Text =
                    "Session uptime unavailable";
                overview.OverviewSessionActivityText.Text =
                    "Input state unavailable";
                SessionSystemUptimeText.Text = UnavailableValue;
                SessionMachineUptimeText.Text = UnavailableValue;
                SessionInputStateText.Text = UnavailableValue;
                SessionIdleDurationText.Text = UnavailableValue;
            }
            return;
        }

        overview.OverviewSessionUptimeText.Text =
            $"Windows up {FormatUptime(snapshot.SystemUptime)} · " +
            $"Matasuri running {FormatUptime(snapshot.MachineUptime)}";
        overview.OverviewSessionActivityText.Text =
            $"{snapshot.CurrentUserInputState} · " +
            $"last input {FormatInputAge(snapshot.CurrentUserIdleDuration)} ago";
        SessionSystemUptimeText.Text = FormatUptime(snapshot.SystemUptime);
        SessionMachineUptimeText.Text = FormatUptime(snapshot.MachineUptime);
        SessionInputStateText.Text = snapshot.CurrentUserInputState.ToString();
        SessionIdleDurationText.Text =
            FormatInputAge(snapshot.CurrentUserIdleDuration);
    }

    private static NetworkInterfaceDisplayItem
        CreateNetworkInterfaceDisplayItem(
            MachineNetworkInterfaceSnapshot networkInterface) => new(
                networkInterface.Name,
                $"{networkInterface.OperationalStatus} · " +
                    networkInterface.InterfaceType,
                networkInterface.Description ?? string.Empty,
                FormatLinkSpeed(
                    networkInterface.ReceiveLinkSpeedBitsPerSecond,
                    networkInterface.TransmitLinkSpeedBitsPerSecond),
                networkInterface.BytesReceived is null
                    ? "Received unavailable"
                    : $"Received {FormatBytes(networkInterface.BytesReceived.Value)}",
                networkInterface.BytesSent is null
                    ? "Sent unavailable"
                    : $"Sent {FormatBytes(networkInterface.BytesSent.Value)}");

    private static string FormatOnlineInterfaceCount(int count) =>
        $"{Math.Max(0, count):N0} " +
        (count == 1 ? "interface" : "interfaces") + " online";

    private static string FormatBytes(ulong bytes)
    {
        if (bytes >= BytesPerTebibyte)
        {
            return $"{bytes / BytesPerTebibyte:F1} TB";
        }
        if (bytes >= BytesPerGibibyte)
        {
            return $"{bytes / BytesPerGibibyte:F1} GB";
        }
        if (bytes >= BytesPerMebibyte)
        {
            return $"{bytes / BytesPerMebibyte:F1} MB";
        }
        if (bytes >= 1024UL)
        {
            return $"{bytes / 1024d:F1} KB";
        }
        return $"{bytes} B";
    }

    private static string FormatByteRate(double? bytesPerSecond)
    {
        if (bytesPerSecond is null ||
            !double.IsFinite(bytesPerSecond.Value) ||
            bytesPerSecond.Value < 0d)
        {
            return UnavailableValue;
        }
        var value = bytesPerSecond.Value;
        if (value >= BytesPerTebibyte)
        {
            return $"{value / BytesPerTebibyte:F1} TB/s";
        }
        if (value >= BytesPerGibibyte)
        {
            return $"{value / BytesPerGibibyte:F1} GB/s";
        }
        if (value >= BytesPerMebibyte)
        {
            return $"{value / BytesPerMebibyte:F1} MB/s";
        }
        if (value >= 1024d)
        {
            return $"{value / 1024d:F1} KB/s";
        }
        return $"{value:F0} B/s";
    }

    private static string FormatLinkSpeed(
        long? receiveBitsPerSecond,
        long? transmitBitsPerSecond)
    {
        if (receiveBitsPerSecond is null && transmitBitsPerSecond is null)
        {
            return "Link speed unavailable";
        }
        if (receiveBitsPerSecond == transmitBitsPerSecond)
        {
            return $"{FormatBitsPerSecond(receiveBitsPerSecond)} link";
        }
        return $"Receive {FormatBitsPerSecond(receiveBitsPerSecond)} · " +
            $"send {FormatBitsPerSecond(transmitBitsPerSecond)} link";
    }

    private static string FormatBitsPerSecond(long? bitsPerSecond)
    {
        if (bitsPerSecond is null || bitsPerSecond <= 0)
        {
            return UnavailableValue;
        }
        return bitsPerSecond >= 1_000_000_000L
            ? $"{bitsPerSecond / 1_000_000_000d:F1} Gbps"
            : bitsPerSecond >= 1_000_000L
                ? $"{bitsPerSecond / 1_000_000d:F1} Mbps"
                : $"{bitsPerSecond / 1_000d:F1} Kbps";
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        var bounded = uptime < TimeSpan.Zero ? TimeSpan.Zero : uptime;
        if (bounded.TotalDays >= 1d)
        {
            return $"{(int)bounded.TotalDays}d {bounded.Hours}h";
        }
        return bounded.TotalHours >= 1d
            ? $"{(int)bounded.TotalHours}h {bounded.Minutes}m"
            : $"{Math.Max(0, bounded.Minutes)}m";
    }

    private static string FormatInputAge(TimeSpan age)
    {
        var bounded = age < TimeSpan.Zero ? TimeSpan.Zero : age;
        if (bounded.TotalHours >= 1d)
        {
            return $"{(int)bounded.TotalHours}h {bounded.Minutes}m";
        }
        if (bounded.TotalMinutes >= 1d)
        {
            return $"{(int)bounded.TotalMinutes}m {bounded.Seconds}s";
        }
        return $"{Math.Max(0, bounded.Seconds)}s";
    }
}
