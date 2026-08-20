namespace Machine.App;

internal static class MatasuriDevelopmentShutdownGate
{
    public static async Task WaitForRuntimeRestorationAsync(
        Task runtimeInitialization)
    {
        ArgumentNullException.ThrowIfNull(runtimeInitialization);
        await runtimeInitialization;
    }
}
