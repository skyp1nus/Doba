using Avalonia;
using Avalonia.Controls;
using Doba.App.Screens;
using Doba.App.Services;
using Doba.App.Shell;
using Doba.App.Theme;
using Doba.Core.Model;
using Doba.Core.Navigation;

namespace Doba.App.Tests.Support;

/// <summary>Records what a screen asked the shell to do.</summary>
public sealed class RecordingCommands : IAppCommands
{
    public List<(AppView View, DateOnly Anchor, Point? Origin)> Zooms { get; } = [];
    public List<(DateOnly? Date, bool Explicit)> NewTasks { get; } = [];
    public List<long> Edits { get; } = [];

    public void ZoomTo(AppView view, DateOnly anchor, Point? origin = null) => Zooms.Add((view, anchor, origin));
    public void NewTask(DateOnly? date = null, bool hasExplicitDate = false) => NewTasks.Add((date, hasExplicitDate));
    public void EditTask(long id) => Edits.Add(id);
}

/// <summary>
/// Hosts one screen view on its own, sized like the content area of the real window at 1600×1000
/// (1315×832: the main panel minus the page header and the body padding 16/24/20/28), with DemoData and the
/// fixed demo clock. Use it while the shell is not available or for focused screen tests; full-window screenshots
/// come from the shell harness.
/// </summary>
public sealed class ScreenHarness : IDisposable
{
    public const double ContentWidth = 1315, ContentHeight = 832;

    public ScreenHarness(AppSettings? settings = null)
    {
        App = AppServices.CreateDemo(settings);
        Commands = new RecordingCommands();
        Actions = new TaskActions(App.Store, App.Settings, App.Platform.Shell, App.Clock);
        Services = new ScreenServices(App, Actions, Commands, () => FilterTag);
        new ThemeService(Application.Current!).Apply(App.Settings.Current);
    }

    public AppServices App { get; }

    public RecordingCommands Commands { get; }

    public TaskActions Actions { get; }

    public ScreenServices Services { get; }

    /// <summary>The sidebar tag filter as the screen sees it.</summary>
    public string? FilterTag { get; set; }

    /// <summary>Puts <paramref name="view"/> (DataContext already set) into a window of the content-area size and shows it.</summary>
    public static Window Host(Control view, double width = ContentWidth, double height = ContentHeight)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = view,
        };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    public void Dispose() => App.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
