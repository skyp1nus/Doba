using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.App.Screens;
using Doba.App.Services;
using Doba.App.Shell;
using Doba.Core.Editing;
using Doba.Core.Model;
using Doba.Core.Time;

namespace Doba.App.Dialogs;

/// <summary>
/// The New / Edit task dialog. Every rule (defaults, pill labels, date picking, "Lasts", wheel commit, save) is in
/// <see cref="TaskDialog"/> (Core); this view-model holds the current <see cref="TaskDraft"/>, replaces it on each edit
/// and exposes what the view renders. Nothing is written while the dialog is open: Save / Enter and Delete only remember
/// what to do and ask the host to close; the store is written in <see cref="OnClosed"/>, after the exit animation
/// (motion spec, "Оверлеї").
/// </summary>
public sealed partial class TaskDialogViewModel : OverlayViewModel
{
    private static readonly string[] s_hours =
        [.. Enumerable.Range(0, 24).Select(h => h.ToString("00", CultureInfo.InvariantCulture))];

    private static readonly string[] s_minutes =
        [.. Enumerable.Range(0, 12).Select(m => (m * 5).ToString("00", CultureInfo.InvariantCulture))];

    private static readonly (Priority Priority, string Label)[] s_priorities =
    [
        (Priority.None, "No priority"),
        (Priority.Low, Priority.Low.DisplayName()),
        (Priority.Medium, Priority.Medium.DisplayName()),
        (Priority.High, Priority.High.DisplayName()),
        (Priority.Asap, Priority.Asap.DisplayName()),
    ];

    private PendingAction _pending;
    private TaskDraft? _closingDraft;

    // Inputs of the mini calendar last pushed into CalendarDays / DateOptions (they are rebuilt only when these change).
    private (DateOnly Month, DateOnly? Date, int Days, DateOnly Today, DayOfWeek WeekStart)? _calendarKey;
    private (DateOnly? Date, DateOnly Today, DayOfWeek WeekStart)? _optionsKey;

    public TaskDialogViewModel(ScreenServices services, TaskDraft draft)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(draft);
        Services = services;

        for (var i = 0; i < 42; i++)
        {
            CalendarDays.Add(new CalendarDayItem(PickDay));
        }

        for (var i = 0; i < 4; i++)
        {
            DateOptions.Add(new DateChipItem(PickQuick));
        }

        foreach (var (priority, label) in s_priorities)
        {
            PriorityOptions.Add(new PriorityOptionItem(priority, label, PickPriority));
        }

        Draft = draft;
        RefreshTags();
    }

    /// <summary>What closing the dialog will do: nothing decided yet, save, delete, just close; Closed = done.</summary>
    private enum PendingAction { None, Commit, Delete, Cancel, Closed }

    /// <summary>Store, settings, clock and task actions.</summary>
    public ScreenServices Services { get; }

    /// <summary>The draft being edited (immutable record; replaced, never mutated).</summary>
    [ObservableProperty] public partial TaskDraft Draft { get; set; }

    /// <summary>Raised when the primary action is used without a title: the view focuses the title box.</summary>
    public event EventHandler? FocusTitleRequested;

    private DateOnly Today => Services.App.DayClock.Today;

    private DayOfWeek WeekStart => Services.App.Settings.Current.WeekStart;

    // ---- Title, notes, header, footer --------------------------------------------------------------------------------

    /// <summary>The task name (two-way with the title box).</summary>
    public string Title
    {
        get => Draft.Title;
        set
        {
            value ??= string.Empty;
            if (!string.Equals(value, Draft.Title, StringComparison.Ordinal))
            {
                Draft = Draft with { Title = value };
            }
        }
    }

    /// <summary>The notes (two-way with the notes box).</summary>
    public string Notes
    {
        get => Draft.Notes;
        set
        {
            value ??= string.Empty;
            if (!string.Equals(value, Draft.Notes, StringComparison.Ordinal))
            {
                Draft = Draft with { Notes = value };
            }
        }
    }

    /// <summary>"New task" / "Edit task".</summary>
    [ObservableProperty] public partial string Heading { get; private set; } = string.Empty;

    /// <summary>"Tomorrow", "Mon 16 Nov → 18 Nov", "Inbox".</summary>
    [ObservableProperty] public partial string WhereLabel { get; private set; } = string.Empty;

    /// <summary>Edit mode: the footer shows Delete.</summary>
    [ObservableProperty] public partial bool IsEdit { get; private set; }

    /// <summary>"Create task" / "Save".</summary>
    [ObservableProperty] public partial string CommitLabel { get; private set; } = string.Empty;

    /// <summary>"↵ create · Esc close" / "↵ save · Esc close".</summary>
    [ObservableProperty] public partial string KeyHint { get; private set; } = string.Empty;

    /// <summary>The title is not blank: the primary button looks enabled.</summary>
    [ObservableProperty] public partial bool CanSave { get; private set; }

    // ---- Pills ---------------------------------------------------------------------------------------------------------

    [ObservableProperty] public partial string DateLabel { get; private set; } = string.Empty;

    [ObservableProperty] public partial string TimeLabel { get; private set; } = string.Empty;

    [ObservableProperty] public partial string PriorityLabel { get; private set; } = string.Empty;

    [ObservableProperty] public partial string TagLabel { get; private set; } = string.Empty;

    [ObservableProperty] public partial bool HasDate { get; private set; }

    [ObservableProperty] public partial bool IsTimed { get; private set; }

    [ObservableProperty] public partial bool HasPriority { get; private set; }

    [ObservableProperty] public partial bool HasTag { get; private set; }

    /// <summary>The draft's priority (the Priority pill's glyph).</summary>
    [ObservableProperty] public partial Priority Priority { get; private set; }

    /// <summary>The Time pill exists only for a dated single-day draft.</summary>
    [ObservableProperty] public partial bool ShowTime { get; private set; }

    [ObservableProperty] public partial bool IsDateOpen { get; private set; }

    [ObservableProperty] public partial bool IsTimeOpen { get; private set; }

    [ObservableProperty] public partial bool IsPriorityOpen { get; private set; }

    [ObservableProperty] public partial bool IsTagOpen { get; private set; }

    // ---- Date popover --------------------------------------------------------------------------------------------------

    /// <summary>"November 2026".</summary>
    [ObservableProperty] public partial string CalendarLabel { get; private set; } = string.Empty;

    /// <summary>Weekday initials in the configured order.</summary>
    public ObservableCollection<WeekdayLetter> WeekdayLetters { get; } = [];

    /// <summary>The 6×7 mini calendar (fixed 42 items, updated in place).</summary>
    public ObservableCollection<CalendarDayItem> CalendarDays { get; } = [];

    /// <summary>Today, Tomorrow, Next week, No date (fixed 4 items, updated in place).</summary>
    public ObservableCollection<DateChipItem> DateOptions { get; } = [];

    /// <summary>"until 18 Nov" or empty.</summary>
    [ObservableProperty] public partial string UntilLabel { get; private set; } = string.Empty;

    /// <summary>"1 day" / "3 days".</summary>
    [ObservableProperty] public partial string DaysLabel { get; private set; } = string.Empty;

    // ---- Time popover --------------------------------------------------------------------------------------------------

    /// <summary>Hour wheel labels "00".."23".</summary>
    public static IReadOnlyList<string> HourLabels => s_hours;

    /// <summary>Minute wheel labels "00".."55" (5-minute steps).</summary>
    public static IReadOnlyList<string> MinuteLabels => s_minutes;

    /// <summary>The draft's time as wheel indices (hour 0..23, minute 0..11).</summary>
    public (int Hour, int Minute) WheelIndices => TaskDialog.WheelIndices(Draft.Time);

    /// <summary>"Any time" (0) / "Set time" (1); two-way with the mode switch.</summary>
    public int TimeModeIndex
    {
        get => Draft.Timed ? 1 : 0;
        set
        {
            if (value is 0 or 1 && (value == 1) != Draft.Timed)
            {
                Draft = Draft with { Timed = value == 1 };
            }
        }
    }

    /// <summary>Index of the draft's duration in <see cref="Durations"/> (-1 for any other value); two-way. Picking a
    /// duration also switches on "Set time", as in the prototype.</summary>
    public int DurationIndex
    {
        get => IndexOf(TaskDialog.DurationOptions, Draft.DurationMinutes);
        set
        {
            if (value >= 0 && value < TaskDialog.DurationOptions.Count)
            {
                var minutes = TaskDialog.DurationOptions[value];
                if (minutes != Draft.DurationMinutes || !Draft.Timed)
                {
                    Draft = Draft with { DurationMinutes = minutes, Timed = true };
                }
            }
        }
    }

    /// <summary>"15m", "30m", "1h", "2h".</summary>
    public IReadOnlyList<string> Durations { get; } = [.. TaskDialog.DurationOptions.Select(TaskDialog.DurationLabel)];

    /// <summary>"14:00 – 15:00".</summary>
    [ObservableProperty] public partial string RangeLabel { get; private set; } = string.Empty;

    // ---- Priority / Tag popovers ---------------------------------------------------------------------------------------

    public ObservableCollection<PriorityOptionItem> PriorityOptions { get; } = [];

    /// <summary>"No tag" plus the store's tags.</summary>
    public ObservableCollection<TagOptionItem> TagOptions { get; } = [];

    // ---- Commands ------------------------------------------------------------------------------------------------------

    /// <summary>A pill: opens its popover, or closes it when it is the open one (another open popover closes).</summary>
    [RelayCommand]
    private void TogglePopover(DraftPopover popover)
    {
        var next = Draft.Open == popover ? DraftPopover.None : popover;
        if (next == DraftPopover.Tag)
        {
            RefreshTags();
        }

        Draft = Draft with { Open = next };
    }

    /// <summary>Closes the open popover (Esc, focus in the title or notes, Done).</summary>
    [RelayCommand]
    public void ClosePopover()
    {
        if (Draft.Open != DraftPopover.None)
        {
            Draft = Draft with { Open = DraftPopover.None };
        }
    }

    [RelayCommand]
    private void PreviousMonth() => Draft = TaskDialog.ShiftCalendarMonth(Draft, -1);

    [RelayCommand]
    private void NextMonth() => Draft = TaskDialog.ShiftCalendarMonth(Draft, 1);

    /// <summary>The calendar's "Today": shows the current month (keeps the popover and the date).</summary>
    [RelayCommand]
    private void ShowTodayMonth() => Draft = Draft with { CalendarMonth = DateMath.FirstOfMonth(Today) };

    [RelayCommand]
    private void FewerDays() => Draft = TaskDialog.StepDays(Draft, -1);

    [RelayCommand]
    private void MoreDays() => Draft = TaskDialog.StepDays(Draft, 1);

    /// <summary>The Time popover's wheel settled on <paramref name="index"/> (switches on "Set time").</summary>
    public void CommitHour(int index) => Draft = TaskDialog.CommitWheel(Draft, hourColumn: true, index);

    /// <summary>The minute wheel settled on <paramref name="index"/> (0..11, 5-minute steps).</summary>
    public void CommitMinute(int index) => Draft = TaskDialog.CommitWheel(Draft, hourColumn: false, index);

    /// <summary>Enter in the title or the primary button. Without a title it only asks the view to focus the title box;
    /// otherwise the dialog closes and the draft as it is now is saved in <see cref="OnClosed"/>.</summary>
    [RelayCommand]
    public void Commit()
    {
        if (_pending != PendingAction.None)
        {
            return; // already closing
        }

        if (!Draft.CanSave)
        {
            FocusTitleRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        Close(PendingAction.Commit);
    }

    /// <summary>Delete (edit mode only, no confirmation): removes the task after the dialog has closed.</summary>
    [RelayCommand]
    private void Delete()
    {
        if (Draft.EditId is not null)
        {
            Close(PendingAction.Delete);
        }
    }

    /// <summary>Cancel and the close button: closes without saving.</summary>
    [RelayCommand]
    private void Cancel() => Close(PendingAction.Cancel);

    /// <summary>Remembers what to do once the dialog has closed and asks the host to close. The first decision wins: a
    /// Cancel or a second Enter during the exit animation does not undo a save, and edits made during it are not saved
    /// (the prototype builds the task when Create is pressed).</summary>
    private void Close(PendingAction action)
    {
        if (_pending is PendingAction.None)
        {
            if (action == PendingAction.Commit && Draft.Open == DraftPopover.Time)
            {
                // Closing the Time popover removes it from the tree, which commits a wheel that has not been still for
                // the settle time yet (a quick Save right after a scroll); only then is the draft taken.
                ClosePopover();
            }

            _pending = action;
            _closingDraft = Draft;
        }

        RequestClose();
    }

    // ---- Overlay -------------------------------------------------------------------------------------------------------

    /// <summary>Esc closes the open popover first; with none open the host closes the dialog.</summary>
    public override bool OnEscape()
    {
        if (Draft.Open == DraftPopover.None)
        {
            return false;
        }

        ClosePopover();
        return true;
    }

    /// <summary>After the exit animation: writes the store (create / update / delete) if Save or Delete closed the dialog.</summary>
    public override void OnClosed()
    {
        var pending = _pending;
        var draft = _closingDraft ?? Draft;
        _pending = PendingAction.Closed;
        var store = Services.App.Store;
        switch (pending)
        {
            case PendingAction.Commit when draft.CanSave:
                if (draft.EditId is { } id)
                {
                    if (store.Get(id) is { } existing)
                    {
                        store.Update(TaskDialog.ToTask(draft, existing));
                    }
                }
                else
                {
                    var created = store.Add(TaskDialog.ToTask(draft, null));
                    Services.Actions.MarkCreated(created.Id);
                }

                break;
            case PendingAction.Delete when draft.EditId is { } deleteId:
                store.Delete(deleteId);
                break;
        }
    }

    // ---- Projection of the draft ---------------------------------------------------------------------------------------

    partial void OnDraftChanged(TaskDraft oldValue, TaskDraft newValue)
    {
        if (oldValue is null || !string.Equals(oldValue.Title, newValue.Title, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(Title));
        }

        if (oldValue is null || !string.Equals(oldValue.Notes, newValue.Notes, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(Notes));
        }

        if (oldValue is null || oldValue.Timed != newValue.Timed)
        {
            OnPropertyChanged(nameof(TimeModeIndex));
        }

        if (oldValue is null || oldValue.DurationMinutes != newValue.DurationMinutes)
        {
            OnPropertyChanged(nameof(DurationIndex));
        }

        if (oldValue is null || oldValue.Time != newValue.Time)
        {
            OnPropertyChanged(nameof(WheelIndices));
        }

        Project(newValue);
    }

    private void Project(TaskDraft d)
    {
        var today = Today;
        var weekStart = WeekStart;

        Heading = TaskDialog.Heading(d);
        WhereLabel = TaskDialog.WhereLabel(d, today);
        IsEdit = d.EditId is not null;
        CommitLabel = TaskDialog.CommitLabel(d);
        KeyHint = TaskDialog.KeyHint(d);
        CanSave = d.CanSave;

        DateLabel = TaskDialog.DatePillLabel(d, today);
        TimeLabel = TaskDialog.TimePillLabel(d);
        PriorityLabel = TaskDialog.PriorityPillLabel(d);
        TagLabel = TaskDialog.TagPillLabel(d);
        HasDate = d.Date is not null;
        IsTimed = d.Timed;
        HasPriority = d.Priority != Priority.None;
        HasTag = !string.IsNullOrEmpty(d.Tag);
        Priority = d.Priority;
        ShowTime = d.ShowTime;

        // A popover whose pill is gone (Time after "Lasts 2 days" or "No date") cannot stay open.
        var open = d.Open == DraftPopover.Time && !d.ShowTime ? DraftPopover.None : d.Open;
        IsDateOpen = open == DraftPopover.Date;
        IsTimeOpen = open == DraftPopover.Time;
        IsPriorityOpen = open == DraftPopover.Priority;
        IsTagOpen = open == DraftPopover.Tag;
        if (!ReferenceEquals(d, Draft))
        {
            // Closing the Time popover above flushed a pending wheel commit, which replaced the draft and projected the
            // newer one completely; carrying on with this one would put stale labels back (the range, the calendar).
            return;
        }

        CalendarLabel = TaskDialog.CalendarLabel(d);
        UntilLabel = TaskDialog.UntilLabel(d);
        DaysLabel = TaskDialog.DaysLabel(d);
        RangeLabel = TaskDialog.RangeLabel(d);

        ProjectCalendar(d, today, weekStart);

        foreach (var option in PriorityOptions)
        {
            option.IsSelected = option.Priority == d.Priority;
        }

        foreach (var option in TagOptions)
        {
            option.IsSelected = string.Equals(option.Tag, string.IsNullOrEmpty(d.Tag) ? null : d.Tag, StringComparison.Ordinal);
        }
    }

    private void ProjectCalendar(TaskDraft d, DateOnly today, DayOfWeek weekStart)
    {
        if (WeekdayLetters.Count == 0 || _calendarKey?.WeekStart != weekStart)
        {
            var order = DateMath.WeekdayOrder(weekStart);
            var letters = TaskDialog.WeekdayLetters(weekStart);
            WeekdayLetters.Clear();
            for (var i = 0; i < letters.Count; i++)
            {
                WeekdayLetters.Add(new WeekdayLetter(letters[i], order[i] is DayOfWeek.Saturday or DayOfWeek.Sunday));
            }
        }

        var calendarKey = (d.CalendarMonth, d.Date, d.Days, today, weekStart);
        if (_calendarKey != calendarKey)
        {
            _calendarKey = calendarKey;
            var cells = TaskDialog.MiniCalendar(d, today, weekStart);
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                var label = DateMath.ShortWithWeekday(cell.Date) + (cell.IsToday ? ", today" : string.Empty);
                CalendarDays[i].Update(cell, label);
            }
        }

        var optionsKey = (d.Date, today, weekStart);
        if (_optionsKey != optionsKey)
        {
            _optionsKey = optionsKey;
            var options = TaskDialog.DateOptions(d, today, weekStart);
            for (var i = 0; i < options.Count; i++)
            {
                DateOptions[i].Update(options[i]);
            }
        }
    }

    /// <summary>"No tag" plus the store's tags, reconciled in place.</summary>
    private void RefreshTags()
    {
        var tags = new List<string?>(Services.App.Store.Tags.Count + 1) { null };
        tags.AddRange(Services.App.Store.Tags);
        KeyedSync.Apply(TagOptions, tags, t => t ?? string.Empty, o => o.Tag ?? string.Empty,
            t => new TagOptionItem(t, PickTag), static (_, _) => { });
        var current = string.IsNullOrEmpty(Draft.Tag) ? null : Draft.Tag;
        foreach (var option in TagOptions)
        {
            option.IsSelected = string.Equals(option.Tag, current, StringComparison.Ordinal);
        }
    }

    private void PickDay(CalendarDayItem day) => Draft = TaskDialog.PickDate(Draft, day.Date);

    private void PickQuick(DateChipItem option) => Draft = TaskDialog.PickDate(Draft, option.Date);

    private void PickPriority(PriorityOptionItem option) =>
        Draft = Draft with { Priority = option.Priority, Open = DraftPopover.None };

    private void PickTag(TagOptionItem option) => Draft = Draft with { Tag = option.Tag, Open = DraftPopover.None };

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return i;
            }
        }

        return -1;
    }
}
