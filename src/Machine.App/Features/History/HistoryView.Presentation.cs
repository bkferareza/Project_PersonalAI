using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Machine.Core;
using Machine.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace Machine.App.Features;

public sealed partial class HistoryView
{
    private MachineHistoryService? _historyService;
    private MachineHistoryRange _selectedHistoryRange =
        MachineHistoryRange.Last24Hours;

    internal void Initialize(MachineHistoryService historyService)
    {
        ArgumentNullException.ThrowIfNull(historyService);
        _historyService = historyService;
    }

    private void OnHistoryRangeClicked(
        object sender,
        RoutedEventArgs args)
    {
        _selectedHistoryRange = (sender as Button)?.Tag?.ToString() switch
        {
            "7d" => MachineHistoryRange.Last7Days,
            "30d" => MachineHistoryRange.Last30Days,
            "all" => MachineHistoryRange.All,
            _ => MachineHistoryRange.Last24Hours
        };
        UpdateDashboard();
    }

    internal void UpdateDashboard()
    {
        if (_historyService is null)
        {
            return;
        }
        var snapshot = _historyService.GetSnapshot(
            _selectedHistoryRange,
            DateTimeOffset.UtcNow);
        HistoryObservedDurationText.Text = snapshot.TotalObservedDuration >
                TimeSpan.Zero
            ? $"{FormatDuration(snapshot.TotalObservedDuration)} observed"
            : "Beginning now";
        HistoryResolutionText.Text =
            $"{FormatHistoryResolution(snapshot.Resolution)} rollups · " +
            "offline and suspended time remain gaps";
        SetHistoryRangeButtonState();

        var cpu = AggregateHistoryMetric(
            snapshot.Rollups,
            static item => item.CpuUtilizationPercent);
        var memory = AggregateHistoryMetric(
            snapshot.Rollups,
            static item => item.MemoryUtilizationPercent);
        var gpu = AggregateHistoryMetric(
            snapshot.Rollups,
            static item => item.GpuUtilizationPercent);
        var summary = new List<string>();
        if (cpu is not null)
        {
            summary.Add($"CPU {cpu.Mean:F0}% avg · {cpu.Maximum:F0}% peak");
        }
        if (memory is not null)
        {
            summary.Add($"Memory {memory.Mean:F0}% avg");
        }
        if (gpu is not null)
        {
            summary.Add($"GPU {gpu.Mean:F0}% avg · {gpu.Maximum:F0}% peak");
        }
        HistoryResourceSummaryText.Text = summary.Count == 0
            ? "Waiting for history"
            : string.Join("\n", summary);
        var wallPower = AggregateHistoryMetric(snapshot.Rollups,
            static item => item.EstimatedSystemPowerWatts);
        var observedEnergy = snapshot.Rollups.Sum(item =>
            item.ObservedEnergyWattHours?.Total ?? 0d);
        HistoryPowerSummaryText.Text = wallPower is null
            ? "Estimated power begins with new observations"
            : $"Estimated wall {wallPower.Mean:F0} W average · " +
                $"{observedEnergy / 1000d:F3} kWh observed";

        var activeTicks = snapshot.Rollups.Aggregate(
            0L,
            (total, item) => SaturatingAddTicks(
                total,
                item.ActivityDurations.ActiveTicks));
        var idleTicks = snapshot.Rollups.Aggregate(
            0L,
            (total, item) => SaturatingAddTicks(
                total,
                item.ActivityDurations.IdleTicks));
        SetDurationColumns(
            [HistoryActiveColumn, HistoryIdleColumn],
            [activeTicks, idleTicks]);
        HistoryActivityText.Text =
            $"Active {FormatDuration(TimeSpan.FromTicks(activeTicks))} · " +
            $"Idle {FormatDuration(TimeSpan.FromTicks(idleTicks))}";

        var stateTicks = new[]
        {
            snapshot.Rollups.Aggregate(0L, (total, item) =>
                SaturatingAddTicks(total, item.StateDurations.StableTicks)),
            snapshot.Rollups.Aggregate(0L, (total, item) =>
                SaturatingAddTicks(total, item.StateDurations.AttentionTicks)),
            snapshot.Rollups.Aggregate(0L, (total, item) =>
                SaturatingAddTicks(total, item.StateDurations.WarningTicks)),
            snapshot.Rollups.Aggregate(0L, (total, item) =>
                SaturatingAddTicks(total, item.StateDurations.CriticalTicks)),
            snapshot.Rollups.Aggregate(0L, (total, item) =>
                SaturatingAddTicks(total, item.StateDurations.UnknownTicks))
        };
        SetDurationColumns(
            [
                HistoryStableColumn,
                HistoryAttentionColumn,
                HistoryWarningColumn,
                HistoryCriticalColumn,
                HistoryUnknownColumn
            ],
            stateTicks);
        HistoryStateDurationText.Text = string.Join(
            " · ",
            new[]
            {
                ("Stable", stateTicks[0]),
                ("Attention", stateTicks[1]),
                ("Warning", stateTicks[2]),
                ("Critical", stateTicks[3]),
                ("Unknown", stateTicks[4])
            }.Where(item => item.Item2 > 0).Select(item =>
                $"{item.Item1} " +
                FormatDuration(TimeSpan.FromTicks(item.Item2)))) switch
        {
            "" => "No state-duration evidence yet",
            var text => text
        };

        var groupedEvents = MachineHistoryEventGrouper.GroupForDisplay(
            snapshot.Events)
            .Take(200)
            .Select(CreateHistoryEventDisplayItem)
            .ToArray();
        HistoryEventsList.ItemsSource = groupedEvents;
        HistoryEventsEmptyText.Visibility = groupedEvents.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        RenderHistoryTrends(snapshot.Rollups);
    }

    private void SetHistoryRangeButtonState()
    {
        var selected = _selectedHistoryRange switch
        {
            MachineHistoryRange.Last7Days => History7DayButton,
            MachineHistoryRange.Last30Days => History30DayButton,
            MachineHistoryRange.All => HistoryAllButton,
            _ => History24HourButton
        };
        foreach (var button in new[]
        {
            History24HourButton,
            History7DayButton,
            History30DayButton,
            HistoryAllButton
        })
        {
            button.Opacity = ReferenceEquals(button, selected) ? 1d : 0.55d;
            button.FontWeight = ReferenceEquals(button, selected)
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    private static string FormatHistoryResolution(
        MachineHistoryResolution resolution) => resolution switch
        {
            MachineHistoryResolution.FiveMinutes => "5-minute",
            MachineHistoryResolution.Hour => "Hourly",
            MachineHistoryResolution.Day => "Daily",
            MachineHistoryResolution.Month => "Monthly",
            _ => "Bounded"
        };

    private static HistoryEventDisplayItem CreateHistoryEventDisplayItem(
        MachineHistoryEvent item)
    {
        var title = item.Count > 1
            ? $"{item.Title} · {item.Count} occurrences"
            : item.Title;
        var time = item.Count > 1 && item.PeriodStart is { } start
            ? $"{start.ToLocalTime():HH:mm}–" +
                $"{(item.PeriodEnd ?? item.OccurredAt).ToLocalTime():HH:mm}"
            : item.OccurredAt.ToLocalTime().ToString("HH:mm");
        return new(
            time,
            title,
            item.Detail,
            string.IsNullOrWhiteSpace(item.Detail)
                ? Visibility.Collapsed
                : Visibility.Visible);
    }

    private void OnHistoryTrendSizeChanged(
        object sender,
        SizeChangedEventArgs args) => UpdateDashboard();

    private void RenderHistoryTrends(
        IReadOnlyList<MachineHistoryRollup> rollups)
    {
        var width = Math.Max(1d, HistoryTrendCanvas.ActualWidth);
        var height = Math.Max(1d, HistoryTrendCanvas.ActualHeight);
        SetHistoryPath(
            HistoryCpuPolyline,
            CreateHistorySegments(
                rollups,
                static item => item.CpuUtilizationPercent?.Mean,
                width,
                height));
        SetHistoryPath(
            HistoryMemoryPolyline,
            CreateHistorySegments(
                rollups,
                static item => item.MemoryUtilizationPercent?.Mean,
                width,
                height));
        var gpuSegments = CreateHistorySegments(
            rollups,
            static item => item.GpuUtilizationPercent?.Mean,
            width,
            height);
        SetHistoryPath(HistoryGpuPolyline, gpuSegments);
        var hasGpuSeries = gpuSegments.Any(segment => segment.Count > 1);
        HistoryGpuPolyline.Visibility = hasGpuSeries
            ? Visibility.Visible
            : Visibility.Collapsed;
        HistoryGpuLegendText.Visibility = hasGpuSeries
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static IReadOnlyList<IReadOnlyList<
        global::Windows.Foundation.Point>> CreateHistorySegments(
            IReadOnlyList<MachineHistoryRollup> rollups,
            Func<MachineHistoryRollup, double?> select,
            double width,
            double height)
    {
        if (rollups.Count == 0)
        {
            return [];
        }
        var start = rollups[0].BucketStart;
        var end = rollups[^1].BucketEnd;
        var durationTicks = Math.Max(1L, (end - start).Ticks);
        var segments = new List<List<global::Windows.Foundation.Point>>();
        List<global::Windows.Foundation.Point>? current = null;
        DateTimeOffset? previousEnd = null;
        foreach (var rollup in rollups)
        {
            var value = select(rollup);
            var isContinuous = previousEnd is null ||
                rollup.BucketStart <= previousEnd.Value;
            if (value is null || !double.IsFinite(value.Value))
            {
                current = null;
                previousEnd = rollup.BucketEnd;
                continue;
            }
            if (current is null || !isContinuous)
            {
                current = [];
                segments.Add(current);
            }
            var x = (rollup.BucketStart - start).Ticks /
                (double)durationTicks * width;
            var y = height - Math.Clamp(value.Value, 0d, 100d) /
                100d * height;
            current.Add(new(x, y));
            previousEnd = rollup.BucketEnd;
        }
        return segments;
    }

    private static void SetHistoryPath(
        Microsoft.UI.Xaml.Shapes.Path path,
        IReadOnlyList<IReadOnlyList<global::Windows.Foundation.Point>>
            segments)
    {
        var geometry = new Microsoft.UI.Xaml.Media.PathGeometry();
        foreach (var points in segments.Where(item => item.Count > 0))
        {
            var figure = new Microsoft.UI.Xaml.Media.PathFigure
            {
                StartPoint = points[0],
                IsClosed = false,
                IsFilled = false
            };
            foreach (var point in points.Skip(1))
            {
                figure.Segments.Add(
                    new Microsoft.UI.Xaml.Media.LineSegment
                    {
                        Point = point
                    });
            }
            geometry.Figures.Add(figure);
        }
        path.Data = geometry;
    }

    private static HistoryMetricAggregate? AggregateHistoryMetric(
        IEnumerable<MachineHistoryRollup> rollups,
        Func<MachineHistoryRollup, MachineHistoryNumericSummary?> select)
    {
        var values = rollups.Select(select)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
        if (values.Length == 0)
        {
            return null;
        }
        var count = values.Sum(item => (double)item.SampleCount);
        return new(
            values.Sum(item => item.Mean * item.SampleCount) / count,
            values.Max(item => item.Maximum));
    }

    private static void SetDurationColumns(
        IReadOnlyList<ColumnDefinition> columns,
        IReadOnlyList<long> values)
    {
        var any = values.Any(value => value > 0);
        for (var index = 0; index < columns.Count; index++)
        {
            columns[index].Width = new GridLength(
                any ? Math.Max(0, values[index]) : index == 0 ? 1 : 0,
                GridUnitType.Star);
        }
    }

    private static long SaturatingAddTicks(long left, long right) =>
        right > 0 && left > long.MaxValue - right
            ? long.MaxValue
            : left + right;

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1d
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{Math.Max(0, duration.Minutes)}m";
}
