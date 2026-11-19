using System.Text.Json.Nodes;
using Doba.Core.Demo;
using Doba.Core.Editing;
using Doba.Core.Model;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using Doba.Core.Store;
using Doba.Core.Time;

namespace Doba.Core.Tests.Golden;

/// <summary>Doba.Core against the prototype's own JavaScript (fixtures from generate.js).</summary>
public class GoldenTests
{
    public static TheoryData<string> ScreenCases => Fixtures.Names("screens.json");

    public static TheoryData<string> DialogCases => Fixtures.Names("dialog.json");

    public static TheoryData<string> NavigationCases => Fixtures.Names("navigation.json");

    public static TheoryData<string> ScheduleCases => Fixtures.Names("schedule.json");

    [Fact]
    public void Seed_matches_prototype()
    {
        var expected = Fixtures.Load("seed.json");
        var actual = new JsonArray([.. DemoData.Tasks().Select(t => (JsonNode)new JsonObject
        {
            ["id"] = t.Id,
            ["date"] = t.Date is { } d ? Shapes.Iso(d) : null,
            ["end"] = t.End is { } e ? Shapes.Iso(e) : null,
            ["title"] = t.Title,
            ["tag"] = t.Tag,
            ["done"] = t.Done,
            ["time"] = t.Time is { } tm ? DateMath.Hhmm(tm) : null,
            ["dur"] = t.DurationMinutes,
            ["prio"] = (int)t.Priority,
        })]);
        Fixtures.Equal(expected, actual, "seed");
        Assert.All(DemoData.Tasks(), t => Assert.NotNull(t.Notes));
    }

    [Theory]
    [MemberData(nameof(ScreenCases))]
    public void Screen_matches_prototype(string name)
    {
        var c = Fixtures.Case("screens.json", name);
        var view = Shapes.View((string)c["view"]!);
        var anchor = Shapes.Date(c["anchor"]);
        var today = Shapes.Date(c["today"]);
        Assert.Equal("22:26", (string?)c["now"]);
        var ctx = Demo.Context(today, (string?)c["filter"]);
        var store = Demo.Store();
        var tab = Enum.Parse<SettingsTab>((string)c["tab"]!);

        Fixtures.Equal(c["header"], Shapes.Header(HeaderProjection.Build(new NavState(view, anchor, tab), today, DayOfWeek.Monday)), name + " header");
        Fixtures.Equal(c["sidebar"], Shapes.Sidebar(Projector.Sidebar(store, ctx)), name + " sidebar");

        switch (view)
        {
            case AppView.Week:
                Fixtures.Equal(c["week"], Shapes.Week(Projector.Week(store, ctx, anchor, WeekMetrics.Design())), name);
                break;
            case AppView.Month:
                Fixtures.Equal(c["month"], Shapes.Month(Projector.Month(store, ctx, anchor, new MonthMetrics())), name);
                break;
            case AppView.Year:
                Fixtures.Equal(c["year"], Shapes.Year(Projector.Year(store, ctx, anchor.Year)), name);
                break;
            case AppView.Day:
                Fixtures.Equal(c["day"], Shapes.Day(Projector.Day(store, ctx, anchor)), name);
                break;
            case AppView.Inbox:
                Fixtures.Equal(c["inbox"], Shapes.Inbox(Projector.Inbox(store, ctx)), name);
                break;
            case AppView.Today:
                Fixtures.Equal(c["todayView"], Shapes.Today(Projector.Today(store, ctx)), name);
                break;
            case AppView.Settings:
                break;
            default:
                Assert.Fail("unknown view " + view);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(DialogCases))]
    public void Dialog_matches_prototype(string name)
    {
        var c = Fixtures.Case("dialog.json", name);
        var today = Shapes.Date(c["today"]);
        var draft = Shapes.Draft(c["draft"]!);
        var tasks = DemoData.Tasks();

        // How the dialog was opened.
        if (c["open"] is JsonObject open)
        {
            var defPrio = (Priority)(int)open["defPrio"]!;
            var opened = (string?)open["date"] == "view"
                ? TaskDialog.NewDraft(Shapes.View((string)open["view"]!), Shapes.Date(open["anchor"]), today, defPrio)
                : TaskDialog.NewDraft(Shapes.OptDate(open["date"]), today, defPrio);
            Fixtures.Equal(c["draft"], Shapes.DraftJson(opened), name + " draft");
        }

        TaskItem? existing = null;
        if (c["editTask"] is { } editId)
        {
            existing = tasks.Single(t => t.Id == (long)editId);
            Fixtures.Equal(c["draft"], Shapes.DraftJson(TaskDialog.EditDraft(existing, today)), name + " draft");
        }

        Fixtures.Equal(c["labels"], Shapes.Labels(draft, today, DayOfWeek.Monday), name + " labels");

        foreach (var w in c["wheel"]!.AsArray())
        {
            var committed = TaskDialog.CommitWheel(draft, (bool)w!["hour"]!, (int)w["index"]!);
            Assert.Equal((string?)w["time"], DateMath.Hhmm(committed.Time));
            Assert.Equal((bool)w["timed"]!, committed.Timed);
        }

        if (c["created"] is { } created)
        {
            existing ??= draft.EditId is { } id ? tasks.Single(t => t.Id == id) : null;
            Fixtures.Equal(created, Shapes.Created(TaskDialog.ToTask(draft, existing)), name + " created");
        }
        else
        {
            Assert.False(draft.CanSave);
        }
    }

    [Theory]
    [MemberData(nameof(NavigationCases))]
    public void Navigation_matches_prototype(string name)
    {
        var c = Fixtures.Case("navigation.json", name);
        var today = Shapes.Date(c["today"]);
        var start = c["start"]!;
        var s = new NavState(Shapes.View((string)start["view"]!), Shapes.Date(start["anchor"]), Enum.Parse<SettingsTab>((string)start["tab"]!));
        var i = 0;
        foreach (var step in c["steps"]!.AsArray())
        {
            var cmd = (string)step!["cmd"]!;
            var args = step["args"]!.AsArray();
            var r = cmd switch
            {
                "zoom" => Navigator.Zoom(s, Shapes.View((string)args[0]!), Shapes.Date(args[1])),
                "shift" => Navigator.Shift(s, (int)args[0]!, today),
                "goToday" => Navigator.GoToday(s, today),
                "openInbox" => Navigator.GoSection(s, AppView.Inbox, s.Anchor),
                "goTodayDay" => Navigator.GoSection(s, AppView.Today, today),
                "section" => Navigator.GoSection(s, Shapes.View((string)args[0]!), Shapes.Date(args[1])),
                "goCalendar" => Navigator.GoCalendar(s),
                "goSettings" => Navigator.OpenSettings(s),
                "backFromSettings" => Navigator.BackFromSettings(s, today),
                "setTab" => Navigator.SetTab(s, Enum.Parse<SettingsTab>((string)args[0]!)),
                _ => throw new InvalidOperationException(cmd),
            };
            var expectedKind = (string)step["kind"]! switch
            {
                "in" => TransitionKind.ZoomIn,
                "out" => TransitionKind.ZoomOut,
                "next" => TransitionKind.Next,
                "prev" => TransitionKind.Prev,
                "up" => TransitionKind.SectionDown,
                "down" => TransitionKind.SectionUp,
                "settings-in" => TransitionKind.SettingsIn,
                "settings-out" => TransitionKind.SettingsOut,
                _ => TransitionKind.None,
            };
            var after = step["after"]!;
            var where = $"{name} step {i} {cmd} {args.ToJsonString()}";
            Assert.True(expectedKind == r.Transition, $"{where}: expected {expectedKind}, got {r.Transition}");
            Assert.True(Shapes.View((string)after["view"]!) == r.State.View, $"{where}: view {after["view"]} vs {r.State.View}");
            Assert.True(Shapes.Date(after["anchor"]) == r.State.Anchor, $"{where}: anchor {after["anchor"]} vs {r.State.Anchor}");
            Assert.True(Enum.Parse<SettingsTab>((string)after["tab"]!) == r.State.Tab, $"{where}: tab {after["tab"]} vs {r.State.Tab}");
            s = r.State;
            i++;
        }
    }

    [Theory]
    [MemberData(nameof(ScheduleCases))]
    public void Schedule_matches_prototype(string name)
    {
        var c = Fixtures.Case("schedule.json", name);
        switch ((string)c["kind"]!)
        {
            case "layout":
            {
                var items = c["items"]!.AsArray().Select(i => new ScheduleSpan((long)i!["id"]!, (int)i["start"]!, (int)i["dur"]!)).ToList();
                var layout = ScheduleColumns.Layout(items);
                Fixtures.Equal(c["pos"], new JsonArray([.. items.Select(i => (JsonNode)Shapes.Placement(i.Id, layout.Placements[i.Id]))]), name + " pos");
                Fixtures.Equal(c["overflows"], new JsonArray([.. layout.Overflows.Select(o => (JsonNode)Shapes.ColumnOverflow(o))]), name + " overflows");
                Assert.Equal(items.Count, layout.Placements.Count);
                break;
            }

            case "sched":
            {
                var today = Shapes.Date(c["today"]);
                var anchor = Shapes.Date(c["anchor"]);
                var store = c["tasks"] is JsonArray tasks
                    ? new TaskStore([.. tasks.Select(t => Shapes.Task(t!))], DemoData.Tags, NullTaskPersistence.Instance)
                    : Demo.Store();
                var ctx = Demo.Context(today, (string?)c["filter"]);
                var model = (string)c["view"]! == "today" ? Projector.Today(store, ctx).Schedule : Projector.Day(store, ctx, anchor).Schedule;
                Fixtures.Equal(c, Shapes.Schedule(model, c), name);
                break;
            }

            case "drag":
                ForEachRow(c, name, r =>
                {
                    var mode = (string)r["mode"]! == "move" ? ScheduleDragMode.Move : ScheduleDragMode.Resize;
                    var slot = ScheduleEdit.Drag(mode, (int)r["s0"]!, (int)r["d0"]!, (double)r["dy"]!, (bool)r["alt"]!);
                    return Shapes.With(r, ("start", slot.Start), ("dur", slot.Duration));
                });
                break;

            case "keys":
                ForEachRow(c, name, r =>
                {
                    var dur = (int)r["dur"]!;
                    var slot = ScheduleEdit.Key((int)r["start"]!, dur > 0 ? dur : 30, (bool)r["down"]!, (bool)r["shift"]!, (bool)r["alt"]!);
                    var mode = (bool)r["shift"]! ? ScheduleDragMode.Resize : ScheduleDragMode.Move;
                    return Shapes.With(r, ("changed", slot is not null), ("ns", slot?.Start), ("nd", slot?.Duration),
                        ("chip", slot is { } s ? ScheduleEdit.ChipMinutes(mode, s) : null));
                });
                break;

            case "autoscroll":
                ForEachRow(c, name, r => Shapes.With(r,
                    ("v", ScheduleEdit.AutoScrollSpeed((double)r["y"]!, (double)r["top"]!, (double)r["bottom"]!, (double)r["scale"]!))));
                break;

            case "durations":
                ForEachRow(c, name, r => Shapes.With(r, ("label", ScheduleEdit.DurationLabel((int)r["m"]!))));
                break;

            case "tiers":
                ForEachRow(c, name, r =>
                {
                    var h = (double)r["h"]!;
                    var meta = ScheduleLayout.Meta((double)r["w"]!, h, 570, 105, (bool)r["active"]!);
                    return Shapes.With(r, ("meta", meta), ("tall", !ScheduleLayout.IsCompact(h)));
                });
                break;

            default:
                Assert.Fail("unknown kind " + c["kind"]);
                break;
        }
    }

    /// <summary>Compares every row of a table case with the row <paramref name="actual"/> computes from its inputs.</summary>
    private static void ForEachRow(JsonObject c, string name, Func<JsonObject, JsonObject> actual)
    {
        var rows = c["rows"]!.AsArray();
        Assert.NotEmpty(rows);
        var i = 0;
        foreach (var r in rows)
        {
            var row = r!.AsObject();
            Fixtures.Equal(row, actual(row), $"{name} row {i++} {row.ToJsonString()}");
        }
    }
}
