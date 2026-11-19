using Avalonia.Headless.XUnit;
using Doba.App.Services;
using Doba.App.Tests.Lifecycle;
using Doba.Core.Model;
using Doba.Core.Time;

namespace Doba.App.Tests.Shell;

/// <summary>A change that cannot be written to disk is shown in the title bar and logged next to the data.</summary>
public sealed class SaveProblemTests
{
    private static FixedTimeProvider Clock() => new(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero));

    /// <summary>Renames the tasks table behind the app's back, so every later task write fails (SQLite "no such table").</summary>
    private static void BreakTaskWrites(TempFolder folder)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + folder.Location.DatabasePath + ";Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "ALTER TABLE tasks RENAME TO tasks_moved;";
        command.ExecuteNonQuery();
    }

    [AvaloniaFact]
    public void A_failed_write_shows_the_notice_with_the_folder_and_reason_and_is_logged()
    {
        using var folder = new TempFolder();
        using var harness = LifecycleHarness.Disk(folder, Clock());
        var notice = harness.Window.TitleBar.SaveProblemButton;
        Assert.Null(harness.Vm.SaveProblem);
        Assert.False(notice.IsVisible);

        BreakTaskWrites(folder);
        harness.App.Store.Add(new TaskItem(0, "will not reach the disk", string.Empty, null, null, null, 0, Priority.None, null, false));
        harness.App.FlushAsync().GetAwaiter().GetResult(); // never throws; the failure is reported, not swallowed
        harness.Settle(); // the report crosses from the writer thread to the UI thread

        var problem = Assert.IsType<string>(harness.Vm.SaveProblem);
        Assert.Contains(folder.Path, problem, StringComparison.Ordinal);
        Assert.Contains("no such table", problem, StringComparison.OrdinalIgnoreCase);
        Assert.True(notice.IsVisible);
        Assert.Equal(problem, Avalonia.Controls.ToolTip.GetTip(notice));
        Assert.True(notice.IsEffectivelyEnabled);
        notice.Command!.Execute(null); // opens the data folder (a no-op shell here)

        var log = File.ReadAllText(DataLog.PathIn(folder.Path));
        Assert.Contains("write failed", log, StringComparison.Ordinal);
        Assert.Contains("no such table", log, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void Writes_that_succeed_show_nothing_and_log_nothing()
    {
        using var folder = new TempFolder();
        using var harness = LifecycleHarness.Disk(folder, Clock());

        harness.App.Store.Add(new TaskItem(0, "saved", string.Empty, null, null, null, 0, Priority.None, null, false));
        harness.App.FlushAsync().GetAwaiter().GetResult();
        harness.Settle();

        Assert.Null(harness.Vm.SaveProblem);
        Assert.False(harness.Window.TitleBar.SaveProblemButton.IsVisible);
        Assert.False(File.Exists(DataLog.PathIn(folder.Path)));
    }

    [Fact]
    public void The_log_starts_again_when_it_grows_too_large()
    {
        using var folder = new TempFolder();
        var path = DataLog.PathIn(folder.Path);
        File.WriteAllText(path, new string('x', (int)DataLog.MaxBytes + 1));

        DataLog.Append(folder.Path, new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero), "write failed: test");

        var text = File.ReadAllText(path);
        Assert.StartsWith("2026-11-15 09:00:00 +00:00 write failed: test", text, StringComparison.Ordinal);
        Assert.True(text.Length < 200);
    }
}
