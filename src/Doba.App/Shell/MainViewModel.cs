using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.App.Dialogs;
using Doba.App.Dialogs.Search;
using Doba.App.Screens;
using Doba.App.Services;
using Doba.App.Theme;
using Doba.Core.Editing;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Store;

namespace Doba.App.Shell;

/// <summary>
/// The shell's view-model: owns the navigation state, the screen cache, the sidebar and page header view-models, the
/// tag filter, the sidebar collapse and the open overlay. It never decides where to go: every step calls a
/// <see cref="Navigator"/> function, applies its <see cref="NavResult"/>, raises <see cref="Navigated"/> once and hands
/// the content swap to <see cref="ContentSwapper"/> (the window plays <c>ShellComposition.Transitions.Run</c> there).
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IAppCommands, IShellNavigation, IDisposable
{
    private readonly ThemeService _theme;
    private readonly Dictionary<AppView, ScreenViewModel> _screens = [];
    private AppSettings _settings;
    private bool _disposed;

    public MainViewModel(AppServices app, ThemeService theme, NavState? start = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(theme);
        App = app;
        _theme = theme;
        _settings = app.Settings.Current;
        Screens = new ScreenServices(app, new TaskActions(app.Store, app.Settings, app.Platform.Shell, app.Clock), this, () => FilterTag);
        // Nav first: the sidebar and header read it in their constructors.
        Nav = start ?? InitialState(_settings, app.DayClock.Today);
        Sidebar = new SidebarViewModel(app, this);
        Header = new PageHeaderViewModel(this, this);

        app.Store.Changed += OnStoreChanged;
        app.Settings.Changed += OnSettingsChanged;
        app.DayClock.DayChanged += OnDayChanged;
        app.DayClock.MinuteTick += OnMinuteTick;

        Present(Nav);
    }

    /// <summary>The composition root.</summary>
    public AppServices App { get; }

    /// <summary>What every screen gets: Commands = this shell, FilterTag = the sidebar filter.</summary>
    public ScreenServices Screens { get; }

    /// <summary>The app and settings sidebars.</summary>
    public SidebarViewModel Sidebar { get; }

    /// <summary>Breadcrumbs, title and the header controls.</summary>
    public PageHeaderViewModel Header { get; }

    /// <summary>The navigation state (changes before the content swap).</summary>
    [ObservableProperty] public partial NavState Nav { get; private set; }

    /// <summary>The visible screen (changes inside the content swap).</summary>
    [ObservableProperty] public partial ScreenViewModel? Current { get; private set; }

    /// <summary>The sidebar tag filter (normalised name), null = none.</summary>
    [ObservableProperty] public partial string? FilterTag { get; private set; }

    /// <summary>The open overlay (task dialog, search palette), null = none.</summary>
    [ObservableProperty] public partial OverlayViewModel? Overlay { get; private set; }

    /// <summary>Sidebar collapsed to the 56 px rail (in memory only).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRail))]
    public partial bool IsSidebarCollapsed { get; private set; }

    /// <summary>The settings sidebar is shown (the presented screen is Settings).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRail))]
    public partial bool ShowsSettings { get; private set; }

    /// <summary>The sidebar column is the narrow rail: collapsed and not in Settings (the settings sidebar is always 232 px).</summary>
    public bool IsRail => IsSidebarCollapsed && !ShowsSettings;

    /// <summary>
    /// Plays a content transition around the swap. The window sets it to
    /// <c>(kind, origin, swap) =&gt; ShellComposition.Transitions.Run(kind, origin, swap, targets)</c>; the default swaps
    /// at once (view-model-only use).
    /// </summary>
    public Action<TransitionKind, Point?, Action> ContentSwapper { get; set; } = static (_, _, swap) => swap();

    /// <inheritdoc />
    public event EventHandler<NavigatedEventArgs>? Navigated;

    private DateOnly Today => App.DayClock.Today;

    /// <summary>Start screen from "Open on launch": Today / Week / Inbox, anchored at today.</summary>
    public static NavState InitialState(AppSettings s, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(s);
        var view = s.LaunchView switch
        {
            LaunchView.Week => AppView.Week,
            LaunchView.Inbox => AppView.Inbox,
            _ => AppView.Today,
        };
        return new NavState(view, today);
    }

    /// <summary>The cached view-model of <paramref name="view"/>, created on first use.</summary>
    public ScreenViewModel ScreenFor(AppView view)
    {
        if (!_screens.TryGetValue(view, out var screen))
        {
            screen = ScreenFactory.Create(view, Screens);
            _screens[view] = screen;
        }

        return screen;
    }

    // ---- IShellNavigation -----------------------------------------------------------------------------------------

    public void GoSection(AppView view) =>
        Apply(Navigator.GoSection(Nav, view, view == AppView.Today ? Today : Nav.Anchor), null);

    public void GoCalendar() => Apply(Navigator.GoCalendar(Nav), null);

    public void SetZoomLevel(AppView level)
    {
        if (level is not (AppView.Year or AppView.Month or AppView.Week or AppView.Day))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Only calendar levels can be zoomed to.");
        }

        Apply(Navigator.Zoom(Nav, level, Nav.Anchor), null);
    }

    public void Shift(int direction) => Apply(Navigator.Shift(Nav, Math.Sign(direction), Today), null);

    public void GoToday() => Apply(Navigator.GoToday(Nav, Today), null);

    public void OpenSettings() => Apply(Navigator.OpenSettings(Nav), null);

    public void BackFromSettings() => Apply(Navigator.BackFromSettings(Nav, Today), null);

    public void SetSettingsTab(SettingsTab tab) => Apply(Navigator.SetTab(Nav, tab), null);

    [RelayCommand]
    public void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
        Header.SetSidebarCollapsed(IsSidebarCollapsed);
        Sidebar.Refresh();
    }

    public void ToggleTagFilter(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        FilterTag = string.Equals(FilterTag, tag, StringComparison.Ordinal) ? null : tag;
        Sidebar.Refresh();
        Current?.RefreshIfActive();
    }

    public string? AddTag(string raw) => App.Store.AddTag(raw);

    [RelayCommand]
    public void OpenSearch() => OpenOverlay(new SearchPaletteViewModel(Screens));

    // ---- IAppCommands ---------------------------------------------------------------------------------------------

    public void ZoomTo(AppView view, DateOnly anchor, Point? origin = null) => Apply(Navigator.Zoom(Nav, view, anchor), origin);

    public void NewTask(DateOnly? date = null, bool hasExplicitDate = false)
    {
        var priority = App.Settings.Current.DefaultPriority;
        var draft = hasExplicitDate
            ? TaskDialog.NewDraft(date, Today, priority)
            : TaskDialog.NewDraft(Nav.View, Nav.Anchor, Today, priority);
        OpenOverlay(new TaskDialogViewModel(Screens, draft));
    }

    public void EditTask(long id)
    {
        if (App.Store.Get(id) is { } task)
        {
            OpenOverlay(new TaskDialogViewModel(Screens, TaskDialog.EditDraft(task, Today)));
        }
    }

    // ---- Overlays -------------------------------------------------------------------------------------------------

    /// <summary>Shows <paramref name="overlay"/>. An overlay that is already open is replaced at once (its
    /// <see cref="OverlayViewModel.OnClosed"/> runs first), e.g. a search hit opening the task dialog.</summary>
    public void OpenOverlay(OverlayViewModel overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (ReferenceEquals(Overlay, overlay))
        {
            return;
        }

        CloseOverlay();
        Overlay = overlay;
    }

    /// <summary>Removes the open overlay and calls its <see cref="OverlayViewModel.OnClosed"/>. Called by the overlay
    /// host after the exit animation.</summary>
    public void CloseOverlay()
    {
        if (Overlay is not { } overlay)
        {
            return;
        }

        Overlay = null;
        overlay.OnClosed();
    }

    // ---- Navigation plumbing --------------------------------------------------------------------------------------

    /// <summary>Applies one navigation step: new <see cref="Nav"/>, one <see cref="Navigated"/>, then the swap through
    /// <see cref="ContentSwapper"/>. A <see cref="TransitionKind.None"/> result changes nothing.</summary>
    private void Apply(NavResult result, Point? origin)
    {
        if (result.Transition == TransitionKind.None)
        {
            return;
        }

        var from = Nav;
        var to = result.State;
        Nav = to;
        Navigated?.Invoke(this, new NavigatedEventArgs(from, to, result.Transition, origin));
        ContentSwapper(result.Transition, origin, () => Present(to));
    }

    /// <summary>The content swap: shows the screen for <paramref name="state"/> (exactly one active screen), the matching
    /// sidebar and header.</summary>
    private void Present(NavState state)
    {
        if (_disposed)
        {
            return;
        }

        var next = ScreenFor(state.View);
        if (Current is { } old && !ReferenceEquals(old, next))
        {
            old.Hide();
        }

        next.Show(state);
        Current = next;
        ShowsSettings = state.View == AppView.Settings;
        Header.Update(state, Today, App.Settings.Current.WeekStart);
        Sidebar.Refresh();
    }

    private void RebuildHeader()
    {
        if (Header.State is { } state)
        {
            Header.Update(state, Today, App.Settings.Current.WeekStart);
        }
    }

    private void OnStoreChanged(object? sender, TaskStoreChangedEventArgs e) => Sidebar.Refresh();

    private void OnSettingsChanged(object? sender, AppSettings next)
    {
        var previous = _settings;
        _settings = next;
        if (previous.Accent != next.Accent)
        {
            _theme.ApplyAccent(next.Accent, animate: true);
        }

        if (previous.Density != next.Density)
        {
            _theme.ApplyDensity(next.Density);
        }

        if (previous.WindowTransparency != next.WindowTransparency)
        {
            _theme.ApplyChromeOpacity(next.WindowTransparency, animate: true);
        }

        if (previous.WeekStart != next.WeekStart)
        {
            RebuildHeader();
        }
    }

    private void OnDayChanged(object? sender, DateOnly e)
    {
        RebuildHeader();
        Sidebar.Refresh();
    }

    private void OnMinuteTick(object? sender, EventArgs e)
    {
        if (Header.Today != Today)
        {
            RebuildHeader();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        App.Store.Changed -= OnStoreChanged;
        App.Settings.Changed -= OnSettingsChanged;
        App.DayClock.DayChanged -= OnDayChanged;
        App.DayClock.MinuteTick -= OnMinuteTick;
        Sidebar.Dispose(); // it subscribes to the store, the day clock and this shell itself
        foreach (var screen in _screens.Values)
        {
            screen.Dispose();
        }

        _screens.Clear();
    }
}
