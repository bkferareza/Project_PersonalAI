using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

public sealed partial class RuntimeView : UserControl
{
    public RuntimeView()
    {
        InitializeComponent();
    }
}

public sealed record ProcessDisplayItem(
    string Name,
    string Details);

public sealed record OllamaModelDisplayItem(
    string Name,
    string ModelDetails,
    string RuntimeDetails);
