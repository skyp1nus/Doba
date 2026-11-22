using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Doba.App.Dialogs;
using Doba.App.Platform;
using Doba.App.Services;
using Doba.App.Tests.DesignSystem;
using Doba.Core.Model;
using Doba.Core.Navigation;

namespace Doba.App.Tests.Lifecycle;

/// <summary>
/// The memory release of a window hidden in the tray (<see cref="HiddenMemoryRelease"/>): when it is scheduled and
/// cancelled, that it runs once after its delay, and that the window comes back exactly as it was hidden. The headless
/// renderer has no GPU, so the graphics step runs on the render thread and finds nothing to empty; what it empties on
/// a real GPU is measured by <c>scripts/perf.ps1</c> ("Memory states").
/// </summary>
public class HiddenMemoryReleaseTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private static LifecycleHarness Tray(FakePlatform platform, LifecycleOptions? options = null) =>
        LifecycleHarness.Demo(platform, options, AppSettings.Default with { KeepInTray = true });

    /// <summary>Runs the dispatcher (timers included) and the render timer until the release has finished.</summary>
    private static bool WaitForRelease(LifecycleHarness h) => Pump.Until(() =>
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return h.Lifecycle.HiddenMemory.Last is not null;
    }, Timeout);

    /// <summary>The colours of the rendered window on a 16 px grid, taken once two frames in a row are the same
    /// (no entrance or state animation is still running).</summary>
    private static Color[] Frame(LifecycleHarness h)
    {
        Color[]? previous = null;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            h.Settle();
            using var frame = h.Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered.");
            var colours = new List<Color>();
            for (var y = 8; y < frame.PixelSize.Height; y += 16)
            {
                for (var x = 8; x < frame.PixelSize.Width; x += 16)
                {
                    colours.Add(Pixels.At(frame, x, y));
                }
            }

            if (previous is not null && previous.AsSpan().SequenceEqual(colours.ToArray()))
            {
                return previous;
            }

            previous = [.. colours];
            Thread.Sleep(20);
        }

        throw new InvalidOperationException("The window never came to rest.");
    }

    [AvaloniaFact]
    public void Hiding_to_the_tray_schedules_the_release_and_showing_the_window_cancels_it()
    {
        var platform = new FakePlatform();
        using var h = Tray(platform);
        Assert.False(h.Lifecycle.HiddenMemory.IsScheduled);
        Assert.Equal(TimeSpan.FromMilliseconds(HiddenMemoryRelease.DelayMs), h.Lifecycle.HiddenMemory.Delay);

        h.Window.Close();
        h.Settle();
        Assert.True(h.Lifecycle.IsHiddenInTray);
        Assert.True(h.Lifecycle.HiddenMemory.IsScheduled);

        platform.Tray.ClickIcon();
        h.Settle();

        Assert.True(h.Window.IsVisible);
        Assert.False(h.Lifecycle.HiddenMemory.IsScheduled);
        Assert.Null(h.Lifecycle.HiddenMemory.Last);
        Assert.Equal(0, platform.Memory.HeapTrims);
    }

    [AvaloniaFact]
    public void The_release_runs_once_after_the_delay_and_the_window_comes_back_as_it_was()
    {
        var platform = new FakePlatform();
        using var h = Tray(platform);
        h.Vm.GoSection(AppView.Inbox);
        h.Settle();
        h.Vm.ZoomTo(AppView.Week, h.App.DayClock.Today);
        h.Settle();
        var screen = h.Vm.Current!;
        var view = h.Window.ScreenHost.VisibleView!;
        var inbox = h.Window.ScreenHost.ViewOf(h.Vm.ScreenFor(AppView.Inbox))!;
        var cached = h.Window.ScreenHost.CachedViewCount;
        var drawn = Frame(h);
        var collections = GC.CollectionCount(GC.MaxGeneration);
        h.Lifecycle.HiddenMemory.Delay = TimeSpan.FromMilliseconds(1);

        h.Window.Close();
        Assert.True(WaitForRelease(h));
        Assert.True(GC.CollectionCount(GC.MaxGeneration) > collections, "the managed heap was collected");

        var report = h.Lifecycle.HiddenMemory.Last!;
        Assert.False(h.Lifecycle.HiddenMemory.IsScheduled);
        Assert.True(h.Lifecycle.HiddenMemory.Completion.IsCompletedSuccessfully);
        // The step reached the render thread through the visual attached while the window was open; headless has no
        // GPU cache, so there was nothing to empty and no device to trim.
        Assert.True(report.GraphicsStepRan);
        Assert.Equal(0, report.GraphicsCacheBefore);
        Assert.False(report.DriverTrimmed);
        Assert.Equal(0, platform.Memory.GraphicsTrims);
        Assert.Equal(1, platform.Memory.HeapTrims);
        Assert.True(h.Lifecycle.IsHiddenInTray);
        Assert.False(h.Window.IsVisible);

        // Nothing the user can see was released: the same screen, the same views (none rebuilt), the same picture.
        platform.Tray.ClickIcon();
        h.Settle();
        Assert.True(h.Window.IsVisible);
        Assert.Same(screen, h.Vm.Current);
        Assert.Same(view, h.Window.ScreenHost.VisibleView);
        Assert.Same(inbox, h.Window.ScreenHost.ViewOf(h.Vm.ScreenFor(AppView.Inbox)));
        Assert.Equal(cached, h.Window.ScreenHost.CachedViewCount);
        Assert.Equal(drawn, Frame(h));

        // And it goes on drawing: another screen gives another picture, from a view that was kept too.
        h.Vm.GoSection(AppView.Inbox);
        Assert.True(Pump.Until(() =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return ReferenceEquals(h.Window.ScreenHost.VisibleView, inbox);
        }, Timeout));
        Assert.NotEqual(drawn, Frame(h));

        // One release per hide: nothing more happens while the window is open.
        Assert.False(Pump.Until(() => platform.Memory.HeapTrims > 1, TimeSpan.FromMilliseconds(HiddenMemoryRelease.DelayMs / 100)));
    }

    [AvaloniaFact]
    public void Every_hide_gets_its_own_release()
    {
        var platform = new FakePlatform();
        using var h = Tray(platform);
        h.Lifecycle.HiddenMemory.Delay = TimeSpan.FromMilliseconds(1);

        h.Window.Close();
        Assert.True(WaitForRelease(h));
        var first = h.Lifecycle.HiddenMemory.Last;
        platform.Tray.ClickIcon();
        h.Settle();
        h.Window.Close();
        Assert.True(Pump.Until(() =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return platform.Memory.HeapTrims == 2;
        }, Timeout));

        Assert.NotSame(first, h.Lifecycle.HiddenMemory.Last);
    }

    [AvaloniaFact]
    public void Quick_add_after_a_release_shows_the_window_and_opens_New_task()
    {
        var platform = new FakePlatform();
        using var h = Tray(platform);
        var drawn = Frame(h);
        h.Lifecycle.HiddenMemory.Delay = TimeSpan.FromMilliseconds(1);
        h.Window.Close();
        Assert.True(WaitForRelease(h));

        platform.Hotkey.Press();
        h.Settle();

        Assert.True(h.Window.IsVisible);
        Assert.IsType<TaskDialogViewModel>(h.Vm.Overlay);
        // The dialog and its shade are drawn over the page that was there before the hide.
        Assert.NotEqual(drawn, Frame(h));
    }

    [AvaloniaFact]
    public void Showing_the_window_while_a_release_is_under_way_stops_it_before_its_next_step()
    {
        var platform = new FakePlatform();
        using var h = Tray(platform);
        using var collecting = new ManualResetEventSlim();
        using var shown = new ManualResetEventSlim();
        h.Lifecycle.HiddenMemory.Collector = () =>
        {
            collecting.Set();
            shown.Wait(Timeout);
        };
        h.Lifecycle.HiddenMemory.Delay = TimeSpan.FromMilliseconds(1);
        h.Window.Close();
        Assert.True(Pump.Until(() =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return collecting.IsSet;
        }, Timeout));

        platform.Tray.ClickIcon();
        h.Settle();
        shown.Set();
        Assert.True(Pump.Until(() => h.Lifecycle.HiddenMemory.Completion.IsCompleted, Timeout));

        Assert.True(h.Window.IsVisible);
        Assert.Null(h.Lifecycle.HiddenMemory.Last);
        Assert.Equal(0, platform.Memory.HeapTrims);
    }

    [AvaloniaFact]
    public void A_hidden_start_schedules_the_release()
    {
        var platform = new FakePlatform();
        using var h = Tray(platform, new LifecycleOptions(StartMinimized: true));

        Assert.True(h.Lifecycle.IsHiddenInTray);
        Assert.True(h.Lifecycle.HiddenMemory.IsScheduled);
    }

    [AvaloniaFact]
    public void A_show_request_that_arrives_before_the_hidden_start_has_settled_leaves_no_release_scheduled()
    {
        // A second launch signals "show" while this one is still starting: ShowWindow runs before the job that the
        // hidden start posted, and the window is open when that job runs.
        var platform = new FakePlatform();
        using var h = LifecycleHarness.Demo(platform, new LifecycleOptions(StartMinimized: true),
            AppSettings.Default with { KeepInTray = true }, afterStart: lifecycle => lifecycle.ShowWindow());

        Assert.True(h.Window.IsVisible);
        Assert.False(h.Lifecycle.IsHiddenInTray);
        Assert.False(h.Lifecycle.HiddenMemory.IsScheduled);
        Assert.True(platform.Hotkey.IsRegistered);
    }

    [AvaloniaFact]
    public void Quit_while_hidden_cancels_the_release()
    {
        var platform = new FakePlatform();
        using var h = Tray(platform);
        h.Window.Close();
        Assert.True(h.Lifecycle.HiddenMemory.IsScheduled);

        platform.Tray.ClickQuit();

        Assert.False(h.Lifecycle.HiddenMemory.IsScheduled);
        Assert.Equal(1, h.ShutdownCalls);
    }

    [AvaloniaFact]
    public void Exit_while_hidden_cancels_the_release_and_none_can_be_scheduled_afterwards()
    {
        var platform = new FakePlatform();
        using var h = Tray(platform);
        h.Window.Close();
        Assert.True(h.Lifecycle.HiddenMemory.IsScheduled);

        h.Exit();
        Assert.False(h.Lifecycle.HiddenMemory.IsScheduled);

        h.Lifecycle.HiddenMemory.Schedule();
        Assert.False(h.Lifecycle.HiddenMemory.IsScheduled);
    }

    [AvaloniaFact]
    public void A_window_that_was_never_opened_releases_without_the_graphics_step()
    {
        var platform = new FakePlatform();
        using var h = Tray(platform, new LifecycleOptions(StartMinimized: true));
        Assert.False(h.Window.IsVisible);
        h.Lifecycle.HiddenMemory.Delay = TimeSpan.FromMilliseconds(1);

        h.Lifecycle.HiddenMemory.Schedule();
        Assert.True(WaitForRelease(h));

        Assert.False(h.Lifecycle.HiddenMemory.Last!.GraphicsStepRan);
        Assert.Equal(1, platform.Memory.HeapTrims);
    }

    [AvaloniaFact]
    public void No_op_memory_does_nothing_and_reports_no_device()
    {
        var memory = PlatformServices.NoOp().Memory;

        memory.TrimNativeHeaps();
        Assert.False(memory.TrimGraphicsDevice(new NoGraphicsContext()));
    }

    [AvaloniaFact]
    public void The_platform_memory_trims_nothing_for_a_context_that_is_not_ANGLE()
    {
        // On Windows this is the real implementation whatever the plan allows (it touches only this process): a
        // context that is not EGL has no Direct3D device to trim, and returning the heaps' free blocks is harmless.
        var memory = PlatformServices.Create(new PlatformPlan(false, false, false, false)).Memory;

        memory.TrimNativeHeaps();
        Assert.False(memory.TrimGraphicsDevice(new NoGraphicsContext()));
    }

    private sealed class NoGraphicsContext : IPlatformGraphicsContext
    {
        public bool IsLost => false;

        public IDisposable EnsureCurrent() => this;

        public object? TryGetFeature(Type featureType) => null;

        public void Dispose()
        {
        }
    }
}
