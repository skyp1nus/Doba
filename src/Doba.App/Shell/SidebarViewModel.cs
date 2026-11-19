using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.App.Services;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using Doba.Core.Store;

namespace Doba.App.Shell;

/// <summary>
/// The app sidebar (sections with counts, tags, collapse, settings) and the settings sidebar (tabs). Renders
/// <see cref="Projector.Sidebar"/>; every action goes through <see cref="IShellNavigation"/>.
/// </summary>
/// <remarks>
/// <para>The sidebar keeps itself up to date: counts and tags re-project on store changes, the day rollover and the
/// tag filter (each once; the shell does not refresh it again), collapse follows the shell's IsSidebarCollapsed.</para>
/// <para>Navigation state (highlight, current row, settings tab) follows the shell's Nav / Navigated, or, with
/// <c>followsContentSwap</c> (the real shell), only <see cref="ShowPresented"/>, which the shell calls inside the content
/// swap: on a section transition the highlight then slides with the new page's entrance, after the old page's 130 ms
/// exit, as in the prototype (whose <c>view</c> changes only in <c>go</c>, after the exit).</para>
/// <para>Everything is idempotent: properties only raise when their value changes and tag rows are reconciled with
/// <see cref="KeyedSync"/> (rows update in place, the list is never rebuilt).</para>
/// </remarks>
public sealed partial class SidebarViewModel : ObservableObject, IDisposable
{
    /// <summary>The settings sidebar footer (DECISIONS: "Doba" replaces the design's "Todo").</summary>
    public const string FooterText = "Doba 0.1 · local only";

    /// <summary>Highlight rows above the tags: Inbox 0, Today 1, Calendar 2.</summary>
    private const int LastSectionRow = 2;

    private readonly bool _followsContentSwap;
    private NavState? _presented;

    /// <param name="app">The composition root.</param>
    /// <param name="shell">Where sidebar clicks go and where navigation state is read.</param>
    /// <param name="followsContentSwap">True when the shell calls <see cref="ShowPresented"/> from its content swap;
    /// the navigation state then changes only there.</param>
    public SidebarViewModel(AppServices app, IShellNavigation shell, bool followsContentSwap = false)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(shell);
        App = app;
        Shell = shell;
        _followsContentSwap = followsContentSwap;
        Tabs = new ObservableCollection<SidebarTabViewModel>(
            Enum.GetValues<SettingsTab>().Select(t => new SidebarTabViewModel(t, SelectTab)));

        shell.PropertyChanged += OnShellPropertyChanged;
        shell.Navigated += OnNavigated;
        app.Store.Changed += OnStoreChanged;
        app.DayClock.DayChanged += OnDayChanged;
        Refresh();
    }

    /// <summary>The composition root (store, settings, clock).</summary>
    public AppServices App { get; }

    /// <summary>Where sidebar clicks go.</summary>
    public IShellNavigation Shell { get; }

    // ---------- Sections ----------

    /// <summary>Open undated tasks.</summary>
    [ObservableProperty] public partial int InboxCount { get; private set; }

    [ObservableProperty] public partial string InboxCountText { get; private set; } = string.Empty;

    /// <summary>Open single-day tasks dated today.</summary>
    [ObservableProperty] public partial int TodayCount { get; private set; }

    [ObservableProperty] public partial string TodayCountText { get; private set; } = string.Empty;

    /// <summary>Row of the sliding highlight: the prototype's min(2, sectionIndex) (Settings parks it under Calendar).</summary>
    [ObservableProperty] public partial int HighlightIndex { get; private set; }

    /// <summary>False in Settings (the highlight fades to 0).</summary>
    [ObservableProperty] public partial bool IsHighlightVisible { get; private set; } = true;

    [ObservableProperty] public partial bool IsInboxCurrent { get; private set; }

    [ObservableProperty] public partial bool IsTodayCurrent { get; private set; }

    [ObservableProperty] public partial bool IsCalendarCurrent { get; private set; }

    // ---------- Collapse ----------

    /// <summary>The sidebar is the 56 px rail (<see cref="IShellNavigation.IsSidebarCollapsed"/>).</summary>
    [ObservableProperty] public partial bool IsCollapsed { get; private set; }

    /// <summary>Tool tip / accessible name of the Collapse button.</summary>
    public string CollapseLabel => IsCollapsed ? "Expand sidebar" : "Collapse sidebar";

    // ---------- Tags ----------

    /// <summary>Tag rows in creation order.</summary>
    public ObservableCollection<TagItemViewModel> Tags { get; } = [];

    /// <summary>The inline "Tag name" input is open.</summary>
    [ObservableProperty] public partial bool IsAddingTag { get; private set; }

    /// <summary>What is typed in the "Tag name" input.</summary>
    [ObservableProperty] public partial string TagDraft { get; set; } = string.Empty;

    // ---------- Settings sidebar ----------

    /// <summary>Appearance, Calendar, Tasks, Windows, Data, Shortcuts.</summary>
    public ObservableCollection<SidebarTabViewModel> Tabs { get; }

    /// <summary>The open settings tab (<see cref="NavState.Tab"/>).</summary>
    [ObservableProperty] public partial SettingsTab ActiveTab { get; private set; }

    /// <summary>How many times counts and tags were projected (tests check that one change projects once).</summary>
    internal int Projections { get; private set; }

    /// <summary>Re-projects counts and tags and re-reads navigation and collapse state. Cheap and idempotent.</summary>
    public void Refresh()
    {
        Project();
        ApplyNav();
        ApplyCollapsed();
    }

    /// <summary>The shell's content swap shows <paramref name="state"/>: the highlight, current row and settings tab
    /// move to it now.</summary>
    public void ShowPresented(NavState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _presented = state;
        ApplyNav();
    }

    public void Dispose()
    {
        Shell.PropertyChanged -= OnShellPropertyChanged;
        Shell.Navigated -= OnNavigated;
        App.Store.Changed -= OnStoreChanged;
        App.DayClock.DayChanged -= OnDayChanged;
    }

    // ---------- Commands ----------

    [RelayCommand]
    private void GoInbox() => Shell.GoSection(AppView.Inbox);

    [RelayCommand]
    private void GoToday() => Shell.GoSection(AppView.Today);

    [RelayCommand]
    private void GoCalendar() => Shell.GoCalendar();

    [RelayCommand]
    private void ToggleSidebar() => Shell.ToggleSidebar();

    [RelayCommand]
    private void OpenSettings() => Shell.OpenSettings();

    [RelayCommand]
    private void BackFromSettings() => Shell.BackFromSettings();

    /// <summary>"+": opens the inline input with an empty draft.</summary>
    [RelayCommand]
    private void StartAddTag()
    {
        TagDraft = string.Empty;
        IsAddingTag = true;
    }

    /// <summary>
    /// Enter, or the input losing focus: closes the input and, when something was typed, passes the raw text to
    /// <see cref="IShellNavigation.AddTag"/> (which normalises it and rejects empty names and duplicates).
    /// </summary>
    [RelayCommand]
    private void CommitTag()
    {
        if (!IsAddingTag)
        {
            return;
        }

        var raw = TagDraft;
        IsAddingTag = false;
        TagDraft = string.Empty;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            Shell.AddTag(raw);
        }
    }

    /// <summary>Esc: closes the input without creating anything.</summary>
    [RelayCommand]
    private void CancelTag()
    {
        IsAddingTag = false;
        TagDraft = string.Empty;
    }

    private void ToggleTag(TagItemViewModel tag) => Shell.ToggleTagFilter(tag.Name);

    private void SelectTab(SettingsTab tab) => Shell.SetSettingsTab(tab);

    // ---------- State ----------

    private void Project()
    {
        var ctx = new ProjectionContext(App.DayClock.Today, App.DayClock.Now, App.Settings.Current.WeekStart, Shell.FilterTag);
        var model = Projector.Sidebar(App.Store, ctx);
        Projections++;
        InboxCount = model.InboxCount;
        InboxCountText = model.InboxCount.ToString(CultureInfo.InvariantCulture);
        TodayCount = model.TodayCount;
        TodayCountText = model.TodayCount.ToString(CultureInfo.InvariantCulture);
        KeyedSync.Apply(Tags, model.Tags, t => t.Name, vm => vm.Name, t => new TagItemViewModel(t, ToggleTag), (vm, t) => vm.Update(t));
    }

    private void ApplyNav()
    {
        var nav = _followsContentSwap && _presented is { } presented ? presented : Shell.Nav;
        var section = nav.SectionIndex;
        HighlightIndex = Math.Min(LastSectionRow, section);
        IsHighlightVisible = nav.View != AppView.Settings;
        IsInboxCurrent = section == 0;
        IsTodayCurrent = section == 1;
        IsCalendarCurrent = section == LastSectionRow;
        ActiveTab = nav.Tab;
        foreach (var tab in Tabs)
        {
            tab.IsActive = tab.Tab == nav.Tab;
        }
    }

    private void ApplyCollapsed()
    {
        var collapsed = Shell.IsSidebarCollapsed;
        if (collapsed == IsCollapsed)
        {
            return;
        }

        // The rail has no input row: collapsing closes it like losing focus does (a typed name is created).
        if (collapsed)
        {
            CommitTag();
        }

        IsCollapsed = collapsed;
        OnPropertyChanged(nameof(CollapseLabel));
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IShellNavigation.Nav):
                if (!_followsContentSwap)
                {
                    ApplyNav();
                }

                break;
            case nameof(IShellNavigation.IsSidebarCollapsed):
                ApplyCollapsed();
                break;
            case nameof(IShellNavigation.FilterTag):
                Project();
                break;
            case null or "":
                Refresh(); // "everything may have changed"
                break;
            default:
                break; // the shell's other properties (current screen, overlay) do not concern the sidebar
        }
    }

    private void OnNavigated(object? sender, NavigatedEventArgs e)
    {
        if (!_followsContentSwap)
        {
            ApplyNav();
        }
    }

    private void OnStoreChanged(object? sender, TaskStoreChangedEventArgs e) => Project();

    private void OnDayChanged(object? sender, DateOnly e) => Project();
}
