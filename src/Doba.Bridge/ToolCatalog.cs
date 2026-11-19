namespace Doba.Bridge;

/// <summary>
/// The one list of Doba's MCP tools: Doba.Mcp serves it as <c>tools/list</c> (static, the same on every connection)
/// and Doba's <see cref="ToolExecutor"/> checks requests against it. The level each tool needs is checked by Doba.
/// </summary>
public static class ToolCatalog
{
    public const string GetToday = "get_today";
    public const string ListTasks = "list_tasks";
    public const string ListInbox = "list_inbox";
    public const string SearchTasks = "search_tasks";
    public const string ListTags = "list_tags";
    public const string GetTask = "get_task";
    public const string CreateTask = "create_task";
    public const string UpdateTask = "update_task";
    public const string SetTaskDone = "set_task_done";
    public const string MoveTask = "move_task";
    public const string DeleteTask = "delete_task";

    /// <summary>Longest range <see cref="ListTasks"/> accepts, in days from <c>from</c> to <c>to</c>.</summary>
    public const int MaxRangeDays = 92;

    /// <summary><see cref="ListTasks"/> / <see cref="ListInbox"/> page size: default and maximum.</summary>
    public const int DefaultListLimit = 100;

    public const int MaxListLimit = 500;

    /// <summary><see cref="SearchTasks"/> result count: default and maximum.</summary>
    public const int DefaultSearchLimit = 20;

    public const int MaxSearchLimit = 50;

    /// <summary>Notes longer than this are cut in list and change results (<c>notes_truncated: true</c>).</summary>
    public const int ListNotesLength = 200;

    /// <summary>Notes longer than this are cut in <see cref="GetTask"/>'s result, which keeps it well under the
    /// bridge's 1 MB answer limit.</summary>
    public const int TaskNotesLength = 100_000;

    /// <summary>Most tasks in each of <see cref="GetToday"/>'s lists.</summary>
    public const int TodayGroupLimit = 50;

    /// <summary>Sent once per session (initialize / server/discover); the model reads it as context.</summary>
    public const string ServerInstructions =
        "Doba is the user's local daily planner on this PC. A task has a title, optional notes, a date (YYYY-MM-DD) " +
        "or none (then it lives in the Inbox), an optional span of up to 14 days, an optional time (HH:mm) for " +
        "single-day tasks, a priority (none|low|medium|high|asap) and at most one existing tag. Call get_today first " +
        "to learn today's date, then turn words like 'tomorrow' or 'next Monday' into YYYY-MM-DD. Take task ids from " +
        "list or search results. If carry-over is on, overdue single-day tasks move to today every morning. Errors " +
        "saying access is off or read-only mean the user has to change Settings > Data > Claude access in Doba.";

    private const string Formats =
        "Dates are YYYY-MM-DD in the user's local calendar, times are HH:mm (24-hour), priority is " +
        "none|low|medium|high|asap.";

    private const string ChangeResult = " Notes in the result are cut to 200 characters (notes_truncated: true).";

    private const string TaskShape =
        "A task is {id, title, notes, date, end, days, time, duration_minutes, priority, tag, done}; date is null " +
        "for Inbox tasks, end is the last day of a multi-day task.";

    public static IReadOnlyList<ToolDefinition> All { get; } =
    [
        new(GetToday, "Get today",
            "Returns today's date (YYYY-MM-DD), weekday, local time (HH:mm), UTC offset and first day of the week, " +
            "plus the tasks Doba shows on Today: overdue open tasks, multi-day tasks covering today, today's timed and " +
            "untimed tasks, and today's completed ones. Call this first so that words like 'tomorrow' or 'next Monday' can be " +
            "turned into YYYY-MM-DD. Each list holds at most 50 tasks; when one is cut, <list>_truncated is true and " +
            "<list>_total gives its size: use list_tasks (overdue tasks are dated) or list_inbox for the rest. Notes are " +
            "cut to 200 characters. " + TaskShape,
            """{"type":"object","properties":{},"additionalProperties":false}""",
            ReadOnly: true, Destructive: false, Idempotent: true),

        new(ListTasks, "List tasks in a date range",
            "Lists the tasks on the days from 'from' to 'to' (inclusive, YYYY-MM-DD, at most 92 days apart; 'to' " +
            "defaults to 'from'). A multi-day task that overlaps the range is listed once. Sorted by date, then " +
            "time. Completed tasks are left out unless include_done is true. Undated (Inbox) tasks are not " +
            "included: use list_inbox. Notes are cut to 200 characters (notes_truncated: true); get_task returns " +
            "the full text. Paged: limit (default 100, max 500) and offset; the result says how many there are in " +
            "total. " + TaskShape,
            """
            {"type":"object","properties":{
              "from":{"type":"string","format":"date","description":"First day, YYYY-MM-DD."},
              "to":{"type":"string","format":"date","description":"Last day, YYYY-MM-DD; at most 92 days after 'from'. Defaults to 'from'."},
              "include_done":{"type":"boolean","default":false,"description":"Also list completed tasks."},
              "limit":{"type":"integer","minimum":1,"maximum":500,"default":100},
              "offset":{"type":"integer","minimum":0,"default":0}
            },"required":["from"],"additionalProperties":false}
            """,
            ReadOnly: true, Destructive: false, Idempotent: true),

        new(ListInbox, "List Inbox tasks",
            "Lists the undated tasks (Doba's Inbox) in Inbox order. Completed tasks are left out unless " +
            "include_done is true. Notes are cut to 200 characters (notes_truncated: true); get_task returns the " +
            "full text. Paged: limit (default 100, max 500) and offset; the result says how many there are in " +
            "total. " + TaskShape,
            """
            {"type":"object","properties":{
              "include_done":{"type":"boolean","default":false,"description":"Also list completed tasks."},
              "limit":{"type":"integer","minimum":1,"maximum":500,"default":100},
              "offset":{"type":"integer","minimum":0,"default":0}
            },"additionalProperties":false}
            """,
            ReadOnly: true, Destructive: false, Idempotent: true),

        new(SearchTasks, "Search tasks",
            "Finds tasks whose title contains the query, ignoring case. This is a title-only search: notes and tags " +
            "are not searched (use list_tags and the tag field of list results for tags). Covers dated and Inbox " +
            "tasks, done or not; open tasks come first, then by date. Returns at most 'limit' matches (1-50, default " +
            "20). Notes are cut to 200 characters. " + TaskShape,
            """
            {"type":"object","properties":{
              "query":{"type":"string","minLength":1,"description":"Text to look for in task titles."},
              "limit":{"type":"integer","minimum":1,"maximum":50,"default":20}
            },"required":["query"],"additionalProperties":false}
            """,
            ReadOnly: true, Destructive: false, Idempotent: true),

        new(ListTags, "List tags",
            "Lists the user's tags (names without '#') and how many open tasks each has. A task can only use one of " +
            "these existing tags; new tags are created by the user in Doba.",
            """{"type":"object","properties":{},"additionalProperties":false}""",
            ReadOnly: true, Destructive: false, Idempotent: true),

        new(GetTask, "Get a task",
            "Returns one task by id, with its full notes (up to 100,000 characters; notes_truncated: true beyond). Ids " +
            "come from get_today, list_tasks, list_inbox or search_tasks. " + TaskShape,
            """
            {"type":"object","properties":{
              "id":{"type":"integer","minimum":1}
            },"required":["id"],"additionalProperties":false}
            """,
            ReadOnly: true, Destructive: false, Idempotent: true),

        new(CreateTask, "Create a task",
            "Creates one task and returns it with its new id. Only 'title' is required. Without 'date' the task goes " +
            "to the Inbox (undated). 'days' (1-14, default 1) makes it span that many days starting at 'date'. " +
            "'time' and 'duration_minutes' (0-1440, 0 = unspecified) are only for a single-day task with a date. " +
            "Without 'priority' the user's default priority is used. 'tag' must be one of the existing tags " +
            "(see list_tags); only existing tags are accepted, new ones cannot be created here. " + Formats +
            " Needs the 'Read & edit' access level." + ChangeResult,
            """
            {"type":"object","properties":{
              "title":{"type":"string","minLength":1},
              "notes":{"type":"string"},
              "date":{"type":"string","format":"date","description":"YYYY-MM-DD. Leave out for the Inbox."},
              "days":{"type":"integer","minimum":1,"maximum":14,"description":"Number of days the task spans, starting at 'date'."},
              "time":{"type":"string","pattern":"^([01]\\d|2[0-3]):[0-5]\\d$","description":"Start time HH:mm (24-hour); single-day dated tasks only."},
              "duration_minutes":{"type":"integer","minimum":0,"maximum":1440,"description":"Length of a timed task; 0 = unspecified."},
              "priority":{"type":"string","enum":["none","low","medium","high","asap"]},
              "tag":{"type":"string","minLength":1,"description":"An existing tag name (see list_tags)."}
            },"required":["title"],"additionalProperties":false}
            """,
            ReadOnly: false, Destructive: false, Idempotent: false),

        new(UpdateTask, "Update a task",
            "Changes fields of one task by id. A missing field = unchanged; use clear[] to clear a field: 'notes', " +
            "'time', 'tag' or 'date' (clearing the date moves the task to the Inbox). null is not accepted as a " +
            "value. The same rules as create_task apply: 'days' 1-14, 'time' only for a single-day task with a " +
            "date, 'duration_minutes' 0-1440, only existing tags. " + Formats + " Fails while the task is open in " +
            "Doba's editor. Returns the updated task. Needs the 'Read & edit' access level." + ChangeResult,
            """
            {"type":"object","properties":{
              "id":{"type":"integer","minimum":1},
              "title":{"type":"string","minLength":1},
              "notes":{"type":"string"},
              "date":{"type":"string","format":"date","description":"YYYY-MM-DD."},
              "days":{"type":"integer","minimum":1,"maximum":14,"description":"Number of days the task spans, starting at its date."},
              "time":{"type":"string","pattern":"^([01]\\d|2[0-3]):[0-5]\\d$","description":"Start time HH:mm (24-hour); single-day dated tasks only."},
              "duration_minutes":{"type":"integer","minimum":0,"maximum":1440,"description":"Length of a timed task; 0 = unspecified."},
              "priority":{"type":"string","enum":["none","low","medium","high","asap"]},
              "tag":{"type":"string","minLength":1,"description":"An existing tag name (see list_tags)."},
              "clear":{"type":"array","items":{"type":"string","enum":["notes","time","tag","date"]},"uniqueItems":true,
                "description":"Fields to clear. 'date' moves the task to the Inbox."}
            },"required":["id"],"additionalProperties":false}
            """,
            ReadOnly: false, Destructive: true, Idempotent: true),

        new(SetTaskDone, "Mark a task done or not done",
            "Marks one task as done (done: true) or not done (done: false). Nothing changes if it already is. " +
            "Fails while the task is open in Doba's editor. Returns the task. Needs the 'Read & edit' access level." +
            ChangeResult,
            """
            {"type":"object","properties":{
              "id":{"type":"integer","minimum":1},
              "done":{"type":"boolean"}
            },"required":["id","done"],"additionalProperties":false}
            """,
            // Destructive in MCP's sense (not only additive: it overwrites the done state), like update_task.
            ReadOnly: false, Destructive: true, Idempotent: true),

        new(MoveTask, "Move a task to another day",
            "Moves one task to another day. 'date' is YYYY-MM-DD, or \"inbox\" to make the task undated. A " +
            "multi-day task moves whole and keeps its length; moving to the Inbox removes its time and span. Fails " +
            "while the task is open in Doba's editor. Returns the task. Needs the 'Read & edit' access level." +
            ChangeResult,
            """
            {"type":"object","properties":{
              "id":{"type":"integer","minimum":1},
              "date":{"type":"string","pattern":"^(\\d{4}-\\d{2}-\\d{2}|inbox)$","description":"YYYY-MM-DD, or \"inbox\"."}
            },"required":["id","date"],"additionalProperties":false}
            """,
            // Destructive: it overwrites the date, and moving to the Inbox drops the time and span for good.
            ReadOnly: false, Destructive: true, Idempotent: true),

        new(DeleteTask, "Delete a task",
            "Deletes one task by id, permanently. Only delete what the user asked to delete. Fails while the task is " +
            "open in Doba's editor. Needs the 'Read & edit' access level. Returns the deleted task; its notes are cut " +
            "to 200 characters.",
            """
            {"type":"object","properties":{
              "id":{"type":"integer","minimum":1}
            },"required":["id"],"additionalProperties":false}
            """,
            ReadOnly: false, Destructive: true, Idempotent: false),
    ];

    /// <summary>The tool named <paramref name="name"/> (exact, case-sensitive), or null.</summary>
    public static ToolDefinition? Find(string name)
    {
        foreach (var tool in All)
        {
            if (string.Equals(tool.Name, name, StringComparison.Ordinal))
            {
                return tool;
            }
        }

        return null;
    }
}
