using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.Core.Editing;
using Doba.Core.Model;

namespace Doba.App.Dialogs;

/// <summary>A weekday initial over the mini calendar; weekend letters are dimmer (Text.Faded).</summary>
public sealed record WeekdayLetter(string Letter, bool IsWeekend);

/// <summary>
/// One of the 42 mini-calendar cells (a <see cref="MiniCalendarDay"/> from Core). The cells are created once and updated
/// in place when the month, the selection or today changes, so the calendar never rebuilds its visual tree.
/// </summary>
public sealed partial class CalendarDayItem : ObservableObject
{
    public CalendarDayItem(Action<CalendarDayItem> pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        PickCommand = new RelayCommand(() => pick(this));
    }

    /// <summary>Picks this day (closes the popover).</summary>
    public ICommand PickCommand { get; }

    [ObservableProperty] public partial DateOnly Date { get; private set; }

    [ObservableProperty] public partial int DayNumber { get; private set; }

    /// <summary>Outside the shown month (Text.Dim).</summary>
    [ObservableProperty] public partial bool IsOutside { get; private set; }

    /// <summary>Before today, in the shown month (Text.Muted).</summary>
    [ObservableProperty] public partial bool IsPast { get; private set; }

    [ObservableProperty] public partial bool IsToday { get; private set; }

    /// <summary>The draft's date (pressed state for assistive tech).</summary>
    [ObservableProperty] public partial bool IsSelected { get; private set; }

    /// <summary>Start or end of the selection: the filled accent square.</summary>
    [ObservableProperty] public partial bool IsMarked { get; private set; }

    /// <summary>Inside a multi-day selection: the 15 % accent band.</summary>
    [ObservableProperty] public partial bool InRange { get; private set; }

    [ObservableProperty] public partial bool BandStart { get; private set; }

    [ObservableProperty] public partial bool BandEnd { get; private set; }

    /// <summary>"Mon 16 Nov" / "Sun 15 Nov, today" (the prototype's aria-label).</summary>
    [ObservableProperty] public partial string Label { get; private set; } = string.Empty;

    internal void Update(MiniCalendarDay day, string label)
    {
        Date = day.Date;
        DayNumber = day.DayNumber;
        IsOutside = !day.InMonth;
        IsPast = day.InMonth && day.IsPast;
        IsToday = day.IsToday;
        IsSelected = day.Selected;
        IsMarked = day.Marked;
        InRange = day.InRange;
        BandStart = day.BandStart;
        BandEnd = day.BandEnd;
        Label = label;
    }
}

/// <summary>A quick option of the Date popover ("Today", "Tomorrow", "Next week 23 Nov", "No date").</summary>
public sealed partial class DateChipItem : ObservableObject
{
    public DateChipItem(Action<DateChipItem> pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        PickCommand = new RelayCommand(() => pick(this));
    }

    public ICommand PickCommand { get; }

    [ObservableProperty] public partial string Label { get; private set; } = string.Empty;

    /// <summary>"23 Nov" for "Next week", else empty (the chip keeps the empty hint, as the prototype does).</summary>
    [ObservableProperty] public partial string Hint { get; private set; } = string.Empty;

    /// <summary>The date it picks; null for "No date".</summary>
    [ObservableProperty] public partial DateOnly? Date { get; private set; }

    [ObservableProperty] public partial bool IsSelected { get; private set; }

    internal void Update(DateQuickOption option)
    {
        Label = option.Label;
        Hint = option.Hint;
        Date = option.Date;
        IsSelected = option.Selected;
    }
}

/// <summary>A row of the Priority popover: the shared priority glyph, the name and the accent tick when chosen.</summary>
public sealed partial class PriorityOptionItem : ObservableObject
{
    public PriorityOptionItem(Priority priority, string label, Action<PriorityOptionItem> pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        Priority = priority;
        Label = label;
        PickCommand = new RelayCommand(() => pick(this));
    }

    public Priority Priority { get; }

    public string Label { get; }

    public ICommand PickCommand { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>A row of the Tag popover: "–" No tag, or "#" and the tag name.</summary>
public sealed partial class TagOptionItem : ObservableObject
{
    public TagOptionItem(string? tag, Action<TagOptionItem> pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        Tag = tag;
        PickCommand = new RelayCommand(() => pick(this));
    }

    /// <summary>The tag it picks; null for "No tag".</summary>
    public string? Tag { get; }

    /// <summary>"No tag" or the tag name.</summary>
    public string Label => Tag ?? "No tag";

    /// <summary>"–" for No tag, "#" for a tag.</summary>
    public string Symbol => Tag is null ? "–" : "#";

    public ICommand PickCommand { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }
}
