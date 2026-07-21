using FileRouter.Wpf.Services;

namespace FileRouter.Wpf.Tests;

/// <summary>Records every viewer interaction; Release counts let tests prove
/// the release-before-move ordering.</summary>
public sealed class FakeViewer : IPdfViewer
{
    public List<string> Shown { get; } = new();
    public int Releases { get; private set; }
    public int Blanks { get; private set; }

    /// <summary>When set, ReleaseAsync awaits this — lets a test hold a commit
    /// mid-flight to prove reentrancy handling.</summary>
    public TaskCompletionSource? HoldRelease { get; set; }

    public Task ShowAsync(string path) { Shown.Add(path); return Task.CompletedTask; }

    public async Task ReleaseAsync()
    {
        Releases++;
        if (HoldRelease is { } hold) await hold.Task;
    }

    public void Blank() => Blanks++;
}

public sealed class FakeDialogs : IDialogService
{
    public List<(string Message, string Title)> Warnings { get; } = new();
    public List<(string Message, string Title)> Infos { get; } = new();
    public bool ConfirmAnswer { get; set; } = true;
    public string? NextSaveFile { get; set; }
    public string? NextOpenFile { get; set; }
    public string? NextFolder { get; set; }

    public void Warn(string message, string title) => Warnings.Add((message, title));
    public void Info(string message, string title) => Infos.Add((message, title));
    public bool Confirm(string message, string title) => ConfirmAnswer;
    public string? AskSaveFile(string filter, string suggested) => NextSaveFile;
    public string? AskOpenFile(string filter) => NextOpenFile;
    public string? BrowseFolder(string? startAt) => NextFolder;
}
