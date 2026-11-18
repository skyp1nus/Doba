using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Services;
using Doba.App.ViewModels;
using Doba.Core.Navigation;
using Doba.Core.Projections;

namespace Doba.App.Screens.Month;

/// <summary>
/// The Month screen: a weekday header and five or six week rows ("W46", seven day cells with up to two lanes of
/// multi-day bars and the items that fit, "+N more"), projected by <see cref="Projector.Month"/> for
/// <see cref="ScreenViewModel.State"/>'s anchor.
/// <para>
/// Measurement: the view reports its height after layout (<see cref="ReportLayout"/>); the projection divides it into
/// week rows and fits items with <see cref="MonthLayout.Metrics"/>. Until the first report it uses the design height
/// (832). Re-projection reconciles weeks (by position) → cells (by column) → items and bars (by task id) through
/// <see cref="KeyedSync"/>, so completing a task only changes that item's properties, and moving to another month
/// updates the existing rows in place.
/// </para>
/// </summary>
public sealed partial class MonthViewModel : ScreenViewModel
{
    /// <summary>Height changes smaller than this (layout rounding) do not re-project.</summary>
    private const double Tolerance = 0.5;

    private readonly Func<TaskRow, TaskRowViewModel> _createRow;
    private readonly Func<MonthWeek, MonthWeekViewModel> _createWeek;
    private readonly Action<MonthWeekViewModel, MonthWeek> _updateWeek = static (vm, w) => vm.Update(w);
    private readonly Func<MonthWeek, int> _indexOf;

    private double _height = double.NaN;
    private DateOnly _firstWeekStart;
    private DateOnly _projectedToday;
    private IReadOnlyList<string>? _labelSource;

    public MonthViewModel(ScreenServices services)
        : base(services)
    {
        _createRow = r => new TaskRowViewModel(r, Services.Actions, Services.Commands);
        _indexOf = w => (w.WeekStart.DayNumber - _firstWeekStart.DayNumber) / 7;
        _createWeek = w => new MonthWeekViewModel(_indexOf(w), w, _createRow);
    }

    /// <summary>"MON" … "SUN" in the configured week order (the prototype upper-cases them with CSS).</summary>
    public ObservableCollection<string> WeekdayLabels { get; } = [];

    /// <summary>The five or six week rows, top to bottom.</summary>
    public ObservableCollection<MonthWeekViewModel> Weeks { get; } = [];

    /// <summary>The shown month (1..12) and its year.</summary>
    [ObservableProperty] public partial int Month { get; private set; }

    [ObservableProperty] public partial int Year { get; private set; }

    /// <summary>The last projection.</summary>
    public MonthModel? Model { get; private set; }

    /// <summary>True once the view has reported its height; before that the design height (832) is used.</summary>
    public bool IsMeasured => !double.IsNaN(_height);

    /// <summary>The metrics the last projection used.</summary>
    public MonthMetrics Metrics => IsMeasured ? MonthLayout.Metrics(_height) : new MonthMetrics();

    /// <summary>
    /// Called by the view after a layout pass with its own height (the weekday header and the week rows). Re-projects
    /// (while active) only when it changed by more than half a pixel; the week rows never change the view's height,
    /// so this cannot loop.
    /// </summary>
    public void ReportLayout(double height)
    {
        if (height <= 0 || Math.Abs(height - _height) < Tolerance)
        {
            return;
        }

        _height = height;
        RefreshIfActive();
    }

    /// <summary>Day cell clicked: zoom into the day from <paramref name="origin"/> (fractions of the view).</summary>
    public void OpenDay(DateOnly date, Point? origin) => Services.Commands.ZoomTo(AppView.Day, date, origin);

    /// <summary>Week label clicked: zoom into the week (today in the current week, else its first day).</summary>
    public void OpenWeek(MonthWeekViewModel week, Point? origin)
    {
        ArgumentNullException.ThrowIfNull(week);
        Services.Commands.ZoomTo(AppView.Week, week.ZoomTarget, origin);
    }

    protected override void Refresh()
    {
        if (State is null)
        {
            return;
        }

        var ctx = Context;
        _projectedToday = ctx.Today;
        var model = Projector.Month(Store, ctx, State.Anchor, Metrics);
        Model = model;
        Year = model.Year;
        Month = model.Month;
        SyncLabels(model.WeekdayLabels);
        _firstWeekStart = model.Weeks[0].WeekStart;
        KeyedSync.Apply(Weeks, model.Weeks, _indexOf, static vm => vm.Index, _createWeek, _updateWeek);
    }

    /// <summary>The month grid depends on the date only (today, current week), not the minute.</summary>
    protected override void OnMinute()
    {
        if (Services.App.DayClock.Today != _projectedToday)
        {
            RefreshIfActive();
        }
    }

    private void SyncLabels(IReadOnlyList<string> labels)
    {
        if (ReferenceEquals(_labelSource, labels) || (_labelSource is not null && _labelSource.SequenceEqual(labels, StringComparer.Ordinal)))
        {
            return;
        }

        _labelSource = labels;
        for (var i = 0; i < labels.Count; i++)
        {
            var upper = labels[i].ToUpperInvariant();
            if (i == WeekdayLabels.Count)
            {
                WeekdayLabels.Add(upper);
            }
            else if (!string.Equals(WeekdayLabels[i], upper, StringComparison.Ordinal))
            {
                WeekdayLabels[i] = upper;
            }
        }
    }
}
