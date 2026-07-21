using Microsoft.Data.Sqlite;

namespace FileRouter.Core;

/// <summary>
/// The durable audit trail: one SQLite row per commit.
///
/// NETWORK SAFETY: this database can live on an SMB share with several
/// workstations open at once. WAL is deliberately NOT used — it relies on
/// shared memory that does not work over a network filesystem and is the
/// documented way to corrupt a shared SQLite file. A rollback journal
/// (TRUNCATE) is network-safe; busy_timeout makes concurrent writers wait for
/// the lock instead of erroring.
/// </summary>
public sealed class History : IDisposable
{
    // Seconds a writer waits for the lock before giving up.
    private const int BusyTimeoutSeconds = 30;

    public static readonly string[] Columns =
    {
        "id", "ts_utc", "original_path", "original_name", "new_name",
        "name_entered", "naming_mode", "suffix_applied", "route_label",
        "route_path", "tagged", "collision_suffix", "reverted", "reverted_ts",
    };

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS history (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          ts_utc TEXT NOT NULL,
          original_path TEXT NOT NULL,
          original_name TEXT NOT NULL,
          new_name TEXT NOT NULL,
          name_entered TEXT NOT NULL,
          naming_mode TEXT NOT NULL,
          suffix_applied TEXT NOT NULL DEFAULT '',
          route_label TEXT NOT NULL,
          route_path TEXT NOT NULL,
          tagged INTEGER NOT NULL DEFAULT 0,
          collision_suffix TEXT NOT NULL DEFAULT '',
          reverted INTEGER NOT NULL DEFAULT 0,
          reverted_ts TEXT NOT NULL DEFAULT ''
        )
        """;

    public string Path { get; }
    private readonly SqliteConnection _conn;

    public History(string dbPath)
    {
        Path = dbPath;
        var parent = System.IO.Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            DefaultTimeout = BusyTimeoutSeconds,   // busy-wait for the lock
            Pooling = false,                       // release the file on close
                                                   // — matters on a network share
        }.ToString());
        _conn.Open();
        Exec($"PRAGMA busy_timeout={BusyTimeoutSeconds * 1000}");
        Exec("PRAGMA journal_mode=TRUNCATE");      // NOT wal — see class doc
        Exec("PRAGMA synchronous=FULL");
        Exec(Schema);
        Migrate();
    }

    public static string UtcNow() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz");

    private void Migrate()
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(history)";
            using var r = cmd.ExecuteReader();
            while (r.Read()) cols.Add(r.GetString(1));
        }
        if (!cols.Contains("reverted_ts"))
            Exec("ALTER TABLE history ADD COLUMN reverted_ts TEXT NOT NULL DEFAULT ''");
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public long LogCommit(
        string originalPath, string originalName, string newName,
        string nameEntered, string namingMode, string suffixApplied,
        string routeLabel, string routePath, bool tagged, string collisionSuffix)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO history (ts_utc, original_path, original_name, new_name,
              name_entered, naming_mode, suffix_applied, route_label, route_path,
              tagged, collision_suffix, reverted)
            VALUES ($ts, $op, $on, $nn, $ne, $nm, $sa, $rl, $rp, $tg, $cs, 0);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$ts", UtcNow());
        cmd.Parameters.AddWithValue("$op", originalPath);
        cmd.Parameters.AddWithValue("$on", originalName);
        cmd.Parameters.AddWithValue("$nn", newName);
        cmd.Parameters.AddWithValue("$ne", nameEntered);
        cmd.Parameters.AddWithValue("$nm", namingMode);
        cmd.Parameters.AddWithValue("$sa", suffixApplied);
        cmd.Parameters.AddWithValue("$rl", routeLabel);
        cmd.Parameters.AddWithValue("$rp", routePath);
        cmd.Parameters.AddWithValue("$tg", tagged ? 1 : 0);
        cmd.Parameters.AddWithValue("$cs", collisionSuffix);
        return (long)cmd.ExecuteScalar()!;
    }

    public void MarkReverted(long rowId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "UPDATE history SET reverted = 1, reverted_ts = $ts WHERE id = $id";
        cmd.Parameters.AddWithValue("$ts", UtcNow());
        cmd.Parameters.AddWithValue("$id", rowId);
        cmd.ExecuteNonQuery();
    }

    public int Count()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM history";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<IReadOnlyDictionary<string, object>> Rows(int? limit = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM history ORDER BY id DESC" +
                          (limit is { } n ? $" LIMIT {n}" : "");
        return Read(cmd);
    }

    /// <summary>Distinct committed names, most recently used first, then by how
    /// often — the autocomplete order. Blank and reverted rows don't count.</summary>
    public List<string> RankedNames()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT name_entered FROM history" +
            " WHERE name_entered != '' AND reverted = 0" +
            " GROUP BY name_entered" +
            " ORDER BY MAX(ts_utc) DESC, COUNT(*) DESC, MAX(id) DESC";
        var names = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) names.Add(r.GetString(0));
        return names;
    }

    /// <summary>Write the whole table to CSV (Excel-friendly BOM), chronological.
    /// Returns the row count. Cells that a spreadsheet would read as a formula
    /// (=, +, -, @, tab, CR) get a leading apostrophe so opening the file can't
    /// execute anything.</summary>
    public int ExportCsv(string dest)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM history ORDER BY id";
        var rows = Read(cmd);
        using var writer = new StreamWriter(dest, false, new System.Text.UTF8Encoding(true));
        writer.WriteLine(string.Join(",", Columns.Select(CsvField)));
        foreach (var row in rows)
            writer.WriteLine(string.Join(",",
                Columns.Select(c => CsvField(row.TryGetValue(c, out var v) ? v?.ToString() ?? "" : ""))));
        return rows.Count;
    }

    private static string CsvField(string value)
    {
        // formula-injection guard first
        if (value.Length > 0 && "=+-@\t\r".IndexOf(value[0]) >= 0)
            value = "'" + value;
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            value = "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private static List<IReadOnlyDictionary<string, object>> Read(SqliteCommand cmd)
    {
        var rows = new List<IReadOnlyDictionary<string, object>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = new Dictionary<string, object>();
            for (var i = 0; i < r.FieldCount; i++)
                row[r.GetName(i)] = r.IsDBNull(i) ? "" : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Return the actual journal mode SQLite is using — "truncate"
    /// on a network share, never "wal". Exposed for the network-safety test.</summary>
    public string JournalMode()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode";
        return (string)cmd.ExecuteScalar()!;
    }

    public void Dispose() => _conn.Dispose();
}
