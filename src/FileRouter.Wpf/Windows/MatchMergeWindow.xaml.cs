using System.Windows;
using Microsoft.Win32;
using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf.Windows;

public partial class MatchMergeWindow : Window
{
    private readonly MatchMergeViewModel _vm;

    public MatchMergeWindow(MatchMergeViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Multiselect = true };
        if (dlg.ShowDialog(this) == true) _vm.AddFiles(dlg.FileNames);
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog(this) == true)
            _vm.AddFiles(Directory.GetFiles(dlg.FolderName, "*.pdf"));
    }

    private void OnRemoveSelected(object sender, RoutedEventArgs e) =>
        _vm.RemoveFiles(MatchGrid.SelectedItems.OfType<MatchRow>()
            .Select(r => r.Source).ToList());

    private void OnTriage(object sender, RoutedEventArgs e)
    {
        var items = _vm.AmbiguousItems;
        if (items.Count == 0) return;
        // the triage viewer is a fresh WebView2 per run — the WinForms version
        // reparented one shared control between forms, fragile ownership
        var win = new TriageWindow(items, _vm.RosterHeaders) { Owner = this };
        win.ShowDialog();
        _vm.Absorb(win.Outcomes);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) _vm.AddFiles(paths);
    }
}
