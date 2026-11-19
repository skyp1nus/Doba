namespace Doba.Core.Model;

public enum Accent { Mist, Sage, Sand, Gray }

public enum ThemeMode { Dark, System }

public enum Density { Comfortable, Compact }

/// <summary>Screen shown when the app starts ("Open on launch").</summary>
public enum LaunchView { Today, Week, Inbox }

/// <summary>What Claude may do with the tasks through the MCP server (Settings → Data → "Claude can").</summary>
public enum ClaudeAccessLevel { Read, ReadWrite }

/// <summary>All user settings. Defaults match the design prototype.</summary>
public sealed record AppSettings
{
    public Accent Accent { get; init; } = Accent.Mist;

    /// <summary>0–100. How much of the desktop shows through the sidebar and header (prototype default 72).</summary>
    public int WindowTransparency { get; init; } = 72;

    public ThemeMode Theme { get; init; } = ThemeMode.Dark;

    public Density Density { get; init; } = Density.Comfortable;

    /// <summary>First day of the week: Monday or Sunday.</summary>
    public DayOfWeek WeekStart { get; init; } = DayOfWeek.Monday;

    public LaunchView LaunchView { get; init; } = LaunchView.Today;

    /// <summary>Pre-selected in the New task window.</summary>
    public Priority DefaultPriority { get; init; } = Priority.None;

    /// <summary>Overdue single-day tasks move to Today every morning.</summary>
    public bool CarryOver { get; init; } = true;

    public bool CompletionSound { get; init; }

    public bool LaunchAtStartup { get; init; } = true;

    public bool KeepInTray { get; init; } = true;

    public bool DailyBackup { get; init; } = true;

    /// <summary>Claude Desktop / Claude Code may reach the tasks through Doba.Mcp (the bridge pipe exists only while on).</summary>
    public bool ClaudeAccess { get; init; }

    /// <summary>Read only, or read and edit (which includes deleting). Checked on every request.</summary>
    public ClaudeAccessLevel ClaudeAccessLevel { get; init; } = ClaudeAccessLevel.Read;

    // The sidebar's collapsed state is not a setting: it is kept in memory only (DECISIONS "Sidebar collapsed state").

    public static AppSettings Default { get; } = new();
}
