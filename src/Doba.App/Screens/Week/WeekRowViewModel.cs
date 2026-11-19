using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Services;
using Doba.App.ViewModels;
using Doba.Core.Projections;

namespace Doba.App.Screens.Week;

/// <summary>
/// One of the two week rows: four day cells (keyed by column, so moving to another week updates the same cells) and
/// the multi-day bars in lanes under the headers (keyed by task id). Lives as long as the screen; <see cref="Update"/>
/// reconciles in place. Which lanes a short row shows, and the "+N more" of the bars it hides, come from the projection
/// (<see cref="WeekMetrics.VisibleLanes"/>).
/// </summary>
public sealed partial class WeekRowViewModel : ObservableObject
{
    private readonly Func<TaskRow, TaskRowViewModel> _createRow;
    private readonly Func<WeekDayCell, WeekCellViewModel> _createCell;
    private readonly Func<SpanBar, WeekSpanViewModel> _createSpan;
    private readonly Action<WeekCellViewModel, WeekDayCell> _updateCell;

    public WeekRowViewModel(Func<TaskRow, TaskRowViewModel> createRow)
    {
        _createRow = createRow;
        _createCell = c => new WeekCellViewModel(c, LaneCount, _createRow);
        _createSpan = s => new WeekSpanViewModel(s, _createRow(s.Row));
        _updateCell = (vm, c) => vm.Update(c, LaneCount);
    }

    public ObservableCollection<WeekCellViewModel> Cells { get; } = [];

    /// <summary>The bars shown (those in the lanes the row shows).</summary>
    public ObservableCollection<WeekSpanViewModel> Spans { get; } = [];

    /// <summary>Lanes shown under the headers (the projection's <see cref="WeekRow.LaneCount"/>).</summary>
    [ObservableProperty] public partial int LaneCount { get; private set; }

    /// <summary>The projection last pushed in.</summary>
    public WeekRow? Model { get; private set; }

    /// <param name="row">The projection's row.</param>
    public void Update(WeekRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Model = row;
        LaneCount = row.LaneCount;
        KeyedSync.Apply(Spans, row.Spans, s => s.Row.Id, vm => vm.Id, _createSpan, (vm, s) => vm.Update(s));
        KeyedSync.Apply(Cells, row.Cells, static c => c.Column, static vm => vm.Column, _createCell, _updateCell);
    }
}
