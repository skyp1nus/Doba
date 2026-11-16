using Doba.Core.Model;
using Doba.Storage.Internal;
using Microsoft.Data.Sqlite;
using static Doba.Storage.Tests.Samples;

namespace Doba.Storage.Tests;

public sealed class DobaDatabaseTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string DbPath => _temp.Location.DatabasePath;

    private async Task<LoadedData> ReopenAndLoad()
    {
        await using var db = DobaDatabase.Open(_temp.Location);
        return db.Load();
    }

    [Fact]
    public async Task Open_creates_folder_and_schema_v1_from_nothing()
    {
        Assert.False(Directory.Exists(_temp.Location.Folder));

        await using (var db = DobaDatabase.Open(_temp.Location))
        {
            Assert.Same(_temp.Location, db.Location);
            var data = db.Load();
            Assert.Empty(data.Tasks);
            Assert.Empty(data.Tags);
            Assert.Equal(AppSettings.Default, data.Settings);
        }

        Assert.True(File.Exists(DbPath));
        Assert.Equal(1, Raw.Number(DbPath, "PRAGMA user_version;"));
        Assert.Equal(1, DobaDatabase.SchemaVersion);
        Assert.Equal("wal", Raw.Scalar(DbPath, "PRAGMA journal_mode;"));
        Assert.Equal(3, Raw.Number(DbPath, "SELECT count(*) FROM sqlite_schema WHERE type = 'table' AND name IN ('tasks', 'tags', 'settings');"));
        // Closing the last connection checkpoints and removes the WAL.
        Assert.False(File.Exists(DbPath + "-wal"));
    }

    [Fact]
    public void Migration_list_is_ordered_and_contiguous_from_1()
    {
        for (int i = 0; i < Schema.Migrations.Count; i++)
        {
            Assert.Equal(i + 1, Schema.Migrations[i].Version);
        }
    }

    [Fact]
    public async Task Reopening_keeps_data_and_user_version()
    {
        await using (var db = DobaDatabase.Open(_temp.Location))
        {
            db.Tasks.Upsert(Item(1, "kept"));
            db.Tasks.SaveTags(["work"]);
            db.Settings.Save(NonDefaultSettings());
        }

        for (int round = 0; round < 2; round++)
        {
            var data = await ReopenAndLoad();
            Assert.Equal([Item(1, "kept")], data.Tasks);
            Assert.Equal(["work"], data.Tags);
            Assert.Equal(NonDefaultSettings(), data.Settings);
            Assert.Equal(1, Raw.Number(DbPath, "PRAGMA user_version;"));
        }
    }

    [Fact]
    public async Task Database_from_a_newer_schema_is_refused()
    {
        await using (DobaDatabase.Open(_temp.Location))
        {
        }

        Raw.Execute(DbPath, "PRAGMA user_version = 99;");
        var ex = Assert.Throws<NotSupportedException>(() => DobaDatabase.Open(_temp.Location));
        Assert.Contains("newer version", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tasks_round_trip_with_every_field_combination()
    {
        var tasks = AllCombinations();
        await using (var db = DobaDatabase.Open(_temp.Location))
        {
            foreach (var task in tasks)
            {
                db.Tasks.Upsert(task);
            }
        }

        var loaded = await ReopenAndLoad();
        Assert.Equal(tasks.Count, loaded.Tasks.Count);
        Assert.Equal(tasks, loaded.Tasks);
    }

    [Fact]
    public async Task Load_orders_tasks_by_id_regardless_of_write_order()
    {
        await using (var db = DobaDatabase.Open(_temp.Location))
        {
            foreach (long id in new long[] { 5, 1, 9, 3 })
            {
                db.Tasks.Upsert(Item(id, $"t{id}"));
            }
        }

        var loaded = await ReopenAndLoad();
        Assert.Equal([1L, 3, 5, 9], loaded.Tasks.Select(t => t.Id));
    }

    [Fact]
    public async Task Tags_keep_their_order()
    {
        string[] tags = ["work", "personal", "learning", "zeta", "alpha"];
        await using (var db = DobaDatabase.Open(_temp.Location))
        {
            db.Tasks.SaveTags(["old"]);
            await db.Tasks.FlushAsync(Ct);
            db.Tasks.SaveTags(tags);
        }

        Assert.Equal(tags, (await ReopenAndLoad()).Tags);
    }

    [Fact]
    public async Task SaveTags_copies_the_list()
    {
        var tags = new List<string> { "a", "b" };
        await using (var db = DobaDatabase.Open(_temp.Location))
        {
            db.Tasks.SaveTags(tags);
            tags.Add("c"); // mutated after the call: must not leak into the queued write
        }

        Assert.Equal(["a", "b"], (await ReopenAndLoad()).Tags);
    }

    [Fact]
    public async Task Settings_round_trip_every_property()
    {
        var settings = NonDefaultSettings();
        await using (var db = DobaDatabase.Open(_temp.Location))
        {
            db.Load();
            db.Settings.Save(settings);
            Assert.Equal(settings, db.Settings.Load()); // live value, without waiting for the disk
        }

        Assert.Equal(settings, (await ReopenAndLoad()).Settings);
        Assert.Equal(SettingsMapping.Keys.Count, Raw.Number(DbPath, "SELECT count(*) FROM settings;"));

        await using var reopened = DobaDatabase.Open(_temp.Location);
        Assert.Equal(settings, reopened.Settings.Load()); // read from disk when Load() was not called
    }

    [Fact]
    public void Test_settings_differ_from_defaults_in_every_property_and_mapping_covers_all()
    {
        var properties = typeof(AppSettings).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        Assert.Equal(properties.Length, SettingsMapping.Keys.Count);
        Assert.Equal(properties.Length, SettingsMapping.Keys.Distinct().Count());
        foreach (var property in properties)
        {
            Assert.NotEqual(property.GetValue(AppSettings.Default), property.GetValue(NonDefaultSettings()));
        }
    }

    [Theory]
    [InlineData("date", "'2026-02-30'")]
    [InlineData("date", "'2026-11-5'")]
    [InlineData("date", "'15.11.2026'")]
    [InlineData("end_date", "'2026-13-01'")]
    [InlineData("time", "'24:00'")]
    [InlineData("time", "'9:30'")]
    [InlineData("time", "'09:30:00'")]
    public async Task Malformed_stored_dates_and_times_are_reported_not_guessed(string column, string value)
    {
        await using (DobaDatabase.Open(_temp.Location))
        {
        }

        Raw.Execute(DbPath, column == "date"
            ? $"INSERT INTO tasks (id, title, date) VALUES (7, 'x', {value});"
            : $"INSERT INTO tasks (id, title, date, {column}) VALUES (7, 'x', '2026-11-15', {value});");
        await using var db = DobaDatabase.Open(_temp.Location);
        var ex = Assert.Throws<InvalidDataException>(() => db.Load());
        Assert.Contains("Task 7", ex.Message, StringComparison.Ordinal);
        Assert.Contains(column, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0001-01-01")]
    [InlineData("2024-02-29")]
    [InlineData("9999-12-31")]
    [InlineData("2026-11-15")]
    public void Byte_date_parser_matches_the_framework(string text)
    {
        Assert.True(DatabaseReader.TryParseDate(System.Text.Encoding.UTF8.GetBytes(text), out var date));
        Assert.True(Formats.TryParseDate(text, out var expected));
        Assert.Equal(expected, date);
    }

    [Fact]
    public async Task Settings_ignore_unknown_keys_and_default_missing_or_bad_values()
    {
        await using (DobaDatabase.Open(_temp.Location))
        {
        }

        Raw.Execute(DbPath, """
            INSERT INTO settings (key, value) VALUES
                ('accent', 'Sage'), ('futureSetting', 'whatever'), ('windowTransparency', 'abc'),
                ('weekStart', 'Wednesday'), ('density', '7'), ('completionSound', 'true'), ('theme', 'Neon');
            """);

        var settings = (await ReopenAndLoad()).Settings;
        Assert.Equal(AppSettings.Default with { Accent = Accent.Sage, CompletionSound = true }, settings);
    }

    [Fact]
    public async Task Updates_and_deletes_are_persisted()
    {
        await using (var db = DobaDatabase.Open(_temp.Location))
        {
            db.Tasks.Upsert(Item(1, "a"));
            db.Tasks.Upsert(Item(2, "b"));
            db.Tasks.Upsert(Item(3, "c"));
            await db.Tasks.FlushAsync(Ct);

            db.Tasks.Upsert(Item(2, "b2") with { Done = true, Date = new DateOnly(2026, 11, 16), Time = new TimeOnly(8, 0), DurationMinutes = 30 });
            db.Tasks.Delete(1);
            db.Tasks.Delete(42); // unknown id: no-op
            db.Tasks.Upsert(Item(4, "d"));
            db.Tasks.Upsert(Item(4, "d2")); // twice in one burst: last wins
            await db.Tasks.FlushAsync(Ct);

            // Clearing fields back to null.
            db.Tasks.Upsert(Item(2, "b3"));
        }

        var loaded = await ReopenAndLoad();
        Assert.Equal([Item(2, "b3"), Item(3, "c"), Item(4, "d2")], loaded.Tasks);
    }

    [Fact]
    public async Task ReplaceAll_replaces_tasks_and_tags_but_not_settings()
    {
        await using (var db = DobaDatabase.Open(_temp.Location))
        {
            db.Tasks.Upsert(Item(1, "old 1"));
            db.Tasks.Upsert(Item(2, "old 2"));
            db.Tasks.SaveTags(["old"]);
            db.Settings.Save(NonDefaultSettings());
            await db.Tasks.FlushAsync(Ct);

            db.Tasks.ReplaceAll([Item(10, "new 10"), Item(7, "new 7")], ["x", "y"]);
        }

        var loaded = await ReopenAndLoad();
        Assert.Equal([Item(7, "new 7"), Item(10, "new 10")], loaded.Tasks);
        Assert.Equal(["x", "y"], loaded.Tags);
        Assert.Equal(NonDefaultSettings(), loaded.Settings);
    }

    [Fact]
    public async Task Writes_in_one_burst_around_ReplaceAll_keep_their_order()
    {
        await using (var db = DobaDatabase.Open(_temp.Location))
        {
            db.Tasks.Upsert(Item(1, "before"));
            db.Tasks.SaveTags(["before"]);
            db.Settings.Save(AppSettings.Default with { Accent = Accent.Sand });
            db.Tasks.ReplaceAll([Item(2, "replaced")], ["replaced"]);
            db.Tasks.Upsert(Item(3, "after"));
            db.Tasks.SaveTags(["replaced", "after"]);
        }

        var loaded = await ReopenAndLoad();
        Assert.Equal([Item(2, "replaced"), Item(3, "after")], loaded.Tasks);
        Assert.Equal(["replaced", "after"], loaded.Tags);
        Assert.Equal(Accent.Sand, loaded.Settings.Accent);
    }

    [Fact]
    public async Task Dispose_flushes_1000_queued_upserts()
    {
        var tasks = Enumerable.Range(1, 1000).Select(i => Item(i, $"task {i}") with { Priority = (Priority)(i % 5), Done = i % 2 == 0 }).ToList();
        await using (var db = DobaDatabase.Open(_temp.Location))
        {
            foreach (var task in tasks)
            {
                db.Tasks.Upsert(task);
            }

            // No FlushAsync: DisposeAsync must drain the queue before closing.
        }

        Assert.Equal(tasks, (await ReopenAndLoad()).Tasks);
    }

    [Fact]
    public async Task FlushAsync_completes_after_everything_queued_before_it_is_on_disk()
    {
        await using var db = DobaDatabase.Open(_temp.Location);
        const int rounds = 50;
        var flushes = new List<Task>();
        var completionOrder = new List<int>();
        for (int i = 1; i <= rounds; i++)
        {
            db.Tasks.Upsert(Item(i));
            int round = i;
            flushes.Add(db.Tasks.FlushAsync(Ct).ContinueWith(_ => { lock (completionOrder) { completionOrder.Add(round); } }, Ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
        }

        for (int i = 1; i <= rounds; i++)
        {
            await flushes[i - 1];
            var onDisk = Raw.TaskIds(DbPath);
            for (long id = 1; id <= i; id++)
            {
                Assert.Contains(id, onDisk);
            }
        }

        Assert.Equal(Enumerable.Range(1, rounds), completionOrder.Order());
        Assert.Equal(rounds, completionOrder.Count);
    }

    [Fact]
    public async Task FlushAsync_with_nothing_queued_completes()
    {
        await using var db = DobaDatabase.Open(_temp.Location);
        await db.Tasks.FlushAsync(Ct);
        await db.Tasks.FlushAsync(Ct);
    }

    [Fact]
    public async Task Writer_errors_surface_from_FlushAsync_and_the_queue_keeps_working()
    {
        await using var db = DobaDatabase.Open(_temp.Location);
        var reported = new List<Exception>();
        db.WriteFailed += (_, e) => { lock (reported) { reported.Add(e.Error); } };

        db.Tasks.Upsert(Item(1, "good before"));
        db.Tasks.SaveTags(["dup", "dup"]); // UNIQUE constraint failure inside SQLite
        db.Tasks.Upsert(Item(2, "good after"));

        var ex = await Assert.ThrowsAsync<StorageWriteException>(() => db.Tasks.FlushAsync(Ct));
        Assert.Equal(1, ex.FailedWrites);
        Assert.IsType<SqliteException>(ex.InnerException);
        Assert.Same(ex.InnerException, db.LastWriteError);
        Assert.Single(reported);

        // The good writes of the same burst still reached the disk.
        Assert.Equal([1L, 2], Raw.TaskIds(DbPath));

        // Reported once; the queue keeps working afterwards.
        await db.Tasks.FlushAsync(Ct);
        db.Tasks.Upsert(Item(3, "later"));
        db.Tasks.SaveTags(["ok"]);
        await db.Tasks.FlushAsync(Ct);
        Assert.Equal([1L, 2, 3], Raw.TaskIds(DbPath));
        Assert.Equal("ok", Raw.Scalar(DbPath, "SELECT name FROM tags;"));
        Assert.NotNull(db.LastWriteError); // the last error stays visible

        // A second, different failure (null title from a buggy caller) is reported on its own.
        db.Tasks.Upsert(Item(4) with { Title = null! });
        var second = await Assert.ThrowsAsync<StorageWriteException>(() => db.Tasks.FlushAsync(Ct));
        Assert.IsType<ArgumentException>(second.InnerException);
        Assert.Equal(2, reported.Count);
    }

    [Fact]
    public async Task A_cancelled_flush_does_not_swallow_the_error()
    {
        await using var db = DobaDatabase.Open(_temp.Location);
        using var blocker = Raw.Open(DbPath);
        using (var begin = blocker.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE;"; // the writer has to wait for this lock
            begin.ExecuteNonQuery();
        }

        db.Tasks.SaveTags(["dup", "dup"]);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var cancelled = db.Tasks.FlushAsync(cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        using (var rollback = blocker.CreateCommand())
        {
            rollback.CommandText = "ROLLBACK;";
            rollback.ExecuteNonQuery();
        }

        await Assert.ThrowsAsync<StorageWriteException>(() => db.Tasks.FlushAsync(Ct));
    }

    [Fact]
    public async Task Unreported_writer_error_surfaces_from_DisposeAsync_after_closing()
    {
        var db = DobaDatabase.Open(_temp.Location);
        db.Tasks.Upsert(Item(1));
        db.Tasks.SaveTags(["dup", "dup"]);
        await Assert.ThrowsAsync<StorageWriteException>(async () => await db.DisposeAsync());

        // Closed anyway, and the good write is on disk.
        Assert.False(File.Exists(DbPath + "-wal"));
        Assert.Equal([Item(1)], (await ReopenAndLoad()).Tasks);
        await db.DisposeAsync(); // idempotent
    }

    [Fact]
    public async Task Writes_after_dispose_throw_and_flush_completes()
    {
        var db = DobaDatabase.Open(_temp.Location);
        await db.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => db.Tasks.Upsert(Item(1)));
        Assert.Throws<ObjectDisposedException>(() => db.Settings.Save(AppSettings.Default));
        Assert.Throws<ObjectDisposedException>(() => db.Load());
        await db.Tasks.FlushAsync(Ct);
    }

    [Fact]
    public async Task Writes_do_not_block_the_caller_on_disk_io()
    {
        await using var db = DobaDatabase.Open(_temp.Location);
        // Hold the write lock from another connection: the writer has to wait, the caller must not.
        using var blocker = Raw.Open(DbPath);
        using (var begin = blocker.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE;";
            begin.ExecuteNonQuery();
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 1; i <= 100; i++)
        {
            db.Tasks.Upsert(Item(i));
        }

        db.Settings.Save(NonDefaultSettings());
        watch.Stop();
        Assert.True(watch.ElapsedMilliseconds < 500, $"Enqueueing took {watch.ElapsedMilliseconds} ms");

        var flush = db.Tasks.FlushAsync(Ct);
        await Task.Delay(100, Ct);
        Assert.False(flush.IsCompleted); // still waiting for the lock

        using (var rollback = blocker.CreateCommand())
        {
            rollback.CommandText = "ROLLBACK;";
            rollback.ExecuteNonQuery();
        }

        await flush;
        Assert.Equal(100, Raw.TaskIds(DbPath).Count);
    }
}
