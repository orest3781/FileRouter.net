using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FileRouter.Core.Tests;

public class UnlockTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "unlocktest_" + Guid.NewGuid());
    public UnlockTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string MakeEncrypted(string name, string userPw = "secret")
    {
        var path = Path.Combine(_dir, name);
        using var doc = new PdfDocument();
        doc.AddPage();
        doc.SecuritySettings.UserPassword = userPw;
        doc.SecuritySettings.OwnerPassword = "owner-" + userPw;
        doc.Save(path);
        return path;
    }

    private string MakePlain(string name)
    {
        var path = Path.Combine(_dir, name);
        using var doc = new PdfDocument();
        doc.AddPage();
        doc.Save(path);
        return path;
    }

    private static bool NeedsPassword(string path)
    {
        try { using var _ = PdfReader.Open(path, PdfDocumentOpenMode.InformationOnly); return false; }
        catch { return true; }
    }

    [Fact]
    public void CorrectPasswordMakesAnOpenCopy()
    {
        var src = MakeEncrypted("locked.pdf");
        var r = Unlock.UnlockPdf(src, "secret", suffix: "_unlocked");
        Assert.True(r.Ok);
        Assert.Equal(Path.Combine(_dir, "locked_unlocked.pdf"), r.NewPath);
        Assert.False(NeedsPassword(r.NewPath!));   // copy opens freely
        Assert.True(NeedsPassword(src));           // original intact
    }

    [Fact]
    public void WrongPasswordWritesNothing()
    {
        var src = MakeEncrypted("locked.pdf");
        var r = Unlock.UnlockPdf(src, "nope", suffix: "_unlocked");
        Assert.Equal("wrong_password", r.Status);
        Assert.Null(r.NewPath);
        Assert.False(File.Exists(Path.Combine(_dir, "locked_unlocked.pdf")));
    }

    [Fact]
    public void UnencryptedIsReportedNotCopied()
    {
        var src = MakePlain("open.pdf");
        var r = Unlock.UnlockPdf(src, "", suffix: "_unlocked");
        Assert.Equal("not_encrypted", r.Status);
        Assert.False(File.Exists(Path.Combine(_dir, "open_unlocked.pdf")));
    }

    [Fact]
    public void MissingFileIsReadableError()
    {
        var r = Unlock.UnlockPdf(@"Z:\nope\gone.pdf", "x");
        Assert.Equal("error", r.Status);
        Assert.Contains("not found", r.Message);
    }

    [Fact]
    public void EmptySuffixUnlocksTheOriginalInPlace()
    {
        var src = MakeEncrypted("20240115--1234567890.pdf");
        var r = Unlock.UnlockPdf(src, "secret", suffix: "");
        Assert.True(r.Ok);
        Assert.True(r.InPlace);
        Assert.Equal(src, r.NewPath);       // same name
        Assert.False(NeedsPassword(src));   // now opens freely
        // exactly one file: no copies, no temp debris
        Assert.Equal(new[] { "20240115--1234567890.pdf" },
            Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(x => x));
    }

    [Fact]
    public void EmptySuffixWrongPasswordLeavesOriginalIntact()
    {
        var src = MakeEncrypted("x.pdf");
        var r = Unlock.UnlockPdf(src, "nope", suffix: "");
        Assert.Equal("wrong_password", r.Status);
        Assert.True(NeedsPassword(src));            // still locked
        Assert.Single(Directory.GetFiles(_dir));    // nothing new
    }

    [Fact]
    public void CollisionCountsAndNeverOverwrites()
    {
        var src = MakeEncrypted("locked.pdf");
        File.WriteAllText(Path.Combine(_dir, "locked_unlocked.pdf"), "existing");
        var r = Unlock.UnlockPdf(src, "secret", suffix: "_unlocked");
        Assert.Equal("locked_unlocked (2).pdf", Path.GetFileName(r.NewPath!));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(_dir, "locked_unlocked.pdf")));
    }
}
