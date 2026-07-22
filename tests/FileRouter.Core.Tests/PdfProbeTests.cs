using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FileRouter.Core.Tests;

/// <summary>Learn/pin PdfSharp behaviour we depend on: creating and opening
/// encrypted PDFs (the Unlock tool's decrypt pattern).</summary>
public class PdfProbeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pdfprobe_" + Guid.NewGuid());
    public PdfProbeTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

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
}
