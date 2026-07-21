using System.Windows;
using Microsoft.Win32;
using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf.Windows;

public partial class UnlockWindow : Window
{
    private readonly UnlockViewModel _vm;
    private bool _syncingPw;

    public UnlockWindow(UnlockViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        // a saved password selection must land in the (write-only) PasswordBox
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Password) && !_syncingPw && PwBox.Password != vm.Password)
                PwBox.Password = vm.Password;
        };
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _syncingPw = true;
        _vm.Password = PwBox.Password;
        _syncingPw = false;
    }

    private void OnShowPw(object sender, RoutedEventArgs e)
    {
        var show = ShowPw.IsChecked == true;
        PwPlain.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PwBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (show) PwPlain.Text = _vm.Password;
        else PwBox.Password = _vm.Password;
    }

    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Multiselect = true };
        if (dlg.ShowDialog(this) == true) _vm.AddFiles(dlg.FileNames);
    }

    private void OnRemoveSelected(object sender, RoutedEventArgs e) =>
        _vm.RemoveFiles(FileList.SelectedItems.Cast<string>().ToList());

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
