using FileRouter.Core;

namespace FileRouter.Core.Tests;

public class NamingTests
{
    private const string Orig = "20240115--1234567890.pdf";
    private static bool Never(string _) => false;

    private static Naming.NameResult Build(
        string typed, string globalMode = "replace",
        string? routeMode = null, string suffix = "", bool appendSuffix = false,
        Func<string, bool>? exists = null) =>
        Naming.BuildTarget(Orig, typed, routeMode, globalMode, suffix,
            appendSuffix, exists ?? Never);

    // ---- inbox pattern ----
    [Theory]
    [InlineData("20240115--1234567890.pdf", true)]
    [InlineData("20240115--1234567890.PDF", true)]
    [InlineData("20240115--1.pdf", true)]
    [InlineData("20240115-1234567890.pdf", false)]   // single hyphen
    [InlineData("2024011--1234567890.pdf", false)]   // short date
    [InlineData("scan_001.pdf", false)]
    public void InboxPattern(string name, bool matches) =>
        Assert.Equal(matches, Naming.InboxRegex().IsMatch(name));

    // ---- insert / replace ----
    [Fact]
    public void InsertFillsGap() =>
        Assert.Equal("20240115-SMITH JOHN-1234567890.pdf",
            Build("SMITH JOHN", "insert").Filename);

    [Fact]
    public void ReplaceBecomesWholeName() =>
        Assert.Equal("SMITH JOHN.pdf", Build("SMITH JOHN", "replace").Filename);

    [Fact]
    public void RouteModeOverridesGlobal() =>
        Assert.Equal("SMITH JOHN.pdf",
            Build("SMITH JOHN", "insert", routeMode: "replace").Filename);

    [Fact]
    public void InsertOnNonMatchingNameThrows() =>
        Assert.Throws<ArgumentException>(() =>
            Naming.BuildTarget("scan_001.pdf", "SMITH JOHN", null, "insert",
                "", false, Never));

    // ---- typed .pdf extension ----
    [Fact]
    public void TypedPdfStrippedReplace() =>
        Assert.Equal("SMITH JOHN.pdf", Build("SMITH JOHN.pdf", "replace").Filename);

    [Fact]
    public void OnlyOneTrailingPdfStripped() =>
        Assert.Equal("SMITH JOHN.pdf.pdf", Build("SMITH JOHN.pdf.pdf", "replace").Filename);

    [Fact]
    public void TypedJustPdfIsBlank() =>
        Assert.Equal(Orig, Build(".pdf", "replace").Filename);

    // ---- blank commit ----
    [Theory]
    [InlineData("insert")]
    [InlineData("replace")]
    public void BlankKeepsOriginal(string mode) =>
        Assert.Equal(Orig, Build("", mode).Filename);

    [Theory]
    [InlineData("   \t ", "insert")]
    [InlineData("   \t ", "replace")]
    public void WhitespaceIsBlank(string typed, string mode) =>
        Assert.Equal(Orig, Build(typed, mode).Filename);

    // ---- route suffix ----
    [Fact]
    public void SuffixAppendedVerbatim()
    {
        var r = Build("SMITH JOHN", "insert", suffix: "_INVOICE", appendSuffix: true);
        Assert.Equal("20240115-SMITH JOHN-1234567890_INVOICE.pdf", r.Filename);
        Assert.Equal("_INVOICE", r.SuffixApplied);
    }

    [Fact]
    public void SuffixOffAppendsNothing() =>
        Assert.Equal("SMITH JOHN.pdf",
            Build("SMITH JOHN", "replace", suffix: "_INVOICE", appendSuffix: false).Filename);

    [Fact]
    public void SuffixAppliesOnBlankCommit() =>
        Assert.Equal("20240115--1234567890_INVOICE.pdf",
            Build("", "insert", suffix: "_INVOICE", appendSuffix: true).Filename);

    // ---- collision counter (Windows " (2)" style) ----
    [Fact]
    public void CollisionCounterStartsAt2()
    {
        var taken = new HashSet<string> { "SMITH JOHN.pdf" };
        var r = Build("SMITH JOHN", "replace", exists: taken.Contains);
        Assert.Equal("SMITH JOHN (2).pdf", r.Filename);
        Assert.Equal(" (2)", r.CollisionSuffix);
    }

    [Fact]
    public void CollisionCounterKeepsCounting()
    {
        var taken = new HashSet<string> { "SMITH JOHN.pdf", "SMITH JOHN (2).pdf" };
        Assert.Equal("SMITH JOHN (3).pdf",
            Build("SMITH JOHN", "replace", exists: taken.Contains).Filename);
    }

    [Fact]
    public void CollisionStacksAfterSuffix()
    {
        var taken = new HashSet<string> { "SMITH JOHN_INVOICE.pdf" };
        var r = Build("SMITH JOHN", "replace", suffix: "_INVOICE",
            appendSuffix: true, exists: taken.Contains);
        Assert.Equal("SMITH JOHN_INVOICE (2).pdf", r.Filename);
    }

    // ---- exact-as-typed ----
    [Fact]
    public void ReplacePreservesEverything() =>
        Assert.Equal("  smith  john  .pdf", Build("  smith  john  ", "replace").Filename);

    [Fact]
    public void UnusualCharactersPass() =>
        Assert.Equal("O'BRIEN, MARY-ANNE (JR.).pdf",
            Build("O'BRIEN, MARY-ANNE (JR.)", "replace").Filename);

    [Fact]
    public void TrailingSpaceKeptSincePdfFollows() =>
        Assert.Equal("SMITH JOHN .pdf", Build("SMITH JOHN ", "replace").Filename);

    // ---- reserved names (network-safety guard) ----
    [Fact]
    public void ColonIsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => Build("SMITH:JOHN"));
        Assert.Contains(":", ex.Message);
    }

    [Theory]
    [InlineData("A<B")]
    [InlineData("A>B")]
    [InlineData("A\"B")]
    [InlineData("A/B")]
    [InlineData("A\\B")]
    [InlineData("A|B")]
    [InlineData("A?B")]
    [InlineData("A*B")]
    [InlineData("A\tB")]
    public void ReservedCharsRejected(string typed) =>
        Assert.Throws<ArgumentException>(() => Build(typed));

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void ReservedDeviceNamesRejected(string typed) =>
        Assert.Throws<ArgumentException>(() => Build(typed));

    [Fact]
    public void ReservedCharInInsertModeToo() =>
        Assert.Throws<ArgumentException>(() => Build("SMITH:JOHN", "insert"));

    [Fact]
    public void IllegalRouteSuffixCaught() =>
        Assert.Throws<ArgumentException>(() =>
            Build("SMITH JOHN", "replace", suffix: ":BAD", appendSuffix: true));

    [Theory]
    [InlineData("SMITH JOHN")]
    [InlineData("O'BRIEN, MARY-ANNE (JR.)")]
    [InlineData("MÜLLER JÜRGEN")]
    [InlineData("café")]
    [InlineData("CONNOR")]     // starts with CON but isn't the device name
    public void LegalNamesUnaffected(string typed) =>
        Assert.EndsWith(".pdf", Build(typed).Filename);

    [Fact]
    public void BlankCommitNeverRejected() =>
        Assert.Equal(Orig, Build("", "replace").Filename);

    // ---- helpers ----
    [Theory]
    [InlineData("A.pdf", "A")]
    [InlineData("A.PDF", "A")]
    [InlineData("A.pdf.pdf", "A.pdf")]
    [InlineData("A", "A")]
    [InlineData("", "")]
    public void StripPdfExt(string input, string expected) =>
        Assert.Equal(expected, Naming.StripPdfExt(input));

    [Theory]
    [InlineData("", true)]
    [InlineData("   \t", true)]
    [InlineData(".pdf", true)]
    [InlineData("SMITH JOHN", false)]
    [InlineData("A.pdf", false)]
    public void IsBlankName(string input, bool expected) =>
        Assert.Equal(expected, Naming.IsBlankName(input));
}
