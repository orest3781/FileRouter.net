using FileRouter.Core;

namespace FileRouter.Core.Tests;

public class CompleterAndExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "compexp_" + Guid.NewGuid());
    public CompleterAndExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var a = 0; ; a++)
        {
            try { Directory.Delete(_dir, true); return; }
            catch (IOException) when (a < 10) { GC.Collect(); GC.WaitForPendingFinalizers(); Thread.Sleep(50); }
        }
    }

    private History NewHistory() => new(Path.Combine(_dir, "h.sqlite"));

    // ---- ranked names ----
    [Fact]
    public void RecencyBeatsFrequency()
    {
        using var h = NewHistory();
        // "OLD" filed 3×, "NEW" filed once but more recently
        h.LogCommit("p", "x.pdf", "OLD.pdf", "OLD FREQUENT", "insert", "", "A", "q", false, "");
        h.LogCommit("p", "x.pdf", "OLD.pdf", "OLD FREQUENT", "insert", "", "A", "q", false, "");
        h.LogCommit("p", "x.pdf", "OLD.pdf", "OLD FREQUENT", "insert", "", "A", "q", false, "");
        Thread.Sleep(1100);  // ensure a later ts (seconds resolution)
        h.LogCommit("p", "x.pdf", "NEW.pdf", "NEW RARE", "insert", "", "A", "q", false, "");
        Assert.Equal(new[] { "NEW RARE", "OLD FREQUENT" }, h.RankedNames());
    }

    [Fact]
    public void BlankAndRevertedExcluded()
    {
        using var h = NewHistory();
        h.LogCommit("p", "x.pdf", "x.pdf", "", "insert", "", "A", "q", false, "");          // blank
        var rev = h.LogCommit("p", "x.pdf", "R.pdf", "REVERTED", "insert", "", "A", "q", false, "");
        h.MarkReverted(rev);
        h.LogCommit("p", "x.pdf", "K.pdf", "KEPT", "insert", "", "A", "q", false, "");
        Assert.Equal(new[] { "KEPT" }, h.RankedNames());
    }

    // ---- seed file + merge ----
    [Fact]
    public void SeedFileParsing()
    {
        var p = Path.Combine(_dir, "names.txt");
        File.WriteAllText(p, "# comment\nSMITH JOHN\n\n   \nGARCIA MARIA  \n# another\n");
        Assert.Equal(new[] { "SMITH JOHN", "GARCIA MARIA" }, Completer.LoadSeedNames(p));
    }

    [Fact]
    public void MissingSeedFileIsEmpty()
    {
        Assert.Empty(Completer.LoadSeedNames(""));
        Assert.Empty(Completer.LoadSeedNames(@"Z:\nope\names.txt"));
    }

    [Fact]
    public void HistoryOutranksSeedsAndDedupes()
    {
        using var h = NewHistory();
        h.LogCommit("p", "x.pdf", "G.pdf", "GARCIA MARIA", "insert", "", "A", "q", false, "");
        var names = Completer.Names(h, new[] { "SMITH JOHN", "GARCIA MARIA" });
        Assert.Equal(new[] { "GARCIA MARIA", "SMITH JOHN" }, names);  // history first, no dup
    }

    // ---- CSV export ----
    [Fact]
    public void ExportWritesHeaderAndRows()
    {
        using var h = NewHistory();
        h.LogCommit("p", "o.pdf", "SMITH JOHN_INVOICE (2).pdf", "SMITH JOHN", "insert",
            "_INVOICE", "Invoices", "q", true, " (2)");
        var dest = Path.Combine(_dir, "out.csv");
        Assert.Equal(1, h.ExportCsv(dest));
        var text = File.ReadAllText(dest);
        Assert.Contains("collision_suffix", text.Split('\n')[0]);   // header
        Assert.Contains("SMITH JOHN", text);
        Assert.Contains("_INVOICE", text);
    }

    [Fact]
    public void ExportNeutralizesFormulaCells()
    {
        using var h = NewHistory();
        h.LogCommit("p", "o.pdf", "n.pdf", "=SUM(A1:A9)", "insert", "", "A", "q", false, "");
        var dest = Path.Combine(_dir, "out.csv");
        h.ExportCsv(dest);
        Assert.Contains("'=SUM(A1:A9)", File.ReadAllText(dest));   // leading apostrophe
    }

    [Fact]
    public void ExportQuotesCommasAndQuotes()
    {
        using var h = NewHistory();
        h.LogCommit("p", "o.pdf", "n.pdf", "DOE, JANE \"JJ\"", "insert", "", "A", "q", false, "");
        var dest = Path.Combine(_dir, "out.csv");
        h.ExportCsv(dest);
        Assert.Contains("\"DOE, JANE \"\"JJ\"\"\"", File.ReadAllText(dest));
    }
}
