using System.Collections.ObjectModel;
using System.Text.Json;
using FileRouter.Core;
using FileRouter.Wpf.Mvvm;
using FileRouter.Wpf.Services;
using FileRouter.Wpf.Theme;

namespace FileRouter.Wpf.ViewModels;

/// <summary>An editable route row (master-detail in the Routes section).</summary>
public sealed class RouteEditVm : ObservableObject
{
    private string _label = "", _path = "", _hotkey = "", _suffix = "", _color = "";
    private bool _appendSuffix;
    private string _namingMode = "";   // "" = session default

    public string Label { get => _label; set => Set(ref _label, value); }
    public string Hotkey { get => _hotkey; set => Set(ref _hotkey, value); }
    public string Suffix { get => _suffix; set => Set(ref _suffix, value); }
    public bool AppendSuffix { get => _appendSuffix; set => Set(ref _appendSuffix, value); }
    public string NamingMode { get => _namingMode; set => Set(ref _namingMode, value); }

    public string Path
    {
        get => _path;
        set { if (Set(ref _path, value)) Raise(nameof(Problem)); }
    }

    public string Color
    {
        get => _color;
        set { if (Set(ref _color, value)) Raise(nameof(ColorValid)); }
    }

    /// <summary>Live destination check, shown in the detail form.</summary>
    public string Problem => Config.ValidateRoute(new Route { Path = Path });
    public bool ColorValid => Color.Length == 0 || ThemePalette.ParseColor(Color) is not null;

    // Derived by SettingsViewModel.RecomputeRouteDerived (it knows the route's
    // position, which decides the Ctrl+1-9 fallback and duplicate detection).
    private string _previewLabel = "";
    public string PreviewLabel { get => _previewLabel; private set => Set(ref _previewLabel, value); }

    private Rgb _previewBack;
    public Rgb PreviewBack { get => _previewBack; private set => Set(ref _previewBack, value); }

    private Rgb _previewFore;
    public Rgb PreviewFore { get => _previewFore; private set => Set(ref _previewFore, value); }

    private string _hotkeyNote = "";
    public string HotkeyNote
    {
        get => _hotkeyNote;
        private set { if (Set(ref _hotkeyNote, value)) Raise(nameof(HasHotkeyNote)); }
    }
    public bool HasHotkeyNote => HotkeyNote.Length > 0;

    internal void SetDerived(string previewLabel, Rgb back, Rgb fore, string hotkeyNote)
    {
        PreviewLabel = previewLabel;
        PreviewBack = back;
        PreviewFore = fore;
        HotkeyNote = hotkeyNote;
    }

    /// <summary>Unknown per-route keys from the original config, carried
    /// through so a hand-edited key survives Settings-OK.</summary>
    public Dictionary<string, JsonElement> Extras { get; init; } = new();

    public static RouteEditVm From(Route r) => new()
    {
        Label = r.Label,
        Path = r.Path,
        Hotkey = r.Hotkey,
        Suffix = r.Suffix,
        AppendSuffix = r.AppendSuffix,
        NamingMode = r.NamingMode ?? "",
        Color = r.Color ?? "",
        Extras = new Dictionary<string, JsonElement>(r.Extras),
    };

    public Route ToRoute() => new()
    {
        Label = Label.Trim(),
        Path = Path.Trim(),
        Hotkey = Hotkey.Trim(),
        Suffix = Suffix,
        AppendSuffix = AppendSuffix,
        NamingMode = NamingMode.Length == 0 ? null : NamingMode,
        Color = Color.Length == 0 ? null : Color.Trim(),
        Extras = new Dictionary<string, JsonElement>(Extras),
    };
}

/// <summary>An editable watch-folder row (Dashboard section).</summary>
public sealed class WatchEditVm : ObservableObject
{
    private string _label = "", _path = "", _filetypes = "", _color = "";
    private bool _recursive;

    public string Label { get => _label; set => Set(ref _label, value); }
    public string Path { get => _path; set => Set(ref _path, value); }
    public string Filetypes { get => _filetypes; set => Set(ref _filetypes, value); }
    public bool Recursive { get => _recursive; set => Set(ref _recursive, value); }

    public string Color
    {
        get => _color;
        set { if (Set(ref _color, value)) Raise(nameof(ColorValid)); }
    }

    public bool ColorValid => Color.Length == 0 || ThemePalette.ParseColor(Color) is not null;

    public Dictionary<string, JsonElement> Extras { get; init; } = new();

    public static WatchEditVm From(WatchFolder w) => new()
    {
        Label = w.Label,
        Path = w.Path,
        Filetypes = w.Filetypes,
        Recursive = w.Recursive,
        Color = w.Color ?? "",
        Extras = new Dictionary<string, JsonElement>(w.Extras),
    };

    public WatchFolder ToWatchFolder() => new()
    {
        Label = Label.Trim(),
        Path = Path.Trim(),
        Filetypes = Filetypes.Trim(),
        Recursive = Recursive,
        Color = Color.Length == 0 ? null : Color.Trim(),
        Extras = new Dictionary<string, JsonElement>(Extras),
    };
}

/// <summary>A saved Unlock password row; the stored value is DPAPI-protected
/// on save if it isn't already.</summary>
public sealed class PasswordEditVm : ObservableObject
{
    private string _label = "";
    public string Label { get => _label; set => Set(ref _label, value); }
    public string Stored { get; set; } = "";
    public string StatusText => PasswordVault.IsProtected(Stored)
        ? "encrypted" : "plain text — will be encrypted on save";
}

/// <summary>The Settings window's brain. Edits copies; OK validates (hard
/// errors block, warnings ask "Save anyway?") and produces a NEW Config built
/// by JSON-cloning the original — so every unedited field and every unknown
/// key survives by construction, killing the Python result_config() footgun.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    // KeyValuePair: WPF binds properties, not tuple fields
    public static readonly KeyValuePair<string, string>[] FontChoices =
    {
        new("", "(system default)"),
        new("Segoe UI", "Segoe UI"),
        new("Tahoma", "Tahoma"),
        new("Verdana", "Verdana"),
        new("Consolas", "Consolas"),
        new("Cascadia Mono", "Cascadia Mono"),
    };

    public static readonly KeyValuePair<string, string>[] SortChoices =
    {
        new("size_desc", "Largest first"),
        new("size_asc", "Smallest first"),
        new("mtime_desc", "Newest first"),
        new("mtime_asc", "Oldest first"),
        new("filename_asc", "Filename A to Z"),
        new("filename_desc", "Filename Z to A"),
    };

    public static readonly KeyValuePair<string, string>[] ModeChoices =
    {
        new("", "(session default)"),
        new("insert", "Keep date + ID"),
        new("replace", "Name only"),
    };

    /// <summary>Curated tile/button palette (all pair with black or white at
    /// WCAG AA via IdealForeground).</summary>
    public static readonly string[] SwatchColors =
    {
        "#2e7d32", "#1565c0", "#c0392b", "#6a1b9a", "#ef6c00", "#00838f",
        "#37474f", "#795548", "#9e9d24", "#d81b60", "#283593", "#00695c",
    };

    private readonly Config _original;
    private readonly IDialogService _dialogs;
    private readonly Func<ThemePalette> _palette;

    public Config? Result { get; private set; }

    public SettingsViewModel(Config current, IDialogService dialogs,
        Func<ThemePalette>? palette = null)
    {
        _original = current;
        _dialogs = dialogs;
        _palette = palette ?? (() => ThemePalette.Light);

        Inbox = current.Inbox;
        Deferred = current.Deferred;
        NamesFile = current.NamesFile;
        HistoryDb = current.HistoryDb;
        MonitorTitle = current.MonitorTitle;
        InsertMode = current.NamingMode == "insert";
        SortKey = current.Sort;
        EnterCommits = current.EnterCommits;
        UppercaseNames = current.UppercaseNames;
        WordSeparator = current.WordSeparator;
        TagWithRoute = current.TagWithRoute;
        FlashAlerts = current.FlashAlerts;
        AlertTextsText = string.Join(Environment.NewLine, current.AlertTexts);
        UiFontFamily = current.UiFontFamily;
        UiFontSizeText = current.UiFontSize == 0 ? "" : current.UiFontSize.ToString();
        UnlockSuffix = current.UnlockSuffix;

        Routes = new ObservableCollection<RouteEditVm>(current.Routes.Select(RouteEditVm.From));
        WatchFolders = new ObservableCollection<WatchEditVm>(current.WatchFolders.Select(WatchEditVm.From));
        Passwords = new ObservableCollection<PasswordEditVm>(current.SavedPasswords
            .Select(p => new PasswordEditVm { Label = p.Label, Stored = p.Password }));

        AddRouteCommand = new RelayCommand(() =>
        {
            var vm = new RouteEditVm { Label = "New destination" };
            Routes.Add(vm);
            SelectedRoute = vm;
        });
        RemoveRouteCommand = new RelayCommand(
            () => { if (SelectedRoute is { } r) Routes.Remove(r); SelectedRoute = Routes.FirstOrDefault(); },
            () => SelectedRoute is not null);
        RouteUpCommand = new RelayCommand(() => MoveRoute(-1), () => CanMoveRoute(-1));
        RouteDownCommand = new RelayCommand(() => MoveRoute(+1), () => CanMoveRoute(+1));

        AddWatchCommand = new RelayCommand(() =>
        {
            var vm = new WatchEditVm { Label = "New folder" };
            WatchFolders.Add(vm);
            SelectedWatch = vm;
        });
        RemoveWatchCommand = new RelayCommand(
            () => { if (SelectedWatch is { } w) WatchFolders.Remove(w); SelectedWatch = WatchFolders.FirstOrDefault(); },
            () => SelectedWatch is not null);

        RemovePasswordCommand = new RelayCommand(
            () => { if (SelectedPassword is { } p) Passwords.Remove(p); SelectedPassword = Passwords.FirstOrDefault(); },
            () => SelectedPassword is not null);

        BrowseInboxCommand = new RelayCommand(() => Inbox = _dialogs.BrowseFolder(Inbox) ?? Inbox);
        BrowseDeferredCommand = new RelayCommand(() => Deferred = _dialogs.BrowseFolder(Deferred) ?? Deferred);
        BrowseNamesFileCommand = new RelayCommand(() =>
            NamesFile = _dialogs.AskOpenFile("Name lists (*.txt)|*.txt|All files (*.*)|*.*") ?? NamesFile);
        // open-style picker: choosing the EXISTING audit db must not trigger
        // the save dialog's "already exists — replace?" prompt
        BrowseHistoryDbCommand = new RelayCommand(() =>
            HistoryDb = _dialogs.AskFilePath("SQLite database (*.sqlite)|*.sqlite|All files (*.*)|*.*",
                System.IO.Path.GetFileName(HistoryDb)) ?? HistoryDb);
        BrowseRoutePathCommand = new RelayCommand(() =>
        {
            if (SelectedRoute is { } r) r.Path = _dialogs.BrowseFolder(r.Path) ?? r.Path;
        });
        BrowseWatchPathCommand = new RelayCommand(() =>
        {
            if (SelectedWatch is { } w) w.Path = _dialogs.BrowseFolder(w.Path) ?? w.Path;
        });

        SelectedRoute = Routes.FirstOrDefault();
        SelectedWatch = WatchFolders.FirstOrDefault();
        SelectedPassword = Passwords.FirstOrDefault();

        // live route previews + duplicate-hotkey notes: recompute whenever a
        // route field or the route order changes
        foreach (var r in Routes) HookRoute(r);
        Routes.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (RouteEditVm r in e.NewItems) HookRoute(r);
            RecomputeRouteDerived();
        };
        RecomputeRouteDerived();
    }

    private void HookRoute(RouteEditVm r) =>
        r.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RouteEditVm.Label) or nameof(RouteEditVm.Hotkey)
                or nameof(RouteEditVm.Suffix) or nameof(RouteEditVm.AppendSuffix)
                or nameof(RouteEditVm.Color))
                RecomputeRouteDerived();
        };

    /// <summary>What each route button will actually look like and answer to —
    /// the same composition rules the Processing screen uses — plus a live
    /// "already used by" note the moment two routes claim one keystroke.</summary>
    private void RecomputeRouteDerived()
    {
        var p = _palette();
        var claimed = new Dictionary<string, string>();
        for (var i = 0; i < Routes.Count; i++)
        {
            var r = Routes[i];
            var gesture = HotkeyParser.ToGesture(r.Hotkey)
                ?? (i < 9 ? new System.Windows.Input.KeyGesture(
                        System.Windows.Input.Key.D1 + i,
                        System.Windows.Input.ModifierKeys.Control) : null);
            var gestureText = gesture is null ? "" : HotkeyParser.Display(gesture);

            var note = "";
            if (gestureText.Length > 0)
            {
                if (claimed.TryGetValue(gestureText, out var other))
                    note = $"{gestureText} is already used by \"{other}\"";
                else
                    claimed[gestureText] = r.Label.Trim();
            }

            var custom = ThemePalette.ParseColor(r.Color);
            var back = custom ?? p.Surface;
            var fore = custom is { } c ? ThemePalette.IdealForeground(c) : p.Text;
            var label = r.Label
                + (r.AppendSuffix && r.Suffix.Length > 0 ? $"   ·   {r.Suffix}" : "")
                + (gestureText.Length > 0 ? $"   ·   {gestureText}" : "");
            r.SetDerived(label, back, fore, note);
        }
    }

    // ------------------------------------------------------------- scalars
    private string _inbox = "";
    public string Inbox { get => _inbox; set => Set(ref _inbox, value); }

    private string _deferred = "";
    public string Deferred { get => _deferred; set => Set(ref _deferred, value); }

    private string _namesFile = "";
    public string NamesFile { get => _namesFile; set => Set(ref _namesFile, value); }

    private string _historyDb = "";
    public string HistoryDb { get => _historyDb; set => Set(ref _historyDb, value); }

    private string _monitorTitle = "";
    public string MonitorTitle { get => _monitorTitle; set => Set(ref _monitorTitle, value); }

    private bool _insertMode;
    public bool InsertMode
    {
        get => _insertMode;
        set { if (Set(ref _insertMode, value)) Raise(nameof(FilingExample)); }
    }

    private string _sortKey = "size_desc";
    public string SortKey { get => _sortKey; set => Set(ref _sortKey, value); }

    private bool _enterCommits;
    public bool EnterCommits { get => _enterCommits; set => Set(ref _enterCommits, value); }

    private bool _uppercaseNames;
    public bool UppercaseNames
    {
        get => _uppercaseNames;
        set { if (Set(ref _uppercaseNames, value)) Raise(nameof(FilingExample)); }
    }

    private string _wordSeparator = "";
    public string WordSeparator
    {
        get => _wordSeparator;
        set { if (Set(ref _wordSeparator, value)) Raise(nameof(FilingExample)); }
    }

    /// <summary>One live example tying mode + UPPERCASE + separator together —
    /// exactly what a fax from Smith John would file as with these settings.</summary>
    public string FilingExample
    {
        get
        {
            var name = "Smith John";
            if (UppercaseNames) name = name.ToUpperInvariant();
            if (WordSeparator.Length > 0 && !WordSeparator.Contains(' '))
                name = name.Replace(" ", WordSeparator);
            try
            {
                var result = Naming.BuildTarget(
                    "20240115--12345.pdf", name,
                    routeMode: null, globalMode: InsertMode ? "insert" : "replace",
                    routeSuffix: "", appendSuffix: false, exists: _ => false);
                return $"A fax typed as \"Smith John\" files as:  {result.Filename}";
            }
            catch (ArgumentException ex)
            {
                return "⚠ " + ex.Message;
            }
        }
    }

    private bool _tagWithRoute;
    public bool TagWithRoute { get => _tagWithRoute; set => Set(ref _tagWithRoute, value); }

    private bool _flashAlerts;
    public bool FlashAlerts { get => _flashAlerts; set => Set(ref _flashAlerts, value); }

    private string _alertTextsText = "";
    public string AlertTextsText { get => _alertTextsText; set => Set(ref _alertTextsText, value); }

    private string _uiFontFamily = "";
    public string UiFontFamily { get => _uiFontFamily; set => Set(ref _uiFontFamily, value); }

    private string _uiFontSizeText = "";
    public string UiFontSizeText { get => _uiFontSizeText; set => Set(ref _uiFontSizeText, value); }

    private string _unlockSuffix = "";
    public string UnlockSuffix { get => _unlockSuffix; set => Set(ref _unlockSuffix, value); }

    // ----------------------------------------------------------- collections
    public ObservableCollection<RouteEditVm> Routes { get; }
    public ObservableCollection<WatchEditVm> WatchFolders { get; }
    public ObservableCollection<PasswordEditVm> Passwords { get; }

    private RouteEditVm? _selectedRoute;
    public RouteEditVm? SelectedRoute
    {
        get => _selectedRoute;
        set
        {
            if (Set(ref _selectedRoute, value))
            {
                RemoveRouteCommand.RaiseCanExecuteChanged();
                RouteUpCommand.RaiseCanExecuteChanged();
                RouteDownCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private WatchEditVm? _selectedWatch;
    public WatchEditVm? SelectedWatch
    {
        get => _selectedWatch;
        set { if (Set(ref _selectedWatch, value)) RemoveWatchCommand.RaiseCanExecuteChanged(); }
    }

    private PasswordEditVm? _selectedPassword;
    public PasswordEditVm? SelectedPassword
    {
        get => _selectedPassword;
        set { if (Set(ref _selectedPassword, value)) RemovePasswordCommand.RaiseCanExecuteChanged(); }
    }

    public RelayCommand AddRouteCommand { get; }
    public RelayCommand RemoveRouteCommand { get; }
    public RelayCommand RouteUpCommand { get; }
    public RelayCommand RouteDownCommand { get; }
    public RelayCommand AddWatchCommand { get; }
    public RelayCommand RemoveWatchCommand { get; }
    public RelayCommand RemovePasswordCommand { get; }
    public RelayCommand BrowseInboxCommand { get; }
    public RelayCommand BrowseDeferredCommand { get; }
    public RelayCommand BrowseNamesFileCommand { get; }
    public RelayCommand BrowseHistoryDbCommand { get; }
    public RelayCommand BrowseRoutePathCommand { get; }
    public RelayCommand BrowseWatchPathCommand { get; }

    public void AddPassword(string label, string plain)
    {
        if (label.Trim().Length == 0 || plain.Length == 0) return;
        Passwords.Add(new PasswordEditVm
        {
            Label = label.Trim(),
            Stored = PasswordVault.Protect(plain),
        });
    }

    private bool CanMoveRoute(int delta)
    {
        if (SelectedRoute is null) return false;
        var i = Routes.IndexOf(SelectedRoute);
        var j = i + delta;
        return j >= 0 && j < Routes.Count;
    }

    private void MoveRoute(int delta)
    {
        if (SelectedRoute is null) return;
        var i = Routes.IndexOf(SelectedRoute);
        Routes.Move(i, i + delta);
        RouteUpCommand.RaiseCanExecuteChanged();
        RouteDownCommand.RaiseCanExecuteChanged();
    }

    // ----------------------------------------------------------- validation
    /// <summary>Problems that block OK outright.</summary>
    public List<string> HardErrors()
    {
        var errors = new List<string>();

        if (UiFontSizeText.Trim().Length > 0
            && (!int.TryParse(UiFontSizeText.Trim(), out var size) || size is < 6 or > 72))
            errors.Add("Base text size must be a number from 6 to 72 (or blank for the default).");

        if (WordSeparator.Contains(' '))
            errors.Add("The word separator can't contain a space.");

        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Routes)
        {
            var label = r.Label.Trim();
            if (label.Length == 0)
                errors.Add("Every destination button needs a label.");
            else if (!labels.Add(label))
                errors.Add($"Two destination buttons are both called \"{label}\".");

            if (r.Hotkey.Trim().Length > 0 && !HotkeyParser.TryParse(r.Hotkey, out _, out _))
                errors.Add($"\"{label}\": can't understand the hotkey \"{r.Hotkey}\".");
            if (!r.ColorValid)
                errors.Add($"\"{label}\": \"{r.Color}\" is not a color (try #2e7d32).");
        }

        // duplicate EFFECTIVE hotkeys — the same keystroke can't file two ways
        var seen = new Dictionary<string, string>();
        for (var i = 0; i < Routes.Count; i++)
        {
            var gesture = HotkeyParser.ToGesture(Routes[i].Hotkey)
                ?? (i < 9 ? new System.Windows.Input.KeyGesture(
                        System.Windows.Input.Key.D1 + i,
                        System.Windows.Input.ModifierKeys.Control) : null);
            if (gesture is null) continue;
            var text = HotkeyParser.Display(gesture);
            if (seen.TryGetValue(text, out var other))
                errors.Add($"\"{Routes[i].Label}\" and \"{other}\" both answer to {text}.");
            else
                seen[text] = Routes[i].Label;
        }

        foreach (var w in WatchFolders)
        {
            if (w.Label.Trim().Length == 0)
                errors.Add("Every monitored folder needs a label.");
            if (!w.ColorValid)
                errors.Add($"\"{w.Label}\": \"{w.Color}\" is not a color (try #c0392b).");
        }

        return errors;
    }

    /// <summary>Problems worth a "Save anyway?" — unreachable folders mostly
    /// (they may simply be offline right now).</summary>
    public List<string> Warnings()
    {
        var warnings = new List<string>();
        if (Inbox.Trim().Length == 0)
            warnings.Add("No inbox folder is set — there will be nothing to process.");
        else if (!Directory.Exists(Inbox.Trim()))
            warnings.Add($"The inbox folder doesn't exist: {Inbox.Trim()}");
        if (Deferred.Trim().Length > 0 && !Directory.Exists(Deferred.Trim()))
            warnings.Add($"The set-aside folder doesn't exist: {Deferred.Trim()}");
        foreach (var r in Routes)
        {
            var problem = Config.ValidateRoute(r.ToRoute());
            if (problem.Length > 0) warnings.Add($"\"{r.Label.Trim()}\": {problem}");
        }
        foreach (var w in WatchFolders)
        {
            if (w.Path.Trim().Length > 0 && !Directory.Exists(w.Path.Trim()))
                warnings.Add($"\"{w.Label.Trim()}\": folder doesn't exist: {w.Path.Trim()}");
        }
        return warnings;
    }

    // ---------------------------------------------------------------- build
    /// <summary>Validate, then produce <see cref="Result"/>. Hard errors show
    /// a dialog and return false; warnings ask "Save anyway?".</summary>
    public bool TryBuildResult()
    {
        var errors = HardErrors();
        if (errors.Count > 0)
        {
            _dialogs.Warn("These need fixing first:\n\n • " + string.Join("\n • ", errors),
                "FileRouter — check the settings");
            return false;
        }

        var warnings = Warnings();
        if (warnings.Count > 0 && !_dialogs.Confirm(
                " • " + string.Join("\n • ", warnings) + "\n\nSave anyway?",
                "FileRouter — possible problems"))
            return false;

        // JSON-clone the original so EVERY unedited field and unknown key
        // survives by construction (no hand-maintained carry-through list).
        var cfg = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(_original))!;

        cfg.Inbox = Inbox.Trim();
        cfg.Deferred = Deferred.Trim();
        cfg.NamesFile = NamesFile.Trim();
        cfg.HistoryDb = HistoryDb.Trim();
        cfg.MonitorTitle = MonitorTitle.Trim();
        cfg.NamingMode = InsertMode ? "insert" : "replace";
        cfg.Sort = SortKey;
        cfg.EnterCommits = EnterCommits;
        cfg.UppercaseNames = UppercaseNames;
        cfg.WordSeparator = WordSeparator;
        cfg.TagWithRoute = TagWithRoute;
        cfg.FlashAlerts = FlashAlerts;
        cfg.AlertTexts = AlertTextsText
            .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        cfg.UiFontFamily = UiFontFamily;
        cfg.UiFontSize = UiFontSizeText.Trim().Length == 0 ? 0 : int.Parse(UiFontSizeText.Trim());
        cfg.UnlockSuffix = UnlockSuffix.Trim();
        cfg.Routes = Routes.Select(r => r.ToRoute()).ToList();
        cfg.WatchFolders = WatchFolders.Select(w => w.ToWatchFolder()).ToList();
        cfg.SavedPasswords = Passwords.Select(p => new SavedPassword
        {
            Label = p.Label,
            // legacy plaintext gets protected the first time Settings saves
            Password = PasswordVault.IsProtected(p.Stored)
                ? p.Stored
                : PasswordVault.Protect(p.Stored),
        }).ToList();

        Result = cfg;
        return true;
    }
}
