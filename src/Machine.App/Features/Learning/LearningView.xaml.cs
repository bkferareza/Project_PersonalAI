using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

public sealed partial class LearningView : UserControl
{
    public LearningView()
    {
        InitializeComponent();
    }
}

public sealed record LearningProfileDisplayItem(
    string Header,
    string Evidence,
    string CpuValue,
    string MemoryValue,
    string NetworkValue,
    string PowerValue,
    string Reinforcement,
    double Opacity);

public sealed record LearningSignalDisplayItem(
    string Signal,
    string CurrentValue,
    string LearnedValue);

public sealed record LearningPatternDisplayItem(
    string Header,
    string Status,
    string CpuValue,
    string MemoryValue,
    string NetworkValue,
    string Evidence);

public sealed record LearnedItemDisplayItem(
    string Label,
    string Text,
    string Evidence);

public sealed record LearningEpisodeDisplayItem(
    string Header,
    string Context,
    string CpuDetails,
    string MemoryDetails,
    string OutcomeAndFindings);

public sealed record LearningActivityDisplayItem(
    string Header,
    string Detail);
