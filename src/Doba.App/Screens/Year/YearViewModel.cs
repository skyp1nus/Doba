using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Services;
using Doba.Core.Projections;

namespace Doba.App.Screens.Year;

/// <summary>
/// Year view: twelve months (4 × 3) with stats, progress and key items, rendering <see cref="Projector.Year"/> for the
/// anchor's year. Months are reconciled by month number and their rows by task id (<see cref="KeyedSync"/>), so
/// completing an item updates that row, the month's stat and progress in place.
/// <para>
/// Measurement: the view reports each month cell's room under its header (<see cref="SetRooms"/>); the projection
/// lists the key items that fit and counts the rest in "+N more" (<see cref="YearMetrics"/>). Until the first report
/// every key item is listed, as in the prototype.
/// </para>
/// </summary>
public sealed partial class YearViewModel : ScreenViewModel
{
    private readonly YearMetrics?[] _rooms = new YearMetrics?[12];
    private readonly Func<int, YearMetrics?> _roomOf;
    private DateOnly _projectedToday;

    public YearViewModel(ScreenServices services)
        : base(services)
    {
        _roomOf = m => _rooms[m - 1];
    }

    /// <summary>The displayed year.</summary>
    [ObservableProperty] public partial int Year { get; private set; }

    /// <summary>January..December in grid order (row-major, 4 per row).</summary>
    public ObservableCollection<YearMonthViewModel> Months { get; } = [];

    /// <summary>The measured room of month <paramref name="month"/>'s cell (1..12), or null before the first report.</summary>
    public YearMetrics? RoomOf(int month) => _rooms[month - 1];

    /// <summary>
    /// Called by the view after arranging the grid with the room each month cell has below its header. Re-projects
    /// (while active) only when a room changed; the header does not depend on the items, so this settles.
    /// Returns true when it re-projected.
    /// </summary>
    public bool SetRooms(IReadOnlyList<(int Month, YearMetrics Room)> rooms)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        var changed = false;
        foreach (var (month, room) in rooms)
        {
            if (month is >= 1 and <= 12 && _rooms[month - 1] != room)
            {
                _rooms[month - 1] = room;
                changed = true;
            }
        }

        if (!changed || !IsActive)
        {
            return false;
        }

        Refresh();
        return true;
    }

    protected override void Refresh()
    {
        var ctx = Context;
        var year = State?.Anchor.Year ?? ctx.Today.Year;
        var model = Projector.Year(Store, ctx, year, _roomOf);
        _projectedToday = ctx.Today;
        Year = model.Year;
        KeyedSync.Apply(Months, model.Months, m => m.Month, vm => vm.Month,
            m => new YearMonthViewModel(m, Services.Actions, Services.Commands), (vm, m) => vm.Update(m));
    }

    /// <summary>The year grid depends on the date only ("Now", past/future, upcoming items), not the minute.</summary>
    protected override void OnMinute()
    {
        if (Services.App.DayClock.Today != _projectedToday)
        {
            RefreshIfActive();
        }
    }
}
