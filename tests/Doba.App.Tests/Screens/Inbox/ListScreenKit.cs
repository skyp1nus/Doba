using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.ViewModels;
using Doba.Core.Time;

namespace Doba.App.Tests.Screens.Inbox;

/// <summary>Helpers shared by the Inbox and Today screen tests.</summary>
internal static class ListScreenKit
{
    /// <summary>Hosts the view at the content-area size on the main panel background and lets virtualising panels
    /// realise their rows (they need an effective viewport, which arrives after the first layout pass).</summary>
    public static Window Host(Control view)
    {
        var window = Support.ScreenHarness.Host(view);
        if (Application.Current!.TryGetResource("Brush.Bg.Main", Application.Current.ActualThemeVariant, out var bg) && bg is IBrush brush)
        {
            window.Background = brush;
        }

        Settle(window);
        return window;
    }

    public static void Settle(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Realised (visible) row controls under <paramref name="root"/>.</summary>
    public static List<TaskListRow> Rows(Visual root) =>
        [.. root.GetVisualDescendants().OfType<TaskListRow>().Where(r => r.IsEffectivelyVisible)];

    public static TaskListRow RowOf(Visual root, TaskRowViewModel vm) =>
        Rows(root).Single(r => ReferenceEquals(r.DataContext, vm));

    public static FixedTimeProvider Clock(Support.ScreenHarness harness) => (FixedTimeProvider)harness.App.Clock;
}
