using static Doba.Storage.Tests.Samples;

namespace Doba.Storage.Tests;

public sealed class BackupTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string BackupFolder => _temp.Location.BackupFolder;

    [Fact]
    public async Task Creates_todays_backup_once_with_pending_writes_included()
    {
        await using var db = DobaDatabase.Open(_temp.Location);
        db.Tasks.Upsert(Item(1, "one"));
        db.Tasks.Upsert(Item(2, "two"));
        db.Tasks.SaveTags(["work"]);
        // No flush: the backup must include writes queued before it.

        string? path = await Backup.RunDailyAsync(db, Today, cancellationToken: Ct);

        Assert.NotNull(path);
        Assert.Equal(Path.Combine(BackupFolder, "doba-2026-11-15.db"), path);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(2, Raw.Number(path, "SELECT count(*) FROM tasks;"));
        Assert.Equal(1, Raw.Number(path, "SELECT count(*) FROM tags;"));
        Assert.Equal(1, Raw.Number(path, "PRAGMA user_version;"));
        Assert.Equal("ok", Raw.Scalar(path, "PRAGMA integrity_check;"));

        db.Tasks.Upsert(Item(3, "three"));
        Assert.Null(await Backup.RunDailyAsync(db, Today, cancellationToken: Ct));
        Assert.Equal(2, Raw.Number(path, "SELECT count(*) FROM tasks;")); // not overwritten
        Assert.Single(Directory.GetFiles(BackupFolder));
    }

    [Fact]
    public async Task Backup_opens_as_a_doba_database()
    {
        await using (var db = DobaDatabase.Open(_temp.Location))
        {
            db.Tasks.Upsert(Item(7, "seven"));
            db.Settings.Save(NonDefaultSettings());
            await Backup.RunDailyAsync(db, Today, cancellationToken: Ct);
        }

        var restored = new DataLocation(Path.Combine(_temp.Root, "Restored"));
        Directory.CreateDirectory(restored.Folder);
        File.Copy(Path.Combine(BackupFolder, Backup.FileName(Today)), restored.DatabasePath);
        await using var copy = DobaDatabase.Open(restored);
        var data = copy.Load();
        Assert.Equal([Item(7, "seven")], data.Tasks);
        Assert.Equal(NonDefaultSettings(), data.Settings);
    }

    [Fact]
    public async Task Keeps_only_the_newest_seven_and_ignores_other_files()
    {
        Directory.CreateDirectory(BackupFolder);
        string[] foreign = ["notes.txt", "doba-latest.db", "doba-2026-11-01.db.tmp", "other-2026-01-01.db", "doba-2026-1-1.db", "doba-2020-01-01.dbx"];
        foreach (string name in foreign)
        {
            await File.WriteAllTextAsync(Path.Combine(BackupFolder, name), "x", Ct);
        }

        await using var db = DobaDatabase.Open(_temp.Location);
        for (int day = 0; day < 10; day++)
        {
            Assert.NotNull(await Backup.RunDailyAsync(db, Today.AddDays(day), cancellationToken: Ct));
            Assert.True(Backup.List(BackupFolder).Count <= 7);
        }

        var kept = Backup.List(BackupFolder);
        Assert.Equal(Enumerable.Range(3, 7).Reverse().Select(d => Today.AddDays(d)), kept.Select(b => b.Day));
        foreach (string name in foreign)
        {
            Assert.True(File.Exists(Path.Combine(BackupFolder, name)), name);
        }
    }

    [Fact]
    public async Task Prunes_older_backups_even_when_today_exists_and_respects_keep()
    {
        Directory.CreateDirectory(BackupFolder);
        for (int day = 1; day <= 5; day++)
        {
            await File.WriteAllTextAsync(Path.Combine(BackupFolder, Backup.FileName(Today.AddDays(-day))), "old", Ct);
        }

        await using var db = DobaDatabase.Open(_temp.Location);
        Assert.NotNull(await Backup.RunDailyAsync(db, Today, keep: 3, cancellationToken: Ct));
        Assert.Equal([Today, Today.AddDays(-1), Today.AddDays(-2)], Backup.List(BackupFolder).Select(b => b.Day));

        Assert.Null(await Backup.RunDailyAsync(db, Today, keep: 1, cancellationToken: Ct));
        Assert.Equal([Today], Backup.List(BackupFolder).Select(b => b.Day));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Backup.RunDailyAsync(db, Today, keep: 0, cancellationToken: Ct));
    }

    [Fact]
    public async Task Leftover_temporary_file_does_not_block_the_backup()
    {
        Directory.CreateDirectory(BackupFolder);
        string temp = Path.Combine(BackupFolder, Backup.FileName(Today) + ".tmp");
        await File.WriteAllTextAsync(temp, "partial", Ct);

        await using var db = DobaDatabase.Open(_temp.Location);
        string? path = await Backup.RunDailyAsync(db, Today, cancellationToken: Ct);
        Assert.NotNull(path);
        Assert.False(File.Exists(temp));
        Assert.Equal("ok", Raw.Scalar(path, "PRAGMA integrity_check;"));
    }
}
