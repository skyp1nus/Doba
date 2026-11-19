using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Services;
using Doba.App.ViewModels;
using Doba.Core.Projections;

namespace Doba.App.Screens.Week;

/// <summary>
/// One of the two week rows: four day cells (keyed by column, so moving to another week updates the same cells) and
/// the multi-day bars in lanes under the headers (keyed by task id). Lives as long as the screen; <see cref="Update"/>
/// reconciles in place.
/// </summary>
public sealed partial class WeekRowViewModel : ObservableObject
{
    private readonly Func<TaskRow, TaskRowViewModel> _createRow;
    private readonly Func<WeekDayCell, WeekCellViewModel> _createCell;
    private readonly Func<SpanBar, WeekSpanViewModel> _createSpan;
    private readonly Action<WeekCellViewModel, WeekDayCell> _updateCell;
    private readonly int[] _hiddenSpans = new int[4];

    public WeekRowViewModel(Func<TaskRow, TaskRowViewModel> createRow)
    {
        _createRow = createRow;
        _createCell = c => new WeekCellViewModel(c, LaneCount, _createRow, HiddenSpansAt(c.Column));
        _createSpan = s => new WeekSpanViewModel(s, _createRow(s.Row));
        _updateCell = (vm, c) => vm.Update(c, LaneCount, HiddenSpansAt(c.Column));
    }

    public ObservableCollection<WeekCellViewModel> Cells { get; } = [];

    /// <summary>The bars shown (those in the visible lanes).</summary>
    public ObservableCollection<WeekSpanViewModel> Spans { get; } = [];

    /// <summary>Lanes shown under the headers (at most the projection's lane count; fewer when the row is short).</summary>
    [ObservableProperty] public partial int LaneCount { get; private set; }

    /// <summary>The projection last pushed in.</summary>
    public WeekRow? Model { get; private set; }

    /// <summary>
    /// How many of <paramref name="lanes"/> multi-day lanes a row of <paramref name="rowHeight"/> shows. All of them while
    /// they leave room under the lanes for the card list's top padding and a "+N more" line (always at the design size);
    /// in a short row (many overlapping spans at 1100×700) only the lanes that leave that room, so no bar is cut by the
    /// row edge and "+N more" stays readable (KICKOFF §2, §6).
    /// </summary>
    public static int VisibleLanes(int lanes, double rowHeight)
    {
        if (double.IsNaN(rowHeight) || lanes == 0)
        {
            return lanes;
        }

        var room = rowHeight - WeekLayout.HeaderHeight - WeekLayout.ListPaddingTop - WeekLayout.MoreReserve;
        return Math.Clamp((int)Math.Floor(room / WeekLayout.LaneHeight), 0, lanes);
    }

    /// <param name="row">The projection's row.</param>
    /// <param name="rowHeight">The measured row height (NaN: not measured, show every lane).</param>
    public void Update(WeekRow row, double rowHeight = double.NaN)
    {
        ArgumentNullException.ThrowIfNull(row);
        Model = row;
        var lanes = VisibleLanes(row.LaneCount, rowHeight);
        LaneCount = lanes;

        // A bar in a lane that does not fit is counted in "+N more" of every day it covers in this row.
        Array.Clear(_hiddenSpans);
        IReadOnlyList<SpanBar> spans = row.Spans;
        if (lanes < row.LaneCount)
        {
            var shown = new List<SpanBar>(row.Spans.Count);
            foreach (var bar in row.Spans)
            {
                if (bar.Lane < lanes)
                {
                    shown.Add(bar);
                    continue;
                }

                for (var c = bar.Column; c < bar.Column + bar.ColumnSpan && c < _hiddenSpans.Length; c++)
                {
                    _hiddenSpans[c]++;
                }
            }

            spans = shown;
        }

        KeyedSync.Apply(Spans, spans, s => s.Row.Id, vm => vm.Id, _createSpan, (vm, s) => vm.Update(s));
        KeyedSync.Apply(Cells, row.Cells, static c => c.Column, static vm => vm.Column, _createCell, _updateCell);
    }

    private int HiddenSpansAt(int column) => column >= 0 && column < _hiddenSpans.Length ? _hiddenSpans[column] : 0;
}
