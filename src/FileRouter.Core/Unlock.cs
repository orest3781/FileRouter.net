using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FileRouter.Core;

/// <summary>
/// Unlock (decrypt) password-protected PDFs. Never throws.
///
/// Two modes, chosen by <c>suffix</c>:
/// - suffix set (e.g. "_unlocked"): a decrypted COPY is written alongside the
///   original; the original is untouched.
/// - suffix EMPTY (default): the original file ITSELF is unlocked — decrypt to
///   a temp name, verify it reads, then atomically replace the original. A
///   decryption that produced garbage can never destroy the encrypted file.
///
/// Decryption uses PdfSharp Import mode (open with the user password, copy the
/// pages into a fresh unencrypted document) — Modify mode would demand the
/// OWNER password, which the person filing a document doesn't have.
/// </summary>
public static class Unlock
{
    public sealed record UnlockResult(
        string Status, string Source, string? NewPath = null,
        string Message = "", bool InPlace = false)
    {
        // ok | not_encrypted | wrong_password | error
        public bool Ok => Status == "ok";
    }

    public static UnlockResult UnlockPdf(string src, string password,
        string? destDir = null, string suffix = "")
    {
        if (!File.Exists(src))
            return new("error", src, Message: "File not found.");

        // encryption state, checked without a password
        try
        {
            using var probe = PdfReader.Open(src, PdfDocumentOpenMode.InformationOnly);
            if (!probe.SecuritySettings.IsEncrypted)
                return new("not_encrypted", src, Message: "This PDF isn't password-protected.");
        }
        catch
        {
            // couldn't open without a password -> it's encrypted; fall through
        }

        var dest = destDir ?? Path.GetDirectoryName(src)!;
        if (!Directory.Exists(dest))
            return new("error", src, Message: $"The output folder isn't available: {dest}");

        var stem = Path.GetFileNameWithoutExtension(src);
        var swapInPlace = string.IsNullOrEmpty(suffix)
            && string.Equals(Path.GetFullPath(dest),
                Path.GetFullPath(Path.GetDirectoryName(src)!), StringComparison.OrdinalIgnoreCase);
        var target = swapInPlace
            ? CollisionFree(Path.Combine(dest, stem + ".unlocking.pdf"))
            : CollisionFree(Path.Combine(dest, stem + suffix + ".pdf"));

        try
        {
            using var input = PdfReader.Open(src, password, PdfDocumentOpenMode.Import);
            using var output = new PdfDocument();
            foreach (var page in input.Pages) output.AddPage(page);
            output.Save(target);
        }
        catch (PdfReaderException)
        {
            RemoveQuietly(target);
            return new("wrong_password", src, Message: "That password didn't work.");
        }
        catch (Exception ex)
        {
            RemoveQuietly(target);
            return new("error", src, Message: $"Couldn't save the unlocked copy: {ex.Message}");
        }

        var problem = VerifyReadable(target);
        if (problem.Length > 0)
        {
            RemoveQuietly(target);
            return new("error", src,
                Message: $"This PDF looks damaged — it couldn't be unlocked cleanly ({problem}).");
        }

        if (swapInPlace)
        {
            try
            {
                File.Move(target, src, overwrite: true);   // atomic on the same volume
            }
            catch (Exception ex)
            {
                RemoveQuietly(target);
                return new("error", src,
                    Message: $"Couldn't replace the original (is it open somewhere?): {ex.Message}");
            }
            return new("ok", src, NewPath: src, InPlace: true);
        }
        return new("ok", src, NewPath: target);
    }

    private static string CollisionFree(string target)
    {
        if (!File.Exists(target)) return target;
        var dir = Path.GetDirectoryName(target)!;
        var stem = Path.GetFileNameWithoutExtension(target);
        var ext = Path.GetExtension(target);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static void RemoveQuietly(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    /// <summary>Reopen the saved copy and force every page to load. "" if it's
    /// a clean, open PDF, else a short problem — catches a decryption that
    /// produced garbage.</summary>
    private static string VerifyReadable(string path)
    {
        try
        {
            using var doc = PdfReader.Open(path, PdfDocumentOpenMode.InformationOnly);
            if (doc.SecuritySettings.IsEncrypted) return "the copy is still password-protected";
            if (doc.PageCount == 0) return "the copy has no readable pages";
            for (var i = 0; i < doc.PageCount; i++) _ = doc.Pages[i];
            return "";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
