using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.App.Services;
using Doba.App.Shell;
using Doba.App.ViewModels;
using Doba.Core.Projections;
using Doba.Core.Time;

namespace Doba.App.Controls;

/// <summary>One hour line of the timeline: "07:00" at <see cref="Top"/> px.</summary>
public sealed record ScheduleHour(string Label, double Top, Thickness Margin);

/// <summary>
/// A drawn (not folded) timed task on the timeline (the prototype's <c>sc.blocks</c> item). Geometry, the content tier,
/// the checkbox and grip positions and the labels come from <see cref="ScheduleLayout"/> through the
/// <see cref="ScheduleBlock"/> record; <see cref="Row"/> is the block's own long-lived row view-model (open, toggle,
/// done and dimmed state), separate from the list row of the same task.
/// </summary>
public sealed partial class ScheduleBlockViewModel : ObservableObject
{
    /// <summary>CSS line box of the block title: 12.5 px × line-height 1.4.</summary>
    public const double TitleLine = 17.5;

    /// <summary>CSS line box of the time line: 10.5 px × line-height 1.4.</summary>
    public const double MetaLine = 14.7;

    /// <summary>The block's border (1 px): every CSS position inside the block is relative to the box inside it.</summary>
    public const double Border = 1;

    /// <summary>Prototype content padding: 7 px top (tall block), 10 px left and right.</summary>
    public const double PaddingTop = 7;

    /// <inheritdoc cref="PaddingTop"/>
    public const double PaddingX = 10;

    /// <summary>Tall block: the time line 2 px under the title line.</summary>
    public const double MetaGap = 2;

    /// <summary>Gap between the checkbox and the title, and between the title and the time on a short block.</summary>
    public const double Gap = 8;

    /// <summary>Tall block: the time line is indented under the title (checkbox 12 + gap 8).</summary>
    public const double MetaIndent = 20;

    /// <summary>The checkbox hit area sits 3 px right of the block's inner edge.</summary>
    public const double CheckLeft = 3;

    /// <summary>Our time glyphs sit about 0.1 px lower in their line than Chromium's (see <see cref="MetaOffsetFor"/>).</summary>
    private const double MetaNudge = 0.1;

    public ScheduleBlockViewModel(ScheduleBlock block, TaskActions actions, IAppCommands commands)
    {
        ArgumentNullException.ThrowIfNull(block);
        Id = block.Row.Id;
        Row = new TaskRowViewModel(block.Row, actions, commands);
        Block = block;
        Update(block);
    }

    public long Id { get; }

    public TaskRowViewModel Row { get; }

    /// <summary>The record last pushed in.</summary>
    public ScheduleBlock Block { get; private set; }

    /// <summary>"14:00 – 15:30".</summary>
    [ObservableProperty] public partial string Range { get; private set; } = string.Empty;

    [ObservableProperty] public partial int StartMinutes { get; private set; }

    /// <summary>Effective duration (an unspecified one counts as 30 min).</summary>
    [ObservableProperty] public partial int DurationMinutes { get; private set; }

    [ObservableProperty] public partial double X { get; private set; }

    [ObservableProperty] public partial double Top { get; private set; }

    [ObservableProperty] public partial double Width { get; private set; }

    [ObservableProperty] public partial double Height { get; private set; }

    /// <summary>Position inside the grid: (x, top).</summary>
    [ObservableProperty] public partial Thickness Margin { get; private set; }

    /// <summary>At least 44 px: title and time stacked; otherwise one row.</summary>
    [ObservableProperty] public partial bool IsTall { get; private set; }

    /// <summary>Shorter than 44 px (the opposite of <see cref="IsTall"/>).</summary>
    public bool IsCompact => !IsTall;

    /// <summary>The time the block's size leaves room for (range, start or nothing; see <see cref="ScheduleLayout.Meta"/>).</summary>
    [ObservableProperty] public partial string Meta { get; private set; } = string.Empty;

    [ObservableProperty] public partial bool HasMeta { get; private set; }

    /// <summary>Content padding inside the 1 px border (see <see cref="ContentPaddingFor"/>).</summary>
    [ObservableProperty] public partial Thickness ContentPadding { get; private set; }

    /// <summary>Top of the time line below the top of the title (see <see cref="MetaOffsetFor"/>).</summary>
    [ObservableProperty] public partial double MetaOffset { get; private set; }

    /// <summary>The 24 px checkbox hit area: left 3, top <c>chkTop</c>, inside the border.</summary>
    [ObservableProperty] public partial Thickness CheckMargin { get; private set; }

    /// <summary>Height of the bottom resize zone (11 px, 8 below 32 px), 3 px of it under the edge.</summary>
    [ObservableProperty] public partial double GripHeight { get; private set; }

    /// <summary>"Title, 09:30–10:30[, done]. Enter to edit, arrows to move, …".</summary>
    [ObservableProperty] public partial string AccessibleLabel { get; private set; } = string.Empty;

    /// <summary>"Title · 09:30–10:30".</summary>
    [ObservableProperty] public partial string ToolTip { get; private set; } = string.Empty;

    /// <summary>"Mark done: Title" / "Mark not done: Title".</summary>
    [ObservableProperty] public partial string CheckLabel { get; private set; } = string.Empty;

    /// <summary>Position in the Tab order of the timeline (by time; see <see cref="ScheduleViewModel"/>).</summary>
    [ObservableProperty] public partial int TabOrder { get; set; }

    public void Update(ScheduleBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        Block = block;
        Row.Update(block.Row);
        Range = block.Range;
        StartMinutes = block.StartMinutes;
        DurationMinutes = block.DurationMinutes;
        X = block.X;
        Top = block.Top;
        Width = block.Width;
        Height = block.Height;
        Margin = new Thickness(block.X, block.Top, 0, 0);
        var wasTall = IsTall;
        IsTall = block.IsTall;
        if (wasTall != IsTall)
        {
            OnPropertyChanged(nameof(IsCompact));
        }

        Meta = block.Meta;
        HasMeta = block.Meta.Length > 0;
        ContentPadding = ContentPaddingFor(block.Height, block.IsTall);
        MetaOffset = MetaOffsetFor(block.Height, block.IsTall);
        CheckMargin = new Thickness(Border + CheckLeft, Border + block.CheckTop, 0, 0);
        GripHeight = block.GripHeight;
        AccessibleLabel = block.AccessibleLabel;
        ToolTip = block.ToolTip;
        CheckLabel = block.CheckLabel;
    }

    /// <summary>
    /// The prototype pads a tall block 7 10 0 inside its 1 px border; a short one 0 10 with its line centred in the box
    /// inside the border — the 17.5 px title line (12.5 × 1.4) at <c>1 + (h − 2 − 17.5) / 2</c>, which Chromium paints
    /// on a whole pixel (rounded); centring the 18 px line our layout measures would land up to 1 px higher.
    /// </summary>
    public static Thickness ContentPaddingFor(double height, bool tall) =>
        tall
            ? new Thickness(PaddingX, PaddingTop, PaddingX, 0)
            : new Thickness(PaddingX, DateMath.RoundHalfUp(Border + ((height - (2 * Border) - TitleLine) / 2)) - Border, PaddingX, 0);

    /// <summary>
    /// Where the mono 10.5 time line goes, relative to the title's top. In the prototype its line box (14.7 px) sits
    /// 2 px under the 17.5 px title line (tall block: block top + 1 + 7 + 17.5 + 2 = 27.5) or is centred in the box
    /// inside the border (short: 1 + (h − 2 − 14.7) / 2), and Chromium paints it at that fractional position. Our text
    /// lands on whole pixels, so the line goes to the row nearest Chromium's glyphs: <c>round(top − 0.1)</c> (27.5 → 27,
    /// 9.15 → 9 for a 33 px block, 4.65 → 5 for a 24 px one).
    /// </summary>
    public static double MetaOffsetFor(double height, bool tall)
    {
        var cssTop = tall
            ? Border + PaddingTop + TitleLine + MetaGap
            : Border + ((height - (2 * Border) - MetaLine) / 2);
        var titleTop = ContentPaddingFor(height, tall).Top + Border;
        return DateMath.RoundHalfUp(cssTop - MetaNudge) - titleTop;
    }
}

/// <summary>One task in an open "+N" list: checkbox and an open button with the title and "10:15–10:45".</summary>
public sealed partial class ScheduleListRowViewModel : ObservableObject
{
    private readonly ScheduleViewModel _owner;

    public ScheduleListRowViewModel(ScheduleOverflowRow row, ScheduleViewModel owner, TaskActions actions, IAppCommands commands)
    {
        ArgumentNullException.ThrowIfNull(row);
        _owner = owner;
        Id = row.Row.Id;
        Row = new TaskRowViewModel(row.Row, actions, commands);
        Update(row);
    }

    public long Id { get; }

    public TaskRowViewModel Row { get; }

    /// <summary>"10:15–10:45".</summary>
    [ObservableProperty] public partial string Range { get; private set; } = string.Empty;

    /// <summary>"Mark done: Title" / "Mark not done: Title".</summary>
    [ObservableProperty] public partial string CheckLabel { get; private set; } = string.Empty;

    public void Update(ScheduleOverflowRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Row.Update(row.Row);
        Range = row.Range;
        CheckLabel = row.CheckLabel;
    }

    /// <summary>Closes the list, then opens the task (prototype: <c>setState({ ovf: null }); openEdit(id)</c>).</summary>
    [RelayCommand]
    private void Open()
    {
        _owner.CloseOverflow();
        Row.OpenCommand.Execute(null);
    }
}

/// <summary>
/// A cluster's "+N" stand-in for the blocks it folds (the prototype's <c>sc.ovfs</c> item) and, while it is open, the
/// list of those tasks (<c>sc.pop</c>). Keyed by <see cref="Start"/>, unique per day.
/// </summary>
public sealed partial class ScheduleOverflowViewModel : ObservableObject
{
    private readonly ScheduleViewModel _owner;
    private readonly TaskActions _actions;
    private readonly IAppCommands _commands;

    public ScheduleOverflowViewModel(ScheduleOverflow overflow, ScheduleViewModel owner, TaskActions actions, IAppCommands commands)
    {
        ArgumentNullException.ThrowIfNull(overflow);
        _owner = owner;
        _actions = actions;
        _commands = commands;
        Start = overflow.Start;
        Overflow = overflow;
        Update(overflow);
    }

    /// <summary>Earliest start of the folded blocks: the overflow's key.</summary>
    public int Start { get; }

    /// <summary>The record last pushed in.</summary>
    public ScheduleOverflow Overflow { get; private set; }

    [ObservableProperty] public partial Thickness Margin { get; private set; }

    [ObservableProperty] public partial double Width { get; private set; }

    [ObservableProperty] public partial double Height { get; private set; }

    /// <summary>At least 44 px: "+N" at the top (6 px down) instead of centred.</summary>
    [ObservableProperty] public partial bool IsTall { get; private set; }

    /// <summary>"+2".</summary>
    [ObservableProperty] public partial string Text { get; private set; } = string.Empty;

    /// <summary>"2 more tasks, 10:15 to 11:00".</summary>
    [ObservableProperty] public partial string AccessibleLabel { get; private set; } = string.Empty;

    /// <summary>The list is open (<c>aria-expanded</c>).</summary>
    [ObservableProperty] public partial bool IsOpen { get; internal set; }

    /// <summary>Position in the Tab order of the timeline (after the blocks that start with it or earlier).</summary>
    [ObservableProperty] public partial int TabOrder { get; set; }

    /// <summary>List head: "2 more · 10:15–11:00".</summary>
    [ObservableProperty] public partial string PopoverHead { get; private set; } = string.Empty;

    /// <summary>The list's accessible name: "2 more tasks".</summary>
    [ObservableProperty] public partial string PopoverLabel { get; private set; } = string.Empty;

    /// <summary>The list opens under the "+N" (scale origin top right), else above it (bottom right).</summary>
    [ObservableProperty] public partial bool PopoverBelow { get; private set; }

    /// <summary>The list's position in the grid: (124, below ? top + h + 6 : top − listHeight − 6).</summary>
    [ObservableProperty] public partial Thickness PopoverMargin { get; private set; }

    /// <summary>The folded tasks by time (the list's rows).</summary>
    public ObservableCollection<ScheduleListRowViewModel> Rows { get; } = [];

    public void Update(ScheduleOverflow overflow)
    {
        ArgumentNullException.ThrowIfNull(overflow);
        Overflow = overflow;
        Margin = new Thickness(overflow.X, overflow.Top, 0, 0);
        Width = overflow.Width;
        Height = overflow.Height;
        IsTall = overflow.IsTall;
        Text = overflow.Text;
        AccessibleLabel = overflow.AccessibleLabel;
        PopoverHead = overflow.PopoverHead;
        PopoverLabel = overflow.PopoverLabel;
        PopoverBelow = overflow.PopoverBelow;
        PopoverMargin = new Thickness(ScheduleLayout.PopoverLeft, overflow.PopoverTop, 0, 0);
        KeyedSync.Apply(Rows, overflow.Rows, r => r.Row.Id, vm => vm.Id,
            r => new ScheduleListRowViewModel(r, _owner, _actions, _commands), static (vm, r) => vm.Update(r));
    }

    /// <summary>Opens the list, or closes it when it is open (a second click).</summary>
    [RelayCommand]
    private void Toggle() => _owner.ToggleOverflow(this);
}

/// <summary>The dragged copy of a block (layer 3): hidden at rest; the drag stage drives it.</summary>
public sealed partial class ScheduleCopyViewModel : ObservableObject
{
    [ObservableProperty] public partial bool IsVisible { get; private set; }

    [ObservableProperty] public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty] public partial bool Done { get; private set; }

    /// <summary>The range with its length ("11:15 – 12:15 · 1h") the copy's size leaves room for.</summary>
    [ObservableProperty] public partial string Meta { get; private set; } = string.Empty;

    [ObservableProperty] public partial bool HasMeta { get; private set; }

    [ObservableProperty] public partial bool IsTall { get; private set; }

    [ObservableProperty] public partial double Width { get; private set; }

    [ObservableProperty] public partial double Height { get; private set; }

    /// <summary>Position in the grid: (the block's x, the slot's top).</summary>
    [ObservableProperty] public partial Thickness Margin { get; private set; }

    [ObservableProperty] public partial Thickness ContentPadding { get; private set; }

    [ObservableProperty] public partial double MetaOffset { get; private set; }

    /// <summary>Shows the copy of <paramref name="block"/> at the slot <paramref name="start"/> + <paramref name="duration"/>.</summary>
    public void Show(ScheduleBlockViewModel block, int start, int duration)
    {
        ArgumentNullException.ThrowIfNull(block);
        var h = ScheduleLayout.BlockHeight(duration, ScheduleLayout.DesignHourHeight);
        Title = block.Row.Title;
        Done = block.Row.Done;
        Width = block.Width;
        Height = h;
        IsTall = !ScheduleLayout.IsCompact(h);
        Meta = ScheduleLayout.Meta(block.Width, h, start, duration, active: true);
        HasMeta = Meta.Length > 0;
        Margin = new Thickness(block.X, ScheduleLayout.BlockTop(start, ScheduleLayout.DesignHourHeight), 0, 0);
        ContentPadding = ScheduleBlockViewModel.ContentPaddingFor(h, IsTall);
        MetaOffset = ScheduleBlockViewModel.MetaOffsetFor(h, IsTall);
        IsVisible = true;
    }

    public void Hide() => IsVisible = false;
}

/// <summary>The time chip in the hour column (layer 4): hidden at rest; dragging and arrow keys drive it.</summary>
public sealed partial class ScheduleChipViewModel : ObservableObject
{
    /// <summary>The chip's 16 px line is centred on the time's line.</summary>
    private const double LineBoxHalf = 8;

    [ObservableProperty] public partial bool IsVisible { get; private set; }

    /// <summary>"11:15".</summary>
    [ObservableProperty] public partial string Label { get; private set; } = string.Empty;

    /// <summary>Top of the chip: round((m − 7h) / 60 · 48) − 8.</summary>
    [ObservableProperty] public partial double Top { get; private set; }

    [ObservableProperty] public partial Thickness Margin { get; private set; }

    public void Show(int minutes)
    {
        Label = DateMath.Hhmm(minutes);
        Top = ScheduleLayout.NowTop(minutes, ScheduleLayout.DesignHourHeight) - LineBoxHalf;
        Margin = new Thickness(0, Top, 0, 0);
        IsVisible = true;
    }

    public void Hide() => IsVisible = false;
}

/// <summary>
/// The schedule column of Today and Day: hour grid 07–23 at 48 px/hour, the day's timed tasks as blocks placed in
/// overlap columns, a "+N" per cluster wider than three columns with its list, and the now-line (today only).
/// <see cref="Update"/> reconciles blocks and "+N"s by key in a stable order (a moved block keeps its container);
/// <see cref="SetNow"/> moves only the now-line (minute tick).
/// </summary>
public sealed partial class ScheduleViewModel : ObservableObject
{
    /// <summary>Design: 48 px per hour.</summary>
    public const double HourHeight = ScheduleLayout.DesignHourHeight;

    /// <summary>Half the height of an hour row / the now label (16 px line box), to centre them on their line.</summary>
    private const double LineBoxHalf = 8;

    private readonly TaskActions _actions;
    private readonly IAppCommands _commands;
    private DateOnly? _date;

    public ScheduleViewModel(TaskActions actions, IAppCommands commands)
    {
        _actions = actions;
        _commands = commands;
    }

    /// <summary>"07–23".</summary>
    public static string RangeLabel { get; } = string.Create(CultureInfo.InvariantCulture,
        $"{ScheduleModel.FirstHour:00}–{ScheduleModel.LastHour:00}");

    /// <summary>16 h · 48 + 2 = 770 px.</summary>
    public static double GridHeight { get; } = ScheduleLayout.GridHeight(HourHeight);

    /// <summary>07:00 … 23:00.</summary>
    public static IReadOnlyList<ScheduleHour> Hours { get; } = CreateHours();

    /// <summary>The drawn blocks, in a stable order (new ones appended): position comes from each block's geometry and the
    /// Tab order from <see cref="ScheduleBlockViewModel.TabOrder"/>.</summary>
    public ObservableCollection<ScheduleBlockViewModel> Blocks { get; } = [];

    /// <summary>The "+N" stand-ins, in a stable order.</summary>
    public ObservableCollection<ScheduleOverflowViewModel> Overflows { get; } = [];

    /// <summary>The "+N" whose list is open, if any.</summary>
    [ObservableProperty] public partial ScheduleOverflowViewModel? OpenOverflow { get; private set; }

    public ScheduleCopyViewModel Copy { get; } = new();

    public ScheduleChipViewModel Chip { get; } = new();

    /// <summary>The polite live region's text (screen readers announce each change).</summary>
    [ObservableProperty] public partial string Message { get; private set; } = string.Empty;

    [ObservableProperty] public partial bool HasNow { get; private set; }

    /// <summary>"22:26".</summary>
    [ObservableProperty] public partial string NowLabel { get; private set; } = string.Empty;

    [ObservableProperty] public partial double NowTop { get; private set; }

    /// <summary>Margin of the now-line row (16 px tall, centred on <see cref="NowTop"/>).</summary>
    [ObservableProperty] public partial Thickness NowMargin { get; private set; }

    /// <summary>Shows <paramref name="model"/>, the schedule of <paramref name="date"/>. Another day closes the "+N" list;
    /// so does a change of the open list's tasks (one added or removed, or its "+N" gone).</summary>
    public void Update(ScheduleModel model, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(model);
        var sameDay = _date == date;
        _date = date;

        // Blocks folded into a "+N" are not drawn.
        KeyedSync.ApplyStable(Blocks, model.Blocks.Where(b => !b.IsHidden).ToList(), b => b.Row.Id, vm => vm.Id,
            b => new ScheduleBlockViewModel(b, _actions, _commands), static (vm, b) => vm.Update(b));

        var open = OpenOverflow;
        var openIds = open?.Rows.Select(r => r.Id).ToHashSet();
        KeyedSync.ApplyStable(Overflows, model.Overflows, o => o.Start, vm => vm.Start,
            o => new ScheduleOverflowViewModel(o, this, _actions, _commands), static (vm, o) => vm.Update(o));
        if (open is not null
            && (!sameDay || !Overflows.Contains(open) || !openIds!.SetEquals(open.Rows.Select(r => r.Id))))
        {
            CloseOverflow();
        }

        AssignTabOrder();
        SetNow(model.NowMinutes);
    }

    /// <summary>Opens the list of <paramref name="overflow"/> (closing another one), or closes it when it is open.</summary>
    public void ToggleOverflow(ScheduleOverflowViewModel overflow)
    {
        ArgumentNullException.ThrowIfNull(overflow);
        if (ReferenceEquals(OpenOverflow, overflow))
        {
            CloseOverflow();
            return;
        }

        if (OpenOverflow is { } other)
        {
            other.IsOpen = false;
        }

        overflow.IsOpen = true;
        OpenOverflow = overflow;
    }

    /// <summary>Closes the open "+N" list, if any.</summary>
    public void CloseOverflow()
    {
        if (OpenOverflow is not { } open)
        {
            return;
        }

        open.IsOpen = false;
        OpenOverflow = null;
    }

    /// <summary>
    /// Says <paramref name="text"/> in the live region. The text is cleared first and set on a posted job, so the same
    /// message twice in a row is announced again.
    /// </summary>
    public void Announce(string text)
    {
        Message = string.Empty;
        Dispatcher.UIThread.Post(() => Message = text);
    }

    /// <summary>True when <paramref name="minutes"/> (since midnight) falls on the 07:00–23:00 grid, ends included.</summary>
    public static bool IsOnGrid(int minutes) =>
        minutes >= ScheduleModel.FirstHour * 60 && minutes <= ScheduleModel.LastHour * 60;

    /// <summary>Moves the now-line. Null, or a time off the 07–23 grid (overnight), hides it: the prototype only ever
    /// showed 22:26 and would draw the line over the header before 07:00 and under the grid after 23:00. Blocks are
    /// untouched.</summary>
    public void SetNow(int? nowMinutes)
    {
        HasNow = nowMinutes is { } m && IsOnGrid(m);
        if (!HasNow || nowMinutes is not { } minutes)
        {
            return;
        }

        NowLabel = DateMath.Hhmm(minutes);
        NowTop = ScheduleLayout.NowTop(minutes, HourHeight);
        NowMargin = new Thickness(0, NowTop - LineBoxHalf, 0, 0);
    }

    /// <summary>
    /// Tab goes through the blocks by time — start, then longer first, then id (the overlap layout's order) — and
    /// reaches a "+N" after the blocks that start with it or earlier (it sits in the cluster's last column).
    /// </summary>
    private void AssignTabOrder()
    {
        var stops = new List<(int Start, int Kind, ScheduleSpan Span, ObservableObject Vm)>(Blocks.Count + Overflows.Count);
        foreach (var b in Blocks)
        {
            stops.Add((b.StartMinutes, 0, new ScheduleSpan(b.Id, b.StartMinutes, b.DurationMinutes), b));
        }

        foreach (var o in Overflows)
        {
            stops.Add((o.Start, 1, default, o));
        }

        stops.Sort((a, b) =>
        {
            var c = a.Start.CompareTo(b.Start);
            if (c != 0)
            {
                return c;
            }

            c = a.Kind.CompareTo(b.Kind);
            return c != 0 || a.Kind == 1 ? c : ScheduleColumns.Compare(a.Span, b.Span);
        });

        for (var i = 0; i < stops.Count; i++)
        {
            switch (stops[i].Vm)
            {
                case ScheduleBlockViewModel block:
                    block.TabOrder = i;
                    break;
                case ScheduleOverflowViewModel overflow:
                    overflow.TabOrder = i;
                    break;
            }
        }
    }

    private static ScheduleHour[] CreateHours()
    {
        var hours = new ScheduleHour[ScheduleModel.LastHour - ScheduleModel.FirstHour + 1];
        for (var i = 0; i < hours.Length; i++)
        {
            var top = i * HourHeight;
            // The hour layer starts LineBoxHalf above the grid, so a row's margin equals its line's top.
            hours[i] = new ScheduleHour(DateMath.Hhmm((ScheduleModel.FirstHour + i) * 60), top, new Thickness(0, top, 0, 0));
        }

        return hours;
    }
}
