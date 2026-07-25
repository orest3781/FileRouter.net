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
        vm.RequestIdFocus += () => { IdBox.Focus(); IdBox.SelectAll(); };
        // client edits are settings — they stick even without printing
        Closing += (_, _) => vm.Persist();
    }

    private void OnCountPreset(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string n })
            ((LabelMakerViewModel)DataContext).LabelCountText = n;
    }

    /// <summary>In-app printing: the batch becomes a US-letter FixedDocument
    /// (same layout as the PDF and the preview card), shown in a print
    /// preview first — printing happens from there. WPF maps 96 DIPs to one
    /// physical inch, so the output is always at true scale; there is no
    /// "fit to page" step to get wrong.</summary>
    private bool PrintSheets(IReadOnlyList<BoxLabels.Item> items, string jobName)
    {
        var vm = (LabelMakerViewModel)DataContext;
        var preview = new PrintPreviewWindow(LabelPrinting.BuildDocument(items), jobName,
            msg => vm.Dialogs.Warn(msg, "FileRouter — label maker")) { Owner = this };
        preview.ShowDialog();
        return preview.Printed;
    }
}
