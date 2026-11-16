using System.Text.Json;
using Doba.Core.Model;
using Doba.Storage.Internal;
using Doba.Storage.Json;

namespace Doba.Storage;

/// <summary>Export / import of all tasks, tags and settings as one JSON file (System.Text.Json source generation).</summary>
/// <remarks>
/// Format: <c>{ "format": "doba", "version": 1, "exportedAt": "…", "tasks": [ … ], "tags": [ … ], "settings": { … } }</c>,
/// camelCase names, dates "yyyy-MM-dd", times "HH:mm", enums as camelCase strings. Only the source-generated
/// <c>DobaJsonContext</c> is used, so there is no reflection path.
/// </remarks>
public static class JsonTransfer
{
    public const string FormatName = "doba";
    public const int FormatVersion = 1;

    public static Task ExportAsync(Stream destination, IReadOnlyList<TaskItem> tasks, IReadOnlyList<string> tags, AppSettings settings, CancellationToken cancellationToken = default) =>
        ExportAsync(destination, tasks, tags, settings, TimeProvider.System, cancellationToken);

    /// <summary>As <see cref="ExportAsync(Stream, IReadOnlyList{TaskItem}, IReadOnlyList{string}, AppSettings, CancellationToken)"/>, with the clock that stamps "exportedAt".</summary>
    public static async Task ExportAsync(Stream destination, IReadOnlyList<TaskItem> tasks, IReadOnlyList<string> tags, AppSettings settings, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);

        var document = new ExportDocument
        {
            Format = FormatName,
            Version = FormatVersion,
            ExportedAt = clock.GetUtcNow(),
            Tasks = tasks.Select(ToDto).ToList(),
            Tags = [.. tags],
            Settings = ToDto(settings),
        };
        await JsonSerializer.SerializeAsync(destination, document, DobaJsonContext.Default.ExportDocument, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Throws <see cref="FormatException"/> for an invalid or unsupported file.</summary>
    /// <remarks>Tasks are returned in id order. Missing optional fields take their defaults (notes "", duration 0, priority none,
    /// not done; missing settings take the app defaults).</remarks>
    public static async Task<LoadedData> ImportAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        JsonDocument json;
        try
        {
            json = await JsonDocument.ParseAsync(source, default, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"The file is not valid JSON: {ex.Message}", ex);
        }

        using (json)
        {
            CheckHeader(json.RootElement);
            ExportDocument? document;
            try
            {
                document = json.Deserialize(DobaJsonContext.Default.ExportDocument);
            }
            catch (JsonException ex)
            {
                // ex.Message names CLR types (System.Nullable`1[...]); the JSON path is what a user can act on.
                string line = ex.LineNumber is { } n ? $" (line {n + 1})" : string.Empty;
                throw new FormatException($"The file is not a valid Doba export: the value at {ex.Path ?? "$"}{line} has the wrong type or is not one of the allowed values.", ex);
            }

            return Validate(document ?? throw new FormatException("The file is not a valid Doba export: it is empty."));
        }
    }

    private static void CheckHeader(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The file is not a Doba export: expected a JSON object at the top level.");
        }

        if (!root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String || format.GetString() != FormatName)
        {
            string found = root.TryGetProperty("format", out var f) ? f.GetRawText() : "missing";
            throw new FormatException($"The file is not a Doba export (\"format\" is {found}, expected \"{FormatName}\").");
        }

        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int v))
        {
            throw new FormatException("The Doba export has no valid \"version\" number.");
        }

        if (v != FormatVersion)
        {
            throw new FormatException($"Unsupported Doba export version {v} (this version of Doba reads version {FormatVersion}).");
        }
    }

    private static LoadedData Validate(ExportDocument document)
    {
        var tasks = new List<TaskItem>(document.Tasks?.Count ?? 0);
        var ids = new HashSet<long>();
        if (document.Tasks is { } dtos)
        {
            for (int i = 0; i < dtos.Count; i++)
            {
                var task = ToTask(dtos[i], i);
                if (!ids.Add(task.Id))
                {
                    throw Invalid(i, $"id {task.Id} is used by more than one task.");
                }

                tasks.Add(task);
            }
        }

        tasks.Sort(static (a, b) => a.Id.CompareTo(b.Id));

        var tags = new List<string>(document.Tags?.Count ?? 0);
        var tagSet = new HashSet<string>(StringComparer.Ordinal);
        if (document.Tags is { } tagDtos)
        {
            for (int i = 0; i < tagDtos.Count; i++)
            {
                string? tag = tagDtos[i];
                if (string.IsNullOrWhiteSpace(tag))
                {
                    throw new FormatException($"Invalid tag at position {i + 1}: a tag name must not be empty.");
                }

                if (!tagSet.Add(tag))
                {
                    throw new FormatException($"Invalid tag at position {i + 1}: \"{tag}\" appears more than once.");
                }

                tags.Add(tag);
            }
        }

        return new LoadedData(tasks, tags, ToSettings(document.Settings));
    }

    private static TaskItem ToTask(ExportTask? dto, int index)
    {
        if (dto is null)
        {
            throw Invalid(index, "the entry is null.");
        }

        if (dto.Id is not { } id || id <= 0)
        {
            throw Invalid(index, "\"id\" must be a positive number.");
        }

        string title = dto.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            throw Invalid(index, "\"title\" must not be empty.");
        }

        DateOnly? date = ParseDate(dto.Date, "date", index);
        DateOnly? end = ParseDate(dto.End, "end", index);
        TimeOnly? time = null;
        if (dto.Time is not null)
        {
            time = Formats.TryParseTime(dto.Time, out var t)
                ? t
                : throw Invalid(index, $"\"time\" is \"{dto.Time}\", expected HH:mm.");
        }

        if (end is not null && date is null)
        {
            throw Invalid(index, "\"end\" is set but \"date\" is not.");
        }

        if (end is { } e && date is { } d && e <= d)
        {
            throw Invalid(index, $"\"end\" ({Formats.FormatDate(e)}) must be after \"date\" ({Formats.FormatDate(d)}).");
        }

        if (time is not null && (date is null || end is not null))
        {
            throw Invalid(index, "\"time\" is only allowed on a single-day task with a date.");
        }

        int duration = dto.DurationMinutes ?? 0;
        if (duration is < 0 or > 24 * 60)
        {
            throw Invalid(index, $"\"durationMinutes\" is {duration}, expected 0 to 1440.");
        }

        var priority = dto.Priority ?? Priority.None;
        if (!Enum.IsDefined(priority))
        {
            throw Invalid(index, "\"priority\" is not a known priority.");
        }

        string? tag = string.IsNullOrWhiteSpace(dto.Tag) ? null : dto.Tag;

        return new TaskItem(id, title, dto.Notes ?? string.Empty, date, end, time, duration, priority, tag, dto.Done ?? false);
    }

    private static DateOnly? ParseDate(string? text, string field, int index)
    {
        if (text is null)
        {
            return null;
        }

        return Formats.TryParseDate(text, out var date)
            ? date
            : throw Invalid(index, $"\"{field}\" is \"{text}\", expected yyyy-MM-dd.");
    }

    private static FormatException Invalid(int index, string problem) =>
        new($"Invalid task at position {index + 1}: {problem}");

    private static AppSettings ToSettings(ExportSettings? dto)
    {
        var d = AppSettings.Default;
        if (dto is null)
        {
            return d;
        }

        int transparency = dto.WindowTransparency ?? d.WindowTransparency;
        if (transparency is < 0 or > 100)
        {
            throw new FormatException($"Invalid settings: \"windowTransparency\" is {transparency}, expected 0 to 100.");
        }

        var weekStart = dto.WeekStart ?? d.WeekStart;
        if (!SettingsMapping.IsValidWeekStart(weekStart))
        {
            throw new FormatException("Invalid settings: \"weekStart\" must be \"monday\" or \"sunday\".");
        }

        return new AppSettings
        {
            Accent = dto.Accent ?? d.Accent,
            WindowTransparency = transparency,
            Theme = dto.Theme ?? d.Theme,
            Density = dto.Density ?? d.Density,
            WeekStart = weekStart,
            LaunchView = dto.LaunchView ?? d.LaunchView,
            DefaultPriority = dto.DefaultPriority ?? d.DefaultPriority,
            CarryOver = dto.CarryOver ?? d.CarryOver,
            CompletionSound = dto.CompletionSound ?? d.CompletionSound,
            LaunchAtStartup = dto.LaunchAtStartup ?? d.LaunchAtStartup,
            KeepInTray = dto.KeepInTray ?? d.KeepInTray,
            DailyBackup = dto.DailyBackup ?? d.DailyBackup,
            SidebarCollapsed = dto.SidebarCollapsed ?? d.SidebarCollapsed,
        };
    }

    private static ExportTask? ToDto(TaskItem task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        Notes = task.Notes,
        Date = task.Date is { } date ? Formats.FormatDate(date) : null,
        End = task.End is { } end ? Formats.FormatDate(end) : null,
        Time = task.Time is { } time ? Formats.FormatTime(time) : null,
        DurationMinutes = task.DurationMinutes,
        Priority = task.Priority,
        Tag = task.Tag,
        Done = task.Done,
    };

    private static ExportSettings ToDto(AppSettings s) => new()
    {
        Accent = s.Accent,
        WindowTransparency = s.WindowTransparency,
        Theme = s.Theme,
        Density = s.Density,
        WeekStart = s.WeekStart,
        LaunchView = s.LaunchView,
        DefaultPriority = s.DefaultPriority,
        CarryOver = s.CarryOver,
        CompletionSound = s.CompletionSound,
        LaunchAtStartup = s.LaunchAtStartup,
        KeepInTray = s.KeepInTray,
        DailyBackup = s.DailyBackup,
        SidebarCollapsed = s.SidebarCollapsed,
    };
}
