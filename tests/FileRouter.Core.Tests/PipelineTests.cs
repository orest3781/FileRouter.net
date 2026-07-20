using FileRouter.Core;

namespace FileRouter.Core.Tests;

/// <summary>Config, Scanner, Commit, and Session — the filing-loop logic.</summary>
public class PipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pltest_" + Guid.NewGuid());
    private readonly string _inbox, _dest, _deferred;

    public PipelineTests()
    {
        _inbox = Path.Combine(_root, "inbox");
        _dest = Path.Combine(_root, "dest");
        _deferred = Path.Combine(_root, "deferred");
        foreach (var d in new[] { _inbox, _dest, _deferred }) Directory.CreateDirectory(d);
    }

    public void Dispose()
    {
        SqliteClear();
        for (var a = 0; ; a++)
        {
            try { Directory.Delete(_root, true); return; }
            catch (IOException) when (a < 10) { GC.Collect(); GC.WaitForPendingFinalizers(); Thread.Sleep(50); }
        }
    }
    private static void SqliteClear() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

    private string MakePdf(string dir, string name)
    {
        var p = Path.Combine(dir, name);
        File.WriteAllBytes(p, new byte[] { 1, 2, 3 });
        return p;
    }

    private Route Dest => new() { Label = "R", Path = _dest };

    // ---- Config ----
    [Fact]
    public void ConfigDefaultsAndRoundTrip()
    {
        var path = Path.Combine(_root, "c.json");
        var cfg = Config.Load(path);   // first run writes defaults
        Assert.Equal("size_desc", cfg.Sort);         // biggest-first default
        Assert.True(cfg.UppercaseNames);
        Assert.Equal("insert", cfg.NamingMode);
        Assert.True(File.Exists(path));

        cfg.Inbox = "C:/ünïcode";
        cfg.Routes.Add(new Route { Label = "Café", Path = "C:/x", Color = "#abc" });
        Config.Save(cfg, path);
        var back = Config.Load(path);
        Assert.Equal("C:/ünïcode", back.Inbox);
        Assert.Equal("Café", back.Routes[0].Label);
        Assert.Equal("#abc", back.Routes[0].Color);
    }

    [Fact]
    public void ConfigUnknownKeysSurvive()
    {
        var path = Path.Combine(_root, "c.json");
        File.WriteAllText(path, """{"inbox":"C:/in","my_note":"hands off"}""");
        var cfg = Config.Load(path);
        Config.Save(cfg, path);
        Assert.Contains("my_note", File.ReadAllText(path));
    }

    [Fact]
    public void ConfigBadSortIsReadable()
    {
        var path = Path.Combine(_root, "c.json");
        File.WriteAllText(path, """{"sort":"banana"}""");
        Assert.Throws<ConfigException>(() => Config.Load(path));
    }

    // ---- Scanner ----
    [Fact]
    public void ScanSeparatesMatchingAndIgnored()
    {
        MakePdf(_inbox, "20240115--1234567890.pdf");
        MakePdf(_inbox, "20240116--222.pdf");
        MakePdf(_inbox, "scan_001.pdf");
        File.WriteAllText(Path.Combine(_inbox, "notes.txt"), "x");
        var r = Scanner.Scan(_inbox, "filename_asc");
        Assert.Equal(2, r.Count);
        Assert.Equal(2, r.IgnoredCount);
    }

    [Fact]
    public void ScanDefaultIsBiggestFirst()
    {
        File.WriteAllBytes(Path.Combine(_inbox, "20240101--1.pdf"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_inbox, "20240102--2.pdf"), new byte[300]);
        var r = Scanner.Scan(_inbox);   // no sort arg -> size_desc
        Assert.Equal("20240102--2.pdf", Path.GetFileName(r.Matching[0]));
    }

    [Fact]
    public void ScanMissingFolderIsMessageNotCrash() =>
        Assert.Contains("does not exist", Scanner.Scan(@"Z:\nope\gone").Error);

    // ---- Commit ----
    [Fact]
    public void CommitMovesAndRenames()
    {
        var src = MakePdf(_inbox, "20240115--1234567890.pdf");
        var outcome = Commit.CommitFile(src, "SMITH JOHN", Dest, "insert", tagEnabled: false);
        Assert.False(File.Exists(src));
        Assert.True(File.Exists(Path.Combine(_dest, "20240115-SMITH JOHN-1234567890.pdf")));
    }

    [Fact]
    public void CommitCollisionCountsAndNeverOverwrites()
    {
        File.WriteAllText(Path.Combine(_dest, "SMITH JOHN.pdf"), "existing");
        var src = MakePdf(_inbox, "20240115--1234567890.pdf");
        var outcome = Commit.CommitFile(src, "SMITH JOHN", Dest, "replace", tagEnabled: false);
        Assert.Equal("SMITH JOHN (2).pdf", Path.GetFileName(outcome.NewPath!));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(_dest, "SMITH JOHN.pdf")));
    }

    [Fact]
    public void CommitColonNameFailsReadablyFileStays()
    {
        var src = MakePdf(_inbox, "20240115--1234567890.pdf");
        var ex = Assert.Throws<CommitError>(() =>
            Commit.CommitFile(src, "SMITH:JOHN", Dest, "replace", tagEnabled: false));
        Assert.Contains("can't contain", ex.Message);
        Assert.True(File.Exists(src));   // untouched — no ADS data loss
        Assert.Empty(Directory.GetFiles(_dest));
    }

    [Fact]
    public void CommitMissingDestIsReadableAndFileStays()
    {
        var src = MakePdf(_inbox, "20240115--1234567890.pdf");
        var route = new Route { Label = "R", Path = Path.Combine(_root, "gone") };
        Assert.Throws<CommitError>(() => Commit.CommitFile(src, "X", route, "insert", tagEnabled: false));
        Assert.True(File.Exists(src));
    }

    // ---- Session (with real history) ----
    [Fact]
    public void SessionCommitLogsAndAdvances()
    {
        using var h = new History(Path.Combine(_root, "h.sqlite"));
        var cfg = new Config { Inbox = _inbox, Deferred = _deferred, NamingMode = "insert" };
        var s = new Session(cfg, h);
        var a = MakePdf(_inbox, "20240101--1.pdf");
        var b = MakePdf(_inbox, "20240102--2.pdf");
        s.Start(new[] { a, b });
        s.CommitCurrent("SMITH JOHN", Dest);
        Assert.Equal(1, s.Pos);
        Assert.Equal(1, s.Filed);
        Assert.Equal(b, s.Current);
        var row = h.Rows(1)[0];
        Assert.Equal("SMITH JOHN", row["name_entered"]);
        Assert.Equal("20240101-SMITH JOHN-1.pdf", row["new_name"]);
    }

    [Fact]
    public void SessionUndoRoundTrip()
    {
        using var h = new History(Path.Combine(_root, "h.sqlite"));
        var cfg = new Config { Inbox = _inbox, Deferred = _deferred };
        var s = new Session(cfg, h);
        var a = MakePdf(_inbox, "20240101--1.pdf");
        s.Start(new[] { a });
        s.CommitCurrent("SMITH JOHN", Dest);
        Assert.False(File.Exists(a));

        var (_, original, _) = s.UndoLast();
        Assert.True(File.Exists(a));         // file is back
        Assert.Equal(0, s.Pos);              // current again
        Assert.Equal(0, s.Filed);
        Assert.Equal(1L, Convert.ToInt64(h.Rows(1)[0]["reverted"]));
    }

    [Fact]
    public void SessionSkipAndExtend()
    {
        using var h = new History(Path.Combine(_root, "h.sqlite"));
        var cfg = new Config { Inbox = _inbox, Deferred = _deferred };
        var s = new Session(cfg, h);
        var a = MakePdf(_inbox, "20240101--1.pdf");
        s.Start(new[] { a });
        Assert.Equal(1, s.Total);
        var b = MakePdf(_inbox, "20240102--2.pdf");
        Assert.Equal(1, s.Extend(new[] { a, b }));   // only b is new
        Assert.Equal(2, s.Total);
        s.SkipCurrent();
        Assert.Equal(1, s.Skipped);
        Assert.True(File.Exists(Path.Combine(_deferred, "20240101--1.pdf")));
    }
}
