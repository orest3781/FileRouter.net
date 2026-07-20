using FileRouter.Core;
using static FileRouter.Core.MatchMerge;

namespace FileRouter.Core.Tests;

public class MatchMergeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mmtest_" + Guid.NewGuid());

    private const string Csv =
        "First Name,Last Name,Control ID,DOB\n" +
        "Adam,Brown,566379260,4/25/1966\n" +
        "Adam,Brown,696009058,11/10/1955\n" +
        "Frank,Evans,176797656,8/9/1997\n" +
        "Maria,De La Cruz,111222333,3/4/1970\n" +
        "Mary,Smith-Jones,444555666,1/2/1980\n" +
        ",Missing,999,\n" +                            // no first name: ignored
        "Adam,Brown,566379260,4/25/1966\n";            // exact dup: collapsed

    public MatchMergeTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private Roster LoadRoster()
    {
        var path = Path.Combine(_dir, "roster.csv");
        File.WriteAllText(path, Csv, new System.Text.UTF8Encoding(true)); // BOM
        return MatchMerge.LoadRoster(path, "First Name", "Last Name", "Control ID");
    }

    private string Touch(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void LoadsAndDedupes()
    {
        var roster = LoadRoster();
        Assert.Equal(2, roster.Lookup("BROWN", "ADAM").Count);
        Assert.Single(roster.Lookup("EVANS", "FRANK"));
        Assert.Empty(roster.Lookup("MISSING", ""));
        Assert.Equal("8/9/1997", roster.Lookup("EVANS", "FRANK")[0].Row["DOB"]);
    }

    [Fact]
    public void MissingHeaderIsReadable()
    {
        var path = Path.Combine(_dir, "r.csv");
        File.WriteAllText(path, Csv);
        var ex = Assert.Throws<RosterException>(() =>
            MatchMerge.LoadRoster(path, "FNAME", "Last Name", "Control ID"));
        Assert.Contains("FNAME", ex.Message);
        Assert.Contains("First Name", ex.Message);  // lists what was found
    }

    [Fact]
    public void MatchingIsCaseAndSeparatorInsensitive()
    {
        var roster = LoadRoster();
        Assert.Single(roster.Lookup("de la cruz", "maria"));
        Assert.Single(roster.Lookup("DE_LA_CRUZ", "MARIA"));
    }

    [Fact]
    public void MergedConventionReading() =>
        Assert.Equal(("BROWN", "ADAM"), NameCandidates("20240126-BROWN-ADAM")[0]);

    [Fact]
    public void HyphenatedNamesOfferEverySplit()
    {
        var readings = NameCandidates("20240126-SMITH-JONES-MARY");
        Assert.Contains(("SMITH-JONES", "MARY"), readings);
        Assert.Contains(("SMITH", "JONES-MARY"), readings);
    }

    [Fact]
    public void TrailingIdIgnoredForReading() =>
        Assert.Equal(("BROWN", "ADAM"), NameCandidates("20240126-BROWN-ADAM-566379260")[0]);

    [Fact]
    public void UniqueMatchMerges()
    {
        var r = MatchFiles(new[] { Touch("20240126-EVANS-FRANK.pdf") }, LoadRoster())[0];
        Assert.Equal("merge", r.Status);
        Assert.Equal("20240126-EVANS-FRANK-176797656", r.NewStem);
    }

    [Fact]
    public void TwoIdsIsAmbiguous()
    {
        var r = MatchFiles(new[] { Touch("20240126-BROWN-ADAM.pdf") }, LoadRoster())[0];
        Assert.Equal("ambiguous", r.Status);
        Assert.Equal(new[] { "566379260", "696009058" },
            r.Candidates!.Select(c => c.ControlId).OrderBy(x => x));
    }

    [Fact]
    public void UnknownNameIsNoMatch() =>
        Assert.Equal("no_match",
            MatchFiles(new[] { Touch("20240126-SMITH-JOHN.pdf") }, LoadRoster())[0].Status);

    [Fact]
    public void NoNameInFilename() =>
        Assert.Equal("no_name",
            MatchFiles(new[] { Touch("scan_001.pdf") }, LoadRoster())[0].Status);

    [Fact]
    public void AlreadyMergedIsDetected() =>
        Assert.Equal("already",
            MatchFiles(new[] { Touch("20240126-EVANS-FRANK-176797656.pdf") }, LoadRoster())[0].Status);

    [Fact]
    public void HyphenatedSurnameResolvedByRoster()
    {
        var r = MatchFiles(new[] { Touch("20240126-SMITH-JONES-MARY.pdf") }, LoadRoster())[0];
        Assert.Equal("merge", r.Status);
        Assert.Equal("20240126-SMITH-JONES-MARY-444555666", r.NewStem);
    }

    [Fact]
    public void RawReviewFileMatchesToo()
    {
        var f = Touch("EVANS_FRANK_8_9_1997_NYCHSRO_MEDREVIEW_176-1_X.pdf");
        Assert.Equal("merge", MatchFiles(new[] { f }, LoadRoster())[0].Status);
    }

    [Fact]
    public void ExecuteMergesRenamesOnlyUnambiguous()
    {
        var sure = Touch("20240126-EVANS-FRANK.pdf");
        var unsure = Touch("20240126-BROWN-ADAM.pdf");
        var results = MatchFiles(new[] { sure, unsure }, LoadRoster());
        var outcomes = ExecuteMerges(results);
        Assert.Single(outcomes);
        Assert.True(File.Exists(Path.Combine(_dir, "20240126-EVANS-FRANK-176797656.pdf")));
        Assert.True(File.Exists(unsure));   // ambiguous: untouched
    }

    [Fact]
    public void MergeOneAppliesTriageDecision()
    {
        var f = Touch("20240126-BROWN-ADAM.pdf");
        var outcome = MergeOne(f, "696009058")[0];
        Assert.Equal("20240126-BROWN-ADAM-696009058.pdf", Path.GetFileName(outcome.Final!));
    }

    [Fact]
    public void MergeNeverOverwrites()
    {
        Touch("20240126-EVANS-FRANK-176797656.pdf");   // target already on disk
        var f = Touch("20240126-EVANS-FRANK.pdf");
        var outcome = ExecuteMerges(MatchFiles(new[] { f }, LoadRoster()))[0];
        Assert.Equal("20240126-EVANS-FRANK-176797656 (2).pdf", Path.GetFileName(outcome.Final!));
    }

    [Fact]
    public void QuotedCsvFieldsWithCommas()
    {
        var path = Path.Combine(_dir, "q.csv");
        File.WriteAllText(path,
            "First Name,Last Name,Control ID,Note\n" +
            "Frank,Evans,176797656,\"Smith, Jones & Co\"\n");
        var roster = MatchMerge.LoadRoster(path, "First Name", "Last Name", "Control ID");
        Assert.Equal("Smith, Jones & Co", roster.Lookup("EVANS", "FRANK")[0].Row["Note"]);
    }
}
