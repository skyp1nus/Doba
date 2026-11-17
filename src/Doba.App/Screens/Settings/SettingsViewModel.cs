using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.App.Services;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Storage;

namespace Doba.App.Screens.Settings;

/// <summary>
/// Settings content: one list of rows per <see cref="SettingsTab"/> (the prototype's <c>cfg.sections</c>, lines
/// 1630–1705 of the design reference) plus the Shortcuts list. Every control writes through
/// <see cref="ISettingsService.Update"/>; <see cref="ISettingsService.Changed"/> re-syncs the rows (a sync never writes
/// back, so there are no loops). The settings navigation and the page title belong to the shell.
/// </summary>
public sealed partial class SettingsViewModel : ScreenViewModel
{
    private readonly ISettingsService _settings;
    private readonly IReadOnlyDictionary<SettingsTab, IReadOnlyList<SettingsRow>> _tabs;
    private readonly ButtonsRow _transferRow;
    private ThemeMode _theme;

    public SettingsViewModel(ScreenServices services)
        : base(services)
    {
        _settings = services.App.Settings;
        var app = services.App;
        var s = _settings;

        _transferRow = new ButtonsRow("io", "Export & import", "Move your tasks as a single JSON file.",
            [new RowButton("Export", ExportCommand), new RowButton("Import", ImportCommand)]);

        _tabs = new Dictionary<SettingsTab, IReadOnlyList<SettingsRow>>
        {
            [SettingsTab.Appearance] =
            [
                new SwatchRow("accent", "Accent color", "Used for today, progress bars and selections.", s),
                new RangeRow("opacity", "Window transparency", "How much of the desktop shows through the sidebar and header.", s),
                SegmentRow.Create("theme", "Theme", "System follows the Windows dark/light setting.", s,
                    [(ThemeMode.Dark, "Dark"), (ThemeMode.System, "System")],
                    x => x.Theme, (x, v) => x with { Theme = v }),
                new DensityRow("density", "Density", "How tightly tasks are packed. Compact fits about a third more on screen.", s),
            ],
            [SettingsTab.Calendar] =
            [
                SegmentRow.Create("weekStart", "Week starts on", "First column in Week, Month and Year views.", s,
                    [(DayOfWeek.Monday, "Monday"), (DayOfWeek.Sunday, "Sunday")],
                    x => x.WeekStart, (x, v) => x with { WeekStart = v }),
                SegmentRow.Create("launch", "Open on launch", "The screen you see when the app starts.", s,
                    [(LaunchView.Today, "Today"), (LaunchView.Week, "Week"), (LaunchView.Inbox, "Inbox")],
                    x => x.LaunchView, (x, v) => x with { LaunchView = v }),
            ],
            [SettingsTab.Tasks] =
            [
                SegmentRow.Create("defPrio", "Default priority", "Pre-selected in the New task window.", s,
                    [(Priority.None, "None"), (Priority.Low, "Low"), (Priority.Medium, "Med"), (Priority.High, "High")],
                    x => x.DefaultPriority, (x, v) => x with { DefaultPriority = v }),
                new SwitchRow("carry", "Carry over unfinished tasks", "Overdue tasks move to Today every morning.", s,
                    x => x.CarryOver, (x, v) => x with { CarryOver = v }),
                new SwitchRow("sound", "Completion sound", "A soft click when you check off a task.", s,
                    x => x.CompletionSound, (x, v) => x with { CompletionSound = v }),
            ],
            [SettingsTab.Windows] =
            [
                new SwitchRow("startup", "Launch at startup", "Open Doba when Windows starts.", s,
                    x => x.LaunchAtStartup, (x, v) => x with { LaunchAtStartup = v }, ApplyAutostart),
                new SwitchRow("tray", "Keep running in the tray", "Closing the window hides it instead of quitting.", s,
                    x => x.KeepInTray, (x, v) => x with { KeepInTray = v }),
                new KeysRow("hotkey", "Quick add from anywhere", "Opens the New task window on top of any app.", ["Ctrl", "Alt", "N"]),
            ],
            [SettingsTab.Data] =
            [
                new ButtonsRow("path", "Stored on this PC", DataFolder, [new RowButton("Open folder", OpenFolderCommand)]) { MonoDescription = true },
                new SwitchRow("backup", "Daily backup", "Keeps the last 7 days of backups next to your data.", s,
                    x => x.DailyBackup, (x, v) => x with { DailyBackup = v }),
                _transferRow,
            ],
            [SettingsTab.Shortcuts] = [],
        };

        Rows = _tabs[SettingsTab.Appearance];
        _theme = app.Settings.Current.Theme;
        if (_theme == ThemeMode.System)
        {
            LogSystemTheme();
        }

        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>The nine lines of the Shortcuts tab.</summary>
    public static IReadOnlyList<ShortcutRow> Shortcuts { get; } =
    [
        new("New task", ["N"]),
        new("Quick add (global)", ["Ctrl", "Alt", "N"]),
        new("Search", ["Ctrl", "K"]),
        new("Inbox", ["G", "I"]),
        new("Today", ["G", "T"]),
        new("Year / Month / Week / Day", ["1", "2", "3", "4"]),
        new("Previous / next", ["←", "→"]),
        new("Complete task", ["Space"]),
        new("Close window", ["Esc"]),
    ];

    /// <summary>The Data tab's footnote.</summary>
    public static string DataFootnote => "Everything is stored locally on this PC. There is no account and no sync between devices yet.";

    /// <summary>The tab whose content is shown (from <see cref="ScreenViewModel.State"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShortcuts), nameof(HasRows), nameof(IsData))]
    public partial SettingsTab Tab { get; private set; }

    /// <summary>Rows of the current tab (empty for Shortcuts).</summary>
    [ObservableProperty] public partial IReadOnlyList<SettingsRow> Rows { get; private set; }

    public bool HasRows => Tab != SettingsTab.Shortcuts;

    public bool IsShortcuts => Tab == SettingsTab.Shortcuts;

    /// <summary>The Data tab shows the footnote under its rows.</summary>
    public bool IsData => Tab == SettingsTab.Data;

    /// <summary>Folder shown in Data › Stored on this PC (the default location in demo mode).</summary>
    public string DataFolder => Services.App.Database?.Location.Folder ?? DataLocation.Default().Folder;

    /// <summary>Supplies export/import streams; the view sets the system pickers when it is attached.</summary>
    public ISettingsFilePicker? FilePicker { get; set; }

    /// <summary>The rows of any tab (tests, and the shell if it pre-renders tabs).</summary>
    public IReadOnlyList<SettingsRow> RowsOf(SettingsTab tab) => _tabs[tab];

    /// <summary>"doba-export-2026-11-15.json".</summary>
    public string ExportFileName => string.Create(CultureInfo.InvariantCulture, $"doba-export-{Services.App.DayClock.Today:yyyy-MM-dd}.json");

    protected override void Refresh()
    {
        Tab = State?.Tab ?? SettingsTab.Appearance;
        Rows = _tabs[Tab];
        Sync(_settings.Current);
    }

    /// <summary>Writes every task, tag and the settings to <paramref name="destination"/> as a Doba export.</summary>
    public async Task ExportToAsync(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var app = Services.App;
        var tasks = app.Store.All.ToArray();
        try
        {
            await JsonTransfer.ExportAsync(destination, tasks, [.. app.Store.Tags], _settings.Current, app.Clock).ConfigureAwait(true);
            ShowTransferMessage(string.Create(CultureInfo.InvariantCulture, $"Exported {Count(tasks.Length)}."), isError: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowTransferMessage($"Export failed: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// Reads a Doba export from <paramref name="source"/> and replaces every task, tag and setting with it. An invalid
    /// file changes nothing and shows the reason under the Export &amp; import row. Returns true on success.
    /// </summary>
    public async Task<bool> ImportFromAsync(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        LoadedData data;
        try
        {
            data = await JsonTransfer.ImportAsync(source).ConfigureAwait(true);
        }
        catch (FormatException ex)
        {
            ShowTransferMessage(ex.Message, isError: true);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowTransferMessage($"Import failed: {ex.Message}", isError: true);
            return false;
        }

        bool wasStartup = _settings.Current.LaunchAtStartup;
        Services.App.Store.ReplaceAll(data.Tasks, data.Tags);
        _settings.Update(_ => data.Settings);
        if (data.Settings.LaunchAtStartup != wasStartup)
        {
            ApplyAutostart(data.Settings.LaunchAtStartup);
        }

        ShowTransferMessage(string.Create(CultureInfo.InvariantCulture, $"Imported {Count(data.Tasks.Count)}."), isError: false);
        return true;
    }

    public override void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        base.Dispose();
    }

    [RelayCommand]
    private void OpenFolder() => Services.App.Platform.Shell.OpenFolder(DataFolder);

    [RelayCommand]
    private async Task Export()
    {
        if (FilePicker is not { } picker)
        {
            return;
        }

        Stream? stream;
        try
        {
            stream = await picker.OpenExportStreamAsync(ExportFileName).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowTransferMessage($"Export failed: {ex.Message}", isError: true);
            return;
        }

        if (stream is null)
        {
            return;
        }

        await using (stream.ConfigureAwait(true))
        {
            await ExportToAsync(stream).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        if (FilePicker is not { } picker)
        {
            return;
        }

        Stream? stream;
        try
        {
            stream = await picker.OpenImportStreamAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowTransferMessage($"Import failed: {ex.Message}", isError: true);
            return;
        }

        if (stream is null)
        {
            return;
        }

        await using (stream.ConfigureAwait(true))
        {
            await ImportFromAsync(stream).ConfigureAwait(true);
        }
    }

    private static string Count(int n) => n == 1 ? "1 task" : string.Create(CultureInfo.InvariantCulture, $"{n} tasks");

    /// <summary>Launch at startup: the setting is always kept; the Run entry is written only where supported.</summary>
    private void ApplyAutostart(bool enabled)
    {
        var autostart = Services.App.Platform.Autostart;
        if (autostart.IsSupported)
        {
            autostart.SetEnabled(enabled);
        }
    }

    private void ShowTransferMessage(string message, bool isError)
    {
        _transferRow.IsError = isError;
        _transferRow.Message = message;
    }

    private void Sync(AppSettings settings)
    {
        foreach (var rows in _tabs.Values)
        {
            foreach (var row in rows)
            {
                row.Sync(settings);
            }
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        if (settings.Theme != _theme)
        {
            _theme = settings.Theme;
            if (_theme == ThemeMode.System)
            {
                LogSystemTheme();
            }
        }

        // Keep the controls in step even while hidden (the base class refreshes only the active screen).
        if (!IsActive)
        {
            Sync(settings);
        }
    }

    private static void LogSystemTheme() =>
        Trace.WriteLine("Doba: Theme \"System\" is selected; only the dark palette exists, so System renders as Dark.");
}
