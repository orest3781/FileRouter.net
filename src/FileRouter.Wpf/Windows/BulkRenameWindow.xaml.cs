using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf.Windows;

public partial class BulkRenameWindow : Window
{
    private readonly BulkRenameViewModel _vm;

    public BulkRenameWindow(BulkRenameViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "All files (*.*)|*.*", Multiselect = true };
        if (dlg.ShowDialog(this) == true) _vm.AddFiles(dlg.FileNames);
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog(this) == true)
            _vm.AddFiles(Directory.GetFiles(dlg.FolderName));
    }

    private void OnRemoveSelected(object sender, RoutedEventArgs e) =>
        _vm.RemoveFiles(PreviewGrid.SelectedItems.OfType<RenameRow>()
            .Select(r => r.Source).ToList());

    private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit
            || e.Row.Item is not RenameRow row
            || e.EditingElement is not TextBox box) return;
        // route the hand edit through the view model (it strips extensions,
        // clears on empty, and rebuilds the preview)
        var text = box.Text;
        Dispatcher.BeginInvoke(() => _vm.SetOverride(row.Source, text));
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
