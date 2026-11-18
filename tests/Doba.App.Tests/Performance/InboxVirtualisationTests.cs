using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Platform;
using Doba.App.Screens.Inbox;
using Doba.App.Shell;
using Doba.App.Tests.Lifecycle;
using Doba.App.Theme;
using Doba.App.ViewModels;
using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Core.Navigation;
using AppMotion = Doba.App.Motion.Motion;

namespace Doba.App.Tests.Performance;

/// <summary>
/// KICKOFF §7 "virtualise anything that can grow without bound (Inbox)": with 5,000 undated and 5,000 dated tasks the
/// Inbox realises only the rows near its viewport, also after scrolling to the end, and opens in bounded time.
/// </summary>
public sealed class InboxVirtualisationTests
{
    /// <summary>Row controls the Inbox may realise at 1600×1000: about 25 rows fit in each column's viewport, plus each
    /// virtualising group's realised margin. 10,000 rows would be ~370,000 px tall.</summary>
    private const int RowCeiling = 150;

    /// <summary>Generous for a slow CI runner; this machine needs a fraction of it (the number is printed).</summary>
    private const double OpenBudgetMs = 4_000;

    [AvaloniaFact]
    public void Inbox_with_10000_tasks_realises_a_bounded_number_of_rows()
    {
        using var folder = new TempFolder();
        var today = DemoData.Today;
        var tasks = ManyInboxTasks(today);
        Task.Run(() => PerfData.WriteDatabaseAsync(folder.Path, tasks, CancellationToken.None)).GetAwaiter().GetResult();
        var animationsWere = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = false;
        var app = AppServices.Open(folder.Location, DemoData.CreateClock(), PlatformServices.NoOp());
        var window = ShellComposition.CreateMainWindow(app, Application.Current!, new NavState(AppView.Today, today));
        window.Width = 1600;
        window.Height = 1000;
        try
        {
            window.Show();
            Settle(window);
            Assert.Equal(10_000, app.Store.Count);

            var watch = Stopwatch.StartNew();
            window.ViewModel!.GoSection(AppView.Inbox);
            Settle(window);
            var openMs = watch.Elapsed.TotalMilliseconds;

            var inbox = Assert.IsType<InboxViewModel>(window.ViewModel.Current);
            var view = window.ScreenHost.ViewOf(inbox)!;
            var groups = inbox.Left.Concat(inbox.Right).ToList();
            Assert.Equal(10_000, groups.Sum(g => g.Items.Count));

            var atTop = RealisedRows(view);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Inbox, 10,000 tasks: open {PerfData.F1(openMs)} ms, {atTop.Count} rows realised at the top");
            Assert.InRange(atTop.Count, 20, RowCeiling);
            Assert.Contains(atTop, r => ReferenceEquals(r.DataContext, inbox.Left[0].Items[0]));
            Assert.True(openMs < OpenBudgetMs, $"Opening the Inbox took {PerfData.F1(openMs)} ms");

            // Scroll the page to its end; the extent is an estimate until the last rows are realised, so repeat.
            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "PART_Scroll");
            for (var i = 0; i < 10 && scroll.Offset.Y + scroll.Viewport.Height < scroll.Extent.Height - 1; i++)
            {
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                Settle(window);
            }

            var atEnd = RealisedRows(view);
            TestContext.Current.TestOutputHelper?.WriteLine($"{atEnd.Count} rows realised at the end (extent {PerfData.F1(scroll.Extent.Height)} px)");
            Assert.InRange(atEnd.Count, 1, RowCeiling);
            var lastRows = LastRows(inbox);
            Assert.Contains(atEnd, r => lastRows.Any(last => ReferenceEquals(r.DataContext, last)));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            new ThemeService(Application.Current!).Apply(AppSettings.Default);
            AppMotion.SystemAnimationsEnabled = animationsWere;
        }
    }

    /// <summary>
    /// 5,000 undated tasks (all priorities, one in seven done) and 5,000 open dated ones that all get a row in the
    /// Scheduled column: half overdue (up to ~2 years back), half within the next 14 days.
    /// </summary>
    private static List<TaskItem> ManyInboxTasks(DateOnly today)
    {
        var tasks = new List<TaskItem>(10_000);
        for (var i = 0; i < 5_000; i++)
        {
            tasks.Add(new TaskItem(tasks.Count + 1, "Undated task " + i, string.Empty, null, null, null, 0, (Priority)(i % 5), i % 3 == 0 ? "work" : null, i % 7 == 0));
        }

        for (var i = 0; i < 5_000; i++)
        {
            var date = i % 2 == 0 ? today.AddDays(-1 - (i % 700)) : today.AddDays(i % 14);
            tasks.Add(new TaskItem(tasks.Count + 1, "Dated task " + i, string.Empty, date, null, null, 0, (Priority)(i % 5), null, false));
        }

        return tasks;
    }

    /// <summary>The last row of each column (the page ends with the longer one).</summary>
    private static TaskRowViewModel[] LastRows(InboxViewModel inbox) =>
        [inbox.Left.Last(g => g.Items.Count > 0).Items[^1], inbox.Right.Last(g => g.Items.Count > 0).Items[^1]];

    private static List<TaskListRow> RealisedRows(Visual root) =>
        [.. root.GetVisualDescendants().OfType<TaskListRow>().Where(r => r.IsEffectivelyVisible)];

    private static void Settle(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        Dispatcher.UIThread.RunJobs();
    }
}
