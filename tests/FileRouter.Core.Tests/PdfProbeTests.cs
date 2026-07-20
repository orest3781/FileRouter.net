using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FileRouter.Core.Tests;

/// <summary>Learn/pin PdfSharp behaviour we depend on: making a real PDF,
/// tagging metadata, and creating + opening encrypted PDFs (for Unlock).</summary>
public class PdfProbeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pdfprobe_" + Guid.NewGuid());
    public PdfProbeTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string MakePdf(string name = "doc.pdf")
    {
        var path = Path.Combine(_dir, name);
        using var doc = new PdfDocument();
        doc.AddPage();
        doc.Save(path);
        return path;
    }

    private string MakeEncrypted(string name, string userPw)
    {
        var path = Path.Combine(_dir, name);
        using var doc = new PdfDocument();
        doc.AddPage();
        // In PdfSharp 6.x, setting the passwords enables encryption.
        doc.SecuritySettings.UserPassword = userPw;
        doc.SecuritySettings.OwnerPassword = "owner-" + userPw;
        doc.Save(path);
        return path;
    }

    [Fact]
    public void CanMakeAndTagAPdf()
    {
        var p = MakePdf();
        var r = Tagger.Tag(p, "Invoices");
        Assert.True(r.Tagged);
        Assert.Equal(("Invoices", "Invoices"), Tagger.ReadMeta(p));
    }

    [Fact]
    public void EncryptedPdfNeedsPasswordAndDecryptsViaImport()
    {
        var p = MakeEncrypted("locked.pdf", "secret");
        // opening WITHOUT a password throws
        Assert.ThrowsAny<Exception>(() => PdfReader.Open(p, PdfDocumentOpenMode.Import));
        // wrong password throws
        Assert.ThrowsAny<Exception>(() => PdfReader.Open(p, "nope", PdfDocumentOpenMode.Import));

        // the decryption pattern: import pages with the user password into a
        // fresh (unencrypted) document
        var outPath = Path.Combine(_dir, "unlocked.pdf");
        using (var input = PdfReader.Open(p, "secret", PdfDocumentOpenMode.Import))
        using (var output = new PdfDocument())
        {
            foreach (var page in input.Pages) output.AddPage(page);
            output.Save(outPath);
        }
        // the copy opens with NO password and isn't encrypted
        using var check = PdfReader.Open(outPath, PdfDocumentOpenMode.InformationOnly);
        Assert.False(check.SecuritySettings.IsEncrypted);
    }

    [Fact]
    public void TaggingAnEncryptedPdfFailsSoft()
    {
        var p = MakeEncrypted("locked.pdf", "secret");
        var r = Tagger.Tag(p, "Invoices");
        Assert.False(r.Tagged);         // couldn't tag...
        Assert.NotEqual("", r.Warning); // ...but reported readably, file kept
        Assert.True(File.Exists(p));
    }
}
