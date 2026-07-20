using FileRouter.Core;

namespace FileRouter.Core.Tests;

/// <summary>QC probes for suspected porting gaps vs the Python original.
/// These encode the EXPECTED (Python-parity or better) behaviour — a failure
/// here is a porting bug to fix.</summary>
public class QcTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qctest_" + Guid.NewGuid());

    public QcTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void RouteUnknownKeysSurviveRoundTrip()
    {
        // Python parity: hand-edited per-route keys must not be lost on save
        var path = Path.Combine(_dir, "c.json");
        File.WriteAllText(path, """
            {"inbox":"C:/in","routes":[
              {"label":"A","path":"C:/a","my_custom_key":"must survive"}]}
            """);
        var cfg = Config.Load(path);
        Config.Save(cfg, path);
        Assert.Contains("my_custom_key", File.ReadAllText(path));
        Assert.Contains("must survive", File.ReadAllText(path));
    }

    [Fact]
    public void BulkRenameOverrideWithColonFailsSoftNoAdsNoLoss()
    {
        // The ADS trap: a hand-edited "SMITH:JOHN" must never hide the file
        // in an NTFS stream. Expected: readable per-file failure, source kept.
        var src = Path.Combine(_dir, "file.pdf");
        File.WriteAllText(src, "IMPORTANT");
        var plans = BulkRename.Plan(new[] { src }, new BulkRename.RenameOp(),
            new Dictionary<string, string> { [src] = "SMITH:JOHN" });
        // better than fail-at-execute: rejected readably at PLAN time
        Assert.False(plans[0].Changed);
        Assert.Contains("can't contain", plans[0].Note);

        var outcomes = BulkRename.Execute(plans);
        Assert.True(File.Exists(src), "source must be untouched");
        Assert.Equal("IMPORTANT", File.ReadAllText(src));
        // no ghost 0-byte "SMITH" file may exist (the ADS host)
        Assert.False(File.Exists(Path.Combine(_dir, "SMITH")));
        Assert.Empty(outcomes);   // nothing was attempted
    }

    [Fact]
    public void MatchMergeControlIdWithColonIsSafeToo()
    {
        // a roster could contain a malformed id like "12:34" — merging it
        // must fail-soft, never overwrite or hide the document
        var src = Path.Combine(_dir, "20240126-EVANS-FRANK.pdf");
        File.WriteAllText(src, "DOC");
        var outcomes = MatchMerge.MergeOne(src, "12:34");
        Assert.True(File.Exists(src));
        Assert.Equal("DOC", File.ReadAllText(src));
        if (outcomes.Count > 0) Assert.Null(outcomes[0].Final);
    }
}
