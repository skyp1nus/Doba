using System.Text.Json;
using System.Text.RegularExpressions;

namespace Doba.Bridge.Tests;

public sealed class ToolCatalogTests
{
    // name, read-only, destructive, idempotent (the plan's table; set_task_done and move_task overwrite data, so they are
    // destructive in MCP's sense: not only additive).
    public static TheoryData<string, bool, bool, bool> Expected => new()
    {
        { "get_today", true, false, true },
        { "list_tasks", true, false, true },
        { "list_inbox", true, false, true },
        { "search_tasks", true, false, true },
        { "list_tags", true, false, true },
        { "get_task", true, false, true },
        { "create_task", false, false, false },
        { "update_task", false, true, true },
        { "set_task_done", false, true, true },
        { "move_task", false, true, true },
        { "delete_task", false, true, false },
    };

    [Fact]
    public void Exactly_eleven_tools_with_unique_snake_case_names()
    {
        Assert.Equal(11, ToolCatalog.All.Count);
        Assert.Equal(ToolCatalog.All.Count, ToolCatalog.All.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(ToolCatalog.All, t => Assert.Matches(new Regex("^[a-z_]+$"), t.Name));
    }

    [Theory]
    [MemberData(nameof(Expected))]
    public void Annotations_match_the_plan(string name, bool readOnly, bool destructive, bool idempotent)
    {
        var tool = ToolCatalog.Find(name);
        Assert.NotNull(tool);
        Assert.Equal(readOnly, tool.ReadOnly);
        Assert.Equal(destructive, tool.Destructive);
        Assert.Equal(idempotent, tool.Idempotent);
        Assert.Equal(!readOnly, tool.RequiresWrite);
        Assert.False(string.IsNullOrWhiteSpace(tool.Title));
        Assert.True(tool.Description.Length >= 80, name + ": description too short for the model");
    }

    [Fact]
    public void Every_tool_is_in_the_expected_table() =>
        Assert.Equal(Expected.Select(row => row.Data.Item1).Order(StringComparer.Ordinal),
            ToolCatalog.All.Select(t => t.Name).Order(StringComparer.Ordinal));

    [Fact]
    public void Every_schema_is_a_closed_object_whose_required_properties_exist()
    {
        foreach (var tool in ToolCatalog.All)
        {
            using var doc = JsonDocument.Parse(tool.InputSchemaJson);
            var root = doc.RootElement;
            Assert.Equal("object", root.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.False, root.GetProperty("additionalProperties").ValueKind);
            var properties = root.GetProperty("properties");
            Assert.Equal(JsonValueKind.Object, properties.ValueKind);
            if (root.TryGetProperty("required", out var required))
            {
                foreach (var name in required.EnumerateArray())
                {
                    Assert.True(properties.TryGetProperty(name.GetString()!, out _), $"{tool.Name}: required '{name}' is not a property");
                }
            }

            foreach (var property in properties.EnumerateObject())
            {
                Assert.Matches(new Regex("^[a-z_]+$"), property.Name);
                Assert.True(property.Value.TryGetProperty("type", out _), $"{tool.Name}.{property.Name} has no type");
            }
        }
    }

    [Fact]
    public void Time_pattern_accepts_hh_mm_only()
    {
        using var doc = JsonDocument.Parse(ToolCatalog.Find(ToolCatalog.CreateTask)!.InputSchemaJson);
        var pattern = new Regex(doc.RootElement.GetProperty("properties").GetProperty("time").GetProperty("pattern").GetString()!);
        Assert.Matches(pattern, "00:00");
        Assert.Matches(pattern, "09:30");
        Assert.Matches(pattern, "23:59");
        Assert.DoesNotMatch(pattern, "24:00");
        Assert.DoesNotMatch(pattern, "9:30");
        Assert.DoesNotMatch(pattern, "09:60");
        Assert.DoesNotMatch(pattern, "09:30:00");
    }

    [Fact]
    public void Move_date_accepts_iso_or_inbox()
    {
        using var doc = JsonDocument.Parse(ToolCatalog.Find(ToolCatalog.MoveTask)!.InputSchemaJson);
        var pattern = new Regex(doc.RootElement.GetProperty("properties").GetProperty("date").GetProperty("pattern").GetString()!);
        Assert.Matches(pattern, "2026-11-20");
        Assert.Matches(pattern, "inbox");
        Assert.DoesNotMatch(pattern, "tomorrow");
        Assert.DoesNotMatch(pattern, "Inbox");
    }

    [Fact]
    public void Find_is_exact()
    {
        Assert.Same(ToolCatalog.All[0], ToolCatalog.Find(ToolCatalog.All[0].Name));
        Assert.Null(ToolCatalog.Find("GET_TODAY"));
        Assert.Null(ToolCatalog.Find("due_date"));
        Assert.Null(ToolCatalog.Find(""));
    }

    [Fact]
    public void Server_instructions_are_short_and_name_the_first_call()
    {
        Assert.InRange(ToolCatalog.ServerInstructions.Length, 400, 800);
        Assert.Contains("get_today", ToolCatalog.ServerInstructions, StringComparison.Ordinal);
    }
}
