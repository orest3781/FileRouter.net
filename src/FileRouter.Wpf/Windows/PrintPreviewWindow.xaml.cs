using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace FileRouter.Wpf.Windows;

/// <summary>A DocumentViewer whose print button (and Ctrl+P) runs OUR print
/// flow — the window must know whether the job was actually sent, so the
/// label counter only advances for real prints.</summary>
public sealed class PreviewDocumentViewer : DocumentViewer
{
    internal Action? PrintRequested { get; set; }
    protected override void OnPrintCommand() => PrintRequested?.Invoke();
}

/// <summary>Print preview for label sheets: shows the exact FixedDocument
/// that will spool, with the viewer's zoom and page navigation. Printing
/// closes the window with <see cref="Printed"/> true; Cancel/Esc leaves the
/// label counter untouched.</summary>
public partial class PrintPreviewWindow : Window
{
    private readonly FixedDocument _doc;
    private readonly string _jobName;
    private readonly Action<string> _warn;

    public bool Printed { get; private set; }

    public PrintPreviewWindow(FixedDocument doc, string jobName, Action<string> warn)
    {
        InitializeComponent();
        _doc = doc;
        _jobName = jobName;
        _warn = warn;
        Viewer.Document = doc;
        Viewer.PrintRequested = PrintNow;
        PageInfo.Text = $"{doc.Pages.Count} sheet{(doc.Pages.Count == 1 ? "" : "s")}"
            + "   ·   labels 4 × 2 in   ·   prints at 100% scale";
        Loaded += (_, _) => Viewer.FitToMaxPagesAcross(1);
    }

    private void OnPrint(object sender, RoutedEventArgs e) => PrintNow();

    private void PrintNow()
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;
        try
        {
            dlg.PrintDocument(_doc.DocumentPaginator, _jobName);
        }
        catch (Exception ex)
        {
            _warn("Printing failed: " + ex.Message);
            return;
        }
        Printed = true;
        DialogResult = true;
    }
}
