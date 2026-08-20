using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Machine.App;

internal static class MatasuriActivationRouter
{
    private static readonly object Sync = new();
    private static App? _app;
    private static AppActivationArguments? _initialActivation;
    private static AppActivationArguments? _pendingRedirectedActivation;

    public static void SetInitialActivation(AppActivationArguments activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        lock (Sync)
        {
            _initialActivation = activation;
        }
    }

    public static MatasuriActivationDisposition Connect(App app)
    {
        ArgumentNullException.ThrowIfNull(app);
        MatasuriActivationDisposition disposition;
        lock (Sync)
        {
            disposition = Resolve(_initialActivation);
        }

        return disposition;
    }

    public static void ProcessPendingRedirectedActivation(App app)
    {
        ArgumentNullException.ThrowIfNull(app);
        AppActivationArguments? pending;
        lock (Sync)
        {
            _app = app;
            pending = _pendingRedirectedActivation;
            _pendingRedirectedActivation = null;
        }

        if (pending is not null)
        {
            app.HandleRedirectedActivation(Resolve(pending));
        }
    }

    public static void HandleRedirectedActivation(
        AppActivationArguments activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        App? app;
        lock (Sync)
        {
            app = _app;
            if (app is null)
            {
                _pendingRedirectedActivation = activation;
                return;
            }
        }

        app.HandleRedirectedActivation(Resolve(activation));
    }

    private static MatasuriActivationDisposition Resolve(
        AppActivationArguments? activation)
    {
#if DEBUG
        var arguments = (activation?.Data as ILaunchActivatedEventArgs)
            ?.Arguments;
        var protocol = activation?.Data as IProtocolActivatedEventArgs;
        return MatasuriActivationPolicy.Resolve(
            activation?.Kind == ExtendedActivationKind.StartupTask,
            arguments,
            isDevelopmentBuild: true,
            isDevelopmentShutdownProtocolActivation:
                string.Equals(
                    protocol?.Uri.Scheme,
                    MatasuriActivationPolicy.DevelopmentShutdownProtocol,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    protocol?.Uri.Host,
                    "shutdown",
                    StringComparison.OrdinalIgnoreCase));
#else
        return MatasuriActivationPolicy.Resolve(
            activation?.Kind == ExtendedActivationKind.StartupTask);
#endif
    }
}
