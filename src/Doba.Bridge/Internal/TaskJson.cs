using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using Doba.Core.Model;
using Doba.Core.Time;

namespace Doba.Bridge.Internal;

/// <summary>Writes the bridge's JSON results with <see cref="Utf8JsonWriter"/> (no reflection).</summary>
internal static class TaskJson
{
    /// <summary>Non-ASCII text (Ukrainian titles) stays readable instead of becoming \uXXXX: the model reads these
    /// results, and every escape costs tokens. Still valid JSON on one line (control characters are escaped).</summary>
    private static readonly JsonWriterOptions s_options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    /// <summary>Builds a JSON value with <paramref name="write"/>.</summary>
    public static JsonElement Build(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, s_options))
        {
            write(writer);
        }

        using var doc = JsonDocument.Parse(buffer.WrittenMemory);
        return doc.RootElement.Clone();
    }

    /// <summary>One task: {id, title, notes, date, end, days, time, duration_minutes, priority, tag, done}, plus
    /// <c>notes_truncated: true</c> when <paramref name="notesLimit"/> cut the notes.</summary>
    public static void WriteTask(Utf8JsonWriter w, TaskItem t, int? notesLimit = null)
    {
        w.WriteStartObject();
        w.WriteNumber("id", t.Id);
        w.WriteString("title", t.Title);
        var notes = t.Notes ?? string.Empty;
        var truncated = false;
        if (notesLimit is { } limit && notes.Length > limit)
        {
            var cut = limit;
            if (cut > 0 && char.IsHighSurrogate(notes[cut - 1]))
            {
                cut--; // never split a surrogate pair
            }

            notes = notes[..cut];
            truncated = true;
        }

        w.WriteString("notes", notes);
        WriteDate(w, "date", t.Date);
        WriteDate(w, "end", t.End);
        w.WriteNumber("days", t is { Date: { } d, End: { } e } ? DateMath.DaysBetween(d, e) + 1 : 1);
        if (t.Time is { } time)
        {
            w.WriteString("time", DateMath.Hhmm(time));
        }
        else
        {
            w.WriteNull("time");
        }

        w.WriteNumber("duration_minutes", t.DurationMinutes);
        w.WriteString("priority", PriorityName(t.Priority));
        if (t.Tag is { } tag)
        {
            w.WriteString("tag", tag);
        }
        else
        {
            w.WriteNull("tag");
        }

        w.WriteBoolean("done", t.Done);
        if (truncated)
        {
            w.WriteBoolean("notes_truncated", true);
        }

        w.WriteEndObject();
    }

    public static void WriteTasks(Utf8JsonWriter w, string name, IEnumerable<TaskItem> tasks, int? notesLimit)
    {
        w.WriteStartArray(name);
        foreach (var t in tasks)
        {
            WriteTask(w, t, notesLimit);
        }

        w.WriteEndArray();
    }

    public static void WriteDate(Utf8JsonWriter w, string name, DateOnly? date)
    {
        if (date is { } d)
        {
            w.WriteString(name, DateMath.Iso(d));
        }
        else
        {
            w.WriteNull(name);
        }
    }

    public static string PriorityName(Priority p) => p switch
    {
        Priority.Low => "low",
        Priority.Medium => "medium",
        Priority.High => "high",
        Priority.Asap => "asap",
        _ => "none",
    };

    public static Priority? ParsePriority(string text) => text switch
    {
        "none" => Priority.None,
        "low" => Priority.Low,
        "medium" => Priority.Medium,
        "high" => Priority.High,
        "asap" => Priority.Asap,
        _ => null,
    };
}
