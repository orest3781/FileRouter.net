using System.Text;

/// <summary>A minimal valid one-page PDF Edge renders fine. One copy — the
/// old harness carried this verbatim in two files.</summary>
internal static class MinimalPdf
{
    public static void Write(string path, string text)
    {
        var stream = $"BT /F1 24 Tf 72 700 Td ({text}) Tj ET";
        var sb = new StringBuilder();
        var offsets = new List<int>();
        void Obj(string body) { offsets.Add(sb.Length); sb.Append(body); }
        sb.Append("%PDF-1.4\n");
        Obj("1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n");
        Obj("2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n");
        Obj("3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj\n");
        Obj($"4 0 obj<</Length {stream.Length}>>stream\n{stream}\nendstream endobj\n");
        Obj("5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\n");
        var xref = sb.Length;
        sb.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:0000000000} 00000 n \n");
        sb.Append($"trailer<</Size 6/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF");
        File.WriteAllText(path, sb.ToString());
    }
}
