using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FileRouter.Core;

namespace FileRouter.Wpf.Views;

/// <summary>Draws <see cref="BoxLabels.LabelDrawing"/> primitives onto a WPF
/// DrawingContext — the same plan the PDF export replays, so the preview and
/// the in-app print output are the printed truth.</summary>
internal static class LabelWpfRender
{
    private static readonly Typeface Bar = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface Mono = new(
        new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Pen CutPen = MakeCutPen();

    private static Pen MakeCutPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(200, 200, 200)), 0.6);
        pen.Freeze();
        return pen;
    }

    /// <summary>Draw one label at the current origin, in POINT units — put a
    /// scale transform on the context first (96/72 prints at true size).</summary>
    public static void DrawLabel(DrawingContext dc, BoxLabels.LabelDrawing d, double pixelsPerDip)
    {
        dc.DrawRectangle(Brushes.White, null,
            new Rect(0, 0, BoxLabels.LabelWidthPt, BoxLabels.LabelHeightPt));
        foreach (var b in d.Bars)
            dc.DrawRectangle(Brushes.Black, null, new Rect(b.X, b.Y, b.W, b.H));
        foreach (var t in d.Texts)
            DrawCentered(dc, t.Text, t.Mono ? Mono : Bar, t.Size,
                t.White ? Brushes.White : Brushes.Black,
                new Rect(t.X, t.Y, t.W, t.H), pixelsPerDip);
        dc.DrawRectangle(null, CutPen,
            new Rect(d.CutGuide.X, d.CutGuide.Y, d.CutGuide.W, d.CutGuide.H));
    }

    public static void DrawCentered(DrawingContext dc, string text, Typeface face,
        double size, Brush brush, Rect box, double pixelsPerDip)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, face, size, brush, pixelsPerDip)
        { TextAlignment = TextAlignment.Center, MaxTextWidth = box.Width };
        dc.DrawText(ft, new Point(box.X, box.Y + (box.Height - ft.Height) / 2));
    }
}

/// <summary>The live label preview in the Box labels window: renders the
/// bound item at whatever size the layout gives it, keeping the 2:1 shape.</summary>
public sealed class LabelPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(BoxLabels.Item), typeof(LabelPreviewControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public BoxLabels.Item? Item
    {
        get => (BoxLabels.Item?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Item is not { } item || ActualWidth <= 0 || ActualHeight <= 0) return;
        var scale = Math.Min(ActualWidth / BoxLabels.LabelWidthPt,
            ActualHeight / BoxLabels.LabelHeightPt);
        var dx = (ActualWidth - BoxLabels.LabelWidthPt * scale) / 2;
        var dy = (ActualHeight - BoxLabels.LabelHeightPt * scale) / 2;
        dc.PushTransform(new TranslateTransform(dx, dy));
        dc.PushTransform(new ScaleTransform(scale, scale));
        LabelWpfRender.DrawLabel(dc, BoxLabels.ComposeDrawing(item),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.Pop();
        dc.Pop();
    }
}

/// <summary>Builds the US-letter print document for a batch — used by both
/// the print-preview window and the actual print call, so what the preview
/// shows is the exact document that spools.</summary>
internal static class LabelPrinting
{
    public static System.Windows.Documents.FixedDocument BuildDocument(
        IReadOnlyList<BoxLabels.Item> items)
    {
        const double pageW = BoxLabels.PageWidthPt * 96 / 72;    // 816 DIPs
        const double pageH = BoxLabels.PageHeightPt * 96 / 72;   // 1056 DIPs
        var doc = new System.Windows.Documents.FixedDocument();
        doc.DocumentPaginator.PageSize = new Size(pageW, pageH);
        for (var i = 0; i < items.Count; i += BoxLabels.PerSheet)
        {
            var sheet = items.Skip(i).Take(BoxLabels.PerSheet).ToList();
            var page = new System.Windows.Documents.FixedPage
            { Width = pageW, Height = pageH, Background = Brushes.White };
            page.Children.Add(new LabelSheetElement(sheet) { Width = pageW, Height = pageH });
            var content = new System.Windows.Documents.PageContent();
            ((System.Windows.Markup.IAddChild)content).AddChild(page);
            doc.Pages.Add(content);
        }
        return doc;
    }
}

/// <summary>One printed sheet (up to 10 labels) for the in-app print path.
/// Sized in DIPs (96/in), drawn in points via a 96/72 scale — WPF printing
/// maps DIPs to physical inches 1:1, so scale is always exactly 100%.</summary>
internal sealed class LabelSheetElement : FrameworkElement
{
    private readonly IReadOnlyList<BoxLabels.Item> _items;

    public LabelSheetElement(IReadOnlyList<BoxLabels.Item> items) => _items = items;

    protected override void OnRender(DrawingContext dc)
    {
        var ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.PushTransform(new ScaleTransform(96.0 / 72.0, 96.0 / 72.0));
        LabelWpfRender.DrawCentered(dc, BoxLabels.SheetNote,
            new Typeface("Segoe UI"), BoxLabels.SheetNoteSize,
            new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            new Rect(0, BoxLabels.SheetNoteY, BoxLabels.PageWidthPt, 10), ppd);
        for (var i = 0; i < _items.Count && i < BoxLabels.PerSheet; i++)
        {
            var (x, y) = BoxLabels.SlotOrigin(i);
            dc.PushTransform(new TranslateTransform(x, y));
            LabelWpfRender.DrawLabel(dc, BoxLabels.ComposeDrawing(_items[i]), ppd);
            dc.Pop();
        }
        dc.Pop();
    }
}
