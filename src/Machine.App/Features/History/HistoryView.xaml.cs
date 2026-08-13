using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

public sealed partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
        History24HourButton.Click += OnHistoryRangeClicked;
        History7DayButton.Click += OnHistoryRangeClicked;
        History30DayButton.Click += OnHistoryRangeClicked;
        HistoryAllButton.Click += OnHistoryRangeClicked;
        HistoryTrendCanvas.SizeChanged += OnHistoryTrendSizeChanged;
    }
}

public sealed record HistoryEventDisplayItem(
    string Time,
    string Title,
    string? Detail,
    Visibility DetailVisibility);

public sealed record HistoryMetricAggregate(
    double Mean,
    double Maximum);
