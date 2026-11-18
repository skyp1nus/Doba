using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Doba.App.Motion;
using Doba.App.Platform;
using Doba.App.Shell;

namespace Doba.App;

/// <summary>
/// The main window: custom chrome, sidebar column, main panel (page header + screen host) and the overlay layer.
/// Built and wired by <see cref="ShellComposition.CreateMainWindow(AppServices, Avalonia.Application)"/>; every content
/// swap runs through <see cref="ShellComposition.Transitions"/>.
/// </summary>
public partial class MainWindow : Window
{
    private bool _launched;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>The shell view-model (null until <see cref="Attach"/>).</summary>
    public MainViewModel? ViewModel { get; private set; }

    /// <summary>What content transitions animate.</summary>
    public ContentTransitionTargets? Targets { get; private set; }

    /// <summary>N, Ctrl+K, G I, G T, 1–4, ← →, Esc; focus after navigation.</summary>
    public KeyboardShortcuts? Shortcuts { get; private set; }

    internal TitleBar TitleBar => PART_TitleBar;

    internal PageHeader Header => PART_Header;

    internal ScreenHost ScreenHost => PART_Screens;

    /// <summary>Clips the content area (the screen host and its page margin) so a transition never draws over the header.</summary>
    internal Panel ContentClip => PART_ContentClip;

    internal OverlayHost Overlays => PART_Overlays;

    internal Border SidebarColumn => PART_SidebarColumn;

    /// <summary>Holds the main panel's left edge (as wide as the sidebar column once a collapse slide has ended).</summary>
    internal Border MainInset => PART_MainInset;

    /// <summary>The sidebar collapse / expand slide (null until <see cref="Attach"/>).</summary>
    internal SidebarCollapse? SidebarCollapse { get; private set; }

    internal Border MainPanel => PART_Main;

    internal Panel AppSidebar => PART_AppSidebar;

    internal Panel SettingsSidebar => PART_SettingsSidebar;

    /// <summary>The painted backdrop (window colour + glows) that stands in for the desktop.</summary>
    internal Control PaintedBackdrop => PART_Backdrop;

    /// <summary>
    /// True while the OS draws a real backdrop (Mica / acrylic) behind the window: the painted glows are hidden and the
    /// window has no background of its own, so the chrome tint (Brush.Chrome, alpha = Window transparency) lies directly
    /// on the system backdrop over the title bar and sidebar. The main panel keeps its opaque Bg.Main.
    /// </summary>
    internal bool HasSystemBackdrop { get; private set; }

    /// <summary>
    /// Asks the OS for its backdrop: Mica on Windows 11, acrylic blur on Windows 10, then none. Where the OS has none
    /// (headless, older Windows) nothing changes and the painted fallback stays exactly as it is. The outcome follows
    /// <see cref="TopLevel.ActualTransparencyLevel"/>. Call before the window is shown.
    /// </summary>
    internal void ApplyBackdrop(ISystemPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!preferences.SupportsBackdrop)
        {
            return;
        }

        TransparencyLevelHint = preferences.PrefersMica
            ? [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None]
            : [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None];
        UpdateBackdrop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActualTransparencyLevelProperty)
        {
            UpdateBackdrop();
        }
    }

    private void UpdateBackdrop()
    {
        var level = ActualTransparencyLevel;
        var system = level.Equals(WindowTransparencyLevel.Mica)
            || level.Equals(WindowTransparencyLevel.AcrylicBlur)
            || level.Equals(WindowTransparencyLevel.Blur);
        if (system == HasSystemBackdrop || PART_Backdrop is null)
        {
            return;
        }

        HasSystemBackdrop = system;
        PART_Backdrop.IsVisible = !system;
        if (system)
        {
            Background = null;
        }
        else
        {
            ClearValue(BackgroundProperty);
        }
    }

    /// <summary>Connects the window to <paramref name="vm"/>: bindings, content transitions, overlays, keys, launch.</summary>
    internal void Attach(MainViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ViewModel = vm;
        DataContext = vm;
        var targets = new ContentTransitionTargets(PART_Screens, PART_Header.TitleBlock, PART_AppSidebar, PART_SettingsSidebar);
        Targets = targets;
        vm.ContentSwapper = (kind, origin, swap) => ShellComposition.Transitions.Run(kind, origin, swap, targets);
        SidebarCollapse = new SidebarCollapse(PART_SidebarColumn, PART_MainInset, PART_Main);
        SidebarCollapse.Snap(vm.IsRail);
        vm.PropertyChanged += OnViewModelPropertyChanged;
        PART_Overlays.FallbackFocus = PART_Header.NewTaskButton;
        Shortcuts = new KeyboardShortcuts(this, vm, PART_Overlays, PART_Screens);
        Opened += OnOpened;
        Closed += OnClosed;
    }

    /// <summary>The Collapse button / header toggle slides the sidebar; entering or leaving Settings (whose sidebar is
    /// always full width) changes it at once, under the settings transition.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel is not { } vm || SidebarCollapse is not { } collapse)
        {
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.IsSidebarCollapsed) && !vm.ShowsSettings)
        {
            collapse.Animate(vm.IsRail);
        }
        else if (e.PropertyName == nameof(MainViewModel.ShowsSettings) && collapse.IsRail != vm.IsRail)
        {
            collapse.Snap(vm.IsRail);
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_launched || Targets is not { } targets)
        {
            return;
        }

        _launched = true;
        ShellComposition.Transitions.Launch(targets);
    }

    /// <summary>Any close (to the tray, Quit, Windows session end) first finishes an overlay whose exit animation is still
    /// running, so a task saved a moment before is in the store before the Closing handlers hide the window and flush.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ViewModel?.CompletePendingOverlayClose();
        base.OnClosing(e);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        Closed -= OnClosed;
        if (ViewModel is { } vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        ViewModel?.Dispose();
    }
}
