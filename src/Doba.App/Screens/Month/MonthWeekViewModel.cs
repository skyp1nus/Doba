using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Services;
using Doba.App.ViewModels;
using Doba.Core.Projections;

namespace Doba.App.Screens.Month;

/// <summary>
/// One week row of the month grid: the "W46" label, seven day cells (keyed by column) and up to two lanes of
/// multi-day bars (keyed by task id). Keyed by its position in the month, so it lives across month changes.
/// </summary>
public sealed partial class MonthWeekViewModel : ObservableObject
{
    private readonly Func<TaskRow, TaskRowViewModel> _createRow;
    private readonly Func<SpanBar, MonthSpanViewModel> _createSpan;
    private readonly Action<MonthSpanViewModel, SpanBar> _updateSpan = static (vm, s) => vm.Update(s);
    private readonly Func<MonthDayCell, int> _columnOf;
    private readonly Func<MonthDayCell, MonthDayViewModel> _createDay;
    private readonly Action<MonthDayViewModel, MonthDayCell> _updateDay;
    private int _weekNumber = -1;

    public MonthWeekViewModel(int index, MonthWeek week, Func<TaskRow, TaskRowViewModel> createRow)
    {
        ArgumentNullException.ThrowIfNull(week);
        Index = index;
        _createRow = createRow;
        _createSpan = s => new MonthSpanViewModel(s, _createRow(s.Row));
        _columnOf = c => c.Date.DayNumber - WeekStart.DayNumber;
        _createDay = c => new MonthDayViewModel(_columnOf(c), c, LaneCount, _createRow);
        _updateDay = (vm, c) => vm.Update(c, LaneCount);
        Update(week);
    }

    /// <summary>0-based row in the month grid (row key).</summary>
    public int Index { get; }

    public ObservableCollection<MonthDayViewModel> Days { get; } = [];

    public ObservableCollection<MonthSpanViewModel> Spans { get; } = [];

    [ObservableProperty] public partial DateOnly WeekStart { get; private set; }

    /// <summary>"W46".</summary>
    [ObservableProperty] public partial string Label { get; private set; } = string.Empty;

    /// <summary>"Open week 46".</summary>
    [ObservableProperty] public partial string OpenLabel { get; private set; } = string.Empty;

    /// <summary>Contains today: the label is drawn in the accent.</summary>
    [ObservableProperty] public partial bool IsCurrent { get; private set; }

    /// <summary>Week view anchor for the label click (today in the current week, else the week start).</summary>
    [ObservableProperty] public partial DateOnly ZoomTarget { get; private set; }

    /// <summary>Row height from the projection: floor((body − header) / weeks).</summary>
    [ObservableProperty] public partial double RowHeight { get; private set; }

    /// <summary>Multi-day lanes shown in this week (0..2).</summary>
    [ObservableProperty] public partial int LaneCount { get; private set; }

    /// <summary>The model last pushed in.</summary>
    public MonthWeek Model { get; private set; } = null!;

    public void Update(MonthWeek week)
    {
        ArgumentNullException.ThrowIfNull(week);
        Model = week;
        WeekStart = week.WeekStart;
        if (_weekNumber != week.WeekNumber)
        {
            _weekNumber = week.WeekNumber;
            var n = week.WeekNumber.ToString(CultureInfo.InvariantCulture);
            Label = "W" + n;
            OpenLabel = "Open week " + n;
        }

        IsCurrent = week.IsCurrent;
        ZoomTarget = week.ZoomTarget;
        RowHeight = week.RowHeight;
        LaneCount = week.LaneCount;
        KeyedSync.Apply(Spans, week.Spans, static s => s.Row.Id, static vm => vm.Id, _createSpan, _updateSpan);
        KeyedSync.Apply(Days, week.Days, _columnOf, static vm => vm.Column, _createDay, _updateDay);
    }
}
