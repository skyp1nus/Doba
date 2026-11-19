using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Services;
using Doba.App.ViewModels;
using Doba.Core.Projections;

namespace Doba.App.Screens.Week;

/// <summary>
/// One day column of a week row (keyed by its column, so it lives across week changes): header, progress and the
/// cards that fit. Every text comes ready from the projection's <see cref="WeekDayCell"/>.
/// </summary>
public sealed partial class WeekCellViewModel : ObservableObject
{
    private readonly Func<TaskRow, TaskRowViewModel> _createRow;
    private readonly Func<TaskRow, WeekCardSlot> _createSlot;
    private static readonly Func<TaskRow, long> s_keyOfRow = static r => r.Id;
    private static readonly Func<WeekCardSlot, long> s_keyOfSlot = static s => s.Row.Id;
    private static readonly Action<WeekCardSlot, TaskRow> s_updateSlot = static (s, r) => s.Row.Update(r);

    public WeekCellViewModel(WeekDayCell cell, int laneCount, Func<TaskRow, TaskRowViewModel> createRow)
    {
        ArgumentNullException.ThrowIfNull(cell);
        _createRow = createRow;
        _createSlot = r => new WeekCardSlot(_createRow(r));
        Tasks = new SlotRows(Cards);
        Column = cell.Column;
        Update(cell, laneCount);
    }

    /// <summary>0-based column (0..3; the row key).</summary>
    public int Column { get; }

    /// <summary>
    /// The card positions the view binds to, in display order. Within one week they are reconciled by task id (a
    /// completed or re-sorted task keeps its card and its transitions); when the cell moves to another day (a week
    /// change) they are reused by position: each slot is pointed at the task now in its place, so the cards and their
    /// containers stay and only the difference in count is added or removed.
    /// </summary>
    public ObservableCollection<WeekCardSlot> Cards { get; } = [];

    /// <summary>The tasks of <see cref="Cards"/>, in display order.</summary>
    public IReadOnlyList<TaskRowViewModel> Tasks { get; }

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

    public void Update(WeekDayCell cell, int laneCount)
    {
        ArgumentNullException.ThrowIfNull(cell);
        Month = cell.MonthShort;
        IsToday = cell.IsToday;
        Empty = cell.Empty;
        LaneCount = laneCount;

        // Progress before Date: the header's progress line jumps (no 500 ms sweep) when its day changes.
        Progress = cell.Progress;
        var dayChanged = Date != cell.Date || DayNumber.Length == 0;
        if (dayChanged)
        {
            Date = cell.Date;
            DayNumber = cell.DayLabel;
            Weekday = cell.WeekdayLabel;
        }

        if (dayChanged || IsPreview != cell.IsPreview)
        {
            IsPreview = cell.IsPreview;
            OpenLabel = cell.OpenLabel;
        }

        if (MoreCount != cell.MoreCount || MoreLabel.Length == 0)
        {
            MoreCount = cell.MoreCount;
            MoreLabel = cell.MoreLabel;
        }

        if (dayChanged)
        {
            ReuseByPosition(cell.Tasks);
        }
        else
        {
            KeyedSync.Apply(Cards, cell.Tasks, s_keyOfRow, s_keyOfSlot, _createSlot, s_updateSlot);
        }
    }

    /// <summary>
    /// Another day in this column: point the existing slots at the new day's tasks in order (a slot whose task is the
    /// same keeps its row view-model), then add or remove slots at the end for the difference in count.
    /// </summary>
    private void ReuseByPosition(IReadOnlyList<TaskRow> rows)
    {
        var reused = Math.Min(rows.Count, Cards.Count);
        for (var i = 0; i < reused; i++)
        {
            var slot = Cards[i];
            var row = rows[i];
            if (slot.Row.Id == row.Id)
            {
                slot.Row.Update(row);
            }
            else
            {
                slot.Row = _createRow(row);
            }
        }

        for (var i = Cards.Count - 1; i >= rows.Count; i--)
        {
            Cards.RemoveAt(i);
        }

        for (var i = reused; i < rows.Count; i++)
        {
            Cards.Add(_createSlot(rows[i]));
        }
    }

    /// <summary>A read-only view of the slots' rows (no copy).</summary>
    private sealed class SlotRows(ObservableCollection<WeekCardSlot> slots) : IReadOnlyList<TaskRowViewModel>
    {
        public TaskRowViewModel this[int index] => slots[index].Row;

        public int Count => slots.Count;

        public IEnumerator<TaskRowViewModel> GetEnumerator()
        {
            foreach (var slot in slots)
            {
                yield return slot.Row;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    partial void OnMoreCountChanged(int value) => OnPropertyChanged(nameof(HasMore));

    partial void OnEmptyChanged(bool value) => OnPropertyChanged(nameof(HasTasks));
}
