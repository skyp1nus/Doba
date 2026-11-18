using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Services;
using Doba.App.ViewModels;
using Doba.Core.Projections;
using Doba.Core.Time;

namespace Doba.App.Screens.Month;

/// <summary>
/// One day cell of a month week. Keyed by its column (0..6) inside the week, so moving to another month updates the
/// cell in place instead of rebuilding it; the items inside are keyed by task id.
/// </summary>
public sealed partial class MonthDayViewModel : ObservableObject
{
    private readonly Func<TaskRow, TaskRowViewModel> _createRow;
    private readonly Action<TaskRowViewModel, TaskRow> _updateRow = static (vm, r) => vm.Update(r);

    public MonthDayViewModel(int column, MonthDayCell cell, int laneCount, Func<TaskRow, TaskRowViewModel> createRow)
    {
        ArgumentNullException.ThrowIfNull(cell);
        Column = column;
        _createRow = createRow;
        Update(cell, laneCount);
    }

    /// <summary>0-based column in the week (row key).</summary>
    public int Column { get; }

    /// <summary>The items that fit, in display order; updated in place by task id.</summary>
    public ObservableCollection<TaskRowViewModel> Items { get; } = [];

    [ObservableProperty] public partial DateOnly Date { get; private set; }

    /// <summary>"15".</summary>
    [ObservableProperty] public partial string DayNumber { get; private set; } = string.Empty;

    /// <summary>False for the leading/trailing days of the neighbouring months (dimmed number, faint background).</summary>
    [ObservableProperty] public partial bool InMonth { get; private set; }

    /// <summary>Accent number (semibold) and the accent dot.</summary>
    [ObservableProperty] public partial bool IsToday { get; private set; }

    /// <summary>Items that did not fit; 0 = none.</summary>
    [ObservableProperty] public partial int MoreCount { get; private set; }

    /// <summary>"+3 more".</summary>
    [ObservableProperty] public partial string MoreLabel { get; private set; } = string.Empty;

    /// <summary>Multi-day lanes of the week (the item list starts below them).</summary>
    [ObservableProperty] public partial int LaneCount { get; private set; }

    /// <summary>"Open Sunday 15 November" (the cell button's accessible name).</summary>
    [ObservableProperty] public partial string OpenLabel { get; private set; } = string.Empty;

    public bool HasMore => MoreCount > 0;

    public bool IsOutside => !InMonth;

    /// <summary>The model last pushed in.</summary>
    public MonthDayCell Model { get; private set; } = null!;

    public void Update(MonthDayCell cell, int laneCount)
    {
        ArgumentNullException.ThrowIfNull(cell);
        Model = cell;
        if (Date != cell.Date || DayNumber.Length == 0)
        {
            Date = cell.Date;
            DayNumber = cell.DayNumber.ToString(CultureInfo.InvariantCulture);
            OpenLabel = string.Concat("Open ", DateMath.WeekdayLong(cell.Date.DayOfWeek), " ", DayNumber, " ",
                DateMath.MonthNames[cell.Date.Month - 1]);
        }

        InMonth = cell.InMonth;
        IsToday = cell.IsToday;
        LaneCount = laneCount;
        if (MoreCount != cell.MoreCount || MoreLabel.Length == 0)
        {
            MoreCount = cell.MoreCount;
            MoreLabel = string.Concat("+", cell.MoreCount.ToString(CultureInfo.InvariantCulture), " more");
        }

        KeyedSync.Apply(Items, cell.Items, static r => r.Id, static vm => vm.Id, _createRow, _updateRow);
        HasItems = Items.Count > 0;
    }

    /// <summary>At least one item is shown (an empty list takes no space, so "+N more" sits at the top of the list).</summary>
    [ObservableProperty] public partial bool HasItems { get; private set; }

    partial void OnMoreCountChanged(int value) => OnPropertyChanged(nameof(HasMore));

    partial void OnInMonthChanged(bool value) => OnPropertyChanged(nameof(IsOutside));
}
