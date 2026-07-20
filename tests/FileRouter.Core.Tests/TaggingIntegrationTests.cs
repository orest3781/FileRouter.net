using FileRouter.Core;
using PdfSharp.Pdf;

namespace FileRouter.Core.Tests;

/// <summary>Commit with tagging on, against REAL PdfSharp PDFs.</summary>
public class TaggingIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tagint_" + Guid.NewGuid());
    private readonly string _inbox, _dest;

    public TaggingIntegrationTests()
    {
        _inbox = Path.Combine(_root, "inbox");
        _dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(_inbox);
        Directory.CreateDirectory(_dest);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var a = 0; ; a++)
        {
            try { Directory.Delete(_root, true); return; }
            catch (IOException) when (a < 10) { GC.Collect(); GC.WaitForPendingFinalizers(); Thread.Sleep(50); }
        }
    }

    private string MakePdf(string name)
    {
        var p = Path.Combine(_inbox, name);
        using var doc = new PdfDocument();
        doc.AddPage();
        doc.Save(p);
        return p;
    }

    private Route Dest => new() { Label = "Invoices", Path = _dest };

    [Fact]
    public void CommitTagsTheFiledPdf()
    {
        var src = MakePdf("20240115--1234567890.pdf");
        var outcome = Commit.CommitFile(src, "SMITH JOHN", Dest, "replace", tagEnabled: true);
        Assert.True(outcome.Tagged);
        Assert.Equal(("Invoices", "Invoices"), Tagger.ReadMeta(outcome.NewPath!));
    }

    [Fact]
    public void TagWithRouteOffLeavesMetadataEmpty()
    {
        var src = MakePdf("20240115--1234567890.pdf");
        var outcome = Commit.CommitFile(src, "SMITH JOHN", Dest, "replace", tagEnabled: false);
        Assert.False(outcome.Tagged);
        Assert.Equal(("", ""), Tagger.ReadMeta(outcome.NewPath!));
    }

    [Fact]
    public void UndoRestoresPriorMetadata()
    {
        using var h = new History(Path.Combine(_root, "h.sqlite"));
        var cfg = new Config { Inbox = _inbox, TagWithRoute = true, NamingMode = "replace" };
        var s = new Session(cfg, h);
        var a = MakePdf("20240101--1.pdf");
        s.Start(new[] { a });
        s.CommitCurrent("SMITH JOHN", Dest);
        Assert.Equal(("Invoices", "Invoices"), Tagger.ReadMeta(Path.Combine(_dest, "SMITH JOHN.pdf")));

        s.UndoLast();
        Assert.True(File.Exists(a));
        Assert.Equal(("", ""), Tagger.ReadMeta(a));   // metadata restored to empty
    }

    [Fact]
    public void IllegalNameNeverTagsTheSource()
    {
        // reserved-char rejection happens BEFORE tagging, so the source's
        // metadata is never touched by a doomed commit
        var src = MakePdf("20240115--1234567890.pdf");
        Assert.Throws<CommitError>(() =>
            Commit.CommitFile(src, "SMITH:JOHN", Dest, "replace", tagEnabled: true));
        Assert.Equal(("", ""), Tagger.ReadMeta(src));   // untouched
        Assert.True(File.Exists(src));
    }
}
