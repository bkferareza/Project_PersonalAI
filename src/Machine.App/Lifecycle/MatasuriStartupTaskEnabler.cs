using Windows.ApplicationModel;

namespace Machine.App;

internal static class MatasuriStartupTaskEnabler
{
    public const string TaskId = "MatasuriStartup";

    public static async Task<StartupTaskState> EnsureEnabledAsync()
    {
        var startupTask = await StartupTask.GetAsync(TaskId);
        return startupTask.State == StartupTaskState.Disabled
            ? await startupTask.RequestEnableAsync()
            : startupTask.State;
    }
}
