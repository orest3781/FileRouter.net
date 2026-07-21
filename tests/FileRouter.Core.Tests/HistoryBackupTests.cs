using FileRouter.Core;

namespace FileRouter.Core.Tests;

public class HistoryBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bkptest_" + Guid.NewGuid());
    private readonly string _db, _backups;

    public HistoryBackupTests()
    {
        Directory.CreateDirectory(_dir);
        _db = Path.Combine(_dir, "history.sqlite");
        _backups = Path.Combine(_dir, "backups");
        File.WriteAllText(_db, "DBDATA");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void CopiesOncePerDay()
    {
        var day = new DateTime(2026, 7, 20);
        var first = HistoryBackup.BackupDaily(_db, _backups, day);
        Assert.NotNull(first);
        Assert.True(File.Exists(Path.Combine(_backups, "history-20260720.sqlite")));
        Assert.Equal("DBDATA", File.ReadAllText(first!));

        // same day again: does not re-copy (even if the DB changed since)
        File.WriteAllText(_db, "CHANGED");
        HistoryBackup.BackupDaily(_db, _backups, day);
        Assert.Equal("DBDATA", File.ReadAllText(first!));   // still the morning's copy
        Assert.Single(Directory.GetFiles(_backups));
    }

    [Fact]
    public void KeepsNewestNPrunesOlder()
    {
        for (var d = 1; d <= 20; d++)
            HistoryBackup.BackupDaily(_db, _backups, new DateTime(2026, 7, d), keep: 14);
        var kept = Directory.GetFiles(_backups).Select(Path.GetFileName).OrderBy(x => x).ToList();
        Assert.Equal(14, kept.Count);
        Assert.Equal("history-20260707.sqlite", kept.First());   // oldest kept is day 7
        Assert.Equal("history-20260720.sqlite", kept.Last());
    }

    [Fact]
    public void MissingDbIsNoOp() =>
        Assert.Null(HistoryBackup.BackupDaily(@"Z:\nope\history.sqlite", _backups, DateTime.Now));

    [Fact]
    public void NeverThrows()
    {
        // an unwritable backup dir must not blow up
        var ex = Record.Exception(() =>
            HistoryBackup.BackupDaily(_db, "\0invalid\0path", DateTime.Now));
        Assert.Null(ex);
    }
}
