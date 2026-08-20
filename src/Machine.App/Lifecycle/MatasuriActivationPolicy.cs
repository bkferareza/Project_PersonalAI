namespace Machine.App;

public enum MatasuriActivationDisposition
{
    EstablishAmbientPresence,
    SummonDashboard
}

public static class MatasuriActivationPolicy
{
    public static MatasuriActivationDisposition Resolve(
        bool isStartupTaskActivation) =>
        isStartupTaskActivation
            ? MatasuriActivationDisposition.EstablishAmbientPresence
            : MatasuriActivationDisposition.SummonDashboard;
}
