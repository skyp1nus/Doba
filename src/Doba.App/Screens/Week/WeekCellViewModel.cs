using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Services;
using Doba.App.ViewModels;
using Doba.Core.Projections;
using Doba.Core.Time;

namespace Doba.App.Screens.Week;

/// <summary>
/// One day column of a week row (keyed by its column, so it lives across week changes): header, progress and the
/// cards that fit.
/// </summary>
public sealed partial class WeekCellViewModel : ObservableObject
{
    private readonly Func<TaskRow, TaskRowViewModel> _createRow;
    private string? _weekdaySource;

    public WeekCellViewModel(WeekDayCell cell, int laneCount, Func<TaskRow, TaskRowViewModel> createRow, int hiddenSpans = 0)
    {
        ArgumentNullException.ThrowIfNull(cell);
        _createRow = createRow;
        Column = cell.Column;
        Update(cell, laneCount, hiddenSpans);
    }

    /// <summary>0-based column (0..3; the row key).</summary>
    public int Column { get; }

    /// <summary>The cards that fit, in display order; updated in place by task id.</summary>
    public ObservableCollection<TaskRowViewModel> Tasks { get; } = [];

    /// <summary>The day shown.</summary>
    [ObservableProperty] public partial DateOnly Date { get; private set; }

    /// <summary>"9".</summary>
    [ObservableProperty] public partial string DayNumber { get; private set; } = string.Empty;

    /// <summary>"MON" (the prototype upper-cases the weekday with CSS).</summary>
    [ObservableProperty] public partial string Weekday { get; private set; } = string.Empty;

    /// <summary>"Nov".</summary>
    [ObservableProperty] public partial string Month { get; private set; } = string.Empty;

    [ObservableProperty] public partial bool IsToday { get; private set; }

    /// <summary>The next week's first day, shown dimmed at the end of row 2.</summary>
    [ObservableProperty] public partial bool IsPreview { get; private set; }

    /// <summary>0..1 done share of the day's single-day tasks.</summary>
    [ObservableProperty] public partial double Progress { get; private set; }

    /// <summary>Tasks that did not fit (and multi-day bars of this day in lanes the row cannot show); 0 = none.</summary>
    [ObservableProperty] public partial int MoreCount { get; private set; }

    /// <summary>"+2 more".</summary>
    [ObservableProperty] public partial string MoreLabel { get; private set; } = string.Empty;

    /// <summary>Nothing to show for the day: no single-day tasks and no hidden bars ("Nothing here").</summary>
    [ObservableProperty] public partial bool Empty { get; private set; }

    /// <summary>Multi-day lanes of the row (the cards start below them).</summary>
    [ObservableProperty] public partial int LaneCount { get; private set; }

    /// <summary>"Open Monday 9 November" / "Open next Monday 16 November" (header button name).</summary>
    [ObservableProperty] public partial string OpenLabel { get; private set; } = string.Empty;

    public bool HasMore => MoreCount > 0;

    public bool HasTasks => !Empty;

    /// <summary>Every column but the first draws a 1 px left separator.</summary>
    public bool HasSeparator => Column != 0;

    /// <param name="hiddenSpans">Multi-day bars covering this day in lanes the short row does not show (counted in
    /// "+N more"; see <see cref="WeekRowViewModel.VisibleLanes"/>).</param>
    public void Update(WeekDayCell cell, int laneCount, int hiddenSpans = 0)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (!string.Equals(_weekdaySource, cell.Weekday, StringComparison.Ordinal))
        {
            _weekdaySource = cell.Weekday;
            Weekday = cell.Weekday.ToUpperInvariant();
        }

        Month = cell.MonthShort;
        IsToday = cell.IsToday;
        Empty = cell.Empty && hiddenSpans == 0;
        LaneCount = laneCount;

        // Progress before Date: the header's progress line jumps (no 500 ms sweep) when its day changes.
        Progress = cell.Progress;
        var dayChanged = Date != cell.Date || DayNumber.Length == 0;
        if (dayChanged)
        {
            Date = cell.Date;
            DayNumber = cell.DayNumber.ToString(CultureInfo.InvariantCulture);
        }

        if (dayChanged || IsPreview != cell.IsPreview)
        {
            IsPreview = cell.IsPreview;
            OpenLabel = string.Concat(IsPreview ? "Open next " : "Open ", DateMath.WeekdayLong(Date.DayOfWeek), " ",
                DayNumber, " ", DateMath.MonthNames[Date.Month - 1]);
        }

        var more = cell.MoreCount + hiddenSpans;
        if (MoreCount != more || MoreLabel.Length == 0)
        {
            MoreCount = more;
            MoreLabel = string.Concat("+", more.ToString(CultureInfo.InvariantCulture), " more");
        }

        KeyedSync.Apply(Tasks, cell.Tasks, r => r.Id, vm => vm.Id, _createRow, (vm, r) => vm.Update(r));
    }

    partial void OnMoreCountChanged(int value) => OnPropertyChanged(nameof(HasMore));

    partial void OnEmptyChanged(bool value) => OnPropertyChanged(nameof(HasTasks));
}
