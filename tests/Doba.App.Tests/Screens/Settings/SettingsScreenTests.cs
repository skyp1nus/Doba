using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Platform;
using Doba.App.Screens;
using Doba.App.Screens.Settings;
using Doba.App.Services;
using Doba.App.Tests.Lifecycle;
using Doba.App.Tests.Support;
using Doba.App.Theme;
using Doba.Bridge;
using Doba.Core.Demo;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Storage;
using AppMotion = Doba.App.Motion.Motion;
using ToggleSwitch = Doba.App.Controls.ToggleSwitch;

namespace Doba.App.Tests.Screens.Settings;

public sealed class SettingsScreenTests
{
    /// <summary>The prototype's copy per tab (renderVals cfg.sections), "Todo" → "Doba".</summary>
    public static TheoryData<SettingsTab, string[]> TabCopy => new()
    {
        {
            SettingsTab.Appearance,
            [
                "Accent color", "Used for today, progress bars and selections.",
                "Window transparency", "How much of the desktop shows through the sidebar and header.", "72%",
                "Theme", "System follows the Windows dark/light setting.", "Dark", "System",
                "Density", "How tightly tasks are packed. Compact fits about a third more on screen.", "Comfortable", "Compact",
            ]
        },
        {
            SettingsTab.Calendar,
            [
                "Week starts on", "First column in Week, Month and Year views.", "Monday", "Sunday",
                "Open on launch", "The screen you see when the app starts.", "Today", "Week", "Inbox",
            ]
        },
        {
            SettingsTab.Tasks,
            [
                "Default priority", "Pre-selected in the New task window.", "None", "Low", "Med", "High",
                "Carry over unfinished tasks", "Overdue tasks move to Today every morning.",
                "Completion sound", "A soft click when you check off a task.",
            ]
        },
        {
            SettingsTab.Windows,
            [
                "Launch at startup", "Open Doba when Windows starts.",
                "Keep running in the tray", "Closing the window hides it instead of quitting.",
                "Quick add from anywhere", "Opens the New task window on top of any app.",
            ]
        },
        {
            SettingsTab.Data,
            [
                "Stored on this PC", DataLocation.Default().Folder, "Open folder",
                "Daily backup", "Keeps the last 7 days of backups next to your data.",
                "Export & import", "Move your tasks as a single JSON file.", "Export", "Import",
                "Claude access", "Not available in demo mode.",
                "Claude can", "Editing includes deleting tasks.", "Read", "Read & edit",
                "Connect Claude", ClaudeConnect.McpExePath, "Copy for Claude Desktop", "Copy for Claude Code",
                "Everything is stored locally on this PC. There is no account and no sync between devices yet. " +
                    "Claude sees your tasks only while Claude access is on.",
            ]
        },
        {
            SettingsTab.Shortcuts,
            [
                "New task", "Quick add (global)", "Search", "Inbox", "Today", "Year / Month / Week / Day",
                "Previous / next", "Complete task", "Close window",
            ]
        },
    };

    [AvaloniaTheory]
    [MemberData(nameof(TabCopy))]
    public void Each_tab_renders_its_rows_with_the_prototype_copy(SettingsTab tab, string[] expected)
    {
        using var f = new Fixture();
        var window = f.Show(tab);

        var texts = VisibleTexts(f.View);
        foreach (var text in expected)
        {
            Assert.Contains(text, texts);
        }

        int rows = f.View.GetVisualDescendants().OfType<Border>().Count(b => b.MinHeight == 66 && b.IsEffectivelyVisible);
        int expectedRows = tab switch
        {
            SettingsTab.Appearance => 4,
            SettingsTab.Calendar => 2,
            SettingsTab.Tasks => 3,
            SettingsTab.Windows => 3,
            SettingsTab.Data => 6,
            _ => 0,
        };
        Assert.Equal(expectedRows, rows);
        window.Close();
    }

    [AvaloniaFact]
    public void Shortcuts_show_nine_lines_with_their_key_caps()
    {
        using var f = new Fixture();
        var window = f.Show(SettingsTab.Shortcuts);

        var lines = f.View.GetVisualDescendants().OfType<Border>().Where(b => b.MinHeight == 52 && b.IsEffectivelyVisible).ToList();
        Assert.Equal(9, lines.Count);
        var caps = lines.Select(l => string.Join(' ', l.GetVisualDescendants().OfType<Kbd>().Select(k => k.Content as string))).ToList();
        Assert.Equal(["N", "Ctrl Alt N", "Ctrl K", "G I", "G T", "1 2 3 4", "← →", "Space", "Esc"], caps);
        Assert.All(lines.SelectMany(l => l.GetVisualDescendants().OfType<Kbd>()), k => Assert.Contains("keycap", k.Classes));
        window.Close();
    }

    [AvaloniaFact]
    public void Windows_quick_add_shows_ctrl_alt_n_key_caps()
    {
        using var f = new Fixture();
        var window = f.Show(SettingsTab.Windows);
        var caps = f.View.GetVisualDescendants().OfType<Kbd>().Where(k => k.IsEffectivelyVisible).Select(k => k.Content as string);
        Assert.Equal(["Ctrl", "Alt", "N"], caps);
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_swatch_changes_the_accent_and_moves_the_ring()
    {
        using var f = new Fixture();
        var window = f.Show(SettingsTab.Appearance);

        var swatches = f.View.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is SwatchOption).ToList();
        Assert.Equal(4, swatches.Count);
        Assert.Equal(["Mist", "Sage", "Sand", "Gray"], swatches.Select(s => ((SwatchOption)s.DataContext!).Name));

        Click(window, swatches[2]);
        Assert.Equal(Accent.Sand, f.App.Settings.Current.Accent);

        var rings = f.View.GetVisualDescendants().OfType<Panel>().Where(p => p.Classes.Contains("swatch-ring")).ToList();
        Assert.Equal([false, false, true, false], rings.Select(r => r.Classes.Contains("selected")));
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_segment_changes_the_setting()
    {
        using var f = new Fixture();
        var window = f.Show(SettingsTab.Calendar);

        var sunday = f.View.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.Content as string == "Sunday");
        Click(window, sunday);
        Assert.Equal(DayOfWeek.Sunday, f.App.Settings.Current.WeekStart);
        Assert.True(sunday.IsSelected);

        var inbox = f.View.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.Content as string == "Inbox");
        Click(window, inbox);
        Assert.Equal(LaunchView.Inbox, f.App.Settings.Current.LaunchView);
        window.Close();
    }

    [AvaloniaFact]
    public void Theme_and_default_priority_segments_write_settings()
    {
        using var f = new Fixture();
        var window = f.Show(SettingsTab.Appearance);
        Click(window, f.View.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.Content as string == "System"));
        Assert.Equal(ThemeMode.System, f.App.Settings.Current.Theme);

        f.Vm.Show(new NavState(AppView.Settings, f.App.DayClock.Today, SettingsTab.Tasks));
        window.UpdateLayout();
        Click(window, f.View.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.Content as string == "Med"));
        Assert.Equal(Priority.Medium, f.App.Settings.Current.DefaultPriority);
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_density_card_changes_density()
    {
        using var f = new Fixture();
        var window = f.Show(SettingsTab.Appearance);

        var compact = f.View.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is DensityOption { Density: Density.Compact });
        Click(window, compact);
        Assert.Equal(Density.Compact, f.App.Settings.Current.Density);
        var cards = f.View.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("density-card")).ToList();
        Assert.Equal([false, true], cards.Select(c => c.Classes.Contains("selected")));
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_toggles_writes_settings_and_leaves_the_run_entry_to_the_lifecycle()
    {
        using var f = new Fixture(autostartSupported: true);
        var window = f.Show(SettingsTab.Windows);

        var switches = f.View.GetVisualDescendants().OfType<ToggleSwitch>().ToList();
        Assert.Equal(2, switches.Count);
        Assert.All(switches, s => Assert.True(s.IsChecked));

        Click(window, switches[0]);
        Assert.False(f.App.Settings.Current.LaunchAtStartup);
        Assert.False(switches[0].IsChecked);

        Click(window, switches[1]);
        Assert.False(f.App.Settings.Current.KeepInTray);

        Click(window, switches[0]);
        Assert.True(f.App.Settings.Current.LaunchAtStartup);
        Assert.True(switches[0].IsChecked);

        // AppLifecycle is the single writer of the Run entry (it reconciles on every LaunchAtStartup change).
        Assert.Empty(f.Autostart.Calls);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Import_stores_launch_at_startup_and_the_row_shows_it_without_touching_the_run_entry()
    {
        using var f = new Fixture(autostartSupported: true);
        var window = f.Show(SettingsTab.Windows);
        f.App.Settings.Update(s => s with { LaunchAtStartup = false });
        using var exported = new MemoryStream();
        await f.Vm.ExportToAsync(exported);
        f.App.Settings.Update(s => s with { LaunchAtStartup = true });
        var startup = (SwitchRow)f.Vm.RowsOf(SettingsTab.Windows)[0];
        Assert.True(startup.IsOn);

        exported.Position = 0;
        Assert.True(await f.Vm.ImportFromAsync(exported));

        Assert.False(f.App.Settings.Current.LaunchAtStartup);
        Assert.False(startup.IsOn);
        Assert.Empty(f.Autostart.Calls);
        window.Close();
    }

    [AvaloniaFact]
    public void Launch_at_startup_keeps_the_setting_but_skips_autostart_when_unsupported()
    {
        using var f = new Fixture(autostartSupported: false);
        var window = f.Show(SettingsTab.Windows);

        Click(window, f.View.GetVisualDescendants().OfType<ToggleSwitch>().First());
        Assert.False(f.App.Settings.Current.LaunchAtStartup);
        Assert.Empty(f.Autostart.Calls);
        window.Close();
    }

    [AvaloniaFact]
    public void Task_switches_write_carry_over_and_sound()
    {
        using var f = new Fixture();
        var window = f.Show(SettingsTab.Tasks);
        var switches = f.View.GetVisualDescendants().OfType<ToggleSwitch>().ToList();
        Assert.Equal([true, false], switches.Select(s => s.IsChecked == true));

        Click(window, switches[0]);
        Click(window, switches[1]);
        Assert.False(f.App.Settings.Current.CarryOver);
        Assert.True(f.App.Settings.Current.CompletionSound);
        window.Close();
    }

    [AvaloniaFact]
    public void Slider_click_changes_transparency_and_label()
    {
        using var f = new Fixture();
        var window = f.Show(SettingsTab.Appearance);

        var slider = f.View.GetVisualDescendants().OfType<Slider>().Single();
        Assert.Equal(72, slider.Value);
        var right = slider.TranslatePoint(new Point(slider.Bounds.Width - 4, slider.Bounds.Height / 2), window)!.Value;
        window.MouseDown(right, MouseButton.Left);
        window.MouseUp(right, MouseButton.Left);
        window.UpdateLayout();

        int value = f.App.Settings.Current.WindowTransparency;
        Assert.True(value > 72, $"transparency {value}");
        Assert.Equal(value, (int)slider.Value);
        Assert.Contains(FormattableString.Invariant($"{value}%"), VisibleTexts(f.View));

        slider.Value = 40.4; // dragging lands on whole percents
        Assert.Equal(40, f.App.Settings.Current.WindowTransparency);
        window.Close();
    }

    [AvaloniaFact]
    public void Outside_settings_changes_update_the_controls()
    {
        using var f = new Fixture();
        var window = f.Show(SettingsTab.Appearance);

        f.App.Settings.Update(s => s with { Accent = Accent.Gray, WindowTransparency = 30, Theme = ThemeMode.System, Density = Density.Compact });
        window.UpdateLayout();

        Assert.Equal(30, f.View.GetVisualDescendants().OfType<Slider>().Single().Value);
        Assert.Contains("30%", VisibleTexts(f.View));
        Assert.Equal("System", f.View.GetVisualDescendants().OfType<ListBox>().Single().SelectedItem);
        var swatch = (SwatchRow)f.Vm.RowsOf(SettingsTab.Appearance)[0];
        Assert.Equal([false, false, false, true], swatch.Swatches.Select(s => s.IsSelected));

        // Hidden tabs follow too (the shell may keep the view-model while another tab is shown).
        f.App.Settings.Update(s => s with { DailyBackup = false });
        Assert.False(((SwitchRow)f.Vm.RowsOf(SettingsTab.Data)[1]).IsOn);
        window.Close();
    }

    [AvaloniaFact]
    public void Sync_does_not_write_back()
    {
        // Counts Update calls, not Changed events: SettingsService ignores an update to the same values, so a row that
        // wrote the synced values back would go unnoticed by an event count.
        using var f = new Fixture();
        var settings = new RecordingSettings(f.App.Settings);
        using var vm = new SettingsViewModel(f.Services, settings);
        var view = new SettingsView { DataContext = vm };
        (SettingsTab Tab, Func<AppSettings, AppSettings> Change)[] steps =
        [
            (SettingsTab.Appearance, s => s with { Accent = Accent.Gray, WindowTransparency = 55, Theme = ThemeMode.System, Density = Density.Compact }),
            (SettingsTab.Calendar, s => s with { WeekStart = DayOfWeek.Sunday, LaunchView = LaunchView.Inbox }),
            (SettingsTab.Tasks, s => s with { DefaultPriority = Priority.High, CarryOver = !s.CarryOver, CompletionSound = !s.CompletionSound }),
            (SettingsTab.Windows, s => s with { KeepInTray = !s.KeepInTray }),
            (SettingsTab.Data, s => s with { DailyBackup = !s.DailyBackup }),
            (SettingsTab.Data, s => s with { ClaudeAccess = !s.ClaudeAccess, ClaudeAccessLevel = ClaudeAccessLevel.ReadWrite }),
            (SettingsTab.Data, s => s with { ClaudeAccess = !s.ClaudeAccess }),
            (SettingsTab.Data, s => s with { ClaudeAccessLevel = ClaudeAccessLevel.Read }),
        ];

        vm.Show(new NavState(AppView.Settings, f.App.DayClock.Today, SettingsTab.Appearance));
        var window = ScreenHarness.Host(view);
        foreach (var (tab, change) in steps)
        {
            vm.Show(new NavState(AppView.Settings, f.App.DayClock.Today, tab));
            window.UpdateLayout();
            var before = settings.Updates;
            var expected = change(settings.Current);
            Assert.NotEqual(settings.Current, expected);

            settings.Update(_ => expected); // the test's own update: the shown controls follow …
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(before + 1, settings.Updates); // … without one more Update from a row
            Assert.Equal(expected, settings.Current);
        }

        window.Close();
    }

    [AvaloniaFact]
    public void Open_folder_opens_the_data_folder()
    {
        using var f = new Fixture();
        var window = f.Show(SettingsTab.Data);
        Click(window, f.View.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Open folder"));
        Assert.Equal([DataLocation.Default().Folder], f.Shell.Opened);
        Assert.Contains(f.View.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == DataLocation.Default().Folder && t.Classes.Contains("mono"));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Export_then_import_round_trips_through_a_temp_file()
    {
        using var f = new Fixture();
        var window = f.Show(SettingsTab.Data);
        var store = f.App.Store;
        var tasks = store.All.ToList();
        var tags = store.Tags.ToList();
        f.App.Settings.Update(s => s with { Accent = Accent.Sage, Density = Density.Compact, WeekStart = DayOfWeek.Sunday });
        var settings = f.App.Settings.Current;

        var path = Path.Combine(Path.GetTempPath(), $"doba-settings-test-{Guid.NewGuid():N}.json");
        try
        {
            await using (var output = File.Create(path))
            {
                await f.Vm.ExportToAsync(output);
            }

            // Change everything, then import the file back.
            store.ReplaceAll([], []);
            f.App.Settings.Update(_ => AppSettings.Default);
            Assert.Empty(store.All);

            await using (var input = File.OpenRead(path))
            {
                Assert.True(await f.Vm.ImportFromAsync(input));
            }

            Assert.Equal(tasks, store.All);
            Assert.Equal(tags, store.Tags);
            Assert.Equal(settings, f.App.Settings.Current);
            Assert.Equal(Accent.Sage, ((SwatchRow)f.Vm.RowsOf(SettingsTab.Appearance)[0]).Swatches.Single(s => s.IsSelected).Accent);
            window.UpdateLayout();
            Assert.Contains(FormattableString.Invariant($"Imported {tasks.Count} tasks."), VisibleTexts(f.View));
        }
        finally
        {
            File.Delete(path);
        }

        window.Close();
    }

    [AvaloniaFact]
    public async Task Export_and_import_buttons_use_the_file_picker()
    {
        using var f = new Fixture();
        var picker = new MemoryPicker();
        f.Vm.FilePicker = picker;
        var window = f.Show(SettingsTab.Data);
        int count = f.App.Store.All.Count;

        Click(window, f.View.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Export"));
        await f.Vm.ExportCommand.ExecutionTask!;
        Assert.Equal("doba-export-2026-11-15.json", picker.SuggestedName);
        Assert.True(picker.Exported.Length > 1000);

        f.App.Store.ReplaceAll([], []);
        Click(window, f.View.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Import"));
        await f.Vm.ImportCommand.ExecutionTask!;
        Assert.Equal(count, f.App.Store.All.Count);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Invalid_import_shows_the_error_and_changes_nothing()
    {
        using var f = new Fixture();
        var window = f.Show(SettingsTab.Data);
        int count = f.App.Store.All.Count;
        var before = f.App.Settings.Current;

        using var bad = new MemoryStream("""{ "format": "something-else", "version": 1 }"""u8.ToArray());
        Assert.False(await f.Vm.ImportFromAsync(bad));
        window.UpdateLayout();

        Assert.Equal(count, f.App.Store.All.Count);
        Assert.Equal(before, f.App.Settings.Current);
        var message = f.View.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("row-message") && t.IsEffectivelyVisible);
        Assert.Same(f.Vm.RowsOf(SettingsTab.Data)[2], message.DataContext); // under the Export & import row
        Assert.Contains("not a Doba export", message.Text, StringComparison.Ordinal);
        Assert.Contains("error", message.Classes);
        Assert.True(message.TryFindResource("Brush.Danger", message.ActualThemeVariant, out var danger));
        Assert.Equal(((ISolidColorBrush)danger!).Color, ((ISolidColorBrush)message.Foreground!).Color);

        using var notJson = new MemoryStream("not json"u8.ToArray());
        Assert.False(await f.Vm.ImportFromAsync(notJson));
        Assert.StartsWith("The file is not valid JSON", message.Text, StringComparison.Ordinal);
        window.Close();
    }

    /// <summary>Under NativeAOT (UseSystemResourceKeys) an exception's own text can be a bare resource key, so the row
    /// shows a fixed sentence per failure kind and never the exception's message.</summary>
    [AvaloniaFact]
    public async Task Export_and_import_failures_show_fixed_sentences_not_the_exception_text()
    {
        using var f = new Fixture();
        var window = f.Show(SettingsTab.Data);
        var row = (ButtonsRow)f.Vm.RowsOf(SettingsTab.Data)[2];
        int count = f.App.Store.All.Count;

        await f.Vm.ExportToAsync(new FailingStream(new IOException("IO_DiskFull_Key")));
        Assert.Equal("Export failed: the file could not be written.", row.Message);
        Assert.True(row.IsError);

        await f.Vm.ExportToAsync(new FailingStream(new UnauthorizedAccessException("UnauthorizedAccess_IODenied_Path, x")));
        Assert.Equal("Export failed: access to the file was denied.", row.Message);

        Assert.False(await f.Vm.ImportFromAsync(new FailingStream(new IOException("IO_SharingViolation_File, x"))));
        Assert.Equal("Import failed: the file could not be read.", row.Message);

        Assert.False(await f.Vm.ImportFromAsync(new FailingStream(new UnauthorizedAccessException("UnauthorizedAccess_IODenied_Path, x"))));
        Assert.Equal("Import failed: access to the file was denied.", row.Message);
        Assert.Equal(count, f.App.Store.All.Count);

        // The pickers' own failures (the file cannot be created or opened) read the same way.
        f.Vm.FilePicker = new FailingPicker(new UnauthorizedAccessException("UnauthorizedAccess_IODenied_Path, x"));
        await f.Vm.ExportCommand.ExecuteAsync(null);
        Assert.Equal("Export failed: access to the file was denied.", row.Message);
        f.Vm.FilePicker = new FailingPicker(new IOException("IO_FileNotFound_FileName, x"));
        await f.Vm.ImportCommand.ExecuteAsync(null);
        Assert.Equal("Import failed: the file could not be read.", row.Message);
        Assert.True(row.IsError);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Import_says_imported_only_once_the_tasks_are_on_disk()
    {
        using var folder = new TempFolder();
        using var f = new Fixture(app: AppServices.Open(folder.Location, DemoData.CreateClock(), PlatformServices.NoOp()));
        var window = f.Show(SettingsTab.Data);
        var row = (ButtonsRow)f.Vm.RowsOf(SettingsTab.Data)[2];
        using var file = await ExportOf(DemoData.Tasks());

        // Another process holds the write lock: the import is in memory, but not yet on disk.
        var stall = WriterStall.Hold(folder.Location);
        Task<bool> import;
        try
        {
            import = f.Vm.ImportFromAsync(file);
            for (var i = 0; i < 10; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                await Task.Delay(30);
            }

            Assert.Equal(DemoData.Tasks().Count, f.App.Store.All.Count);
            Assert.False(import.IsCompleted);
            Assert.Equal(string.Empty, row.Message);
            Assert.Empty(folder.TitlesOnDisk());
        }
        finally
        {
            stall.Release();
        }

        Assert.True(await import);
        Assert.Equal(DemoData.Tasks().Count, folder.TitlesOnDisk().Count);
        Assert.Equal(FormattableString.Invariant($"Imported {DemoData.Tasks().Count} tasks."), row.Message);
        Assert.False(row.IsError);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Import_whose_write_fails_shows_the_failure_instead_of_imported()
    {
        using var folder = new TempFolder();
        using var f = new Fixture(app: AppServices.Open(folder.Location, DemoData.CreateClock(), PlatformServices.NoOp()));
        var window = f.Show(SettingsTab.Data);
        var row = (ButtonsRow)f.Vm.RowsOf(SettingsTab.Data)[2];
        using var file = await ExportOf(DemoData.Tasks());

        // Renames the tasks table behind the app's back, so the import's write fails (SQLite "no such table").
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + folder.Location.DatabasePath + ";Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE tasks RENAME TO tasks_moved;";
            command.ExecuteNonQuery();
        }

        Assert.False(await f.Vm.ImportFromAsync(file));

        Assert.Equal(SettingsViewModel.ImportNotSaved, row.Message);
        Assert.StartsWith("Import failed: the tasks could not be saved.", row.Message, StringComparison.Ordinal);
        Assert.True(row.IsError);
        window.Close();
    }

    // ---- Data › Claude (MCP) --------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Claude_rows_come_last_on_the_data_tab_in_order()
    {
        using var f = new Fixture();
        var rows = f.Vm.RowsOf(SettingsTab.Data);

        Assert.Equal(["path", "backup", "io", "claude", "claudeLevel", "claudeConnect"], rows.Select(r => r.Key));
        var access = Assert.IsType<SwitchRow>(rows[3]);
        Assert.Equal("Claude access", access.Label);
        var level = Assert.IsType<SegmentRow>(rows[4]);
        Assert.Equal(("Claude can", "Editing includes deleting tasks."), (level.Label, level.Description));
        Assert.Equal(["Read", "Read & edit"], level.Options);
        var connect = Assert.IsType<ButtonsRow>(rows[5]);
        Assert.Equal(("Connect Claude", ClaudeConnect.McpExePath), (connect.Label, connect.Description));
        Assert.True(connect.MonoDescription);
        Assert.Equal(["Copy for Claude Desktop", "Copy for Claude Code"], connect.Buttons.Select(b => b.Label));
    }

    [AvaloniaFact]
    public void In_demo_mode_every_claude_row_is_disabled()
    {
        using var f = new Fixture(settings: AppSettings.Default with { ClaudeAccess = true });
        var window = f.Show(SettingsTab.Data);

        var rows = f.Vm.RowsOf(SettingsTab.Data);
        Assert.Equal([true, true, true, false, false, false], rows.Select(r => r.IsEnabled));
        Assert.Equal("Not available in demo mode.", rows[3].Description);
        Assert.Contains("Not available in demo mode.", VisibleTexts(f.View));

        var claudeSwitch = ClaudeSwitch(f.View);
        Assert.False(claudeSwitch.IsEffectivelyEnabled);
        Assert.All(ClaudeButtons(f.View), b => Assert.False(b.IsEffectivelyEnabled));
        Assert.All(SegmentItems(f.View, "Read", "Read & edit"), i => Assert.False(i.IsEffectivelyEnabled));
        Click(window, claudeSwitch);
        Assert.True(f.App.Settings.Current.ClaudeAccess); // unchanged: the click went nowhere
        Assert.Equal([1, 1, 1, Disabled(f.View), Disabled(f.View), Disabled(f.View)], RowTextOpacities(f.View));
        Assert.Equal(Disabled(f.View), LevelGroup(f.View).Opacity);
        window.Close();
    }

    [AvaloniaFact]
    public void Level_and_connect_follow_the_access_switch()
    {
        using var data = new TempFolder();
        using var f = Fixture.OnDisk(data);
        var window = f.Show(SettingsTab.Data);
        var rows = f.Vm.RowsOf(SettingsTab.Data);

        Assert.Equal(SettingsViewModel.ClaudeDescription, rows[3].Description);
        Assert.Contains("Let Claude Desktop and Claude Code see your tasks through MCP. Tasks Claude reads are sent to Anthropic.", VisibleTexts(f.View));
        Assert.Equal([true, true, true, true, false, false], rows.Select(r => r.IsEnabled));
        Assert.True(ClaudeSwitch(f.View).IsEffectivelyEnabled);
        Assert.All(ClaudeButtons(f.View), b => Assert.False(b.IsEffectivelyEnabled));
        Assert.Equal([1, 1, 1, 1, Disabled(f.View), Disabled(f.View)], RowTextOpacities(f.View));
        // The level group fades as a whole (track included), once: its items keep full opacity inside it.
        Assert.Equal(Disabled(f.View), LevelGroup(f.View).Opacity);
        Assert.All(SegmentItems(f.View, "Read", "Read & edit"), i => Assert.Equal(1, i.Opacity));
        var switches = f.View.GetVisualDescendants().OfType<ToggleSwitch>().ToList();
        Assert.Equal(2, switches.Count); // Daily backup, Claude access
        Assert.Equal([true, false], switches.Select(s => s.IsChecked == true));

        Click(window, ClaudeSwitch(f.View));
        Assert.True(f.App.Settings.Current.ClaudeAccess);
        Assert.All(rows, r => Assert.True(r.IsEnabled));
        Assert.All(ClaudeButtons(f.View), b => Assert.True(b.IsEffectivelyEnabled));
        Assert.All(RowTextOpacities(f.View), o => Assert.Equal(1, o));
        Assert.Equal(1, LevelGroup(f.View).Opacity);

        Click(window, SegmentItems(f.View, "Read & edit").Single());
        Assert.Equal(ClaudeAccessLevel.ReadWrite, f.App.Settings.Current.ClaudeAccessLevel);
        Click(window, SegmentItems(f.View, "Read").Single());
        Assert.Equal(ClaudeAccessLevel.Read, f.App.Settings.Current.ClaudeAccessLevel);

        Click(window, ClaudeSwitch(f.View));
        Assert.False(f.App.Settings.Current.ClaudeAccess);
        Assert.Equal([true, true, true, true, false, false], rows.Select(r => r.IsEnabled));
        Assert.Equal(Disabled(f.View), LevelGroup(f.View).Opacity);
        window.Close();
    }

    [AvaloniaFact]
    public void Outside_access_changes_update_the_rows_while_another_tab_is_shown()
    {
        using var data = new TempFolder();
        using var f = Fixture.OnDisk(data);
        var window = f.Show(SettingsTab.Appearance);

        f.App.Settings.Update(s => s with { ClaudeAccess = true, ClaudeAccessLevel = ClaudeAccessLevel.ReadWrite });

        var rows = f.Vm.RowsOf(SettingsTab.Data);
        Assert.True(((SwitchRow)rows[3]).IsOn);
        Assert.Equal(1, ((SegmentRow)rows[4]).SelectedIndex);
        Assert.All(rows, r => Assert.True(r.IsEnabled));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Copy_buttons_put_the_connect_texts_on_the_clipboard_and_say_so()
    {
        using var data = new TempFolder();
        using var f = Fixture.OnDisk(data, AppSettings.Default with { ClaudeAccess = true });
        var clipboard = new RecordingClipboard();
        f.Vm.Clipboard = clipboard;
        var window = f.Show(SettingsTab.Data);
        var connect = (ButtonsRow)f.Vm.RowsOf(SettingsTab.Data)[5];

        Click(window, ClaudeButtons(f.View).Single(b => b.Content as string == "Copy for Claude Desktop"));
        await f.Vm.CopyForDesktopCommand.ExecutionTask!;
        var desktop = Assert.Single(clipboard.Texts);
        Assert.Equal(ClaudeConnect.DesktopConfig(ClaudeConnect.McpExePath, data.Path, BridgeNames.DefaultDataFolder()), desktop);
        using (var json = System.Text.Json.JsonDocument.Parse(desktop))
        {
            var doba = json.RootElement.GetProperty("mcpServers").GetProperty("doba");
            Assert.Equal(ClaudeConnect.McpExePath, doba.GetProperty("command").GetString());
            Assert.Equal(["--data", data.Path], doba.GetProperty("args").EnumerateArray().Select(a => a.GetString()));
        }

        Assert.Contains(data.Path.Replace(@"\", @"\\", StringComparison.Ordinal), desktop, StringComparison.Ordinal);
        Assert.Equal("Copied — paste it into Claude Desktop's config", connect.Message);
        Assert.False(connect.IsError);
        window.UpdateLayout();
        Assert.Contains("Copied — paste it into Claude Desktop's config", VisibleTexts(f.View));

        Click(window, ClaudeButtons(f.View).Single(b => b.Content as string == "Copy for Claude Code"));
        await f.Vm.CopyForCodeCommand.ExecutionTask!;
        Assert.Equal("claude mcp add --scope user doba -- \"" + ClaudeConnect.McpExePath + "\" --data \"" + data.Path + "\"", clipboard.Texts[1]);
        Assert.Equal("Copied — run it in a terminal", connect.Message);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_busy_clipboard_shows_an_error_and_no_clipboard_shows_nothing()
    {
        using var data = new TempFolder();
        using var f = Fixture.OnDisk(data, AppSettings.Default with { ClaudeAccess = true });
        var window = f.Show(SettingsTab.Data);
        var connect = (ButtonsRow)f.Vm.RowsOf(SettingsTab.Data)[5];

        f.Vm.Clipboard = new RecordingClipboard(available: false);
        await f.Vm.CopyForCodeCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, connect.Message);

        f.Vm.Clipboard = new RecordingClipboard(error: new TimeoutException("Timeout opening clipboard."));
        await f.Vm.CopyForDesktopCommand.ExecuteAsync(null);
        Assert.Equal("Could not copy: the clipboard is busy. Try again.", connect.Message);
        Assert.True(connect.IsError);
        window.Close();
    }

    [AvaloniaFact]
    public void The_view_gives_the_view_model_the_window_clipboard()
    {
        using var f = new Fixture();
        var window = f.Show(SettingsTab.Data);
        Assert.IsType<TopLevelClipboard>(f.Vm.Clipboard);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(true, ClaudeAccessLevel.ReadWrite)]
    [InlineData(false, ClaudeAccessLevel.Read)]
    public async Task Import_keeps_the_current_claude_access(bool access, ClaudeAccessLevel level)
    {
        using var data = new TempFolder();
        using var f = Fixture.OnDisk(data, AppSettings.Default with { ClaudeAccess = !access, ClaudeAccessLevel = level == ClaudeAccessLevel.Read ? ClaudeAccessLevel.ReadWrite : ClaudeAccessLevel.Read });
        using var exported = new MemoryStream();
        await f.Vm.ExportToAsync(exported); // made while access was the other way round
        f.App.Settings.Update(s => s with { ClaudeAccess = access, ClaudeAccessLevel = level, Accent = Accent.Sand });

        exported.Position = 0;
        Assert.True(await f.Vm.ImportFromAsync(exported));

        Assert.Equal(Accent.Mist, f.App.Settings.Current.Accent); // the file's settings are applied …
        Assert.Equal(access, f.App.Settings.Current.ClaudeAccess); // … except Claude access
        Assert.Equal(level, f.App.Settings.Current.ClaudeAccessLevel);
        Assert.Equal(access, ((SwitchRow)f.Vm.RowsOf(SettingsTab.Data)[3]).IsOn);
    }

    [AvaloniaFact]
    public async Task A_file_that_says_claude_access_on_cannot_turn_it_on()
    {
        using var data = new TempFolder();
        using var f = Fixture.OnDisk(data);
        using var file = new MemoryStream("""{ "format": "doba", "version": 1, "settings": { "claudeAccess": true, "claudeAccessLevel": "readWrite" } }"""u8.ToArray());

        Assert.True(await f.Vm.ImportFromAsync(file));

        Assert.False(f.App.Settings.Current.ClaudeAccess);
        Assert.Equal(ClaudeAccessLevel.Read, f.App.Settings.Current.ClaudeAccessLevel);
    }

    /// <summary>The Data tab with Claude access on, outside demo mode (data folder under the temp directory):
    /// artifacts/screenshots/settings-data-claude-content.png.</summary>
    [AvaloniaFact]
    public void Settings_data_with_claude_access_screenshot()
    {
        var animations = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = false;
        try
        {
            using var data = new TempFolder();
            using var f = Fixture.OnDisk(data, AppSettings.Default with { ClaudeAccess = true });
            var window = f.Show(SettingsTab.Data);
            if (window.TryFindResource("Brush.Bg.Main", window.ActualThemeVariant, out var main) && main is IBrush brush)
            {
                window.Background = brush;
            }

            ((ButtonsRow)f.Vm.RowsOf(SettingsTab.Data)[5]).Message = SettingsViewModel.CopiedForCode;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            var path = Screenshots.Capture(window, "settings-data-claude-content");
            Assert.True(new FileInfo(path).Length > 5_000);
            window.Close();
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = animations;
        }
    }

    private static ToggleSwitch ClaudeSwitch(Control view) =>
        view.GetVisualDescendants().OfType<ToggleSwitch>().Single(s => s.DataContext is SwitchRow { Key: "claude" });

    private static List<Button> ClaudeButtons(Control view) =>
        [.. view.GetVisualDescendants().OfType<Button>().Where(b => b.Content is string text && text.StartsWith("Copy for Claude", StringComparison.Ordinal))];

    private static List<ListBoxItem> SegmentItems(Control view, params string[] labels) =>
        [.. view.GetVisualDescendants().OfType<ListBoxItem>().Where(i => labels.Contains(i.Content as string))];

    /// <summary>The "Claude can" segmented group.</summary>
    private static ListBox LevelGroup(Control view) =>
        view.GetVisualDescendants().OfType<ListBox>().Single(l => l.DataContext is SegmentRow { Key: "claudeLevel" });

    /// <summary>Opacity of each visible row's label + description column, in row order.</summary>
    private static List<double> RowTextOpacities(Control view) =>
        [.. view.GetVisualDescendants().OfType<StackPanel>().Where(p => p.Classes.Contains("row-text") && p.IsVisible).Select(p => p.Opacity)];

    private static double Disabled(Control view)
    {
        Assert.True(view.TryFindResource("Opacity.Disabled", view.ActualThemeVariant, out var value));
        return (double)value!;
    }

    private static async Task<MemoryStream> ExportOf(IReadOnlyList<TaskItem> tasks)
    {
        var file = new MemoryStream();
        await JsonTransfer.ExportAsync(file, tasks, DemoData.Tags, AppSettings.Default, DemoData.CreateClock());
        file.Position = 0;
        return file;
    }

    [AvaloniaFact]
    public void Tab_changes_swap_the_content()
    {
        using var f = new Fixture();
        var window = f.Show(SettingsTab.Appearance);
        Assert.Contains(f.View.GetVisualDescendants().OfType<Slider>(), s => s.IsEffectivelyVisible);

        f.Vm.Show(new NavState(AppView.Settings, f.App.DayClock.Today, SettingsTab.Shortcuts));
        window.UpdateLayout();
        Assert.True(f.Vm.IsShortcuts);
        Assert.DoesNotContain(f.View.GetVisualDescendants().OfType<Slider>(), s => s.IsEffectivelyVisible);
        Assert.Contains("Complete task", VisibleTexts(f.View));
        window.Close();
    }

    /// <summary>Renders each tab's content at the content-area size (1315×832) to docs/screenshots/settings-&lt;tab&gt;-content.png.</summary>
    [AvaloniaTheory]
    [InlineData(SettingsTab.Appearance)]
    [InlineData(SettingsTab.Calendar)]
    [InlineData(SettingsTab.Tasks)]
    [InlineData(SettingsTab.Windows)]
    [InlineData(SettingsTab.Data)]
    [InlineData(SettingsTab.Shortcuts)]
    public void Settings_tab_content_screenshot(SettingsTab tab)
    {
        var animations = AppMotion.SystemAnimationsEnabled;
        AppMotion.SystemAnimationsEnabled = false;
        try
        {
            using var f = new Fixture();
            var window = f.Show(tab);
            if (window.TryFindResource("Brush.Bg.Main", window.ActualThemeVariant, out var main) && main is IBrush brush)
            {
                window.Background = brush; // the content area sits on the main panel
            }

            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            var path = Screenshots.Capture(window, $"settings-{tab.ToString().ToLowerInvariant()}-content");
            Assert.True(new FileInfo(path).Length > 5_000);
            window.Close();
        }
        finally
        {
            AppMotion.SystemAnimationsEnabled = animations;
        }
    }

    private static List<string> VisibleTexts(Control root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Text is not null).Select(t => t.Text!)];

    private static void Click(Window window, Control target)
    {
        window.UpdateLayout();
        var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window);
        Assert.NotNull(center);
        window.MouseDown(center.Value, MouseButton.Left);
        window.MouseUp(center.Value, MouseButton.Left);
        window.UpdateLayout();
    }

    private sealed class Fixture : IDisposable
    {
        /// <param name="app">A database-backed app (its platform is used as is); null builds the demo app on recording
        /// autostart and shell.</param>
        /// <param name="settings">The demo app's settings (default: the defaults).</param>
        public Fixture(bool autostartSupported = true, AppServices? app = null, AppSettings? settings = null)
        {
            Autostart = new RecordingAutostart(autostartSupported);
            Shell = new RecordingShell();
            App = app ?? AppServices.CreateDemo(settings, PlatformServices.NoOp() with { Autostart = Autostart, Shell = Shell });
            var actions = new TaskActions(App.Store, App.Settings, App.Platform.Shell, App.Clock);
            Services = new ScreenServices(App, actions, new RecordingCommands(), () => null);
            Vm = new SettingsViewModel(Services);
            View = new SettingsView { DataContext = Vm };
            new ThemeService(Application.Current!).Apply(App.Settings.Current);
        }

        public AppServices App { get; }

        public ScreenServices Services { get; }

        public RecordingAutostart Autostart { get; }

        public RecordingShell Shell { get; }

        public SettingsViewModel Vm { get; }

        public SettingsView View { get; }

        /// <summary>Outside demo mode: a database in <paramref name="folder"/>, the demo clock, no-op platform.</summary>
        public static Fixture OnDisk(TempFolder folder, AppSettings? settings = null)
        {
            var app = AppServices.Open(folder.Location, DemoData.CreateClock(), PlatformServices.NoOp());
            if (settings is not null)
            {
                app.Settings.Update(_ => settings);
            }

            return new Fixture(app: app);
        }

        public Window Show(SettingsTab tab)
        {
            Vm.Show(new NavState(AppView.Settings, App.DayClock.Today, tab));
            return ScreenHarness.Host(View);
        }

        public void Dispose()
        {
            Vm.Dispose();
            App.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>The app's settings service, counting every <see cref="Update"/> call (even ones that change nothing).</summary>
    private sealed class RecordingSettings(ISettingsService inner) : ISettingsService
    {
        public int Updates { get; private set; }

        public AppSettings Current => inner.Current;

        public event EventHandler<AppSettings>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public void Update(Func<AppSettings, AppSettings> change)
        {
            Updates++;
            inner.Update(change);
        }
    }

    private sealed class RecordingAutostart(bool supported) : IAutostart
    {
        public List<bool> Calls { get; } = [];

        public bool IsSupported => supported;

        public bool IsEnabled { get; private set; }

        public void SetEnabled(bool enabled)
        {
            Calls.Add(enabled);
            IsEnabled = enabled;
        }
    }

    private sealed class RecordingShell : IShellIntegration
    {
        public List<string> Opened { get; } = [];

        public void OpenFolder(string path) => Opened.Add(path);

        public void PlayCompletionSound()
        {
        }
    }

    /// <summary>A file that fails every read and write with <paramref name="error"/>.</summary>
    private sealed class FailingStream(Exception error) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override void Flush() => throw error;

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.FromException(error);

        public override int Read(byte[] buffer, int offset, int count) => throw error;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(error);

        public override void Write(byte[] buffer, int offset, int count) => throw error;

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(error);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }

    /// <summary>Records what is copied; <paramref name="available"/> false = no clipboard, <paramref name="error"/> =
    /// another app holds it.</summary>
    private sealed class RecordingClipboard(bool available = true, Exception? error = null) : ISettingsClipboard
    {
        public List<string> Texts { get; } = [];

        public Task<bool> TrySetTextAsync(string text)
        {
            if (error is not null)
            {
                return Task.FromException<bool>(error);
            }

            if (available)
            {
                Texts.Add(text);
            }

            return Task.FromResult(available);
        }
    }

    private sealed class FailingPicker(Exception error) : ISettingsFilePicker
    {
        public Task<Stream?> OpenExportStreamAsync(string suggestedFileName) => Task.FromException<Stream?>(error);

        public Task<Stream?> OpenImportStreamAsync() => Task.FromException<Stream?>(error);
    }

    private sealed class MemoryPicker : ISettingsFilePicker
    {
        public string? SuggestedName { get; private set; }

        public byte[] Exported { get; private set; } = [];

        public Task<Stream?> OpenExportStreamAsync(string suggestedFileName)
        {
            SuggestedName = suggestedFileName;
            return Task.FromResult<Stream?>(new CapturingStream(this));
        }

        public Task<Stream?> OpenImportStreamAsync() => Task.FromResult<Stream?>(new MemoryStream(Exported));

        private sealed class CapturingStream(MemoryPicker owner) : MemoryStream
        {
            protected override void Dispose(bool disposing)
            {
                owner.Exported = ToArray();
                base.Dispose(disposing);
            }
        }
    }
}
