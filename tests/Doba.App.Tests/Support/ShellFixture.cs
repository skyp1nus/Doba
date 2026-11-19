using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Doba.App.Motion;
using Doba.App.Platform;
using Doba.App.Screens;
using Doba.App.Shell;
using Doba.App.Theme;
using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Core.Navigation;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.Support;

/// <summary>
/// The full-window test harness: the real <see cref="MainWindow"/> built by
/// <see cref="ShellComposition.CreateMainWindow(AppServices, Application, NavState?)"/> on
/// <see cref="AppServices.CreateDemo"/> (DemoData, fixed clock 2026-11-15 22:26, no-op platform services), shown
/// headlessly at the requested size with animations off.
/// </summary>
public sealed class ShellFixture : IDisposable
{
    private readonly bool _animationsWere;

    private ShellFixture(AppServices app, MainWindow window, bool animationsWere)
    {
        App = app;
        Window = window;
        Vm = window.ViewModel ?? throw new InvalidOperationException("The main window has no view-model.");
        _animationsWere = animationsWere;
    }

    /// <summary>Always <c>AppServices.CreateDemo(settings, PlatformServices.NoOp())</c>.</summary>
    public AppServices App { get; }

    public MainViewModel Vm { get; }

    public MainWindow Window { get; }

    /// <summary>Opens the shell on <paramref name="view"/> at <paramref name="anchor"/> (default: the demo's today).</summary>
    public static ShellFixture Open(AppView view = AppView.Week, DateOnly? anchor = null, AppSettings? settings = null,
        SettingsTab tab = SettingsTab.Appearance, double width = 1600, double height = 1000)
    {
        var animationsWere = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = false;
        var app = AppServices.CreateDemo(settings, PlatformServices.NoOp());
        var start = new NavState(view, anchor ?? DemoData.Today, tab);
        var window = ShellComposition.CreateMainWindow(app, Application.Current!, start);
        window.Width = width;
        window.Height = height;
        window.Show();
        var fixture = new ShellFixture(app, window, animationsWere);
        fixture.Settle();
        return fixture;
    }

    /// <summary>The screen view-model of type <typeparamref name="T"/> (the active one, or the cached/created one).</summary>
    public T Screen<T>()
        where T : ScreenViewModel
    {
        if (Vm.Current is T current)
        {
            return current;
        }

        foreach (var view in Enum.GetValues<AppView>())
        {
            if (Vm.ScreenFor(view) is T screen)
            {
                return screen;
            }
        }

        throw new InvalidOperationException($"No screen of type {typeof(T).Name}.");
    }

    /// <summary>Goes to <paramref name="view"/> the way the UI would: sidebar sections, Settings, or a calendar zoom.</summary>
    public void Navigate(AppView view, DateOnly? anchor = null)
    {
        switch (view)
        {
            case AppView.Inbox or AppView.Today:
                Vm.GoSection(view);
                break;
            case AppView.Settings:
                Vm.OpenSettings();
                break;
            default:
                if (Vm.Nav.View == AppView.Settings)
                {
                    Vm.BackFromSettings();
                }

                Vm.ZoomTo(view, anchor ?? Vm.Nav.Anchor);
                break;
        }

        Settle();
    }

    /// <summary>Presses and releases <paramref name="key"/> on the window.</summary>
    public void Key(Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        Window.KeyPress(key, mods, PhysicalKey.None, null);
        Window.KeyRelease(key, mods, PhysicalKey.None, null);
        Settle();
    }

    /// <summary>Left-clicks the centre of <paramref name="target"/>.</summary>
    public void Click(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Settle();
        var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException("The target is not in the window.");
        Window.MouseMove(centre);
        Window.MouseDown(centre, MouseButton.Left);
        Window.MouseUp(centre, MouseButton.Left);
        Settle();
    }

    /// <summary>Runs dispatcher jobs and layout, forces render ticks and completes running Animator handles.</summary>
    public void Settle()
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            foreach (var visual in Window.GetVisualDescendants().ToList()) // completing a handle may swap screens
            {
                Animator.Running(visual)?.Complete();
            }
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Settles, renders and saves the window as <c>&lt;name&gt;.png</c>; returns the path.</summary>
    public string Capture(string name)
    {
        Settle();
        return Screenshots.Capture(Window, name);
    }

    public void Dispose()
    {
        Window.Close();
        App.DisposeAsync().AsTask().GetAwaiter().GetResult();

        // Accent, density and chrome tint live in the shared Application resources: put the defaults back.
        new ThemeService(Application.Current!).Apply(AppSettings.Default);
        AppMotion.SystemAnimationsEnabled = _animationsWere;
    }
}
