using FileRouter.Core;
using static FileRouter.Core.BulkRename;

namespace FileRouter.Core.Tests;

public class BulkRenameParserTests
{
    private const string Tail = "_2_18_1944_NYCHSRO_MEDREVIEW_605151339-1_01_26_24_X";

    [Fact]
    public void StandardLayout() =>
        Assert.Equal(("BROWN", "ADAM"), ParseReviewStem(
            "BROWN_ADAM_4_25_1966_NYCHSRO_MEDREVIEW_566379260-1_01_26_24_1007_DUNN_TRACY_A_MultiCare"));

    [Theory]
    [InlineData("BROWN_III_DAVID_2_18_1944_X", "BROWN", "DAVID")]   // gen between
    [InlineData("EVANS_JR_EDWARD_6_24_1951_X", "EVANS", "EDWARD")]
    [InlineData("BROWN_DAVID_JR_2_18_1944_X", "BROWN", "DAVID")]    // gen after
    public void Generations(string stem, string last, string first) =>
        Assert.Equal((last, first), ParseReviewStem(stem));

    [Fact]
    public void SpaceInsteadOfUnderscore() =>
        Assert.Equal(("EVANS", "BRIAN"),
            ParseReviewStem("EVANS_BRIAN 5_14_1998_NYCHSRO_MEDREVIEW_939471082-1_X"));

    [Fact]
    public void FullyMangledSeparators() =>
        Assert.Equal(("JONES", "ADAM"),
            ParseReviewStem("JONES_ADAM_8 2_1962_NYCHSRO MEDREVIEW 608879482-1 01 26_24_X"));

    [Theory]
    [InlineData("JR")] [InlineData("SR")] [InlineData("JR.")] [InlineData("SENIOR")]
    [InlineData("II")] [InlineData("III")] [InlineData("IV")] [InlineData("V")]
    [InlineData("VI")] [InlineData("VII")] [InlineData("VIII")] [InlineData("IX")]
    [InlineData("X")] [InlineData("2ND")] [InlineData("3RD")] [InlineData("4TH")]
    public void AllGenerationFormsBothPositions(string gen)
    {
        Assert.Equal(("BROWN", "DAVID"), ParseReviewStem($"BROWN_{gen}_DAVID{Tail}"));
        Assert.Equal(("BROWN", "DAVID"), ParseReviewStem($"BROWN_DAVID_{gen}{Tail}"));
    }

    [Theory]
    [InlineData("BROWN_VICTOR", "BROWN", "VICTOR")]   // VI is a prefix of VICTOR
    [InlineData("BROWN_XAVIER", "BROWN", "XAVIER")]   // X prefix of XAVIER
    [InlineData("IVERSON_ALLEN", "IVERSON", "ALLEN")] // IV inside a surname
    public void NamesStartingWithGenLettersNotEaten(string head, string last, string first) =>
        Assert.Equal((last, first), ParseReviewStem($"{head}{Tail}"));

    [Theory]
    [InlineData("VAN_DYKE_JOHN", "VAN DYKE", "JOHN")]
    [InlineData("DE_LA_CRUZ_MARIA", "DE LA CRUZ", "MARIA")]
    [InlineData("VAN_DER_BERG_HANS", "VAN DER BERG", "HANS")]
    [InlineData("MC_DONALD_JOHN", "MC DONALD", "JOHN")]
    [InlineData("ST_JOHN_MARY", "ST JOHN", "MARY")]
    [InlineData("VAN DYKE_JOHN", "VAN DYKE", "JOHN")]   // mixed separators
    public void ParticleSurnamesKeptTogether(string head, string last, string first) =>
        Assert.Equal((last, first), ParseReviewStem($"{head}{Tail}"));

    [Fact]
    public void ParticleAsWholeSurnameBacktracks() =>
        Assert.Equal(("VAN", "JOHN"), ParseReviewStem($"VAN_JOHN{Tail}"));

    [Theory]
    [InlineData("VANCE_JOHN", "VANCE", "JOHN")]
    [InlineData("DELGADO_MARIA", "DELGADO", "MARIA")]
    public void NamesStartingWithParticleLettersNotSplit(string head, string last, string first) =>
        Assert.Equal((last, first), ParseReviewStem($"{head}{Tail}"));

    [Fact]
    public void ParticleSurnameWithGeneration() =>
        Assert.Equal(("VAN DYKE", "JOHN"), ParseReviewStem($"VAN_DYKE_JR_JOHN{Tail}"));

    [Theory]
    [InlineData("BROWN_ADAM_C")]
    [InlineData("BROWN_ADAM_C_J")]
    public void MiddleInitialsDropped(string head) =>
        Assert.Equal(("BROWN", "ADAM"), ParseReviewStem($"{head}{Tail}"));

    [Fact]
    public void AmbiguousThreeFullNamesSkip() =>
        Assert.Null(ParseReviewStem($"GARCIA_LOPEZ_MARIA{Tail}"));

    [Theory]
    [InlineData("scan_001")]
    [InlineData("20240115--123")]
    [InlineData("BROWN_ADAM_13_45_1966_X")]  // impossible date
    public void NonMatchingReturnsNull(string stem) =>
        Assert.Null(ParseReviewStem(stem));

    [Fact]
    public void ReviewModeRebuildsToDateLastFirst() =>
        Assert.Equal("20240126-BROWN-ADAM",
            TransformStem("BROWN_ADAM_4_25_1966_NYCHSRO_M", new RenameOp(ReceivedDate: "20240126")));

    [Fact]
    public void NonMatchingReviewFileTransformsToNull() =>
        Assert.Null(TransformStem("notes", new RenameOp(ReceivedDate: "20240126")));
}

public class BulkRenameFsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "brtest_" + Guid.NewGuid());

    public BulkRenameFsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Touch(string name, string content = "x")
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void DiskCollisionGetsCounter()
    {
        Touch("b.pdf");
        var src = Touch("a.pdf");
        var pr = Plan(new[] { src }, new RenameOp(Find: "a", Replace: "b"))[0];
        Assert.Equal("b (2).pdf", Path.GetFileName(pr.Target));
    }

    [Fact]
    public void BatchCollisionGetsCounter()
    {
        var a = Touch("fax-1.pdf");
        var b = Touch("fax=1.pdf");
        // find "-" -> "=" makes a's name collide with b's existing name
        var plans = Plan(new[] { a, b }, new RenameOp(Find: "-", Replace: "="));
        Assert.Equal("fax=1 (2).pdf", Path.GetFileName(plans[0].Target));
        Assert.False(plans[1].Changed);  // b itself doesn't change
    }

    [Fact]
    public void ExecuteRenamesAndRevertRestores()
    {
        var a = Touch("one.pdf");
        var b = Touch("two.pdf");
        var plans = Plan(new[] { a, b }, new RenameOp(Prefix: "2024 "));
        var outcomes = Execute(plans);
        Assert.True(File.Exists(Path.Combine(_dir, "2024 one.pdf")));
        Assert.False(File.Exists(a));

        var problems = Revert(outcomes);
        Assert.Empty(problems);
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
    }

    [Fact]
    public void OverrideWinsAndCollisionCounts()
    {
        Touch("TAKEN.pdf", "keep");
        var src = Touch("a.pdf");
        var pr = Plan(new[] { src }, new RenameOp(),
            new Dictionary<string, string> { [src] = "TAKEN" })[0];
        Assert.Equal("TAKEN (2).pdf", Path.GetFileName(pr.Target));
        Assert.True(pr.Manual);
    }

    [Fact]
    public void ReviewMergeEndToEnd()
    {
        var src = Touch("BROWN_ADAM_4_25_1966_NYCHSRO_M.pdf");
        var pr = Plan(new[] { src }, new RenameOp(ReceivedDate: "20240126"))[0];
        Assert.Equal("20240126-BROWN-ADAM.pdf", Path.GetFileName(pr.Target));
    }
}
