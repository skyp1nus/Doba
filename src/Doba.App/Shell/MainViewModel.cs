using Avalonia;
using Avalonia.Threading;
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
    private DateOnly _today; // the day the navigation state was last brought up to date with
    private bool _disposed;

    public MainViewModel(AppServices app, ThemeService theme, NavState? start = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(theme);
        App = app;
        _theme = theme;
        _settings = app.Settings.Current;
        Screens = new ScreenServices(app, new TaskActions(app.Store, app.Settings, app.Platform.Shell, app.Clock), this, () => FilterTag);
        _today = app.DayClock.Today;
        // Nav first: the sidebar and header read it in their constructors.
        Nav = start ?? InitialState(_settings, _today);
        // The sidebar's highlight and current row follow the content swap (Present), not the Nav change, so on a
        // section transition they move with the entrance after the exit, as in the prototype.
        Sidebar = new SidebarViewModel(app, this, followsContentSwap: true);
        Header = new PageHeaderViewModel(this, this);

        app.Settings.Changed += OnSettingsChanged;
        app.DayClock.DayChanged += OnDayChanged;
        app.DayClock.MinuteTick += OnMinuteTick;
        app.SaveFailed += OnSaveFailed;

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
    /// Set (on the UI thread) when a change could not be written to disk: the owner-facing sentence with the folder and
    /// the reason, shown by the title bar's "Changes not saved" notice until the app restarts. Null = nothing failed.
    /// </summary>
    [ObservableProperty] public partial string? SaveProblem { get; private set; }

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

    // The sidebar follows IsSidebarCollapsed and FilterTag through PropertyChanged by itself (no second refresh here).
    [RelayCommand]
    public void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
        Header.SetSidebarCollapsed(IsSidebarCollapsed);
    }

    public void ToggleTagFilter(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        FilterTag = string.Equals(FilterTag, tag, StringComparison.Ordinal) ? null : tag;
        Current?.RefreshIfActive();
    }

    public string? AddTag(string raw) => App.Store.AddTag(raw);

    [RelayCommand]
    public void OpenSearch() => OpenOverlay(new SearchPaletteViewModel(Screens));

    /// <summary>The "Changes not saved" notice: opens the data folder (where doba.log is) in Explorer.</summary>
    [RelayCommand]
    private void OpenDataFolder()
    {
        if (App.DataFolder is { } folder)
        {
            App.Platform.Shell.OpenFolder(folder);
        }
    }

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

    /// <summary>True while the open overlay plays its exit animation (the host asked it to close; Save / Delete have
    /// already decided what <see cref="OverlayViewModel.OnClosed"/> will write).</summary>
    public bool IsOverlayClosing { get; private set; }

    /// <summary>The overlay host started the exit animation of the open overlay.</summary>
    internal void MarkOverlayClosing() => IsOverlayClosing = Overlay is not null;

    /// <summary>Removes the open overlay and calls its <see cref="OverlayViewModel.OnClosed"/> (once). Called by the
    /// overlay host after the exit animation, and by <see cref="CompletePendingOverlayClose"/> and <see cref="Dispose"/>.</summary>
    public void CloseOverlay()
    {
        if (Overlay is not { } overlay)
        {
            return;
        }

        IsOverlayClosing = false;
        Overlay = null;
        overlay.OnClosed();
    }

    /// <summary>
    /// Finishes an overlay close that is still playing its exit animation, at once: the task dialog writes the save the
    /// user already decided on. Called before anything that may end the process or flush the store (window close,
    /// hide to the tray, shutdown), so a save made a moment before is never lost. An overlay that is merely open stays.
    /// </summary>
    public void CompletePendingOverlayClose()
    {
        if (IsOverlayClosing)
        {
            CloseOverlay();
        }
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
        Sidebar.ShowPresented(state); // highlight, current row and settings tab; the counts do not depend on navigation
    }

    private void RebuildHeader()
    {
        if (Header.State is { } state)
        {
            Header.Update(state, Today, App.Settings.Current.WeekStart);
        }
    }

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

    /// <summary>
    /// Midnight (or a wake-up on a later day): Inbox and Today anchored at the old today move their anchor to the new
    /// one, without a transition, so the sidebar's Calendar opens the week of the new day (Navigator.GoCalendar uses
    /// the anchor). The sidebar re-projects its counts from its own DayChanged subscription.
    /// </summary>
    private void OnDayChanged(object? sender, DateOnly today)
    {
        var previous = _today;
        _today = today;
        if (Nav.View is AppView.Inbox or AppView.Today && Nav.Anchor == previous && today != previous)
        {
            Nav = Nav with { Anchor = today };
            if (Current is { } screen && _screens.TryGetValue(Nav.View, out var shown) && ReferenceEquals(screen, shown))
            {
                screen.Show(Nav); // the screen keeps the state it shows (not while a section swap is still pending)
            }

            Header.Update(Nav, Today, App.Settings.Current.WeekStart);
            Sidebar.ShowPresented(Nav);
            return;
        }

        RebuildHeader();
    }

    private void OnSaveFailed(object? sender, string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            SaveProblem = message;
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed)
                {
                    SaveProblem = message;
                }
            });
        }
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

        // The overlay's OnClosed runs first, exactly once, while everything is still wired: a save whose exit animation
        // is still running is written now (a dialog that was merely open writes nothing).
        CloseOverlay();
        _disposed = true;
        App.SaveFailed -= OnSaveFailed;
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
