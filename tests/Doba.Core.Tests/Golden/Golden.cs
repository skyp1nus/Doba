using System.Globalization;
using System.Text.Json.Nodes;
using Doba.Core.Demo;
using Doba.Core.Editing;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using Doba.Core.Store;
using Doba.Core.Time;

namespace Doba.Core.Tests.Golden;

/// <summary>Loads the fixtures written by generate.js and compares JSON trees with a readable first-difference message.</summary>
internal static class Fixtures
{
    private static readonly Dictionary<string, JsonArray> s_cache = [];

    public static JsonArray Load(string file)
    {
        lock (s_cache)
        {
            if (!s_cache.TryGetValue(file, out var a))
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Golden", "fixtures", file);
                a = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
                s_cache[file] = a;
            }

            return a;
        }
    }

    public static JsonObject Case(string file, string name) =>
        Load(file).Select(n => n!.AsObject()).Single(o => (string?)o["name"] == name);

    public static TheoryData<string> Names(string file)
    {
        var data = new TheoryData<string>();
        foreach (var n in Load(file))
        {
            data.Add((string)n!["name"]!);
        }

        return data;
    }

    public static void Equal(JsonNode? expected, JsonNode? actual, string what)
    {
        var diff = FirstDifference(expected, actual, "$");
        Assert.True(diff is null, $"{what}: {diff}");
    }

    private static string? FirstDifference(JsonNode? e, JsonNode? a, string path)
    {
        if (e is null || a is null)
        {
            return e is null && a is null ? null : $"{path}: expected {Show(e)}, got {Show(a)}";
        }

        switch (e)
        {
            case JsonObject eo when a is JsonObject ao:
                foreach (var (k, v) in eo)
                {
                    if (!ao.ContainsKey(k))
                    {
                        return $"{path}.{k}: missing (expected {Show(v)})";
                    }

                    var d = FirstDifference(v, ao[k], path + "." + k);
                    if (d is not null)
                    {
                        return d;
                    }
                }

                foreach (var (k, v) in ao)
                {
                    if (!eo.ContainsKey(k))
                    {
                        return $"{path}.{k}: unexpected {Show(v)}";
                    }
                }

                return null;
            case JsonArray ea when a is JsonArray aa:
                for (var i = 0; i < Math.Min(ea.Count, aa.Count); i++)
                {
                    var d = FirstDifference(ea[i], aa[i], $"{path}[{i}]");
                    if (d is not null)
                    {
                        return d;
                    }
                }

                return ea.Count == aa.Count ? null : $"{path}: expected {ea.Count} items, got {aa.Count}\n expected: {Show(e)}\n actual:   {Show(a)}";
            case JsonValue when a is JsonValue:
                return JsonNode.DeepEquals(e, a) ? null : $"{path}: expected {Show(e)}, got {Show(a)}";
            default:
                return $"{path}: expected {Show(e)}, got {Show(a)}";
        }
    }

    private static string Show(JsonNode? n)
    {
        var s = n?.ToJsonString() ?? "null";
        return s.Length > 600 ? s[..600] + "…" : s;
    }
}

/// <summary>Maps Doba.Core records to the semantic JSON shapes generate.js extracts from the prototype.</summary>
internal static class Shapes
{
    public static DateOnly Date(JsonNode? n) => DateOnly.ParseExact((string)n!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly? OptDate(JsonNode? n) => n is null ? null : Date(n);

    public static TimeOnly Time(JsonNode? n) => TimeOnly.ParseExact((string)n!, "HH:mm", CultureInfo.InvariantCulture);

    public static string Iso(DateOnly d) => DateMath.Iso(d);

    public static string ViewName(AppView v) => v.ToString().ToLowerInvariant();

    public static AppView View(string s) => Enum.Parse<AppView>(s, ignoreCase: true);

    public static int Pct(double p) => DateMath.RoundHalfUp(p * 100);

    public static JsonNode? Target(NavTarget? t) => t is null ? null : new JsonObject { ["view"] = ViewName(t.View), ["anchor"] = Iso(t.Anchor) };

    public static JsonObject Row(TaskRow r) => new()
    {
        ["id"] = r.Id,
        ["title"] = r.Title,
        ["done"] = r.Done,
        ["prio"] = (int)r.Priority,
        ["tag"] = r.TagLabel,
        ["time"] = r.Time,
        ["meta"] = r.Meta,
        ["multi"] = r.IsMultiDay,
        ["dim"] = r.Dimmed,
        ["move"] = r.CanMoveToToday,
    };

    public static JsonArray Rows(IEnumerable<TaskRow> rows) => [.. rows.Select(r => (JsonNode)Row(r))];

    public static JsonObject Span(SpanBar s) => new()
    {
        ["row"] = Row(s.Row),
        ["col"] = s.Column,
        ["span"] = s.ColumnSpan,
        ["lane"] = s.Lane,
        ["clipL"] = s.ClipStart,
        ["clipR"] = s.ClipEnd,
    };

    public static JsonObject Group(TaskGroup g) => new()
    {
        ["key"] = g.Key,
        ["label"] = g.Label,
        ["count"] = g.Count,
        ["tone"] = g.Tone.ToString().ToLowerInvariant(),
        ["link"] = Target(g.Link),
        ["items"] = Rows(g.Items),
    };

    public static JsonArray Blocks(ScheduleModel s) => [.. s.Blocks.Select(b =>
    {
        var h = ScheduleLayout.BlockHeight(b.DurationMinutes, 48);
        return (JsonNode)new JsonObject
        {
            ["row"] = Row(b.Row),
            ["start"] = b.StartMinutes,
            ["dur"] = b.DurationMinutes,
            ["range"] = b.Range,
            ["top"] = (int)ScheduleLayout.BlockTop(b.StartMinutes, 48),
            ["h"] = (int)h,
            ["compact"] = ScheduleLayout.IsCompact(h),
        };
    })];

    public static JsonNode? Now(ScheduleModel s) => s.NowMinutes is { } m
        ? new JsonObject { ["top"] = (int)ScheduleLayout.NowTop(m, 48), ["label"] = s.NowLabel }
        : null;

    public static JsonObject Week(WeekModel w) => new()
    {
        ["weekStart"] = Iso(w.WeekStart),
        ["rows"] = new JsonArray([.. w.Rows.Select(r => (JsonNode)new JsonObject
        {
            ["lanes"] = r.LaneCount,
            ["cells"] = new JsonArray([.. r.Cells.Select(c => (JsonNode)new JsonObject
            {
                ["date"] = Iso(c.Date),
                ["col"] = c.Column,
                ["wd"] = c.Weekday,
                ["num"] = c.DayNumber,
                ["mon"] = c.MonthShort,
                ["today"] = c.IsToday,
                ["preview"] = c.IsPreview,
                ["pct"] = Pct(c.Progress),
                ["more"] = c.MoreCount,
                ["empty"] = c.Empty,
                ["tasks"] = Rows(c.Tasks),
            })]),
            ["spans"] = new JsonArray([.. r.Spans.Select(s => (JsonNode)Span(s))]),
        })]),
    };

    public static JsonObject Month(MonthModel m) => new()
    {
        ["year"] = m.Year,
        ["month"] = m.Month,
        ["wds"] = new JsonArray([.. m.WeekdayLabels.Select(x => (JsonNode)x)]),
        ["weeks"] = new JsonArray([.. m.Weeks.Select(w => (JsonNode)new JsonObject
        {
            ["weekStart"] = Iso(w.WeekStart),
            ["wn"] = w.WeekNumber,
            ["cur"] = w.IsCurrent,
            ["zoom"] = Target(new NavTarget(AppView.Week, w.ZoomTarget)),
            ["h"] = (int)w.RowHeight,
            ["lanes"] = w.LaneCount,
            ["days"] = new JsonArray([.. w.Days.Select(d => (JsonNode)new JsonObject
            {
                ["date"] = Iso(d.Date),
                ["num"] = d.DayNumber,
                ["inMonth"] = d.InMonth,
                ["today"] = d.IsToday,
                ["more"] = d.MoreCount,
                ["items"] = Rows(d.Items),
            })]),
            ["spans"] = new JsonArray([.. w.Spans.Select(s => (JsonNode)Span(s))]),
        })]),
    };

    public static JsonObject Year(YearModel y) => new()
    {
        ["year"] = y.Year,
        ["months"] = new JsonArray([.. y.Months.Select(m => (JsonNode)new JsonObject
        {
            ["month"] = m.Month,
            ["name"] = m.Name,
            ["cur"] = m.IsCurrent,
            ["past"] = m.IsPast,
            ["stat"] = m.Stat,
            ["pct"] = Pct(m.Progress),
            ["more"] = m.MoreCount,
            ["zoom"] = Target(new NavTarget(AppView.Month, m.ZoomTarget)),
            ["rows"] = Rows(m.Rows),
        })]),
    };

    public static JsonObject Day(DayModel d) => new()
    {
        ["date"] = Iso(d.Date),
        ["progress"] = d.Progress,
        ["empty"] = d.Empty,
        ["spans"] = Rows(d.Spans),
        ["items"] = Rows(d.Items),
        ["blocks"] = Blocks(d.Schedule),
        ["now"] = Now(d.Schedule),
    };

    public static JsonObject Inbox(InboxModel m) => new()
    {
        ["left"] = new JsonArray([.. m.Left.Select(g => (JsonNode)Group(g))]),
        ["right"] = new JsonArray([.. m.Right.Select(g => (JsonNode)Group(g))]),
        ["schedCount"] = m.ScheduledCount,
    };

    public static JsonObject Today(TodayModel m) => new()
    {
        ["summary"] = m.Summary,
        ["pct"] = Pct(m.Progress),
        ["groups"] = new JsonArray([.. m.Groups.Select(g => (JsonNode)Group(g))]),
        ["blocks"] = Blocks(m.Schedule),
        ["now"] = Now(m.Schedule),
    };

    public static JsonObject Header(HeaderModel h) => new()
    {
        ["crumbs"] = new JsonArray([.. h.Crumbs.Select(c => (JsonNode)new JsonObject { ["label"] = c.Label, ["target"] = Target(c.Target) })]),
        ["title"] = h.Title,
        ["subtitle"] = h.Subtitle,
    };

    public static JsonObject Sidebar(SidebarModel s) => new()
    {
        ["inbox"] = s.InboxCount,
        ["today"] = s.TodayCount,
        ["tags"] = new JsonArray([.. s.Tags.Select(t => (JsonNode)new JsonObject { ["name"] = t.Name, ["count"] = t.OpenCount, ["active"] = t.Active })]),
    };

    // ---------- dialog ----------

    public static TaskDraft Draft(JsonNode j) => new(
        j["editId"] is { } id ? (long)id : null,
        (string)j["title"]!,
        (string)j["notes"]!,
        OptDate(j["date"]),
        (int)j["days"]!,
        (bool)j["timed"]!,
        Time(j["time"]),
        (int)j["dur"]!,
        (Priority)(int)j["prio"]!,
        (string?)j["tag"],
        DraftPopover.None,
        Date(j["calMonth"]));

    public static JsonObject DraftJson(TaskDraft d) => new()
    {
        ["editId"] = d.EditId,
        ["title"] = d.Title,
        ["notes"] = d.Notes,
        ["date"] = d.Date is { } x ? Iso(x) : null,
        ["days"] = d.Days,
        ["timed"] = d.Timed,
        ["time"] = DateMath.Hhmm(d.Time),
        ["dur"] = d.DurationMinutes,
        ["prio"] = (int)d.Priority,
        ["tag"] = d.Tag,
        ["calMonth"] = Iso(d.CalendarMonth),
    };

    public static JsonObject Labels(TaskDraft d, DateOnly today, DayOfWeek weekStart)
    {
        var (h, m) = TaskDialog.WheelIndices(d.Time);
        return new JsonObject
        {
            ["where"] = TaskDialog.WhereLabel(d, today),
            ["date"] = TaskDialog.DatePillLabel(d, today),
            ["time"] = TaskDialog.TimePillLabel(d),
            ["prio"] = TaskDialog.PriorityPillLabel(d),
            ["tag"] = TaskDialog.TagPillLabel(d),
            ["hasDate"] = d.Date is not null,
            ["showTime"] = d.ShowTime,
            ["dateOpts"] = new JsonArray([.. TaskDialog.DateOptions(d, today, weekStart).Select(o => (JsonNode)new JsonObject
            {
                ["label"] = o.Label,
                ["hint"] = o.Hint,
                ["selected"] = o.Selected,
            })]),
            ["calLabel"] = TaskDialog.CalendarLabel(d),
            ["calDays"] = new JsonArray([.. TaskDialog.MiniCalendar(d, today, weekStart).Select(c => (JsonNode)new JsonObject
            {
                ["date"] = Iso(c.Date),
                ["num"] = c.DayNumber,
                ["selected"] = c.Selected,
                ["marked"] = c.Marked,
                ["band"] = c.InRange,
                ["bandL"] = c.BandStart,
                ["bandR"] = c.BandEnd,
                ["today"] = c.IsToday,
                ["fg"] = c.Marked ? "marked" : c.IsToday ? "today" : c.InMonth ? (c.IsPast ? "past" : "normal") : "out",
                ["past"] = c.IsPast,
            })]),
            ["days"] = TaskDialog.DaysLabel(d),
            ["until"] = TaskDialog.UntilLabel(d),
            ["range"] = TaskDialog.RangeLabel(d),
            ["hour"] = d.Timed ? h : -1,
            ["minute"] = d.Timed ? m : -1,
            ["heading"] = TaskDialog.Heading(d),
            ["cta"] = TaskDialog.CommitLabel(d),
            ["hint"] = TaskDialog.KeyHint(d),
            ["canSave"] = d.CanSave,
        };
    }

    public static JsonObject Created(TaskItem t) => new()
    {
        ["date"] = t.Date is { } d ? Iso(d) : null,
        ["end"] = t.End is { } e ? Iso(e) : null,
        ["title"] = t.Title,
        ["tag"] = t.Tag,
        ["prio"] = (int)t.Priority,
        ["done"] = t.Done,
        ["time"] = t.Time is { } tm ? DateMath.Hhmm(tm) : null,
        ["dur"] = t.DurationMinutes,
        ["notes"] = t.Notes,
    };
}

/// <summary>The demo store shared by the golden tests (read-only use).</summary>
internal static class Demo
{
    public static TaskStore Store() => new(DemoData.Tasks(), DemoData.Tags, NullTaskPersistence.Instance);

    public static ProjectionContext Context(DateOnly today, string? filter = null, DayOfWeek weekStart = DayOfWeek.Monday) =>
        new(today, DemoData.Now, weekStart, filter);
}
