using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.ViewModels;
using Doba.Core.Projections;

namespace Doba.App.Screens.Month;

/// <summary>A multi-day bar in one month week (keyed by task id within the week). Display only, like the prototype.</summary>
public sealed partial class MonthSpanViewModel : ObservableObject
{
    public MonthSpanViewModel(SpanBar bar, TaskRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(bar);
        Row = row;
        Update(bar);
    }

    /// <summary>The task (title, done, dimmed).</summary>
    public TaskRowViewModel Row { get; }

    public long Id => Row.Id;

    /// <summary>0-based start column (0..6).</summary>
    [ObservableProperty] public partial int Column { get; private set; }

    [ObservableProperty] public partial int ColumnSpan { get; private set; }

    /// <summary>0-based lane under the day numbers (0 or 1).</summary>
    [ObservableProperty] public partial int Lane { get; private set; }

    /// <summary>The span started before this week: square left end, no left inset.</summary>
    [ObservableProperty] public partial bool ClipStart { get; private set; }

    /// <summary>The span continues after this week: square right end, no right inset.</summary>
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
