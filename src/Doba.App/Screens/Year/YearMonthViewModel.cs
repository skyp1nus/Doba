using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Services;
using Doba.App.Shell;
using Doba.App.ViewModels;
using Doba.Core.Navigation;
using Doba.Core.Projections;

namespace Doba.App.Screens.Year;

/// <summary>
/// One month cell of the Year grid (keyed by <see cref="Month"/>). Long-lived: <see cref="Update"/> pushes a fresh
/// <see cref="YearMonth"/> in and reconciles its key items by task id, so toggling an item changes that row in place.
/// </summary>
public sealed partial class YearMonthViewModel : ObservableObject
{
    /// <summary>The prototype's grid: 4 columns × 3 rows.</summary>
    public const int Columns = 4;

    private readonly TaskActions _actions;
    private readonly IAppCommands _commands;

    public YearMonthViewModel(YearMonth month, TaskActions actions, IAppCommands commands)
    {
        ArgumentNullException.ThrowIfNull(month);
        _actions = actions;
        _commands = commands;
        Month = month.Month;
        Update(month);
    }

    /// <summary>1..12 (the key).</summary>
    public int Month { get; }

    /// <summary>0-based grid column (0..3).</summary>
    public int Column => (Month - 1) % Columns;

    /// <summary>0-based grid row (0..2).</summary>
    public int GridRow => (Month - 1) / Columns;

    /// <summary>
    /// The prototype's per-position cell style (yearData <c>cell</c>) is picked by the view from these: no left rule
    /// and no left padding in the first column, no right padding in the last, no top rule and a smaller top padding in
    /// the first row (Space.Year.Cell* / Year.CellRule* tokens).
    /// </summary>
    public bool IsFirstColumn => Column == 0;

    /// <inheritdoc cref="IsFirstColumn"/>
    public bool IsLastColumn => Column == Columns - 1;

    /// <inheritdoc cref="IsFirstColumn"/>
    public bool IsFirstRow => GridRow == 0;

    [ObservableProperty] public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty] public partial bool IsCurrent { get; private set; }

    [ObservableProperty] public partial bool IsPast { get; private set; }

    /// <summary>"31 tasks · 12 done" / "No tasks" (upper-cased by the view).</summary>
    [ObservableProperty] public partial string Stat { get; private set; } = string.Empty;

    /// <summary>0..1 done share of the month's single-day tasks.</summary>
    [ObservableProperty] public partial double Progress { get; private set; }

    [ObservableProperty] public partial int MoreCount { get; private set; }

    [ObservableProperty] public partial DateOnly ZoomTarget { get; private set; }

    public bool HasMore => MoreCount > 0;

    /// <summary>"+3 more".</summary>
    public string MoreLabel => "+" + MoreCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " more";

    /// <summary>Header button accessible name ("Open November").</summary>
    public string OpenLabel => "Open " + Name;

    /// <summary>Key items: spans, then upcoming open tasks; at most four.</summary>
    public ObservableCollection<TaskRowViewModel> Rows { get; } = [];

    /// <summary>The record last pushed in.</summary>
    public YearMonth Model { get; private set; } = null!;

    public void Update(YearMonth month)
    {
        ArgumentNullException.ThrowIfNull(month);
        Model = month;
        Name = month.Name;
        IsCurrent = month.IsCurrent;
        IsPast = month.IsPast;
        Stat = month.Stat;
        Progress = month.Progress;
        MoreCount = month.MoreCount;
        ZoomTarget = month.ZoomTarget;
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(MoreLabel));
        OnPropertyChanged(nameof(OpenLabel));
        KeyedSync.Apply(Rows, month.Rows, r => r.Id, vm => vm.Id, r => new TaskRowViewModel(r, _actions, _commands), (vm, r) => vm.Update(r));
    }

    /// <summary>Month header or "+N more": zoom into the Month view.</summary>
    /// <param name="origin">Clicked element's centre relative to the Year content area (0..1), or null for keyboard.</param>
    public void Open(Point? origin = null) => _commands.ZoomTo(AppView.Month, ZoomTarget, origin);
}
