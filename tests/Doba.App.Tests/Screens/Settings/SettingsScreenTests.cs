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
using Doba.App.Tests.Support;
using Doba.App.Theme;
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
                "Everything is stored locally on this PC. There is no account and no sync between devices yet.",
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
            SettingsTab.Data => 3,
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
        public Fixture(bool autostartSupported = true)
        {
            Autostart = new RecordingAutostart(autostartSupported);
            Shell = new RecordingShell();
            App = AppServices.CreateDemo(platform: PlatformServices.NoOp() with { Autostart = Autostart, Shell = Shell });
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
