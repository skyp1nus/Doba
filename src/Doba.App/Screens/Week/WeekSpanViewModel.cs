using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.ViewModels;
using Doba.Core.Projections;

namespace Doba.App.Screens.Week;

/// <summary>A multi-day bar in one week row (keyed by task id within the row).</summary>
public sealed partial class WeekSpanViewModel : ObservableObject
{
    public WeekSpanViewModel(SpanBar bar, TaskRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(bar);
        Row = row;
        Update(bar);
    }

    /// <summary>The task (title, done, meta "3 days" / "← 4 days" / "4 days →").</summary>
    public TaskRowViewModel Row { get; }

    public long Id => Row.Id;

    /// <summary>0-based start column.</summary>
    [ObservableProperty] public partial int Column { get; private set; }

    [ObservableProperty] public partial int ColumnSpan { get; private set; }

    /// <summary>0-based lane under the day header.</summary>
    [ObservableProperty] public partial int Lane { get; private set; }

    /// <summary>The span started before this row (square left end, no inset).</summary>
    [ObservableProperty] public partial bool ClipStart { get; private set; }

    /// <summary>The span continues after this row.</summary>
    [ObservableProperty] public partial bool ClipEnd { get; private set; }

    public void Update(SpanBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        Row.Update(bar.Row);
        Column = bar.Column;
        ColumnSpan = bar.ColumnSpan;
        Lane = bar.Lane;
        ClipStart = bar.ClipStart;
        ClipEnd = bar.ClipEnd;
    }
}
