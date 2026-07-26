using FileRouter.Core;

namespace FileRouter.Core.Tests;

/// <summary>The document moves first, the audit row is written second. If the
/// write fails the move has ALREADY happened — the session must advance and say
/// so, never leave the queue pointing at a file that is no longer there (the
/// next commit would log it as &lt;vanished&gt;, which would be a lie).</summary>
public class AuditFailureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fraudit_" + Guid.NewGuid());
    private readonly string _inbox, _dest, _deferred;

    public AuditFailureTests()
    {
        _inbox = Path.Combine(_root, "inbox");
        _dest = Path.Combine(_root, "dest");
        _deferred = Path.Combine(_root, "deferred");
        foreach (var d in new[] { _inbox, _dest, _deferred }) Directory.CreateDirectory(d);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var a = 0; ; a++)
        {
            try { Directory.Delete(_root, true); return; }
            catch (IOException) when (a < 10) { Thread.Sleep(50); }
        }
    }

    private string MakePdf(string name)
    {
        var p = Path.Combine(_inbox, name);
        File.WriteAllBytes(p, new byte[] { 1, 2, 3 });
        return p;
    }

    private (Session, History) Broken(out Config cfg)
    {
        cfg = new Config { Inbox = _inbox, Deferred = _deferred };
        var history = new History(Path.Combine(_root, "h.sqlite"));
        var session = new Session(cfg, history);
        history.Dispose();   // the audit DB dies mid-session
        return (session, history);
    }

    [Fact]
    public void AFailedAuditWriteStillReportsWhereTheDocumentWent()
    {
        var (session, _) = Broken(out _);
        var src = MakePdf("20240115--111111.pdf");
        var second = MakePdf("20240115--222222.pdf");
        session.Start(new[] { src, second });

        var ex = Assert.Throws<AuditError>(() =>
            session.CommitCurrent("SMITH", new Route { Label = "R", Path = _dest }));

        // the truth the user needs: it IS filed, and here is where
        Assert.Equal(Path.Combine(_dest, "20240115-SMITH-111111.pdf"), ex.NewPath);
        Assert.True(File.Exists(ex.NewPath));
        Assert.False(File.Exists(src));
        Assert.Contains("20240115-SMITH-111111.pdf", ex.Message);
    }

    [Fact]
    public void AFailedAuditWriteAdvancesTheQueueSoNothingIsLoggedAsVanished()
    {
        var (session, _) = Broken(out _);
        var src = MakePdf("20240115--111111.pdf");
        var second = MakePdf("20240115--222222.pdf");
        session.Start(new[] { src, second });

        Assert.Throws<AuditError>(() =>
            session.CommitCurrent("SMITH", new Route { Label = "R", Path = _dest }));

        Assert.Equal(second, session.Current);   // moved on, not stuck
        Assert.Equal(1, session.Filed);
        Assert.Equal(0, session.Vanished);
    }

    [Fact]
    public void AFailedAuditWriteOnSetAsideBehavesTheSameWay()
    {
        var (session, _) = Broken(out _);
        var src = MakePdf("20240115--111111.pdf");
        session.Start(new[] { src });

        var ex = Assert.Throws<AuditError>(() => session.SkipCurrent());

        Assert.True(File.Exists(ex.NewPath));
        Assert.False(File.Exists(src));
        Assert.True(session.Done);
        Assert.Equal(1, session.Skipped);
    }

    [Fact]
    public void AFailedVanishedLogStillAdvancesPastTheMissingFile()
    {
        var (session, _) = Broken(out _);
        var gone = Path.Combine(_inbox, "20240115--111111.pdf");   // never created
        session.Start(new[] { gone });

        Assert.Throws<AuditError>(() =>
            session.CommitCurrent("X", new Route { Label = "R", Path = _dest }));

        Assert.True(session.Done);      // must not loop forever on a ghost
        Assert.Equal(1, session.Vanished);
    }

    [Fact]
    public void UndoStillWorksAfterAnUnrecordedCommit()
    {
        var cfg = new Config { Inbox = _inbox, Deferred = _deferred };
        var history = new History(Path.Combine(_root, "h.sqlite"));
        var session = new Session(cfg, history);
        var src = MakePdf("20240115--111111.pdf");
        session.Start(new[] { src });
        history.Dispose();

        Assert.Throws<AuditError>(() =>
            session.CommitCurrent("SMITH", new Route { Label = "R", Path = _dest }));
        Assert.True(session.CanUndo);

        // the physical move is reversible even though no row was written
        var (filed, original) = session.UndoLast();
        Assert.Equal(src, original);
        Assert.True(File.Exists(src));
        Assert.False(File.Exists(filed));
        Assert.Equal(0, session.Filed);
    }

    [Fact]
    public void AHealthyHistoryStillLogsNormally()
    {
        var cfg = new Config { Inbox = _inbox, Deferred = _deferred };
        using var history = new History(Path.Combine(_root, "ok.sqlite"));
        var session = new Session(cfg, history);
        var src = MakePdf("20240115--111111.pdf");
        session.Start(new[] { src });

        var outcome = session.CommitCurrent("SMITH", new Route { Label = "R", Path = _dest });
        Assert.False(outcome.Vanished);
        Assert.Equal(1, history.Count());
        Assert.Single(session.RowIds);
    }
}
