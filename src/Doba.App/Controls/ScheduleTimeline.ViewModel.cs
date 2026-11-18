using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Services;
using Doba.App.Shell;
using Doba.App.ViewModels;
using Doba.Core.Projections;
using Doba.Core.Time;

namespace Doba.App.Controls;

/// <summary>One hour line of the timeline: "07:00" at <see cref="Top"/> px.</summary>
public sealed record ScheduleHour(string Label, double Top, Thickness Margin);

/// <summary>
/// A timed task on the timeline. Geometry comes from <see cref="ScheduleLayout"/> at
/// <see cref="ScheduleViewModel.HourHeight"/>; <see cref="Row"/> is its own long-lived row view-model (the block's toggle,
/// done and dimmed state), separate from the list row of the same task.
/// </summary>
public sealed partial class ScheduleBlockViewModel : ObservableObject
{
    public ScheduleBlockViewModel(ScheduleBlock block, TaskActions actions, IAppCommands commands)
    {
        ArgumentNullException.ThrowIfNull(block);
        Id = block.Row.Id;
        Row = new TaskRowViewModel(block.Row, actions, commands);
        Update(block);
    }

    public long Id { get; }

    public TaskRowViewModel Row { get; }

    /// <summary>"14:00 – 15:30".</summary>
    [ObservableProperty] public partial string Range { get; private set; } = string.Empty;

    [ObservableProperty] public partial double Top { get; private set; }

    [ObservableProperty] public partial double Height { get; private set; }

    /// <summary>Shorter than 44 px: title and range on one line.</summary>
    [ObservableProperty] public partial bool IsCompact { get; private set; }

    /// <summary>Position inside the grid: left 54 (hour label 40 + gap 12 + 2), top.</summary>
    [ObservableProperty] public partial Thickness Margin { get; private set; }

    /// <summary>Content padding inside the block's 1 px ring (see <see cref="ContentPaddingFor"/>).</summary>
    [ObservableProperty] public partial Thickness ContentPadding { get; private set; }

    public void Update(ScheduleBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        Row.Update(block.Row);
        Range = block.Range;
        Top = ScheduleLayout.BlockTop(block.StartMinutes, ScheduleViewModel.HourHeight);
        Height = ScheduleLayout.BlockHeight(block.DurationMinutes, ScheduleViewModel.HourHeight);
        IsCompact = ScheduleLayout.IsCompact(Height);
        Margin = new Thickness(ScheduleViewModel.BlockLeft, Top, 0, 0);
        ContentPadding = ContentPaddingFor(Height, IsCompact);
    }

    /// <summary>
    /// The prototype pads a block 7 10 0 (compact: 0 10, the line centred) inside its edge, and draws the ring as an
    /// inset box-shadow; Button.Bar's ring is a 1 px border, so 1 px comes off every side. A compact block centres its
    /// 17.5 px title line (12.5 × 1.4) in the full block height, rounded to a whole pixel as Chromium paints it —
    /// centring the 18 px line our layout measures inside the border lands up to 1 px higher.
    /// </summary>
    public static Thickness ContentPaddingFor(double height, bool compact) =>
        compact
            ? new Thickness(9, Math.Round((height - CompactTitleLine) / 2) - 1, 9, 0)
            : new Thickness(9, 6, 9, 0);

    /// <summary>CSS line box of the block title: 12.5 px × line-height 1.4.</summary>
    private const double CompactTitleLine = 17.5;
}

/// <summary>
/// The schedule column of Today and Day: hour grid 07–23 at 48 px/hour, the day's timed tasks as blocks and the
/// now-line (today only). <see cref="Update"/> reconciles blocks by task id (they update in place);
/// <see cref="SetNow"/> moves only the now-line (minute tick).
/// </summary>
public sealed partial class ScheduleViewModel : ObservableObject
{
    /// <summary>Design: 48 px per hour.</summary>
    public const double HourHeight = 48;

    /// <summary>Blocks start right of the hour labels (prototype <c>left: 54px</c>).</summary>
    public const double BlockLeft = 54;

    /// <summary>Half the height of an hour row / the now label (16 px line box), to centre them on their line.</summary>
    private const double LineBoxHalf = 8;

    private readonly TaskActions _actions;
    private readonly IAppCommands _commands;

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

    public ObservableCollection<ScheduleBlockViewModel> Blocks { get; } = [];

    [ObservableProperty] public partial bool HasNow { get; private set; }

    /// <summary>"22:26".</summary>
    [ObservableProperty] public partial string NowLabel { get; private set; } = string.Empty;

    [ObservableProperty] public partial double NowTop { get; private set; }

    /// <summary>Margin of the now-line row (16 px tall, centred on <see cref="NowTop"/>).</summary>
    [ObservableProperty] public partial Thickness NowMargin { get; private set; }

    public void Update(ScheduleModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        KeyedSync.Apply(Blocks, model.Blocks, b => b.Row.Id, vm => vm.Id,
            b => new ScheduleBlockViewModel(b, _actions, _commands), (vm, b) => vm.Update(b));
        SetNow(model.NowMinutes);
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
