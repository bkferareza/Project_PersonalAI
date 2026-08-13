using System.Runtime.InteropServices;
using Machine.Core;

namespace Machine.Windows;

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
