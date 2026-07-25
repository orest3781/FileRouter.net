using FileRouter.Core;
using PdfSharp.Pdf.IO;

namespace FileRouter.Core.Tests;

public class BoxLabelsTests
{
    [Fact]
    public void ComposePadsTheNumberToEightDigits()
    {
        Assert.Equal("ABCD00000001", BoxLabels.Compose("ABCD", 1));
        Assert.Equal("ABCD00000042", BoxLabels.Compose("ABCD", 42));
        Assert.Equal("XY99999999", BoxLabels.Compose("XY", 99_999_999));
    }

    [Theory]
    [InlineData("ABCD", "")]
    [InlineData("AB", "")]
    [InlineData("CLIENT01", "")]
    [InlineData("A", "2 to 8")]
    [InlineData("TOOLONGID", "2 to 8")]
    [InlineData("", "2 to 8")]
    [InlineData("abcd", "capital")]
    [InlineData("AB CD", "capital")]
    [InlineData("AB-1", "capital")]
    public void ClientIdValidationCatchesTheBadOnes(string id, string expectedFragment)
    {
        var problem = BoxLabels.ValidateClientId(id);
        if (expectedFragment.Length == 0) Assert.Equal("", problem);
        else Assert.Contains(expectedFragment, problem);
    }

    [Fact]
    public void BatchNumbersRunConsecutivelyWithTheRetentionDate()
    {
        var created = new DateTime(2026, 1, 1, 14, 30, 0);   // time of day dropped
        var items = BoxLabels.Batch("ABCD", 7, 3, created, 30);

        Assert.Equal(new[] { "ABCD00000007", "ABCD00000008", "ABCD00000009" },
            items.Select(i => i.Code));
        Assert.All(items, i => Assert.Equal(new DateTime(2026, 1, 1), i.Created));
        Assert.All(items, i => Assert.Equal(new DateTime(2026, 1, 31), i.Destroy));
    }

    [Fact]
    public void BatchRefusesToRunPastTheEightDigitCeiling()
    {
        Assert.Throws<ArgumentException>(() =>
            BoxLabels.Batch("ABCD", 99_999_995, 10, DateTime.Now, 30));
        Assert.Throws<ArgumentException>(() => BoxLabels.Batch("ABCD", 0, 1, DateTime.Now, 30));
        Assert.Throws<ArgumentException>(() => BoxLabels.Batch("ABCD", 1, 0, DateTime.Now, 30));
    }

    [Fact]
    public void Code39EncodesWithStartStopAndGaps()
    {
        // "AB" → *AB* = 4 characters × 9 elements + 3 inter-character gaps
        var elements = Code39.Encode("AB");
        Assert.Equal(4 * 9 + 3, elements.Count);
        Assert.True(elements[0].Bar);                       // always starts on a bar
        Assert.Equal(4 * 5, elements.Count(e => e.Bar));    // 5 bars per character
        Assert.Equal(4 * 3, elements.Count(e => e.Wide));   // exactly 3 wide per character
        Assert.False(elements[9].Bar);                      // the gap is a space
        Assert.False(elements[9].Wide);                     // ...a narrow one
    }

    [Fact]
    public void Code39StartAndStopAreTheAsteriskPattern()
    {
        // '*' = 010010100: wide at elements 1, 4, 6
        var elements = Code39.Encode("7");
        var star = elements.Take(9).Select(e => e.Wide).ToArray();
        Assert.Equal(new[] { false, true, false, false, true, false, true, false, false }, star);
        var stop = elements.Skip(elements.Count - 9).Select(e => e.Wide).ToArray();
        Assert.Equal(star, stop);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("AB*CD")]
    [InlineData("AB CD")]
    [InlineData("AB-1")]
    public void Code39RejectsCharactersOutsideItsAlphabet(string text) =>
        Assert.ThrowsAny<ArgumentException>(() => Code39.Encode(text));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(25, 3)]
    public void PdfHoldsTenLabelsPerSheet(int labels, int expectedPages)
    {
        var dir = Directory.CreateTempSubdirectory("fr_labels").FullName;
        var path = Path.Combine(dir, "labels.pdf");
        try
        {
            var items = BoxLabels.Batch("ABCD", 1, labels, new DateTime(2026, 7, 25), 30);
            BoxLabels.RenderPdf(path, items);

            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            Assert.Equal(expectedPages, pdf.PageCount);
            // US letter, in points
            Assert.Equal(612, pdf.Pages[0].Width.Point, 1);
            Assert.Equal(792, pdf.Pages[0].Height.Point, 1);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void EmptyBatchDoesNotRender() =>
        Assert.Throws<ArgumentException>(() =>
            BoxLabels.RenderPdf(Path.Combine(Path.GetTempPath(), "x.pdf"),
                new List<BoxLabels.Item>()));
}
