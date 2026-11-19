using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.App.Services;
using Doba.Bridge;
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
    private readonly SwitchRow _claudeRow;
    private readonly SegmentRow _claudeLevelRow;
    private readonly ButtonsRow _claudeConnectRow;
    private readonly string _mcpExePath;
    private ThemeMode _theme;

    public SettingsViewModel(ScreenServices services)
        : this(services, (services ?? throw new ArgumentNullException(nameof(services))).App.Settings)
    {
    }

    /// <summary>Rows read and write <paramref name="settings"/> (tests pass a recording wrapper of the app's service).</summary>
    /// <param name="mcpExePath">Doba.Mcp.exe for Connect Claude (default: <see cref="ClaudeConnect.McpExePath"/>).</param>
    internal SettingsViewModel(ScreenServices services, ISettingsService settings, string? mcpExePath = null)
        : base(services)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _mcpExePath = mcpExePath ?? ClaudeConnect.McpExePath;
        var app = services.App;
        var s = _settings;

        _transferRow = new ButtonsRow("io", "Export & import", "Move your tasks as a single JSON file.",
            [new RowButton("Export", ExportCommand), new RowButton("Import", ImportCommand)]);

        // Claude (MCP): the switch only writes the setting; ClaudeAccessService opens and closes the bridge pipe.
        _claudeRow = new SwitchRow("claude", "Claude access", app.IsDemo ? ClaudeDemoDescription : ClaudeDescription, s,
            x => x.ClaudeAccess, (x, v) => x with { ClaudeAccess = v });
        _claudeLevelRow = SegmentRow.Create("claudeLevel", "Claude can", "Editing includes deleting tasks.", s,
            [(ClaudeAccessLevel.Read, "Read"), (ClaudeAccessLevel.ReadWrite, "Read & edit")],
            x => x.ClaudeAccessLevel, (x, v) => x with { ClaudeAccessLevel = v });
        _claudeConnectRow = new ButtonsRow("claudeConnect", "Connect Claude", _mcpExePath,
            [new RowButton("Copy for Claude Desktop", CopyForDesktopCommand), new RowButton("Copy for Claude Code", CopyForCodeCommand)])
        {
            MonoDescription = true,
        };
        if (!File.Exists(_mcpExePath))
        {
            ShowClaudeMessage(McpMissing, isError: true);
        }

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
                // Only the setting is written here: AppLifecycle reconciles the Run entry on every change of it.
                new SwitchRow("startup", "Launch at startup", "Open Doba when Windows starts.", s,
                    x => x.LaunchAtStartup, (x, v) => x with { LaunchAtStartup = v }),
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
                _claudeRow,
                _claudeLevelRow,
                _claudeConnectRow,
            ],
            [SettingsTab.Shortcuts] = [],
        };

        Rows = _tabs[SettingsTab.Appearance];
        SyncClaudeRows(_settings.Current);
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
    public static string DataFootnote =>
        "Everything is stored locally on this PC. There is no account and no sync between devices yet. " +
        "Claude sees your tasks only while Claude access is on.";

    /// <summary>Data › Claude access, normally.</summary>
    internal const string ClaudeDescription =
        "Let Claude Desktop and Claude Code see your tasks through MCP. Tasks Claude reads are sent to Anthropic.";

    /// <summary>Data › Claude access in demo mode (there is no data folder for Claude to reach).</summary>
    internal const string ClaudeDemoDescription = "Not available in demo mode.";

    /// <summary>Shown under Connect Claude after "Copy for Claude Desktop".</summary>
    internal const string CopiedForDesktop =
        "Copied — add the \"doba\" entry to \"mcpServers\" in Claude Desktop's config (Settings › Developer › Edit Config)";

    /// <summary>Shown under Connect Claude after "Copy for Claude Code" (the command is quoted for PowerShell).</summary>
    internal const string CopiedForCode = "Copied — run it in PowerShell";

    /// <summary>Shown under Connect Claude (and instead of copying) when Doba.Mcp.exe is not beside Doba.exe.</summary>
    internal const string McpMissing = "Doba.Mcp.exe is missing next to Doba.exe, so Claude cannot start it. Reinstall Doba.";

    /// <summary>Shown under Connect Claude when another app holds the clipboard.</summary>
    internal const string CopyFailed = "Could not copy: the clipboard is busy. Try again.";

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

    /// <summary>Takes the Connect Claude texts; the view sets the window's clipboard when it is attached.</summary>
    public ISettingsClipboard? Clipboard { get; set; }

    /// <summary>What "Copy for Claude Desktop" copies (for this Doba.Mcp.exe and this data folder).</summary>
    public string ClaudeDesktopConfig => ClaudeConnect.DesktopConfig(_mcpExePath, DataFolder, BridgeNames.DefaultDataFolder());

    /// <summary>What "Copy for Claude Code" copies.</summary>
    public string ClaudeCodeCommand => ClaudeConnect.CodeCommand(_mcpExePath, DataFolder, BridgeNames.DefaultDataFolder());

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
            ShowTransferMessage(ExportFailure(ex), isError: true);
        }
    }

    /// <summary>
    /// Fixed sentences for a file that could not be written or read. Not the exception's own text: it is written for
    /// developers (and is a bare resource key in a NativeAOT build with UseSystemResourceKeys), and the owner should
    /// read a short sentence.
    /// </summary>
    internal static string ExportFailure(Exception error) => error is UnauthorizedAccessException
        ? "Export failed: access to the file was denied."
        : "Export failed: the file could not be written.";

    /// <inheritdoc cref="ExportFailure"/>
    internal static string ImportFailure(Exception error) => error is UnauthorizedAccessException
        ? "Import failed: access to the file was denied."
        : "Import failed: the file could not be read.";

    /// <summary>Shown when the imported tasks are in memory but could not be written to the database.</summary>
    internal const string ImportNotSaved =
        "Import failed: the tasks could not be saved. They are shown now but will be missing after a restart; details are in " +
        DataLog.FileName + ".";

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
            ShowTransferMessage(ImportFailure(ex), isError: true);
            return false;
        }

        // "Imported" only once the import is on disk: wait for the writer, and report a write that failed meanwhile
        // (SaveFailed comes from the writer's thread; the title-bar notice shows it as well).
        var app = Services.App;
        var failed = 0;
        void OnSaveFailed(object? sender, string message) => Interlocked.Exchange(ref failed, 1);
        app.SaveFailed += OnSaveFailed;
        try
        {
            app.Store.ReplaceAll(data.Tasks, data.Tags);
            // AppLifecycle reconciles the Run entry with "Launch at startup". Claude access is not in an export, and
            // the current values are kept: a file can never turn it on (or off).
            _settings.Update(cur => data.Settings with { ClaudeAccess = cur.ClaudeAccess, ClaudeAccessLevel = cur.ClaudeAccessLevel });
            await app.FlushAsync().ConfigureAwait(true);
        }
        finally
        {
            app.SaveFailed -= OnSaveFailed;
        }

        if (Volatile.Read(ref failed) != 0)
        {
            ShowTransferMessage(ImportNotSaved, isError: true);
            return false;
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
            ShowTransferMessage(ExportFailure(ex), isError: true);
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
            ShowTransferMessage(ImportFailure(ex), isError: true);
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

    [RelayCommand]
    private Task CopyForDesktop() => CopyAsync(ClaudeDesktopConfig, CopiedForDesktop);

    [RelayCommand]
    private Task CopyForCode() => CopyAsync(ClaudeCodeCommand, CopiedForCode);

    private async Task CopyAsync(string text, string copied)
    {
        if (Clipboard is not { } clipboard)
        {
            return;
        }

        if (!File.Exists(_mcpExePath))
        {
            ShowClaudeMessage(McpMissing, isError: true);
            return;
        }

        bool done;
        try
        {
            done = await clipboard.TrySetTextAsync(text).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is TimeoutException or ExternalException)
        {
            // Another app holds the clipboard: Avalonia gives up opening it with a TimeoutException; OLE errors are
            // COMExceptions.
            ShowClaudeMessage(CopyFailed, isError: true);
            return;
        }

        if (done)
        {
            ShowClaudeMessage(copied, isError: false);
        }
    }

    private void ShowClaudeMessage(string message, bool isError)
    {
        _claudeConnectRow.IsError = isError;
        _claudeConnectRow.Message = message;
    }

    private static string Count(int n) => n == 1 ? "1 task" : string.Create(CultureInfo.InvariantCulture, $"{n} tasks");

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

        SyncClaudeRows(settings);
    }

    /// <summary>Level and Connect follow the switch; in demo mode all three are off (no data folder to reach).</summary>
    private void SyncClaudeRows(AppSettings settings)
    {
        var demo = Services.App.IsDemo;
        _claudeRow.IsEnabled = !demo;
        _claudeLevelRow.IsEnabled = !demo && settings.ClaudeAccess;
        _claudeConnectRow.IsEnabled = !demo && settings.ClaudeAccess;
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
