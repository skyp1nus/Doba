using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Services;
using Doba.Core.Projections;

namespace Doba.App.Screens.Year;

/// <summary>
/// Year view: twelve months (4 × 3) with stats, progress and key items, rendering <see cref="Projector.Year"/> for the
/// anchor's year. Months are reconciled by month number and their rows by task id (<see cref="KeyedSync"/>), so
/// completing an item updates that row, the month's stat and progress in place.
/// </summary>
public sealed partial class YearViewModel : ScreenViewModel
{
    private DateOnly _projectedToday;

    public YearViewModel(ScreenServices services)
        : base(services)
    {
    }

    /// <summary>The displayed year.</summary>
    [ObservableProperty] public partial int Year { get; private set; }

    /// <summary>January..December in grid order (row-major, 4 per row).</summary>
    public ObservableCollection<YearMonthViewModel> Months { get; } = [];

    protected override void Refresh()
    {
        var ctx = Context;
        var year = State?.Anchor.Year ?? ctx.Today.Year;
        var model = Projector.Year(Store, ctx, year);
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
