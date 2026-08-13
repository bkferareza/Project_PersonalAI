using System.Diagnostics;
using Machine.Core;

namespace Machine.Windows;

public sealed class WindowsMachineSessionProvider : IMachineSessionProvider
{
    private readonly IMachineUserActivityProvider _userActivityProvider;
    private readonly long _machineStartedTimestamp;

    public WindowsMachineSessionProvider(
        IMachineUserActivityProvider userActivityProvider)
    {
        ArgumentNullException.ThrowIfNull(userActivityProvider);
        _userActivityProvider = userActivityProvider;
        _machineStartedTimestamp = Stopwatch.GetTimestamp();
    }

    public async Task<MachineSessionSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activity = await _userActivityProvider.GetAsync(
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return new MachineSessionSnapshot(
            SystemUptime: ConvertSystemUptimeMilliseconds(
                Environment.TickCount64),
            MachineUptime: CalculateMonotonicElapsed(
                _machineStartedTimestamp,
                Stopwatch.GetTimestamp(),
                Stopwatch.Frequency),
            CurrentUserInputState: activity.State,
            CurrentUserIdleDuration: activity.LastInputAge,
            CapturedAt: DateTimeOffset.UtcNow);
    }

    public static TimeSpan ConvertSystemUptimeMilliseconds(
        long elapsedMilliseconds) => elapsedMilliseconds <= 0
            ? TimeSpan.Zero
            : elapsedMilliseconds >= TimeSpan.MaxValue.TotalMilliseconds
                ? TimeSpan.MaxValue
                : TimeSpan.FromMilliseconds(elapsedMilliseconds);

    public static TimeSpan CalculateMonotonicElapsed(
        long startedTimestamp,
        long currentTimestamp,
        long timestampFrequency)
    {
        if (timestampFrequency <= 0 || currentTimestamp <= startedTimestamp)
        {
            return TimeSpan.Zero;
        }

        var seconds = (currentTimestamp - startedTimestamp) /
            (double)timestampFrequency;
        return !double.IsFinite(seconds) || seconds <= 0d
            ? TimeSpan.Zero
            : seconds >= TimeSpan.MaxValue.TotalSeconds
                ? TimeSpan.MaxValue
                : TimeSpan.FromSeconds(seconds);
    }
}
