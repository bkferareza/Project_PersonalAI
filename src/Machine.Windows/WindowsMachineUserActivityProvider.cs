using System.ComponentModel;
using System.Runtime.InteropServices;
using Machine.Core;

namespace Machine.Windows;

public sealed class WindowsMachineUserActivityProvider
    : IMachineUserActivityProvider
{
    public static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(5);

    public Task<MachineUserActivitySnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capturedAt = DateTimeOffset.UtcNow;
        var lastInput = new LastInputInfo
        {
            Size = checked((uint)Marshal.SizeOf<LastInputInfo>())
        };
        if (!GetLastInputInfo(ref lastInput))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "GetLastInputInfo failed.");
        }

        var tickCount = unchecked((uint)Environment.TickCount);
        var elapsedMilliseconds = unchecked(tickCount - lastInput.TickCount);
        var age = TimeSpan.FromMilliseconds(elapsedMilliseconds);
        var state = GetState(age);
        return Task.FromResult(new MachineUserActivitySnapshot(
            age, state, capturedAt));
    }

    public static MachineUserActivityState GetState(TimeSpan lastInputAge) =>
        lastInputAge >= IdleThreshold
            ? MachineUserActivityState.Idle
            : MachineUserActivityState.Active;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint TickCount;
    }
}
