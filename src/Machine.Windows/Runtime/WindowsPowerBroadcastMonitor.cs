using System.ComponentModel;
using System.Runtime.InteropServices;
using Machine.Core;

namespace Machine.Windows;

public sealed class WindowsPowerBroadcastMonitor : IDisposable
{
    internal const uint WindowMessagePowerBroadcast = 0x0218;
    internal const nuint PowerBroadcastSuspend = 0x0004;
    internal const nuint PowerBroadcastResumeSuspend = 0x0007;
    internal const nuint PowerBroadcastResumeAutomatic = 0x0012;

    private static readonly nuint SubclassId = 0x4D415441;
    private readonly IntPtr _windowHandle;
    private readonly Action<MachinePowerTransition> _observer;
    private readonly SubclassProcedure _procedure;
    private bool _disposed;

    public WindowsPowerBroadcastMonitor(
        IntPtr windowHandle,
        Action<MachinePowerTransition> observer)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException(
                "A valid window handle is required.",
                nameof(windowHandle));
        }
        ArgumentNullException.ThrowIfNull(observer);
        _windowHandle = windowHandle;
        _observer = observer;
        _procedure = WindowSubclassProcedure;
        if (!SetWindowSubclass(
                _windowHandle,
                _procedure,
                SubclassId,
                UIntPtr.Zero))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows power-broadcast monitoring could not start.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _ = RemoveWindowSubclass(
            _windowHandle,
            _procedure,
            SubclassId);
    }

    internal static bool TryMap(
        nuint value,
        out MachinePowerTransitionKind kind)
    {
        switch (value)
        {
            case PowerBroadcastSuspend:
                kind = MachinePowerTransitionKind.Suspend;
                return true;
            case PowerBroadcastResumeAutomatic:
                kind = MachinePowerTransitionKind.ResumeAutomatic;
                return true;
            case PowerBroadcastResumeSuspend:
                kind = MachinePowerTransitionKind.ResumeSuspend;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private IntPtr WindowSubclassProcedure(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        nuint subclassId,
        UIntPtr referenceData)
    {
        if (!_disposed &&
            message == WindowMessagePowerBroadcast &&
            TryMap((nuint)wParam.ToUInt64(),
                out var kind))
        {
            try
            {
                _observer(new(kind, DateTimeOffset.UtcNow));
            }
            catch
            {
                // A notification must never escape the native window proc.
            }
        }
        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private delegate IntPtr SubclassProcedure(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        nuint subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr windowHandle,
        SubclassProcedure subclassProcedure,
        nuint subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr windowHandle,
        SubclassProcedure subclassProcedure,
        nuint subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam);
}
