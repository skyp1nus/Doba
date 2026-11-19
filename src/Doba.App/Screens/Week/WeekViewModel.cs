using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Services;
using Doba.App.ViewModels;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using Doba.Core.Time;

namespace Doba.App.Screens.Week;

/// <summary>
/// The Week screen (the app's main view): two rows of four day columns, multi-day bars in lanes, task cards and
/// "+N more", projected by <see cref="Projector.Week"/> for <see cref="ScreenViewModel.State"/>'s anchor.
/// <para>
/// Measurement: the view reports the rows' height and width when it measures them (<see cref="ReportLayout"/>); the
/// projection then fits cards with <see cref="WeekMetrics"/> built from those heights and a card-height function that
/// shapes each title with the real font at the real column width (<see cref="WeekCardMeasurer"/>). Until the first
/// report no cards are built (only the week start is known), so the first visit creates its cards once, at the real
/// size. Re-projection reconciles rows → cells (by column) → cards and bars (by task id) through <c>KeyedSync</c>, so
/// completing a task only changes that card's properties and moving to another week reuses the cells.
/// </para>
/// </summary>
public sealed partial class WeekViewModel : ScreenViewModel
{
    /// <summary>Row size changes smaller than this (layout rounding) do not re-project.</summary>
    private const double Tolerance = 0.5;

    private readonly WeekCardMeasurer _measurer = new();
    private readonly Func<string, double> _cardHeight;
    private readonly Func<Core.Model.TaskItem, double> _taskCardHeight;
    private readonly Func<TaskRow, TaskRowViewModel> _createRow;
    private readonly Action _dropStaleEntrance;

    private double _row1Height = double.NaN;
    private double _row2Height = double.NaN;
    private double _rowWidth = double.NaN;
    private DateOnly _projectedToday;

    public WeekViewModel(ScreenServices services)
        : base(services)
    {
        _createRow = r => new TaskRowViewModel(r, Services.Actions, Services.Commands);
        _cardHeight = CardHeight;
        _taskCardHeight = TaskCardHeight;
        _dropStaleEntrance = DropStaleEntrance;
        Row1 = new WeekRowViewModel(_createRow);
        Row2 = new WeekRowViewModel(_createRow);
    }

    /// <summary>Days 1–4 of the week.</summary>
    public WeekRowViewModel Row1 { get; }

    /// <summary>Days 5–7 and the next week's first day (preview).</summary>
    public WeekRowViewModel Row2 { get; }

    /// <summary>First day of the shown week.</summary>
    [ObservableProperty] public partial DateOnly WeekStart { get; private set; }

    /// <summary>The last projection (row 2 comes from the second row's metrics).</summary>
    public WeekModel? Model { get; private set; }

    /// <summary>True once the view has reported its size; before that the design metrics (403 px rows) are used.</summary>
    public bool IsMeasured => !double.IsNaN(_rowWidth);

    /// <summary>The metrics the last projection used for row <paramref name="row"/> (0 or 1).</summary>
    public WeekMetrics MetricsFor(int row)
    {
        if (!IsMeasured)
        {
            return WeekMetrics.Design();
        }

        return new WeekMetrics(row == 0 ? _row1Height : _row2Height, _cardHeight, WeekLayout.CardGap, WeekLayout.MoreReserve,
            WeekLayout.HeaderHeight, WeekLayout.LaneHeight, WeekLayout.ListPaddingTop)
        {
            TaskCardHeight = _taskCardHeight,
        };
    }

    /// <summary>
    /// Called by the view after a layout pass with the content height of each row grid and their width. Re-projects
    /// (while active) only when something changed by more than half a pixel, so the resulting card changes — which
    /// never change the row sizes — cannot loop.
    /// </summary>
    public void ReportLayout(double row1Height, double row2Height, double rowWidth)
    {
        if (row1Height <= 0 || row2Height <= 0 || rowWidth <= 0)
        {
            return;
        }

        if (Near(row1Height, _row1Height) && Near(row2Height, _row2Height) && Near(rowWidth, _rowWidth))
        {
            return;
        }

        _row1Height = row1Height;
        _row2Height = row2Height;
        _rowWidth = rowWidth;
        RefreshIfActive();
    }

    /// <summary>Day header or "+N more" clicked: zoom into the day from <paramref name="origin"/> (fractions of the view).</summary>
    public void OpenDay(DateOnly date, Point? origin) => Services.Commands.ZoomTo(AppView.Day, date, origin);

    /// <summary>The pending new-row entrance (cards and bars play it when they appear; see <c>TaskListRow.Entrances</c>).</summary>
    public TaskActions Entrances => Services.Actions;

    protected override void Refresh()
    {
        if (State is null)
        {
            return;
        }

        var ctx = Context;
        _projectedToday = ctx.Today;
        var anchor = State.Anchor;
        var weekStart = DateMath.WeekStart(anchor, ctx.WeekStart);
        WeekStart = weekStart;
        if (!IsMeasured)
        {
            return; // the view reports its size in its first measure pass, before anything is drawn
        }

        // Both rows get the same height from WeekRowsPanel, so one projection serves both.
        ConfigureMeasurer();
        var first = Projector.Week(Store, ctx, anchor, MetricsFor(0));
        var model = Near(_row1Height, _row2Height)
            ? first
            : first with { Rows = [first.Rows[0], Projector.Week(Store, ctx, anchor, MetricsFor(1)).Rows[1]] };

        Model = model;
        Row1.Update(model.Rows[0]);
        Row2.Update(model.Rows[1]);
        Dispatcher.UIThread.Post(_dropStaleEntrance, DispatcherPriority.Background);
    }

    /// <summary>
    /// A just-created task of the shown week whose card or bar did not appear (it landed behind "+N more") must not
    /// keep its entrance pending until it is realized on another screen later. Runs after the new visuals were loaded
    /// (and had their chance to consume it).
    /// </summary>
    private void DropStaleEntrance()
    {
        if (Services.Actions.PendingEntranceId is not { } id || Store.Get(id) is not { Date: { } date } task)
        {
            return;
        }

        var end = task.End ?? date;
        if (date <= WeekStart.AddDays(7) && end >= WeekStart)
        {
            Services.Actions.ConsumeEntrance(id);
        }
    }

    /// <summary>The week view only depends on the date, not the minute.</summary>
    protected override void OnMinute()
    {
        if (Services.App.DayClock.Today != _projectedToday)
        {
            RefreshIfActive();
        }
    }

    /// <summary>The card width and density the projection's card heights are measured at (the measurer caches per card).</summary>
    private void ConfigureMeasurer()
    {
        var cardWidth = Math.Floor(_rowWidth / 4) - (2 * WeekLayout.ListPaddingX);
        _measurer.Configure(cardWidth, Services.App.Settings.Current.Density);
    }

    /// <summary>A task's card: its title shaped at the width its tag, time and priority leave (WeekMetrics.TaskCardHeight).</summary>
    private double TaskCardHeight(Core.Model.TaskItem t) =>
        _measurer.Height(t.Title, t.Tag, t.Time, t.Priority != Core.Model.Priority.None);

    /// <summary>Title-only fallback (WeekMetrics.CardHeight): a card with no tag, time or priority.</summary>
    private double CardHeight(string title) => _measurer.Height(title, null, null, false);

    private static bool Near(double a, double b) => Math.Abs(a - b) < Tolerance;
}
