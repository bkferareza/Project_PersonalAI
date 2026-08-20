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
}
