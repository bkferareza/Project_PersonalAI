using Machine.Core;
using Machine.Ollama;
using Machine.Windows;
using Microsoft.UI.Xaml;

namespace Machine.App;

public partial class App : Application
{
    private Window? _window;
    private HttpClient? _ollamaHttpClient;

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
        IMachineProcessProvider processProvider =
            new WindowsMachineProcessProvider();
        var ollamaHttpClient = new HttpClient
        {
            BaseAddress = new Uri(
                "http://127.0.0.1:11434/",
                UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(2),
        };
        _ollamaHttpClient = ollamaHttpClient;
        IOllamaStatusProvider ollamaStatusProvider =
            new OllamaStatusProvider(ollamaHttpClient);

        _window = new MainWindow(
            identityProvider,
            resourceProvider,
            processProvider,
            ollamaStatusProvider);
        _window.Closed += OnWindowClosed;
        _window.Activate();
    }

    private void OnWindowClosed(
        object sender,
        WindowEventArgs args)
    {
        if (_window is not null)
        {
            _window.Closed -= OnWindowClosed;
            _window = null;
        }

        _ollamaHttpClient?.Dispose();
        _ollamaHttpClient = null;
    }
}
