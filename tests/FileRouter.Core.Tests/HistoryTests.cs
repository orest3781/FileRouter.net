using FileRouter.Core;

namespace FileRouter.Core.Tests;

public class HistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "histest_" + Guid.NewGuid());

    public HistoryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        // SQLite releases its native file handle slightly after Dispose on
        // Windows; retry the temp-dir cleanup rather than fail the test.
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch (IOException) when (attempt < 10)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(50);
            }
        }
    }

    private History NewHistory(string name = "h.sqlite") => new(Path.Combine(_dir, name));

    private static long Log(History h, string name) => h.LogCommit(
        "C:/in/x.pdf", "x.pdf", $"{name}.pdf", name, "insert", "", "A", "C:/a",
        false, "");

    [Fact]
    public void LogsAndCounts()
    {
        using var h = NewHistory();
        Assert.Equal(0, h.Count());
        Log(h, "SMITH JOHN");
        Log(h, "GARCIA MARIA");
        Assert.Equal(2, h.Count());
    }

    [Fact]
    public void MarkRevertedRecordsWhen()
    {
        using var h = NewHistory();
        var id = Log(h, "SMITH JOHN");
        Assert.Equal("", h.Rows()[0]["reverted_ts"]);
        h.MarkReverted(id);
        var row = h.Rows()[0];
        Assert.Equal(1L, Convert.ToInt64(row["reverted"]));
        Assert.NotEqual("", (string)row["reverted_ts"]);
    }

    // ---- network safety (the reason for the .NET port's DB choices) ----

    [Fact]
    public void JournalModeIsNetworkSafe()
    {
        using var h = NewHistory();
        Assert.Equal("truncate", h.JournalMode().ToLowerInvariant());
        Assert.NotEqual("wal", h.JournalMode().ToLowerInvariant());
    }

    [Fact]
    public void ConcurrentWritersDoNotError()
    {
        NewHistory("shared.sqlite").Dispose();  // create schema
        var path = Path.Combine(_dir, "shared.sqlite");
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, 4).Select(n => Task.Run(() =>
        {
            try
            {
                using var h = new History(path);
                for (var i = 0; i < 20; i++) Log(h, $"{n}-{i}");
            }
            catch (Exception ex) { errors.Add(ex.Message); }
        })).ToArray();
        Task.WaitAll(tasks);

        Assert.Empty(errors);
        using var check = new History(path);
        Assert.Equal(80, check.Count());  // 4 writers x 20, none lost
    }

    [Fact]
    public void OldWalDatabaseConvertsCleanly()
    {
        var path = Path.Combine(_dir, "old.sqlite");
        // simulate an older FileRouter that left a WAL-mode DB with a row
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                   $"Data Source={path};Pooling=False"))
        {
            conn.Open();
            foreach (var sql in new[]
            {
                "PRAGMA journal_mode=WAL",
                """
                CREATE TABLE history(
                  id INTEGER PRIMARY KEY AUTOINCREMENT, ts_utc TEXT NOT NULL,
                  original_path TEXT NOT NULL, original_name TEXT NOT NULL,
                  new_name TEXT NOT NULL, name_entered TEXT NOT NULL,
                  naming_mode TEXT NOT NULL, suffix_applied TEXT DEFAULT '',
                  route_label TEXT NOT NULL, route_path TEXT NOT NULL,
                  tagged INTEGER DEFAULT 0, collision_suffix TEXT DEFAULT '',
                  reverted INTEGER DEFAULT 0)
                """,
                "INSERT INTO history(ts_utc,original_path,original_name,new_name," +
                "name_entered,naming_mode,route_label,route_path) " +
                "VALUES('t','p','o.pdf','n.pdf','KEEP','insert','A','q')",
            })
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }
        using var h = new History(path);
        Assert.Equal("truncate", h.JournalMode().ToLowerInvariant());
        Assert.Equal("KEEP", h.Rows()[0]["name_entered"]);  // data survived
    }
}
