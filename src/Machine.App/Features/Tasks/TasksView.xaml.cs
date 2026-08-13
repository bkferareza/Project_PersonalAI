using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

public sealed partial class TasksView : UserControl
{
    public TasksView()
    {
        InitializeComponent();
        RefreshTasksButton.Click += OnRefreshTasksClicked;
        TaskSearchBox.TextChanged += OnTaskFilterChanged;
        TaskEnabledFilter.SelectionChanged += OnTaskFilterChanged;
        TaskStateFilter.SelectionChanged += OnTaskFilterChanged;
        TaskResultFilter.SelectionChanged += OnTaskFilterChanged;
    }
}

public sealed record ScheduledTaskDisplayItem(
    string Name,
    string Path,
    string State,
    string ScheduleDetails,
    string EvidenceDetails);
