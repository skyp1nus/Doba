using System.Globalization;
using Doba.Core.Model;

namespace Doba.Storage.Internal;

/// <summary>
/// Hand-written mapping of <see cref="AppSettings"/> to key/value strings (one row per property in the settings table).
/// Reading is lenient: unknown keys are ignored, missing or unparsable values fall back to the default.
/// </summary>
internal static class SettingsMapping
{
    public const string Accent = "accent";
    public const string WindowTransparency = "windowTransparency";
    public const string Theme = "theme";
    public const string Density = "density";
    public const string WeekStart = "weekStart";
    public const string LaunchView = "launchView";
    public const string DefaultPriority = "defaultPriority";
    public const string CarryOver = "carryOver";
    public const string CompletionSound = "completionSound";
    public const string LaunchAtStartup = "launchAtStartup";
    public const string KeepInTray = "keepInTray";
    public const string DailyBackup = "dailyBackup";
    public const string ClaudeAccess = "claudeAccess";
    public const string ClaudeAccessLevel = "claudeAccessLevel";

    /// <summary>Every key written by <see cref="ToPairs"/>, one per <see cref="AppSettings"/> property.</summary>
    /// <remarks>A "sidebarCollapsed" row written by earlier builds is an unknown key now and ignored.</remarks>
    public static IReadOnlyList<string> Keys { get; } =
    [
        Accent, WindowTransparency, Theme, Density, WeekStart, LaunchView, DefaultPriority,
        CarryOver, CompletionSound, LaunchAtStartup, KeepInTray, DailyBackup, ClaudeAccess, ClaudeAccessLevel,
    ];

    public static KeyValuePair<string, string>[] ToPairs(AppSettings s) =>
    [
        new(Accent, s.Accent.ToString()),
        new(WindowTransparency, s.WindowTransparency.ToString(CultureInfo.InvariantCulture)),
        new(Theme, s.Theme.ToString()),
        new(Density, s.Density.ToString()),
        new(WeekStart, s.WeekStart.ToString()),
        new(LaunchView, s.LaunchView.ToString()),
        new(DefaultPriority, s.DefaultPriority.ToString()),
        new(CarryOver, FormatBool(s.CarryOver)),
        new(CompletionSound, FormatBool(s.CompletionSound)),
        new(LaunchAtStartup, FormatBool(s.LaunchAtStartup)),
        new(KeepInTray, FormatBool(s.KeepInTray)),
        new(DailyBackup, FormatBool(s.DailyBackup)),
        new(ClaudeAccess, FormatBool(s.ClaudeAccess)),
        new(ClaudeAccessLevel, s.ClaudeAccessLevel.ToString()),
    ];

    public static AppSettings FromPairs(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var s = AppSettings.Default;
        foreach (var (key, value) in pairs)
        {
            s = key switch
            {
                Accent when TryParseEnum(value, out Accent v) => s with { Accent = v },
                WindowTransparency when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                    => s with { WindowTransparency = Math.Clamp(v, 0, 100) },
                Theme when TryParseEnum(value, out ThemeMode v) => s with { Theme = v },
                Density when TryParseEnum(value, out Density v) => s with { Density = v },
                WeekStart when TryParseEnum(value, out DayOfWeek v) && IsValidWeekStart(v) => s with { WeekStart = v },
                LaunchView when TryParseEnum(value, out LaunchView v) => s with { LaunchView = v },
                DefaultPriority when TryParseEnum(value, out Priority v) => s with { DefaultPriority = v },
                CarryOver when bool.TryParse(value, out bool v) => s with { CarryOver = v },
                CompletionSound when bool.TryParse(value, out bool v) => s with { CompletionSound = v },
                LaunchAtStartup when bool.TryParse(value, out bool v) => s with { LaunchAtStartup = v },
                KeepInTray when bool.TryParse(value, out bool v) => s with { KeepInTray = v },
                DailyBackup when bool.TryParse(value, out bool v) => s with { DailyBackup = v },
                ClaudeAccess when bool.TryParse(value, out bool v) => s with { ClaudeAccess = v },
                ClaudeAccessLevel when TryParseEnum(value, out ClaudeAccessLevel v) => s with { ClaudeAccessLevel = v },
                _ => s,
            };
        }

        return s;
    }

    public static bool IsValidWeekStart(DayOfWeek day) => day is DayOfWeek.Monday or DayOfWeek.Sunday;

    private static string FormatBool(bool value) => value ? "true" : "false";

    // Generic Enum.TryParse/IsDefined are AOT-safe (no reflection over arbitrary types). IsDefined rejects numeric
    // strings that are out of range.
    private static bool TryParseEnum<T>(string value, out T result) where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result);
}
