using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace FileRouter.Core;

/// <summary>Code 39 barcode encoding — the symbology every handheld scanner
/// reads out of the box: uppercase letters, digits, no checksum required,
/// self-checking per character.</summary>
public static class Code39
{
    // 9 elements per character (bar,space,bar,space,bar,space,bar,space,bar),
    // '1' = wide, '0' = narrow — the standard AIM table for A-Z, 0-9 and '*'.
    private static readonly Dictionary<char, string> Patterns = new()
    {
        ['0'] = "000110100", ['1'] = "100100001", ['2'] = "001100001",
        ['3'] = "101100000", ['4'] = "000110001", ['5'] = "100110000",
        ['6'] = "001110000", ['7'] = "000100101", ['8'] = "100100100",
        ['9'] = "001100100",
        ['A'] = "100001001", ['B'] = "001001001", ['C'] = "101001000",
        ['D'] = "000011001", ['E'] = "100011000", ['F'] = "001011000",
        ['G'] = "000001101", ['H'] = "100001100", ['I'] = "001001100",
        ['J'] = "000011100", ['K'] = "100000011", ['L'] = "001000011",
        ['M'] = "101000010", ['N'] = "000010011", ['O'] = "100010010",
        ['P'] = "001010010", ['Q'] = "000000111", ['R'] = "100000110",
        ['S'] = "001000110", ['T'] = "000010110", ['U'] = "110000001",
        ['V'] = "011000001", ['W'] = "111000000", ['X'] = "010010001",
        ['Y'] = "110010000", ['Z'] = "011010000",
        ['*'] = "010010100",
    };

    /// <summary>One printed element: a bar or a space, narrow or wide.</summary>
    public readonly record struct Element(bool Bar, bool Wide);

    /// <summary>Encode text (A-Z, 0-9) as elements, including the start/stop
    /// '*' characters and the narrow inter-character gaps. Throws on any
    /// character outside the supported set.</summary>
    public static List<Element> Encode(string text)
    {
        var elements = new List<Element>();
        var full = "*" + text + "*";
        for (var c = 0; c < full.Length; c++)
        {
            if (c > 0) elements.Add(new Element(Bar: false, Wide: false));   // gap
            if (full[c] == '*' && c != 0 && c != full.Length - 1)
                throw new ArgumentException("'*' is reserved for start/stop.");
            if (!Patterns.TryGetValue(full[c], out var pattern))
                throw new ArgumentException($"Code 39 can't encode '{full[c]}' — use A-Z and 0-9.");
            for (var i = 0; i < 9; i++)
                elements.Add(new Element(Bar: i % 2 == 0, Wide: pattern[i] == '1'));
        }
        return elements;
    }
}

/// <summary>Box labels: ten 4"×2" labels per US-letter sheet (the standard
/// Avery 5163 grid), each carrying the created date, a large client+number
/// code, its Code 39 barcode, and the destruction date.</summary>
public static class BoxLabels
{
    static BoxLabels()
    {
        // the core PdfSharp build resolves NO fonts on its own — point it at
        // the Windows font folder once, before the first XFont is created
        if (PdfSharp.Fonts.GlobalFontSettings.FontResolver is null)
            PdfSharp.Fonts.GlobalFontSettings.FontResolver = new LabelFontResolver();
    }

    /// <summary>Serves Segoe UI and Consolas (regular/bold) from
    /// C:\Windows\Fonts — the only families the labels use. Consolas carries
    /// the code line: monospaced with a slashed zero, so 0/O and 1/I can't be
    /// confused when someone transcribes a label by hand.</summary>
    private sealed class LabelFontResolver : PdfSharp.Fonts.IFontResolver
    {
        public PdfSharp.Fonts.FontResolverInfo ResolveTypeface(
            string familyName, bool bold, bool italic) =>
            familyName.Equals("Consolas", StringComparison.OrdinalIgnoreCase)
                ? new(bold ? "consola#b" : "consola#r")
                : new(bold ? "segoe#b" : "segoe#r");

        public byte[] GetFont(string faceName) => File.ReadAllBytes(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
            faceName switch
            {
                "consola#b" => "consolab.ttf",
                "consola#r" => "consola.ttf",
                "segoe#b" => "segoeuib.ttf",
                _ => "segoeui.ttf",
            }));
    }

    public sealed record Item(string Code, DateTime Created, DateTime Destroy);

    public const int PerSheet = 10;
    public const long MaxNumber = 99_999_999;   // the code carries 8 digits

    /// <summary>"ABCD" + 42 → "ABCD00000042".</summary>
    public static string Compose(string clientId, long number) =>
        clientId + number.ToString("D8");

    /// <summary>Display-only digit grouping for the printed code line:
    /// "ABCD00000042" → "ABCD 0000 0042". The barcode always carries the
    /// continuous code — this is purely for human reading/transcription.</summary>
    public static string DisplayCode(string code)
    {
        if (code.Length <= 8) return code;
        for (var i = code.Length - 8; i < code.Length; i++)
            if (code[i] is < '0' or > '9') return code;
        return $"{code[..^8]} {code[^8..^4]} {code[^4..]}";
    }

    /// <summary>Problem with a client id, or "" when it's usable (2-8
    /// characters, capital letters and digits only — Code 39's alphabet).</summary>
    public static string ValidateClientId(string id)
    {
        if (id.Length is < 2 or > 8)
            return "Client id needs 2 to 8 characters (like ABCD).";
        foreach (var c in id)
            if (c is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9')))
                return "Client id can only use capital letters and digits.";
        return "";
    }

    /// <summary>The consecutive labels for one print run.</summary>
    public static List<Item> Batch(string clientId, long start, int count,
        DateTime created, int destroyDays)
    {
        if (count < 1) throw new ArgumentException("Need at least one label.");
        if (start < 1 || start + count - 1 > MaxNumber)
            throw new ArgumentException($"Label numbers must stay within 1–{MaxNumber}.");
        var destroy = created.Date.AddDays(destroyDays);
        var items = new List<Item>(count);
        for (var i = 0; i < count; i++)
            items.Add(new Item(Compose(clientId, start + i), created.Date, destroy));
        return items;
    }

    // Avery 5163 sheet geometry (inches): 2 columns × 5 rows of 4×2 labels.
    private const double LabelW = 4.0, LabelH = 2.0;
    private const double MarginLeft = 0.15625, MarginTop = 0.5;
    private const double PitchX = 4.1875, PitchY = 2.0;

    /// <summary>Write the print-ready PDF (US letter, print at 100% scale).</summary>
    public static void RenderPdf(string path, IReadOnlyList<Item> items)
    {
        if (items.Count == 0) throw new ArgumentException("Nothing to print.");
        using var doc = new PdfDocument();
        var small = new XFont("Segoe UI", 8);
        var destroyFont = new XFont("Segoe UI", 11.5, XFontStyleEx.Bold);
        var cutLine = new XPen(XColor.FromArgb(210, 210, 210), 0.4);

        for (var i = 0; i < items.Count; i++)
        {
            if (i % PerSheet == 0) AddPage(doc);
            var page = doc.Pages[doc.PageCount - 1];
            using var gfx = XGraphics.FromPdfPage(page);
            var slot = i % PerSheet;
            var x = XUnit.FromInch(MarginLeft + slot % 2 * PitchX).Point;
            var y = XUnit.FromInch(MarginTop + slot / 2 * PitchY).Point;
            DrawLabel(gfx, items[i], x, y, small, destroyFont, cutLine);
        }
        doc.Save(path);
    }

    private static void AddPage(PdfDocument doc)
    {
        var page = doc.AddPage();
        page.Width = XUnit.FromInch(8.5);
        page.Height = XUnit.FromInch(11);
        // the top 0.5" is outside the die-cut area — spend it on the one
        // instruction that prevents a whole misprinted sheet
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawString("Print at 100% scale (no “fit to page”)  ·  Avery 5163 — 10 labels, 4 × 2 in",
            new XFont("Segoe UI", 6.5), new XSolidBrush(XColor.FromArgb(150, 150, 150)),
            new XRect(0, XUnit.FromInch(0.18).Point, page.Width.Point, 10),
            XStringFormats.Center);
    }

    /// <summary>26pt when it fits, stepped down for long client ids — the
    /// code line must never crowd the label edges or clip.</summary>
    private static XFont FitCodeFont(XGraphics gfx, string display, double maxWidth)
    {
        for (var size = 26.0; size > 10; size -= 1)
        {
            var font = new XFont("Consolas", size, XFontStyleEx.Bold);
            if (gfx.MeasureString(display, font).Width <= maxWidth) return font;
        }
        return new XFont("Consolas", 10, XFontStyleEx.Bold);
    }

    private const double TopBarH = 18, BottomBarH = 22;

    private static void DrawLabel(XGraphics gfx, Item item, double x, double y,
        XFont small, XFont destroyFont, XPen cutLine)
    {
        var w = XUnit.FromInch(LabelW).Point;
        var h = XUnit.FromInch(LabelH).Point;

        // the dates ride on full-width black bars — visible across a storage
        // room, and the destruction date can't be mistaken for decoration
        gfx.DrawRectangle(XBrushes.Black, x, y, w, TopBarH);
        gfx.DrawString($"Created {item.Created:yyyy-MM-dd}", small, XBrushes.White,
            new XRect(x, y, w, TopBarH), XStringFormats.Center);

        var display = DisplayCode(item.Code);
        gfx.DrawString(display, FitCodeFont(gfx, display, w - 20), XBrushes.Black,
            new XRect(x, y + TopBarH + 4, w, 36), XStringFormats.Center);
        // clear air between the digit strokes and the bars — an angled scan
        // sweep must never catch both
        DrawBarcode(gfx, item.Code, x, y + 68, w, height: 42);

        gfx.DrawRectangle(XBrushes.Black, x, y + h - BottomBarH, w, BottomBarH);
        gfx.DrawString($"DESTROY AFTER {item.Destroy:yyyy-MM-dd}", destroyFont, XBrushes.White,
            new XRect(x, y + h - BottomBarH, w, BottomBarH), XStringFormats.Center);

        // faint cut guide — invisible-ish on label stock, useful on plain paper
        gfx.DrawRectangle(cutLine, x, y, w, h);
    }

    private static void DrawBarcode(XGraphics gfx, string code, double x, double y,
        double labelW, double height)
    {
        var elements = Code39.Encode(code);
        // 3:1 wide:narrow ratio; the bar field spans the label minus quiet
        // zones (≥10 narrow units each side keeps hand scanners happy). The
        // 0.18" side insets keep long client ids above ~12 mil bars.
        var units = elements.Sum(e => e.Wide ? 3 : 1) + 20;
        var printable = XUnit.FromInch(LabelW - 0.36).Point;
        var narrow = printable / units;
        var cursor = x + (labelW - printable) / 2 + narrow * 10;
        foreach (var e in elements)
        {
            var wBar = narrow * (e.Wide ? 3 : 1);
            if (e.Bar) gfx.DrawRectangle(XBrushes.Black, cursor, y, wBar, height);
            cursor += wBar;
        }
    }
}
