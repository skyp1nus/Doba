using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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

    /// <summary>Groups (key, label, tone, count) and their row ids, as text, for comparing with a projection.</summary>
    public static string Shape(IEnumerable<Doba.Core.Projections.TaskGroup> groups) =>
        string.Join("\n", groups.Select(g => $"{g.Key}|{g.Label}|{g.Tone}|{g.Count}|{string.Join(",", g.Items.Select(r => r.Id))}"));

    public static string Shape(IEnumerable<TaskGroupViewModel> groups) =>
        string.Join("\n", groups.Select(g => $"{g.Key}|{g.Label}|{g.Tone}|{g.Count}|{string.Join(",", g.Items.Select(r => r.Id))}"));

    /// <summary>The named part (PART_Title, PART_Priority …) of a row.</summary>
    public static T Part<T>(Visual row, string name)
        where T : Control =>
        row.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    /// <summary>A left click at the centre of <paramref name="target"/>.</summary>
    public static void Click(Window window, Control target)
    {
        window.UpdateLayout();
        var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window);
        Xunit.Assert.NotNull(centre);
        window.MouseDown(centre.Value, MouseButton.Left);
        window.MouseUp(centre.Value, MouseButton.Left);
        Settle(window);
    }

    /// <summary>Presses and releases Space on the focused element.</summary>
    public static void PressSpace(Window window)
    {
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Settle(window);
    }
}
