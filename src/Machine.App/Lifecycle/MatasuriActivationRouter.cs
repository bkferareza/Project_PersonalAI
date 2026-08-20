using Microsoft.Windows.AppLifecycle;

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
        AppActivationArguments? pending;
        MatasuriActivationDisposition disposition;
        lock (Sync)
        {
            _app = app;
            disposition = Resolve(_initialActivation);
            pending = _pendingRedirectedActivation;
            _pendingRedirectedActivation = null;
        }

        if (pending is not null)
        {
            app.HandleRedirectedActivation(Resolve(pending));
        }

        return disposition;
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
        AppActivationArguments? activation) =>
        MatasuriActivationPolicy.Resolve(
            activation?.Kind == ExtendedActivationKind.StartupTask);
}
