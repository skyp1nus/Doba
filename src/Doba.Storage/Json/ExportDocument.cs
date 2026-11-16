using System.Text.Json;
using System.Text.Json.Serialization;
using Doba.Core.Model;

namespace Doba.Storage.Json;

// Wire shapes of the export file. Everything is nullable so that import can report missing fields itself;
// dates and times are strings ("yyyy-MM-dd", "HH:mm") so that the validator can name the bad value.

internal sealed class ExportDocument
{
    public string? Format { get; set; }

    public int? Version { get; set; }

    public DateTimeOffset? ExportedAt { get; set; }

    public List<ExportTask?>? Tasks { get; set; }

    public List<string?>? Tags { get; set; }

    public ExportSettings? Settings { get; set; }
}

internal sealed class ExportTask
{
    public long? Id { get; set; }

    public string? Title { get; set; }

    public string? Notes { get; set; }

    public string? Date { get; set; }

    public string? End { get; set; }

    public string? Time { get; set; }

    public int? DurationMinutes { get; set; }

    public Priority? Priority { get; set; }

    public string? Tag { get; set; }

    public bool? Done { get; set; }
}

/// <summary>One property per <see cref="AppSettings"/> property (a test keeps them in sync).</summary>
internal sealed class ExportSettings
{
    public Accent? Accent { get; set; }

    public int? WindowTransparency { get; set; }

    public ThemeMode? Theme { get; set; }

    public Density? Density { get; set; }

    public DayOfWeek? WeekStart { get; set; }

    public LaunchView? LaunchView { get; set; }

    public Priority? DefaultPriority { get; set; }

    public bool? CarryOver { get; set; }

    public bool? CompletionSound { get; set; }

    public bool? LaunchAtStartup { get; set; }

    public bool? KeepInTray { get; set; }

    public bool? DailyBackup { get; set; }

    public bool? SidebarCollapsed { get; set; }
}

/// <summary>Enums as camelCase strings ("asap", "monday"); numbers are rejected.</summary>
internal sealed class CamelCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    where TEnum : struct, Enum;

/// <summary>Source-generated metadata; the only path through which export/import touches System.Text.Json.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    NumberHandling = JsonNumberHandling.Strict,
    Converters =
    [
        typeof(CamelCaseEnumConverter<Priority>),
        typeof(CamelCaseEnumConverter<Accent>),
        typeof(CamelCaseEnumConverter<ThemeMode>),
        typeof(CamelCaseEnumConverter<Density>),
        typeof(CamelCaseEnumConverter<DayOfWeek>),
        typeof(CamelCaseEnumConverter<LaunchView>),
    ])]
[JsonSerializable(typeof(ExportDocument))]
internal sealed partial class DobaJsonContext : JsonSerializerContext;
