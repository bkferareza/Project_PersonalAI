using Machine.Core;
using Machine.Windows;
using Microsoft.UI.Xaml;

namespace Machine.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        IMachineIdentityProvider identityProvider =
            new WindowsMachineIdentityProvider();
        IMachineResourceProvider resourceProvider =
            new WindowsMachineResourceProvider();

        _window = new MainWindow(
            identityProvider,
            resourceProvider);
        _window.Activate();
    }
}
