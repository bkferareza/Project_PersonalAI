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
internal interface IWindowsScheduledTaskSource
{
    IWindowsScheduledTaskSession Open(CancellationToken cancellationToken);
}

internal interface IWindowsScheduledTaskSession : IDisposable
{
    NativeScheduledTaskFolder ReadFolder(
        string path,
        CancellationToken cancellationToken);
}

internal sealed record NativeScheduledTaskFolder(
    IReadOnlyList<NativeScheduledTask> Tasks,
    IReadOnlyList<string> SubfolderPaths,
    int ReadFailureCount,
    bool IsComplete);

internal sealed record NativeScheduledTask(
    string? Name,
    string? Path,
    bool Enabled,
    int State,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? NextRunAt,
    int? LastResult,
    IReadOnlyList<int> TriggerTypes,
    string? Author,
    IReadOnlyList<string> ExecutablePaths);

internal sealed class WindowsScheduledTaskSource
    : IWindowsScheduledTaskSource
{
    public IWindowsScheduledTaskSession Open(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var type = Type.GetTypeFromProgID("Schedule.Service") ??
            throw new PlatformNotSupportedException(
                "Windows Task Scheduler COM API is unavailable.");
        var service = Activator.CreateInstance(type) ??
            throw new InvalidOperationException(
                "Windows Task Scheduler could not be created.");
        try
        {
            ((dynamic)service).Connect();
            return new WindowsScheduledTaskSession(service);
        }
        catch
        {
            Release(service);
            throw;
        }
    }

    internal static void Release(object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
        {
            return;
        }
        try
        {
            Marshal.FinalReleaseComObject(value);
        }
        catch (InvalidComObjectException)
        {
        }
    }
}

internal sealed class WindowsScheduledTaskSession
    : IWindowsScheduledTaskSession
{
    private object? _service;

    public WindowsScheduledTaskSession(object service)
    {
        _service = service;
    }

    public NativeScheduledTaskFolder ReadFolder(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = _service ?? throw new ObjectDisposedException(
            nameof(WindowsScheduledTaskSession));
        object? folder = null;
        object? tasks = null;
        object? folders = null;
        var readFailures = 0;
        var complete = true;
        var results = new List<NativeScheduledTask>();
        var childPaths = new List<string>();
        try
        {
            folder = ((dynamic)service).GetFolder(path);
            try
            {
                tasks = ((dynamic)folder).GetTasks(1);
                var count = Convert.ToInt32(((dynamic)tasks).Count);
                for (var index = 1; index <= count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    object? task = null;
                    try
                    {
                        task = ((dynamic)tasks)[index];
                        results.Add(ReadTask(task));
                    }
                    catch (Exception exception) when (
                        exception is COMException or
                            InvalidOperationException)
                    {
                        readFailures++;
                        complete = false;
                    }
                    finally
                    {
                        WindowsScheduledTaskSource.Release(task);
                    }
                }
            }
            catch (COMException)
            {
                readFailures++;
                complete = false;
            }

            try
            {
                folders = ((dynamic)folder).GetFolders(0);
                var count = Convert.ToInt32(((dynamic)folders).Count);
                for (var index = 1; index <= count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    object? child = null;
                    try
                    {
                        child = ((dynamic)folders)[index];
                        var childPath = Convert.ToString(
                            ((dynamic)child).Path);
                        if (!string.IsNullOrWhiteSpace(childPath))
                        {
                            childPaths.Add(childPath);
                        }
                    }
                    catch (COMException)
                    {
                        readFailures++;
                        complete = false;
                    }
                    finally
                    {
                        WindowsScheduledTaskSource.Release(child);
                    }
                }
            }
            catch (COMException)
            {
                readFailures++;
                complete = false;
            }
        }
        finally
        {
            WindowsScheduledTaskSource.Release(tasks);
            WindowsScheduledTaskSource.Release(folders);
            WindowsScheduledTaskSource.Release(folder);
        }

        return new(results, childPaths, readFailures, complete);
    }

    public void Dispose()
    {
        var service = Interlocked.Exchange(ref _service, null);
        WindowsScheduledTaskSource.Release(service);
    }

    private static NativeScheduledTask ReadTask(object task)
    {
        dynamic value = task;
        object? definition = null;
        object? registrationInfo = null;
        object? triggers = null;
        object? actions = null;
        try
        {
            definition = value.Definition;
            dynamic dynamicDefinition = definition;
            registrationInfo = dynamicDefinition.RegistrationInfo;
            var author = Convert.ToString(
                ((dynamic)registrationInfo).Author);

            var triggerTypes = new List<int>();
            triggers = dynamicDefinition.Triggers;
            var triggerCount = Convert.ToInt32(((dynamic)triggers).Count);
            for (var index = 1; index <= triggerCount; index++)
            {
                object? trigger = null;
                try
                {
                    trigger = ((dynamic)triggers)[index];
                    triggerTypes.Add(Convert.ToInt32(
                        ((dynamic)trigger).Type));
                }
                finally
                {
                    WindowsScheduledTaskSource.Release(trigger);
                }
            }

            var executablePaths = new List<string>();
            actions = dynamicDefinition.Actions;
            var actionCount = Convert.ToInt32(((dynamic)actions).Count);
            for (var index = 1; index <= actionCount; index++)
            {
                object? action = null;
                try
                {
                    action = ((dynamic)actions)[index];
                    if (Convert.ToInt32(((dynamic)action).Type) != 0)
                    {
                        continue;
                    }
                    var executable = Convert.ToString(
                        ((dynamic)action).Path);
                    if (!string.IsNullOrWhiteSpace(executable))
                    {
                        executablePaths.Add(executable);
                    }
                    // Action arguments are deliberately never read.
                }
                finally
                {
                    WindowsScheduledTaskSource.Release(action);
                }
            }

            return new(
                Convert.ToString(value.Name),
                Convert.ToString(value.Path),
                Convert.ToBoolean(value.Enabled),
                Convert.ToInt32(value.State),
                ReadDate(value.LastRunTime),
                ReadDate(value.NextRunTime),
                Convert.ToInt32(value.LastTaskResult),
                triggerTypes,
                author,
                executablePaths);
        }
        finally
        {
            WindowsScheduledTaskSource.Release(actions);
            WindowsScheduledTaskSource.Release(triggers);
            WindowsScheduledTaskSource.Release(registrationInfo);
            WindowsScheduledTaskSource.Release(definition);
        }
    }

    private static DateTimeOffset? ReadDate(object? value)
    {
        if (value is not DateTime date || date.Year < 1900)
        {
            return null;
        }
        var local = DateTime.SpecifyKind(date, DateTimeKind.Local);
        return new DateTimeOffset(local).ToUniversalTime();
    }
}
