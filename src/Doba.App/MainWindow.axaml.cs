using Avalonia.Controls;
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

    /// <summary>N / Ctrl+K / Esc.</summary>
    public KeyboardShortcuts? Shortcuts { get; private set; }

    internal TitleBar TitleBar => PART_TitleBar;

    internal PageHeader Header => PART_Header;

    internal ScreenHost ScreenHost => PART_Screens;

    internal OverlayHost Overlays => PART_Overlays;

    internal Border SidebarColumn => PART_SidebarColumn;

    internal Border MainPanel => PART_Main;

    internal Panel AppSidebar => PART_AppSidebar;

    internal Panel SettingsSidebar => PART_SettingsSidebar;

    /// <summary>Connects the window to <paramref name="vm"/>: bindings, content transitions, overlays, keys, launch.</summary>
    internal void Attach(MainViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ViewModel = vm;
        DataContext = vm;
        var targets = new ContentTransitionTargets(PART_Screens, PART_Header.TitleBlock, PART_AppSidebar, PART_SettingsSidebar);
        Targets = targets;
        vm.ContentSwapper = (kind, origin, swap) => ShellComposition.Transitions.Run(kind, origin, swap, targets);
        PART_Overlays.FallbackFocus = PART_Header.NewTaskButton;
        Shortcuts = new KeyboardShortcuts(this, vm, PART_Overlays);
        Opened += OnOpened;
        Closed += OnClosed;
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

    private void OnClosed(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        Closed -= OnClosed;
        ViewModel?.Dispose();
    }
}
