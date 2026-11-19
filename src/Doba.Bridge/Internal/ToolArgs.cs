using System.Globalization;
using System.Text.Json;
using Doba.Core.Editing;
using Doba.Core.Model;

namespace Doba.Bridge.Internal;

/// <summary>
/// Strict reader of one call's arguments (the JSON object the MCP client sent), checked against the tool's schema in
/// <see cref="ToolCatalog"/>: unknown names, JSON null, wrong JSON types and malformed dates/times are errors, never
/// coerced. The first problem found is kept in <see cref="Error"/>; the getters return null after it.
/// </summary>
internal sealed class ToolArgs
{
    private static readonly Dictionary<string, Shape> s_shapes = BuildShapes();

    /// <summary>Fields update_task can clear (and so the hint for a null value of one of them).</summary>
    private static readonly string[] s_clearable = ["notes", "time", "tag", "date"];

    private readonly ToolDefinition _tool;
    private readonly Dictionary<string, JsonElement> _values;

    private ToolArgs(ToolDefinition tool, Dictionary<string, JsonElement> values)
    {
        _tool = tool;
        _values = values;
    }

    /// <summary>The first problem, as a sentence for the model; null while all is well.</summary>
    public string? Error { get; private set; }

    /// <summary>Reads <paramref name="args"/> for <paramref name="tool"/>: checks names, nulls and required arguments
    /// up front (<see cref="Error"/> is set when one is wrong).</summary>
    public static ToolArgs Read(ToolDefinition tool, Dictionary<string, JsonElement>? args)
    {
        var reader = new ToolArgs(tool, args ?? []);
        reader.CheckNames();
        return reader;
    }

    /// <summary>The argument names <paramref name="tool"/> accepts, in schema order.</summary>
    public static IReadOnlyList<string> AllowedNames(ToolDefinition tool) => s_shapes[tool.Name].Names;

    public bool Has(string name) => Error is null && _values.ContainsKey(name);

    public string? GetString(string name)
    {
        if (!TryGet(name, out var e))
        {
            return null;
        }

        if (e.ValueKind != JsonValueKind.String)
        {
            return Fail($"'{name}' must be a string.");
        }

        return e.GetString();
    }

    /// <summary>A whole number in [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public long? GetInteger(string name, long min, long max)
    {
        if (!TryGet(name, out var e))
        {
            return null;
        }

        if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt64(out var value))
        {
            return FailNull<long>($"'{name}' must be a whole number{(max == long.MaxValue ? ", " : " ")}{Range(min, max)}.");
        }

        if (value < min || value > max)
        {
            return FailNull<long>($"'{name}' must be {Range(min, max)}; got {value.ToString(CultureInfo.InvariantCulture)}.");
        }

        return value;
    }

    public bool? GetBoolean(string name)
    {
        if (!TryGet(name, out var e))
        {
            return null;
        }

        return e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => FailNull<bool>($"'{name}' must be true or false."),
        };
    }

    /// <summary>A day as YYYY-MM-DD.</summary>
    public DateOnly? GetDate(string name)
    {
        var text = GetString(name);
        if (text is null)
        {
            return null;
        }

        return ParseDate(text) ?? FailNull<DateOnly>($"'{name}' must be a date as YYYY-MM-DD (e.g. 2026-11-20); got '{text}'.");
    }

    /// <summary>A time of day as HH:mm (24-hour).</summary>
    public TimeOnly? GetTime(string name)
    {
        var text = GetString(name);
        if (text is null)
        {
            return null;
        }

        // By hand: TimeOnly.TryParseExact("HH:mm") takes "24:00" as midnight.
        if (text is [var h1 and >= '0' and <= '2', var h2 and >= '0' and <= '9', ':', var m1 and >= '0' and <= '5', var m2 and >= '0' and <= '9'])
        {
            var hour = (h1 - '0') * 10 + (h2 - '0');
            if (hour < 24)
            {
                return new TimeOnly(hour, (m1 - '0') * 10 + (m2 - '0'));
            }
        }

        return FailNull<TimeOnly>($"'{name}' must be a time as HH:mm, 24-hour (e.g. 09:30); got '{text}'.");
    }

    public Priority? GetPriority(string name)
    {
        var text = GetString(name);
        if (text is null)
        {
            return null;
        }

        return TaskJson.ParsePriority(text)
            ?? FailNull<Priority>($"'{name}' must be one of none, low, medium, high, asap; got '{text}'.");
    }

    /// <summary>update_task's <c>clear</c>: an array of field names.</summary>
    public TaskClear GetClear(string name)
    {
        if (!TryGet(name, out var e))
        {
            return TaskClear.None;
        }

        if (e.ValueKind != JsonValueKind.Array)
        {
            Fail($"'{name}' must be an array of field names: notes, time, tag, date.");
            return TaskClear.None;
        }

        var clear = TaskClear.None;
        foreach (var item in e.EnumerateArray())
        {
            var field = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            TaskClear flag = field switch
            {
                "notes" => TaskClear.Notes,
                "time" => TaskClear.Time,
                "tag" => TaskClear.Tag,
                "date" => TaskClear.Date,
                _ => TaskClear.None,
            };
            if (flag == TaskClear.None)
            {
                Fail($"'{name}' may only list notes, time, tag and date; got {item.GetRawText()}.");
                return TaskClear.None;
            }

            clear |= flag;
        }

        return clear;
    }

    internal static DateOnly? ParseDate(string text) =>
        text.Length == 10 && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private void CheckNames()
    {
        var shape = s_shapes[_tool.Name];
        var unknown = _values.Keys.Where(name => Array.IndexOf(shape.Names, name) < 0).Select(name => "'" + name + "'").ToList();
        if (unknown.Count > 0)
        {
            var list = string.Join(", ", unknown);
            Error = shape.Names.Length == 0
                ? $"{_tool.Name} takes no arguments; got {list}."
                : $"Unknown argument{(unknown.Count == 1 ? "" : "s")} {list} for {_tool.Name}. Allowed arguments: {string.Join(", ", shape.Names)}.";
            return;
        }

        foreach (var (name, value) in _values)
        {
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                Error = _tool.Name == ToolCatalog.UpdateTask && Array.IndexOf(s_clearable, name) >= 0
                    ? $"'{name}' must not be null. To clear it use clear: [\"{name}\"]; to keep it, leave '{name}' out."
                    : $"'{name}' must not be null; leave it out instead.";
                return;
            }
        }

        foreach (var name in shape.Required)
        {
            if (!_values.ContainsKey(name))
            {
                Error = $"'{name}' is required.";
                return;
            }
        }
    }

    private bool TryGet(string name, out JsonElement value)
    {
        value = default;
        return Error is null && _values.TryGetValue(name, out value);
    }

    private string? Fail(string error)
    {
        Error ??= error;
        return null;
    }

    private T? FailNull<T>(string error)
        where T : struct
    {
        Error ??= error;
        return null;
    }

    private static string Range(long min, long max) =>
        max == long.MaxValue
            ? "at least " + min.ToString(CultureInfo.InvariantCulture)
            : $"from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}";

    private static Dictionary<string, Shape> BuildShapes()
    {
        var shapes = new Dictionary<string, Shape>(StringComparer.Ordinal);
        foreach (var tool in ToolCatalog.All)
        {
            using var schema = JsonDocument.Parse(tool.InputSchemaJson);
            var names = schema.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray();
            var required = schema.RootElement.TryGetProperty("required", out var r)
                ? r.EnumerateArray().Select(n => n.GetString()!).ToArray()
                : [];
            shapes.Add(tool.Name, new Shape(names, required));
        }

        return shapes;
    }

    private sealed record Shape(string[] Names, string[] Required);
}
