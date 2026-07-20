using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FileRouter.Core;

/// <summary>
/// PDF metadata tagging (keywords + subject). Best-effort by design: a PDF
/// that can't be tagged (encrypted, damaged) is still filed — never lose a
/// file over a tag. Unlike the Python original's incremental save, PdfSharp
/// re-serialises the file; the content is preserved.
/// </summary>
public static class Tagger
{
    public sealed record TagResult(
        bool Tagged, string OldKeywords = "", string OldSubject = "", string Warning = "");

    /// <summary>Write the route label into keywords and subject. Never throws.</summary>
    public static TagResult Tag(string path, string label)
    {
        try
        {
            string oldKw, oldSubj;
            using (var doc = PdfReader.Open(path, PdfDocumentOpenMode.Modify))
            {
                oldKw = doc.Info.Keywords ?? "";
                oldSubj = doc.Info.Subject ?? "";
                doc.Info.Keywords = label;
                doc.Info.Subject = label;
                doc.Save(path);
            }
            return new TagResult(true, oldKw, oldSubj);
        }
        catch (Exception ex)
        {
            return new TagResult(false, Warning:
                $"Filed untagged — couldn't write metadata: {ex.Message}");
        }
    }

    /// <summary>Restore prior keywords/subject (undo). Never throws.</summary>
    public static (bool Ok, string Message) Untag(string path, string oldKeywords, string oldSubject)
    {
        try
        {
            using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
            doc.Info.Keywords = oldKeywords;
            doc.Info.Subject = oldSubject;
            doc.Save(path);
            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, $"Couldn't restore metadata: {ex.Message}");
        }
    }

    /// <summary>Read (keywords, subject) — used to verify tagging in tests.</summary>
    public static (string Keywords, string Subject) ReadMeta(string path)
    {
        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.InformationOnly);
        return (doc.Info.Keywords ?? "", doc.Info.Subject ?? "");
    }
}
