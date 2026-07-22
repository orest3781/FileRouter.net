namespace FileRouter.Wpf.Services;

/// <summary>Every modal the app can show, behind an interface so view models
/// stay testable and the smoke harness can record instead of block.</summary>
public interface IDialogService
{
    void Warn(string message, string title);
    void Info(string message, string title);
    bool Confirm(string message, string title);
    string? AskSaveFile(string filter, string suggestedName);
    string? AskOpenFile(string filter);

    /// <summary>Pick a file that may or may not exist yet (the history DB
    /// path). An open-style dialog — never the "replace it?" save prompt.</summary>
    string? AskFilePath(string filter, string suggestedName);
    string? BrowseFolder(string? startAt);
}
