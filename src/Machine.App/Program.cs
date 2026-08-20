using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Machine.App;

internal static class Program
{
    private const string PrimaryInstanceKey = "Matasuri.Primary";
    private static App? _application;

    [STAThread]
    private static async Task Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var current = AppInstance.GetCurrent();
        var activation = current.GetActivatedEventArgs();
        var primary = AppInstance.FindOrRegisterForKey(PrimaryInstanceKey);

        if (!primary.IsCurrent)
        {
            await primary.RedirectActivationToAsync(activation);
            return;
        }

        MatasuriActivationRouter.SetInitialActivation(activation);
        primary.Activated += OnRedirectedActivation;

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _application = new App();
        });
    }

    private static void OnRedirectedActivation(
        object? sender,
        AppActivationArguments args) =>
        MatasuriActivationRouter.HandleRedirectedActivation(args);
}
