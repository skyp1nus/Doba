using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.Core.Navigation;
using Doba.Core.Projections;

namespace Doba.App.Shell;

/// <summary>
/// The page header (breadcrumbs, title + subtitle, sidebar toggle, Year/Month/Week/Day switch, Today, ‹ ›, New task).
/// Every string comes from <see cref="HeaderProjection"/>; every button goes through <see cref="IShellNavigation"/> /
/// <see cref="IAppCommands"/>. Updated by the shell inside the content swap, so the header changes together with the
/// screen.
/// </summary>
public sealed partial class PageHeaderViewModel : ObservableObject
{
    private readonly IShellNavigation _shell;
    private readonly IAppCommands _commands;
    private bool _updating;

    public PageHeaderViewModel(IShellNavigation shell, IAppCommands commands)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(commands);
        _shell = shell;
        _commands = commands;
        SidebarToggleLabel = ToggleLabel(shell.IsSidebarCollapsed);
    }

    /// <summary>The state the header shows (null before the first update).</summary>
    public NavState? State { get; private set; }

    /// <summary>The "today" the header was built with.</summary>
    public DateOnly Today { get; private set; }

    /// <summary>The projection the header renders.</summary>
    public HeaderModel? Model { get; private set; }

    /// <summary>Breadcrumbs, reconciled in place (at most three).</summary>
    public ObservableCollection<CrumbViewModel> Crumbs { get; } = [];

    [ObservableProperty] public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubtitle))]
    public partial string Subtitle { get; private set; } = string.Empty;

    public bool HasSubtitle => Subtitle.Length > 0;

    /// <summary>Year / Month / Week / Day: the zoom switch, Today and ‹ › are shown.</summary>
    [ObservableProperty] public partial bool IsCalendar { get; private set; }

    /// <summary>The sidebar toggle is shown (not in Settings).</summary>
    [ObservableProperty] public partial bool ShowSidebarToggle { get; private set; } = true;

    /// <summary>"New task" is shown (not in Settings).</summary>
    [ObservableProperty] public partial bool ShowNewTask { get; private set; } = true;

    /// <summary>"Collapse sidebar" / "Expand sidebar" (accessible name and tooltip of the toggle).</summary>
    [ObservableProperty] public partial string SidebarToggleLabel { get; private set; }

    /// <summary>Selected segment: Year 0 … Day 3, −1 outside the calendar. Set by the switch: zooms to that level.</summary>
    [ObservableProperty] public partial int ZoomIndex { get; set; } = -1;

    /// <summary>Rebuilds every string from <see cref="HeaderProjection.Build"/>.</summary>
    public void Update(NavState state, DateOnly today, DayOfWeek weekStart)
    {
        ArgumentNullException.ThrowIfNull(state);
        var model = HeaderProjection.Build(state, today, weekStart);
        State = state;
        Today = today;
        Model = model;

        for (var i = 0; i < model.Crumbs.Count; i++)
        {
            var c = model.Crumbs[i];
            if (i < Crumbs.Count)
            {
                Crumbs[i].Set(c.Label, c.Target);
            }
            else
            {
                Crumbs.Add(new CrumbViewModel(_commands, i > 0, c.Label, c.Target));
            }
        }

        while (Crumbs.Count > model.Crumbs.Count)
        {
            Crumbs.RemoveAt(Crumbs.Count - 1);
        }

        Title = model.Title;
        Subtitle = model.Subtitle;
        IsCalendar = state.IsCalendar;
        ShowSidebarToggle = state.View != AppView.Settings;
        ShowNewTask = state.View != AppView.Settings;
        _updating = true;
        try
        {
            ZoomIndex = state.IsCalendar ? state.View - AppView.Year : -1;
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Keeps the toggle's label in step with the sidebar.</summary>
    public void SetSidebarCollapsed(bool collapsed) => SidebarToggleLabel = ToggleLabel(collapsed);

    partial void OnZoomIndexChanged(int value)
    {
        if (_updating || value < 0 || State is not { IsCalendar: true } state)
        {
            return;
        }

        var level = AppView.Year + value;
        if (level != state.View)
        {
            _shell.SetZoomLevel(level);
        }
    }

    [RelayCommand]
    private void ToggleSidebar() => _shell.ToggleSidebar();

    [RelayCommand]
    private void GoToday() => _shell.GoToday();

    [RelayCommand]
    private void Previous() => _shell.Shift(-1);

    [RelayCommand]
    private void Next() => _shell.Shift(1);

    [RelayCommand]
    private void NewTask() => _commands.NewTask();

    private static string ToggleLabel(bool collapsed) => collapsed ? "Expand sidebar" : "Collapse sidebar";
}

/// <summary>One breadcrumb: a link (zooms to its target, no click origin) or plain text, with a "/" before all but the first.</summary>
public sealed partial class CrumbViewModel : ObservableObject
{
    private readonly IAppCommands _commands;

    public CrumbViewModel(IAppCommands commands, bool hasSeparator, string label, NavTarget? target)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        HasSeparator = hasSeparator;
        Label = label;
        Target = target;
    }

    public bool HasSeparator { get; }

    [ObservableProperty] public partial string Label { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLink), nameof(IsPlain))]
    public partial NavTarget? Target { get; private set; }

    public bool IsLink => Target is not null;

    public bool IsPlain => Target is null;

    internal void Set(string label, NavTarget? target)
    {
        Label = label;
        Target = target;
    }

    [RelayCommand]
    private void Open()
    {
        if (Target is { } t)
        {
            _commands.ZoomTo(t.View, t.Anchor);
        }
    }
}
