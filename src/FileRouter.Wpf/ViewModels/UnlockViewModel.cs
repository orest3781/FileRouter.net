using System.Collections.ObjectModel;
using FileRouter.Core;
using FileRouter.Wpf.Mvvm;
using FileRouter.Wpf.Services;

namespace FileRouter.Wpf.ViewModels;

/// <summary>Unlock PDFs: add password-protected files, give (or pick a saved)
/// password, unlock. Default unlocks the original in place (verified, then
/// atomically swapped); "keep a copy" writes a suffixed copy instead. Saved
/// passwords are DPAPI-encrypted — the Python original kept them plaintext.</summary>
public sealed class UnlockViewModel : ObservableObject
{
    private readonly Config _cfg;
    private readonly Action _saveCfg;

    public ObservableCollection<string> Files { get; } = new();
    public ObservableCollection<SavedPassword> Saved { get; }

    public UnlockViewModel(Config cfg, Action saveCfg)
    {
        _cfg = cfg;
        _saveCfg = saveCfg;
        Saved = new ObservableCollection<SavedPassword>(cfg.SavedPasswords);
        KeepCopy = cfg.UnlockSuffix.Length > 0;
        UnlockCommand = new AsyncRelayCommand(UnlockAsync);
        ClearCommand = new RelayCommand(() => { Files.Clear(); Results = ""; });
    }

    private string _password = "";
    public string Password { get => _password; set => Set(ref _password, value); }

    private bool _keepCopy;
    public bool KeepCopy { get => _keepCopy; set => Set(ref _keepCopy, value); }

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

    private string _results = "";
    public string Results { get => _results; private set => Set(ref _results, value); }

    public AsyncRelayCommand UnlockCommand { get; }
    public RelayCommand ClearCommand { get; }

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
            if (File.Exists(p) && p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                && !Files.Contains(p))
                Files.Add(p);
    }

    public void RemoveFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths.ToList()) Files.Remove(p);
    }

    internal async Task UnlockAsync()
    {
        if (Files.Count == 0)
        {
            Results = "Add at least one PDF first.";
            return;
        }
        var suffix = KeepCopy
            ? (_cfg.UnlockSuffix.Length > 0 ? _cfg.UnlockSuffix : "_unlocked")
            : "";
        var password = Password;
        var paths = Files.ToList();

        var lines = await Task.Run(() => paths.Select(path =>
        {
            var r = Unlock.UnlockPdf(path, password, suffix: suffix);
            var name = Path.GetFileName(path);
            return r.Ok
                ? (r.InPlace ? $"✓ {name} — unlocked" : $"✓ {name}  →  {Path.GetFileName(r.NewPath!)}")
                : r.Status == "not_encrypted"
                    ? $"•  {name} — {r.Message}"
                    : $"✗  {name} — {r.Message}";
        }).ToList());

        Results = string.Join(Environment.NewLine, lines);
        RememberIfAsked();
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
