using System.Printing;
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
/// that will spool, with the viewer's zoom and page navigation, plus a
/// printer picker and copies — Print spools straight to the chosen queue
/// with no OS dialog (Windows 11's print dialog shows a bogus "no preview"
/// pane for XPS jobs; this window IS the preview). Cancel/Esc leaves the
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
        LoadPrinters();
        Loaded += (_, _) => Viewer.FitToMaxPagesAcross(1);
    }

    private void LoadPrinters()
    {
        try
        {
            using var server = new LocalPrintServer();
            foreach (var q in server.GetPrintQueues(new[]
            {
                EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections,
            }))
                Printers.Items.Add(q.FullName);
            try
            {
                Printers.SelectedItem = server.DefaultPrintQueue.FullName;
            }
            catch { /* no default set */ }
        }
        catch { /* spooler unavailable — handled below */ }

        if (Printers.SelectedIndex < 0 && Printers.Items.Count > 0) Printers.SelectedIndex = 0;
        if (Printers.Items.Count == 0)
        {
            PrintButton.IsEnabled = false;
            PrintNote.Text = "No printers found.";
        }
    }

    private void OnPrint(object sender, RoutedEventArgs e) => PrintNow();

    private void PrintNow()
    {
        if (Printers.SelectedItem is not string printerName) return;
        if (!int.TryParse(Copies.Text.Trim(), out var copies) || copies is < 1 or > 99)
        {
            PrintNote.Text = "Copies must be 1 to 99.";
            return;
        }
        try
        {
            using var server = new LocalPrintServer();
            var queue = new PrintQueue(server, printerName);
            var dlg = new PrintDialog { PrintQueue = queue };
            dlg.PrintTicket.CopyCount = copies;
            // no ShowDialog: this window already chose everything — spool it
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
