using Machine.App;

namespace Machine.Tests;

public sealed class MatasuriActivationPolicyTests
{
    [Fact]
    public void StartupActivationEstablishesAmbientPresence() =>
        Assert.Equal(
            MatasuriActivationDisposition.EstablishAmbientPresence,
            MatasuriActivationPolicy.Resolve(isStartupTaskActivation: true));

    [Fact]
    public void InitialNormalActivationSummonsTheDashboard() =>
        Assert.Equal(
            MatasuriActivationDisposition.SummonDashboard,
            MatasuriActivationPolicy.Resolve(isStartupTaskActivation: false));

    [Fact]
    public void RedirectedNormalActivationAlsoSummonsTheDashboard() =>
        Assert.Equal(
            MatasuriActivationDisposition.SummonDashboard,
            MatasuriActivationPolicy.Resolve(isStartupTaskActivation: false));

    [Fact]
    public void DebugShutdownActivationIsAvailableOnlyToDebugBuilds()
    {
        Assert.Equal(
            MatasuriActivationDisposition.DevelopmentShutdown,
            MatasuriActivationPolicy.Resolve(
                isStartupTaskActivation: false,
                MatasuriActivationPolicy.DevelopmentShutdownArgument,
                isDevelopmentBuild: true));
        Assert.Equal(
            MatasuriActivationDisposition.SummonDashboard,
            MatasuriActivationPolicy.Resolve(
                isStartupTaskActivation: false,
                MatasuriActivationPolicy.DevelopmentShutdownArgument,
                isDevelopmentBuild: false));
    }

    [Fact]
    public async Task DevelopmentShutdownWaitsForRestoration()
    {
        var restoration = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = MatasuriDevelopmentShutdownGate
            .WaitForRuntimeRestorationAsync(restoration.Task);

        Assert.False(wait.IsCompleted);
        restoration.SetResult();
        await wait;
    }

    [Fact]
    public void DebugProtocolShutdownIsIgnoredOutsideDebugBuilds()
    {
        Assert.Equal(
            MatasuriActivationDisposition.DevelopmentShutdown,
            MatasuriActivationPolicy.Resolve(
                isStartupTaskActivation: false,
                isDevelopmentBuild: true,
                isDevelopmentShutdownProtocolActivation: true));
        Assert.Equal(
            MatasuriActivationDisposition.SummonDashboard,
            MatasuriActivationPolicy.Resolve(
                isStartupTaskActivation: false,
                isDevelopmentBuild: false,
                isDevelopmentShutdownProtocolActivation: true));
    }
}
