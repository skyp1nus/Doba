using System.Reflection;
using System.Text;
using System.Text.Json;
using Doba.Core.Model;
using Doba.Storage.Json;
using static Doba.Storage.Tests.Samples;

namespace Doba.Storage.Tests;

public sealed class JsonTransferTests
{
    private static readonly DateTimeOffset ExportTime = new(2026, 11, 15, 20, 26, 0, TimeSpan.Zero);

    private static async Task<byte[]> Export(IReadOnlyList<TaskItem> tasks, IReadOnlyList<string> tags, AppSettings settings)
    {
        using var stream = new MemoryStream();
        await JsonTransfer.ExportAsync(stream, tasks, tags, settings, new TestClock(ExportTime), Ct);
        return stream.ToArray();
    }

    private static Task<LoadedData> Import(string json) => Import(Encoding.UTF8.GetBytes(json));

    private static async Task<LoadedData> Import(byte[] json)
    {
        using var stream = new MemoryStream(json);
        return await JsonTransfer.ImportAsync(stream, Ct);
    }

    [Fact]
    public async Task Export_then_import_is_lossless()
    {
        var tasks = AllCombinations();
        string[] tags = ["work", "deep-work", "personal"];
        var settings = NonDefaultSettings();

        var imported = await Import(await Export(tasks, tags, settings));

        Assert.Equal(tasks, imported.Tasks);
        Assert.Equal(tags, imported.Tags);
        Assert.Equal(settings, imported.Settings);
    }

    [Fact]
    public async Task Default_settings_and_empty_data_round_trip()
    {
        var imported = await Import(await Export([], [], AppSettings.Default));
        Assert.Empty(imported.Tasks);
        Assert.Empty(imported.Tags);
        Assert.Equal(AppSettings.Default, imported.Settings);
    }

    [Fact]
    public async Task Export_writes_the_documented_shape()
    {
        var task = new TaskItem(3, "Standup", "notes", new DateOnly(2026, 11, 16), null, new TimeOnly(9, 5), 15, Priority.Asap, "work", true);
        byte[] bytes = await Export([task], ["work"], NonDefaultSettings());
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;

        Assert.Equal(["format", "version", "exportedAt", "tasks", "tags", "settings"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal("doba", root.GetProperty("format").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(ExportTime, root.GetProperty("exportedAt").GetDateTimeOffset());

        var t = root.GetProperty("tasks")[0];
        Assert.Equal(3, t.GetProperty("id").GetInt64());
        Assert.Equal("Standup", t.GetProperty("title").GetString());
        Assert.Equal("2026-11-16", t.GetProperty("date").GetString());
        Assert.Equal(JsonValueKind.Null, t.GetProperty("end").ValueKind);
        Assert.Equal("09:05", t.GetProperty("time").GetString());
        Assert.Equal(15, t.GetProperty("durationMinutes").GetInt32());
        Assert.Equal("asap", t.GetProperty("priority").GetString());
        Assert.Equal("work", t.GetProperty("tag").GetString());
        Assert.True(t.GetProperty("done").GetBoolean());

        Assert.Equal("work", root.GetProperty("tags")[0].GetString());
        var s = root.GetProperty("settings");
        Assert.Equal("gray", s.GetProperty("accent").GetString());
        Assert.Equal("sunday", s.GetProperty("weekStart").GetString());
        Assert.Equal("system", s.GetProperty("theme").GetString());
        Assert.Equal(35, s.GetProperty("windowTransparency").GetInt32());
        Assert.Equal("high", s.GetProperty("defaultPriority").GetString());
        Assert.True(s.GetProperty("sidebarCollapsed").GetBoolean());
    }

    [Fact]
    public void Export_settings_dto_covers_every_settings_property()
    {
        var expected = typeof(AppSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(p => p.Name).Order();
        var actual = typeof(ExportSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(p => p.Name).Order();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Import_is_lenient_about_optional_and_unknown_fields()
    {
        var data = await Import("""
            {
              "format": "doba", "version": 1, "comment": "unknown fields are ignored",
              "tasks": [ { "id": 5, "title": "  Minimal  ", "tag": "" }, { "id": 2, "title": "Other", "priority": "LOW", "futureField": [1, 2] } ],
              "settings": { "accent": "sage" }
            }
            """);

        Assert.Equal(
            [
                new TaskItem(2, "Other", "", null, null, null, 0, Priority.Low, null, false),
                new TaskItem(5, "Minimal", "", null, null, null, 0, Priority.None, null, false),
            ],
            data.Tasks);
        Assert.Empty(data.Tags);
        Assert.Equal(AppSettings.Default with { Accent = Accent.Sage }, data.Settings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("hello")]
    [InlineData("""{ "format": "doba", "version": 1, "tasks": [ """)]
    public async Task Invalid_json_throws_FormatException(string json)
    {
        var ex = await Assert.ThrowsAsync<FormatException>(() => Import(json));
        Assert.Contains("not valid JSON", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[]", "top level")]
    [InlineData("null", "top level")]
    [InlineData("""{ "version": 1, "tasks": [] }""", "not a Doba export")]
    [InlineData("""{ "format": "todo", "version": 1 }""", "not a Doba export")]
    [InlineData("""{ "format": 1, "version": 1 }""", "not a Doba export")]
    [InlineData("""{ "format": "doba" }""", "version")]
    [InlineData("""{ "format": "doba", "version": "1" }""", "version")]
    [InlineData("""{ "format": "doba", "version": 2 }""", "Unsupported Doba export version 2")]
    public async Task Wrong_format_or_version_throws_FormatException(string json, string expected)
    {
        var ex = await Assert.ThrowsAsync<FormatException>(() => Import(json));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "title": "no id" }""", "\"id\"")]
    [InlineData("""{ "id": 0, "title": "zero id" }""", "\"id\"")]
    [InlineData("""{ "id": 1 }""", "\"title\" must not be empty")]
    [InlineData("""{ "id": 1, "title": "   " }""", "\"title\" must not be empty")]
    [InlineData("""{ "id": 1, "title": "x", "date": "2026-13-01" }""", "\"date\" is \"2026-13-01\"")]
    [InlineData("""{ "id": 1, "title": "x", "date": "15.11.2026" }""", "expected yyyy-MM-dd")]
    [InlineData("""{ "id": 1, "title": "x", "date": "2026-11-15", "end": "2026-11-15" }""", "must be after")]
    [InlineData("""{ "id": 1, "title": "x", "date": "2026-11-15", "end": "2026-11-10" }""", "must be after")]
    [InlineData("""{ "id": 1, "title": "x", "end": "2026-11-15" }""", "\"end\" is set but \"date\" is not")]
    [InlineData("""{ "id": 1, "title": "x", "time": "09:00" }""", "\"time\" is only allowed")]
    [InlineData("""{ "id": 1, "title": "x", "date": "2026-11-15", "end": "2026-11-17", "time": "09:00" }""", "\"time\" is only allowed")]
    [InlineData("""{ "id": 1, "title": "x", "date": "2026-11-15", "time": "9am" }""", "expected HH:mm")]
    [InlineData("""{ "id": 1, "title": "x", "date": "2026-11-15", "time": "25:00" }""", "expected HH:mm")]
    [InlineData("""{ "id": 1, "title": "x", "durationMinutes": -5 }""", "durationMinutes")]
    [InlineData("""{ "id": 1, "title": "x", "durationMinutes": 5000 }""", "durationMinutes")]
    [InlineData("""{ "id": 1, "title": "x", "priority": "urgent" }""", "priority")]
    [InlineData("""{ "id": 1, "title": "x", "priority": 3 }""", "priority")]
    [InlineData("""{ "id": 1, "title": "x", "done": "yes" }""", "done")]
    [InlineData("""{ "id": "1", "title": "x" }""", "id")]
    [InlineData("""null""", "null")]
    public async Task Bad_task_fields_throw_FormatException(string task, string expected)
    {
        var ex = await Assert.ThrowsAsync<FormatException>(() => Import($$"""{ "format": "doba", "version": 1, "tasks": [ {{task}} ] }"""));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "format": "doba", "version": 1, "tasks": [ { "id": 1, "title": "a" }, { "id": 1, "title": "b" } ] }""", "more than one task")]
    [InlineData("""{ "format": "doba", "version": 1, "tasks": "none" }""", "tasks")]
    [InlineData("""{ "format": "doba", "version": 1, "tags": [ "a", "a" ] }""", "appears more than once")]
    [InlineData("""{ "format": "doba", "version": 1, "tags": [ "" ] }""", "must not be empty")]
    [InlineData("""{ "format": "doba", "version": 1, "tags": [ null ] }""", "must not be empty")]
    [InlineData("""{ "format": "doba", "version": 1, "settings": { "windowTransparency": 101 } }""", "windowTransparency")]
    [InlineData("""{ "format": "doba", "version": 1, "settings": { "weekStart": "wednesday" } }""", "weekStart")]
    [InlineData("""{ "format": "doba", "version": 1, "settings": { "accent": "pink" } }""", "accent")]
    [InlineData("""{ "format": "doba", "version": 1, "settings": { "carryOver": 1 } }""", "carryOver")]
    [InlineData("""{ "format": "doba", "version": 1, "exportedAt": "yesterday" }""", "exportedAt")]
    public async Task Bad_document_fields_throw_FormatException(string json, string expected)
    {
        var ex = await Assert.ThrowsAsync<FormatException>(() => Import(json));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_import_through_the_database()
    {
        using var temp = new TempFolder();
        var tasks = AllCombinations();
        await using (var db = DobaDatabase.Open(temp.Location))
        {
            foreach (var task in tasks)
            {
                db.Tasks.Upsert(task);
            }

            db.Tasks.SaveTags(["work", "deep-work"]);
            db.Settings.Save(NonDefaultSettings());
        }

        byte[] file;
        await using (var db = DobaDatabase.Open(temp.Location))
        {
            var data = db.Load();
            file = await Export(data.Tasks, data.Tags, data.Settings);
        }

        var imported = await Import(file);
        var other = new DataLocation(Path.Combine(temp.Root, "Other"));
        await using (var db = DobaDatabase.Open(other))
        {
            db.Tasks.ReplaceAll(imported.Tasks, imported.Tags);
            db.Settings.Save(imported.Settings);
        }

        await using (var db = DobaDatabase.Open(other))
        {
            var data = db.Load();
            Assert.Equal(tasks, data.Tasks);
            Assert.Equal(["work", "deep-work"], data.Tags);
            Assert.Equal(NonDefaultSettings(), data.Settings);
        }
    }
}
