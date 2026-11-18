using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Doba.App.Motion;
using Doba.App.Platform;
using Doba.App.Shell;
using Doba.App.Theme;
using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Core.Navigation;
using AppMotion = Doba.App.Motion.Motion;

// Not "Doba.App.Tests.Motion": that would shadow Doba.App.Motion for every test namespace under Doba.App.Tests.
namespace Doba.App.Tests.MotionChecks;

/// <summary>
/// The real main window (DemoData, fixed clock, no-op platform services) built with OS animations ON, so control
/// templates get their real transition durations (MotionTimes are read when a template is applied). Unlike
/// <see cref="Support.ShellFixture"/>, <see cref="Pump"/> lets animations run: tests look at running handles and
/// transitions, and call <see cref="Settle"/> to finish them.
/// <para>The overlay backdrop blur is off unless <c>blur</c> is set: on the headless CPU raster every frame drawn while
/// the shade fades costs about three times as much with it (OverlayBlurCostTests), which would eat the real-time margin
/// of the tests that look at a running 150 ms exit. The blur has its own tests.</para>
/// </summary>
internal sealed class AnimatedShell : IDisposable
{
    private readonly bool _animationsWere;
    private readonly double? _blurWas;

    public AnimatedShell(AppView view = AppView.Week, DateOnly? anchor = null, AppSettings? settings = null,
        bool animations = true, bool blur = false)
    {
        _animationsWere = AppMotion.SystemAnimationsEnabled;
        _blurWas = OverlayHost.BackdropBlurRadius;
        OverlayHost.BackdropBlurRadius = blur ? null : 0;
        AppMotion.SystemAnimationsEnabled = animations;
        App = AppServices.CreateDemo(settings, PlatformServices.NoOp());
        Window = ShellComposition.CreateMainWindow(App, Application.Current!, new NavState(view, anchor ?? DemoData.Today));
        Window.Width = 1600;
        Window.Height = 1000;
        Window.Show();
        Vm = Window.ViewModel ?? throw new InvalidOperationException("The main window has no view-model.");
        Settle();
    }

    public AppServices App { get; }

    public MainWindow Window { get; }

    public MainViewModel Vm { get; }

    public OverlayHost Overlays => Window.Overlays;

    /// <summary>Runs queued jobs and layout and renders a frame, without finishing running animations.</summary>
    public void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Pumps and completes every running Animator handle until nothing is left.</summary>
    public void Settle()
    {
        for (var i = 0; i < 6; i++)
        {
            Pump();
            foreach (var visual in Window.GetVisualDescendants())
            {
                Animator.Running(visual)?.Complete();
            }
        }

        Pump();
    }

    /// <summary>Left-clicks the centre of <paramref name="target"/> (pumps, does not finish animations).</summary>
    public void Click(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException("The target is not in the window.");
        Window.MouseMove(centre);
        Window.MouseDown(centre, Avalonia.Input.MouseButton.Left);
        Window.MouseUp(centre, Avalonia.Input.MouseButton.Left);
        Pump();
    }

    /// <summary>Moves the pointer over <paramref name="target"/>'s centre (pumps, does not finish animations).</summary>
    public void Hover(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException("The target is not in the window.");
        Window.MouseMove(centre);
        Pump();
    }

    /// <summary>Presses and releases <paramref name="key"/> (pumps, does not finish animations).</summary>
    public void Key(Avalonia.Input.Key key, Avalonia.Input.RawInputModifiers mods = Avalonia.Input.RawInputModifiers.None)
    {
        Window.KeyPress(key, mods, Avalonia.Input.PhysicalKey.None, null);
        Window.KeyRelease(key, mods, Avalonia.Input.PhysicalKey.None, null);
        Pump();
    }

    public object? Focused => Window.FocusManager?.GetFocusedElement();

    /// <summary>Goes to <paramref name="view"/> and settles.</summary>
    public void Show(AppView view, DateOnly? anchor = null)
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

    /// <summary>Visible descendants of the current screen of type <typeparamref name="T"/>.</summary>
    public IEnumerable<T> OnScreen<T>()
        where T : Visual =>
        Window.ScreenHost.GetVisualDescendants().OfType<T>().Where(v => v.IsEffectivelyVisible);

    public void Dispose()
    {
        Window.Close();
        App.DisposeAsync().AsTask().GetAwaiter().GetResult();
        new ThemeService(Application.Current!).Apply(AppSettings.Default);
        AppMotion.SystemAnimationsEnabled = _animationsWere;
        OverlayHost.BackdropBlurRadius = _blurWas;
    }
}

/// <summary>A small window around one control, with OS animations on (restored on dispose).</summary>
internal sealed class Stage : IDisposable
{
    private readonly bool _animationsWere;

    public Stage(Control content, bool animations = true, double width = 400, double height = 200)
        : this(() => content, animations, width, height)
    {
    }

    /// <summary>Creates the content after switching OS animations to <paramref name="animations"/> (a UserControl reads
    /// its MotionTimes when it is constructed).</summary>
    public Stage(Func<Control> content, bool animations = true, double width = 400, double height = 200)
    {
        ArgumentNullException.ThrowIfNull(content);
        _animationsWere = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = animations;
        Content = content();
        Window = new Window
        {
            Width = width,
            Height = height,
            Content = Content,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        Window.Show();
        Window.UpdateLayout();
        Pixels();
    }

    public Window Window { get; }

    public Control Content { get; }

    /// <summary>Moves the pointer over <paramref name="target"/>'s centre.</summary>
    public void Hover(Control target)
    {
        var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException("The target is not in the window.");
        Window.MouseMove(centre);
        Window.UpdateLayout();
    }

    /// <summary>Moves the pointer to the window's bottom-right corner (off every control).</summary>
    public void Leave() => Window.MouseMove(new Point(Window.Width - 1, Window.Height - 1));

    /// <summary>Renders a frame (so transitions and composition animations start from a drawn state).</summary>
    public void Pixels() => DesignSystem.Pixels.At(Window, 0, 0);

    public void Dispose()
    {
        Window.Close();
        AppMotion.SystemAnimationsEnabled = _animationsWere;
    }
}

internal static class Transit
{
    /// <summary>The transition of <paramref name="property"/> declared on <paramref name="target"/>.</summary>
    public static TransitionBase Of(Animatable target, AvaloniaProperty property) =>
        target.Transitions?.OfType<TransitionBase>().SingleOrDefault(t => t.Property == property)
        ?? throw new Xunit.Sdk.XunitException($"{target.GetType().Name} has no transition on {property.Name}.");

    /// <summary>Asserts that <paramref name="target"/> animates <paramref name="property"/> over <paramref name="ms"/>
    /// (a Motion token, OS animations on) with <paramref name="easing"/>.</summary>
    public static void Is(Animatable target, AvaloniaProperty property, int ms, Easing easing)
    {
        var t = Of(target, property);
        Assert.Equal(TimeSpan.FromMilliseconds(ms), t.Duration);
        Assert.Same(easing, t.Easing);
        Assert.Equal(TimeSpan.Zero, t.Delay);
    }

    /// <summary>Asserts a running Animator handle.</summary>
    public static void Is(MotionHandle? handle, MotionState from, MotionState to, int ms, Easing easing)
    {
        Assert.NotNull(handle);
        Assert.True(handle.IsAnimating);
        Assert.Equal(from, handle.From);
        Assert.Equal(to, handle.To);
        Assert.Equal(TimeSpan.FromMilliseconds(ms), handle.Duration);
        Assert.Same(easing, handle.Easing);
    }

    /// <summary>The template's content presenter of a templated control (named PART_ContentPresenter).</summary>
    public static ContentPresenter Presenter(Visual templated) =>
        templated.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");

    public static T Part<T>(Visual root, string name)
        where T : Visual =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c is Control control && control.Name == name)
        ?? throw new Xunit.Sdk.XunitException($"No {typeof(T).Name} named {name}.");

    public static Color ColorOf(IBrush? brush) => brush is ISolidColorBrush s ? s.Color : throw new Xunit.Sdk.XunitException($"Not a solid brush: {brush}");

    public static Color Resource(string key) => DesignSystem.Pixels.Resource(key);

    public static ControlTheme Theme(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) && value is ControlTheme theme
            ? theme
            : throw new KeyNotFoundException(key);

    public static double Number(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) && value is double d
            ? d
            : throw new KeyNotFoundException(key);

    /// <summary>Runs the real dispatcher (render timer included) until <paramref name="condition"/> holds.</summary>
    public static bool Until(Func<bool> condition, double seconds = 3) =>
        DesignSystem.Pump.Until(condition, TimeSpan.FromSeconds(seconds));
}
