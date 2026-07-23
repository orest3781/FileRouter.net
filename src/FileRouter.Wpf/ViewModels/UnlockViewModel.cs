using System.Collections.ObjectModel;
using FileRouter.Core;
using FileRouter.Wpf.Mvvm;
using FileRouter.Wpf.Services;

namespace FileRouter.Wpf.ViewModels;

public enum UnlockResultKind { Ok, Skip, Fail }

public sealed record UnlockResultLine(string Text, UnlockResultKind Kind);

/// <summary>Unlock PDFs: add password-protected files, give (or pick a saved)
/// password, unlock. Default unlocks the original in place (verified, then
/// atomically swapped); "keep a copy" writes a suffixed copy instead. Results
/// stream per file with a live "n of m" counter; a typed "remember as" label
/// is only saved when the password actually unlocked something.</summary>
public sealed class UnlockViewModel : ObservableObject
{
    private readonly Config _cfg;
    private readonly Action _saveCfg;

    public ObservableCollection<string> Files { get; } = new();
    public ObservableCollection<SavedPassword> Saved { get; }
    public ObservableCollection<UnlockResultLine> ResultLines { get; } = new();

    public UnlockViewModel(Config cfg, Action saveCfg)
    {
        _cfg = cfg;
        _saveCfg = saveCfg;
        Saved = new ObservableCollection<SavedPassword>(cfg.SavedPasswords);
        KeepCopy = cfg.UnlockSuffix.Length > 0;
        UnlockCommand = new AsyncRelayCommand(UnlockAsync, () => Files.Count > 0);
        ClearCommand = new RelayCommand(() =>
        {
            Files.Clear();
            ResultLines.Clear();
            Summary = "";
            AddNote = "";
        });
        Files.CollectionChanged += (_, _) => UnlockCommand.RaiseCanExecuteChanged();
    }

    private string _password = "";
    public string Password { get => _password; set => Set(ref _password, value); }

    private bool _keepCopy;
    public bool KeepCopy { get => _keepCopy; set => Set(ref _keepCopy, value); }

    /// <summary>What the copy will actually be called.</summary>
    public string CopySuffixHint =>
        $"the copy gets \"{(_cfg.UnlockSuffix.Length > 0 ? _cfg.UnlockSuffix : "_unlocked")}\" added to its name";

    private SavedPassword? _selectedSaved;
    public SavedPassword? SelectedSaved
    {
        get => _selectedSaved;
        set
        {
            if (Set(ref _selectedSaved, value) && value is not null)
                Password = PasswordVault.Reveal(value.Password);
        }
    }

    private string _rememberLabel = "";
    public string RememberLabel { get => _rememberLabel; set => Set(ref _rememberLabel, value); }

    /// <summary>The verdict line: live progress while running, then
    /// "3 unlocked · 1 already unlocked · 1 failed".</summary>
    private string _summary = "";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    /// <summary>Feedback for the last add/drop ("2 added · 1 ignored…").</summary>
    private string _addNote = "";
    public string AddNote { get => _addNote; private set => Set(ref _addNote, value); }

    public AsyncRelayCommand UnlockCommand { get; }
    public RelayCommand ClearCommand { get; }

    public void AddFiles(IEnumerable<string> paths)
    {
        int added = 0, ignored = 0;
        foreach (var p in paths)
        {
            if (File.Exists(p) && p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                && !Files.Contains(p))
            {
                Files.Add(p);
                added++;
            }
            else
            {
                ignored++;
            }
        }
        // a silently-shrinking drop reads as "it didn't work" — say what happened
        AddNote = added == 0 && ignored > 0
            ? $"nothing added — {ignored} item{(ignored == 1 ? " isn't a PDF" : "s aren't PDFs")} (or already listed)"
            : ignored > 0
                ? $"{added} added · {ignored} ignored (not PDFs, or already listed)"
                : "";
    }

    public void RemoveFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths.ToList()) Files.Remove(p);
        AddNote = "";
    }

    internal async Task UnlockAsync()
    {
        if (Files.Count == 0)
        {
            Summary = "Add at least one PDF first.";
            return;
        }
        ResultLines.Clear();
        var suffix = KeepCopy
            ? (_cfg.UnlockSuffix.Length > 0 ? _cfg.UnlockSuffix : "_unlocked")
            : "";
        var password = Password;
        var paths = Files.ToList();

        int ok = 0, skip = 0, fail = 0;
        for (var i = 0; i < paths.Count; i++)
        {
            Summary = $"Unlocking {i + 1} of {paths.Count}…";
            var path = paths[i];
            var r = await Task.Run(() => Unlock.UnlockPdf(path, password, suffix: suffix));
            var name = Path.GetFileName(path);
            if (r.Ok)
            {
                ok++;
                ResultLines.Add(new UnlockResultLine(
                    r.InPlace ? $"✓  {name} — unlocked"
                              : $"✓  {name}  →  {Path.GetFileName(r.NewPath!)}",
                    UnlockResultKind.Ok));
            }
            else if (r.Status == "not_encrypted")
            {
                skip++;
                ResultLines.Add(new UnlockResultLine($"•  {name} — {r.Message}",
                    UnlockResultKind.Skip));
            }
            else
            {
                fail++;
                ResultLines.Add(new UnlockResultLine($"✗  {name} — {r.Message}",
                    UnlockResultKind.Fail));
            }
        }

        var parts = new List<string> { $"{ok} unlocked" };
        if (skip > 0) parts.Add($"{skip} already unlocked");
        if (fail > 0) parts.Add($"{fail} failed");
        Summary = string.Join(" · ", parts);

        // never remember a password that opened nothing — a saved wrong
        // password would just fail again silently next session
        if (ok > 0) RememberIfAsked();
    }

    /// <summary>"Remember this password" — stored DPAPI-protected, saved to
    /// config immediately so the Unlock tool works next session.</summary>
    private void RememberIfAsked()
    {
        var label = RememberLabel.Trim();
        if (label.Length == 0 || Password.Length == 0) return;
        var entry = new SavedPassword { Label = label, Password = PasswordVault.Protect(Password) };
        _cfg.SavedPasswords.Add(entry);
        Saved.Add(entry);
        RememberLabel = "";
        _saveCfg();
    }
}
