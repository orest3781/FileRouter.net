using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;
using FileRouter.Core;
using FileRouter.Wpf.ViewModels;
using FileRouter.Wpf.Views;

namespace FileRouter.Wpf.Windows;

public partial class LabelMakerWindow : Window
{
    public LabelMakerWindow(LabelMakerViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.PrintSheets = PrintSheets;
        // client edits are settings — they stick even without printing
        Closing += (_, _) => vm.Persist();
    }

    /// <summary>In-app printing: US-letter FixedDocument pages drawn from the
    /// same layout as the PDF and the preview. WPF maps 96 DIPs to one
    /// physical inch, so the output is always at true scale — there is no
    /// "fit to page" step to get wrong.</summary>
    private bool PrintSheets(IReadOnlyList<BoxLabels.Item> items, string jobName)
    {
        var dlg = new System.Windows.Controls.PrintDialog();
        if (dlg.ShowDialog() != true) return false;

        const double pageW = BoxLabels.PageWidthPt * 96 / 72;    // 816 DIPs
        const double pageH = BoxLabels.PageHeightPt * 96 / 72;   // 1056 DIPs
        var doc = new FixedDocument();
        doc.DocumentPaginator.PageSize = new Size(pageW, pageH);
        for (var i = 0; i < items.Count; i += BoxLabels.PerSheet)
        {
            var sheet = items.Skip(i).Take(BoxLabels.PerSheet).ToList();
            var page = new FixedPage { Width = pageW, Height = pageH };
            page.Children.Add(new LabelSheetElement(sheet) { Width = pageW, Height = pageH });
            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            doc.Pages.Add(content);
        }

        try
        {
            dlg.PrintDocument(doc.DocumentPaginator, jobName);
            return true;
        }
        catch (Exception ex)
        {
            ((LabelMakerViewModel)DataContext).Dialogs
                .Warn("Printing failed: " + ex.Message, "FileRouter — label maker");
            return false;
        }
    }
}
