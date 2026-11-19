namespace Doba.Core.Model;

public enum Accent { Mist, Sage, Sand, Gray }

public enum ThemeMode { Dark, System }

public enum Density { Comfortable, Compact }

/// <summary>Screen shown when the app starts ("Open on launch").</summary>
public enum LaunchView { Today, Week, Inbox }

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

    // The sidebar's collapsed state is not a setting: it is kept in memory only (DECISIONS "Sidebar collapsed state").

    public static AppSettings Default { get; } = new();
}
