using System.Runtime.InteropServices;
using Machine.Core;

namespace Machine.Windows;

public sealed class WindowsMachineScheduledTaskInventoryProvider
    : IMachineScheduledTaskInventoryProvider
{
    public const int MaximumTaskCount = 4_096;
    public const int MaximumFolderCount = 4_096;

    private readonly IWindowsScheduledTaskSource _source;

    public WindowsMachineScheduledTaskInventoryProvider()
        : this(new WindowsScheduledTaskSource())
    {
    }

    internal WindowsMachineScheduledTaskInventoryProvider(
        IWindowsScheduledTaskSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public Task<MachineScheduledTaskInventorySnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(
            () => Capture(cancellationToken),
            cancellationToken);
    }

    private MachineScheduledTaskInventorySnapshot Capture(
        CancellationToken cancellationToken)
    {
        var rawTasks = new List<NativeScheduledTask>();
        var readFailureCount = 0;
        var isComplete = true;
        try
        {
            using var session = _source.Open(cancellationToken);
            var pending = new Stack<string>();
            var visited = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            pending.Push("\\");
            while (pending.Count > 0 &&
                visited.Count < MaximumFolderCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = pending.Pop();
                if (!visited.Add(path))
                {
                    continue;
                }

                try
                {
                    var folder = session.ReadFolder(
                        path,
                        cancellationToken);
                    rawTasks.AddRange(folder.Tasks);
                    readFailureCount += folder.ReadFailureCount;
                    isComplete &= folder.IsComplete;
                    foreach (var child in folder.SubfolderPaths
                        .Where(child => !string.IsNullOrWhiteSpace(child))
                        .OrderByDescending(child => child,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        pending.Push(child);
                    }
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsReadFailure(exception))
                {
                    readFailureCount++;
                    isComplete = false;
                }
            }

            if (pending.Count > 0)
            {
                readFailureCount++;
                isComplete = false;
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            readFailureCount++;
            isComplete = false;
        }

        var mapped = rawTasks
            .Select(Map)
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.Path,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Path, StringComparer.Ordinal)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        var rejected = rawTasks.Count - mapped.Length;
        readFailureCount += rejected;
        isComplete &= rejected == 0;
        var truncated = Math.Max(0, mapped.Length - MaximumTaskCount);
        return new(
            mapped.Take(MaximumTaskCount).ToArray(),
            isComplete && truncated == 0,
            readFailureCount,
            truncated,
            DateTimeOffset.UtcNow);
    }

    internal static MachineScheduledTaskSnapshot? Map(
        NativeScheduledTask task)
    {
        var name = Normalize(task.Name, 260);
        var path = Normalize(task.Path, 1_024);
        if (name is null || path is null)
        {
            return null;
        }

        var executable = task.ExecutablePaths
            .Select(NormalizeExecutableName)
            .FirstOrDefault(item => item is not null);
        return new(
            name,
            path,
            task.Enabled,
            MapState(task.State),
            NormalizeDate(task.LastRunAt),
            NormalizeDate(task.NextRunAt),
            task.LastResult,
            task.TriggerTypes
                .Select(MapTrigger)
                .Distinct()
                .OrderBy(item => item)
                .Take(8)
                .ToArray(),
            Normalize(task.Author, 120),
            executable);
    }

    internal static MachineScheduledTaskState MapState(int value) =>
        value switch
        {
            1 => MachineScheduledTaskState.Disabled,
            2 => MachineScheduledTaskState.Queued,
            3 => MachineScheduledTaskState.Ready,
            4 => MachineScheduledTaskState.Running,
            _ => MachineScheduledTaskState.Unknown
        };

    internal static MachineScheduledTaskTriggerCategory MapTrigger(
        int value) => value switch
        {
            0 => MachineScheduledTaskTriggerCategory.Event,
            1 => MachineScheduledTaskTriggerCategory.Time,
            2 or 3 or 4 or 5 =>
                MachineScheduledTaskTriggerCategory.Calendar,
            6 => MachineScheduledTaskTriggerCategory.Idle,
            7 => MachineScheduledTaskTriggerCategory.Registration,
            8 => MachineScheduledTaskTriggerCategory.Boot,
            9 => MachineScheduledTaskTriggerCategory.Logon,
            11 => MachineScheduledTaskTriggerCategory.Session,
            12 => MachineScheduledTaskTriggerCategory.Custom,
            _ => MachineScheduledTaskTriggerCategory.Unknown
        };

    internal static string? NormalizeExecutableName(string? path)
    {
        var normalized = Normalize(path, 1_024)?.Trim('"');
        if (normalized is null)
        {
            return null;
        }
        try
        {
            return Normalize(Path.GetFileName(normalized), 260);
        }
        catch (ArgumentException)
        {
            return null;
        }
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

    private static DateTimeOffset? NormalizeDate(DateTimeOffset? value) =>
        value is { Year: >= 1900 } date
            ? date.ToUniversalTime()
            : null;

    private static bool IsReadFailure(Exception exception) =>
        exception is COMException or
            InvalidOperationException or
            UnauthorizedAccessException or
            PlatformNotSupportedException;
}
