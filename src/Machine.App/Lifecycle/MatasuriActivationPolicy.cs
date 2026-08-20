namespace Machine.App;

public enum MatasuriActivationDisposition
{
    EstablishAmbientPresence,
    SummonDashboard,
    DevelopmentShutdown
}

public static class MatasuriActivationPolicy
{
    public const string DevelopmentShutdownArgument =
        "--matasuri-dev-shutdown";
    public const string DevelopmentShutdownProtocol = "matasuri-dev";

    public static MatasuriActivationDisposition Resolve(
        bool isStartupTaskActivation,
        string? arguments = null,
        bool isDevelopmentBuild = false,
        bool isDevelopmentShutdownProtocolActivation = false)
    {
        if (isDevelopmentBuild &&
            (ContainsDevelopmentShutdownArgument(arguments) ||
             isDevelopmentShutdownProtocolActivation))
        {
            return MatasuriActivationDisposition.DevelopmentShutdown;
        }

        return
        isStartupTaskActivation
            ? MatasuriActivationDisposition.EstablishAmbientPresence
            : MatasuriActivationDisposition.SummonDashboard;
    }

    private static bool ContainsDevelopmentShutdownArgument(
        string? arguments) =>
        !string.IsNullOrWhiteSpace(arguments) &&
        arguments.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
            .Any(argument => string.Equals(
                argument,
                DevelopmentShutdownArgument,
                StringComparison.OrdinalIgnoreCase));
}
