using System.Windows;
using Microsoft.Win32;

namespace FileRouter.Wpf.Services;

public sealed class DialogService : IDialogService
{
    private readonly Window _owner;

    public DialogService(Window owner) => _owner = owner;

    public void Warn(string message, string title) =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void Info(string message, string title) =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string message, string title) =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? AskSaveFile(string filter, string suggestedName)
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = suggestedName };
        return dlg.ShowDialog(_owner) == true ? dlg.FileName : null;
    }

    public string? AskOpenFile(string filter)
    {
        var dlg = new OpenFileDialog { Filter = filter };
        return dlg.ShowDialog(_owner) == true ? dlg.FileName : null;
    }

    public string? BrowseFolder(string? startAt)
    {
        var dlg = new OpenFolderDialog();
        if (!string.IsNullOrWhiteSpace(startAt) && Directory.Exists(startAt))
            dlg.InitialDirectory = startAt;
        return dlg.ShowDialog(_owner) == true ? dlg.FolderName : null;
    }
}
