using System.Collections.ObjectModel;
using FileRouter.Core;
using FileRouter.Wpf.Mvvm;
using FileRouter.Wpf.Services;
using FileRouter.Wpf.Theme;

namespace FileRouter.Wpf.ViewModels;

public enum Screen { Ready, Processing, Done }

/// <summary>The app's state machine: Ready (dashboard) → Processing (filing
/// loop) → Done (summary), plus live folder monitoring. Owns Config, History,
/// Session. No WPF types — the whole lifecycle is unit-tested headless, which
/// the WinForms MainForm never could be.</summary>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private Config _cfg;               // replaced by Settings
    private readonly string _cfgPath;
    private History _history;          // re-opened if history_db changes
    private Session _session;
    private readonly IPdfViewer _viewer;
    private readonly IDialogService _dialogs;
    private readonly FolderWatchService _watch;
    private readonly SynchronizationContext? _uiContext;
    private readonly Func<ThemePalette> _palette;
    private readonly System.Threading.Timer _flash;

    public ShellViewModel(Config cfg, string cfgPath, IPdfViewer viewer,
        IDialogService dialogs, FolderWatchService watch,
        SynchronizationContext? uiContext = null, Func<ThemePalette>? palette = null)
    {
        _cfg = cfg;
        _cfgPath = cfgPath;
        _viewer = viewer;
        _dialogs = dialogs;
        _watch = watch;
        _uiContext = uiContext;
        _palette = palette ?? (() => ThemeManager.Current);
        _flash = new System.Threading.Timer(_ =>
        {
            if (_uiContext is null) FlashTick();
            else _uiContext.Post(_ => FlashTick(), null);
        });

        var dbPath = ResolvePath(cfg.HistoryDb, cfgPath);
        // Daily point-in-time backup, taken while the file is at rest — BEFORE
        // we open the connection. The audit DB is the only link between a
        // filed document and its original id, so it must have redundancy.
        HistoryBackup.BackupDaily(dbPath,
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "backups"),
            DateTime.Now);
        _history = new History(dbPath);
        _session = new Session(cfg, _history);

        StartCommand = new RelayCommand(StartProcessing, () => StartEnabled);
        RescanCommand = new RelayCommand(Rescan);
        OpenDeferredCommand = new RelayCommand(() => OpenFolder(_cfg.Deferred));
        RouteCommand = new AsyncRelayCommand<int>(OnRouteAsync);
        SkipCommand = new AsyncRelayCommand(OnSkipAsync);
        UndoCommand = new RelayCommand(OnUndo, () => _session.CanUndo);
        StopCommand = new RelayCommand(StopSession);

        _watch.Activity += OnFolderActivity;
    }

    internal Config Cfg => _cfg;
    internal Session Session => _session;
    internal History History => _history;

    /// <summary>The view uses this to turn on TextBox CharacterCasing so
    /// uppercase typing keeps the caret steady.</summary>
    public bool UppercaseNames => _cfg.UppercaseNames;

    // ------------------------------------------------------------- screen
    private Screen _screen = Screen.Ready;
    public Screen Screen
    {
        get => _screen;
        private set
        {
            if (Set(ref _screen, value))
            {
                Raise(nameof(IsReady));
                Raise(nameof(IsProcessing));
                Raise(nameof(IsDone));
            }
        }
    }

    public bool IsReady => Screen == Screen.Ready;
    public bool IsProcessing => Screen == Screen.Processing;
    public bool IsDone => Screen == Screen.Done;

    // -------------------------------------------------------- ready state
    private string _countLine = "";
    public string CountLine { get => _countLine; private set => Set(ref _countLine, value); }

    private string _detailLine = "";
    public string DetailLine { get => _detailLine; private set => Set(ref _detailLine, value); }

    private bool _startEnabled;
    public bool StartEnabled
    {
        get => _startEnabled;
        private set { if (Set(ref _startEnabled, value)) StartCommand.RaiseCanExecuteChanged(); }
    }

    private string _deferredAlert = "";
    public string DeferredAlert { get => _deferredAlert; private set { if (Set(ref _deferredAlert, value)) Raise(nameof(HasDeferred)); } }
    public bool HasDeferred => DeferredAlert.Length > 0;

    public RelayCommand StartCommand { get; }
    public RelayCommand RescanCommand { get; }
    public RelayCommand OpenDeferredCommand { get; }

    /// <summary>Called once by the window after the viewer init attempt:
    /// start watching and take the first scan.</summary>
    public void Initialize()
    {
        _watch.SetFolders(_cfg.Inbox, _cfg.Deferred);
        Rescan();
    }

    public void Rescan()
    {
        Screen = Screen.Ready;
        ShowReady(Scanner.Scan(_cfg.Inbox, _cfg.Sort));
        RefreshDeferredAlert();
    }

    private void ShowReady(Scanner.ScanResult scan)
    {
        _viewer.Blank();
        CountLine = scan.Error.Length > 0
            ? "Inbox problem"
            : $"{scan.Count} file{(scan.Count == 1 ? "" : "s")} ready";
        DetailLine = scan.Error.Length > 0
            ? scan.Error
            : (scan.IgnoredCount > 0
                ? $"{scan.IgnoredCount} other file{(scan.IgnoredCount == 1 ? "" : "s")} ignored"
                : "");
        StartEnabled = scan.Count > 0;
        RefreshDashboard(scan);
    }

    private void RefreshDeferredAlert()
    {
        var count = Scanner.CountFiles(_cfg.Deferred);
        DeferredAlert = count > 0
            ? $"⚠ {count} set-aside file{(count == 1 ? "" : "s")} waiting — click to open"
            : "";
    }

    // ----------------------------------------------------------- dashboard
    public ObservableCollection<TileViewModel> Tiles { get; } = new();

    private string _monitorTitle = "";
    public string MonitorTitle { get => _monitorTitle; private set => Set(ref _monitorTitle, value); }

    private bool _dashboardVisible;
    public bool DashboardVisible { get => _dashboardVisible; private set => Set(ref _dashboardVisible, value); }

    private bool _inboxAlerting;
    public bool InboxAlerting { get => _inboxAlerting; private set => Set(ref _inboxAlerting, value); }

    private bool _flashOn;
    internal bool FlashRunning { get; private set; }

    /// <summary>The Ready count goes alert-red while an inbox filename trips an
    /// alert term (flashing, or steady when flash_alerts is off).</summary>
    public bool CountAlertOn => InboxAlerting && (_flashOn || !_cfg.FlashAlerts);

    /// <summary>Rebuild the monitored-folder tiles (shown on Ready only, and
    /// only for folders holding files or in error), the inbox alert state, and
    /// (re)start the 600 ms flash if anything is alerting.</summary>
    private void RefreshDashboard(Scanner.ScanResult inboxScan)
    {
        var statuses = FolderMonitor.All(_cfg.WatchFolders, _cfg.AlertTexts)
            .Where(s => s.HasFiles || s.Error.Length > 0).ToList();

        Tiles.Clear();
        foreach (var s in statuses) Tiles.Add(new TileViewModel(s, _palette()));

        MonitorTitle = _cfg.MonitorTitle;
        DashboardVisible = Screen == Screen.Ready && statuses.Count > 0;

        InboxAlerting = inboxScan.Matching
            .Any(f => FolderMonitor.IsAlerting(Path.GetFileName(f), _cfg.AlertTexts));

        var anyAlert = InboxAlerting || statuses.Any(s => s.Alerting);
        if (anyAlert && _cfg.FlashAlerts)
        {
            FlashRunning = true;
            _flash.Change(600, 600);
        }
        else
        {
            StopFlash();
        }
        ApplyFlashAll();
    }

    internal void FlashTick()
    {
        _flashOn = !_flashOn;
        ApplyFlashAll();
    }

    private void StopFlash()
    {
        FlashRunning = false;
        _flash.Change(Timeout.Infinite, Timeout.Infinite);
        _flashOn = false;
    }

    private void ApplyFlashAll()
    {
        var p = _palette();
        foreach (var tile in Tiles) tile.ApplyFlash(_flashOn, _cfg.FlashAlerts, p);
        Raise(nameof(CountAlertOn));
    }

    // ------------------------------------------------------------ watching
    /// <summary>Debounced watcher/poll tick: refresh the set-aside alert, and
    /// either update the Ready count or feed new arrivals into the queue.</summary>
    internal void OnFolderActivity()
    {
        RefreshDeferredAlert();
        var scan = Scanner.Scan(_cfg.Inbox, _cfg.Sort);
        if (scan.Error.Length > 0) return;
        if (Screen == Screen.Processing && !_session.Done)
        {
            var added = _session.Extend(scan.Matching);
            if (added > 0)
            {
                RaiseProgress();
                StatusLine = $"{added} new file{(added == 1 ? "" : "s")} arrived — added to this session.";
            }
        }
        else if (Screen != Screen.Processing)
        {
            ShowReady(scan);
        }
    }

    // ------------------------------------------------------ processing state
    private string _progressLine = "";
    public string ProgressLine { get => _progressLine; private set => Set(ref _progressLine, value); }

    private string _currentFilename = "";
    public string CurrentFilename { get => _currentFilename; private set => Set(ref _currentFilename, value); }

    private string _statusLine = "";
    public string StatusLine { get => _statusLine; internal set => Set(ref _statusLine, value); }

    public ObservableCollection<RouteButtonViewModel> Routes { get; } = new();

    /// <summary>Raised whenever the route set is rebuilt so the window can
    /// re-register the hotkey bindings.</summary>
    public event Action? RoutesRebuilt;

    /// <summary>Raised when the name box should take focus (new document).</summary>
    public event Action? RequestNameFocus;

    public AsyncRelayCommand<int> RouteCommand { get; }
    public AsyncRelayCommand SkipCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand StopCommand { get; }

    private int? _lastRoute;
    private bool _busy;   // cross-command reentrancy guard (commit/skip/undo)

    private string _typedName = "";
    public string TypedName
    {
        get => _typedName;
        set
        {
            var polished = _cfg.UppercaseNames ? value.ToUpperInvariant() : value;
            if (Set(ref _typedName, polished))
            {
                UpdatePreview();
                RefreshSuggestions();
            }
            else if (polished != value)
            {
                Raise();   // the view's text differs from the polished value
            }
        }
    }

    private string _preview = "";
    public string Preview { get => _preview; private set => Set(ref _preview, value); }

    private bool _previewIsWarning;
    public bool PreviewIsWarning { get => _previewIsWarning; private set => Set(ref _previewIsWarning, value); }

    public bool CanUndo => _session.CanUndo;

    private void RaiseProgress() => ProgressLine = $"{_session.Pos + 1} / {_session.Total}";

    internal void StartProcessing()
    {
        var scan = Scanner.Scan(_cfg.Inbox, _cfg.Sort);
        if (scan.Count == 0) { Rescan(); return; }
        BuildRoutes();
        _session.Start(scan.Matching);
        _lastRoute = null;
        Screen = Screen.Processing;
        StatusLine = "";
        // hide the dashboard while filing
        DashboardVisible = false;
        StopFlash();
        ApplyFlashAll();
        RefreshCompleter();
        _ = LoadCurrentAsync();
    }

    private void BuildRoutes()
    {
        Routes.Clear();
        var p = _palette();
        for (var i = 0; i < _cfg.Routes.Count; i++)
            Routes.Add(new RouteButtonViewModel(i, _cfg.Routes[i], p));
        RoutesRebuilt?.Invoke();
    }

    internal async Task LoadCurrentAsync()
    {
        var path = _session.Current;
        if (path is null) { ShowDone(); return; }
        RaiseProgress();
        CurrentFilename = Path.GetFileName(path);
        _typedName = "";
        Raise(nameof(TypedName));
        RefreshSuggestions();
        UpdatePreview();
        RaiseUndoState();
        await _viewer.ShowAsync(path);
        RequestNameFocus?.Invoke();
    }

    private void ShowDone()
    {
        Screen = Screen.Done;
        _viewer.Blank();
        CountLine = "Session complete";
        DetailLine = $"{_session.Filed} filed, {_session.Skipped} set aside"
            + (_session.Vanished > 0 ? $", {_session.Vanished} vanished" : "");
        RaiseUndoState();
    }

    private void RaiseUndoState()
    {
        Raise(nameof(CanUndo));
        UndoCommand.RaiseCanExecuteChanged();
    }

    // ------------------------------------------------------------- actions
    internal async Task OnRouteAsync(int index)
    {
        // _busy is set BEFORE the first await: without it, a fast second
        // Enter/Ctrl+1 would start a second commit during ReleaseAsync's
        // yield, capturing the same textbox text and mislabeling the next doc.
        if (_busy || Screen != Screen.Processing || _session.Current is null
            || index >= _cfg.Routes.Count) return;
        if (Routes.Count > index && !Routes[index].Enabled) return;
        _busy = true;
        try
        {
            var typed = TypedName;
            await _viewer.ReleaseAsync();
            try
            {
                var outcome = _session.CommitCurrent(typed, _cfg.Routes[index]);
                _lastRoute = index;
                if (outcome.Vanished)
                    StatusLine = "That file disappeared from the inbox — logged and moved on.";
            }
            catch (CommitError ex)
            {
                _dialogs.Warn(ex.Message, "FileRouter — couldn't file it");
                await LoadCurrentAsync();   // reload the same doc; nothing moved
                return;
            }
            RefreshDeferredAlert();
            RefreshCompleter();   // the just-used name is now suggestable
            await LoadCurrentAsync();
        }
        finally { _busy = false; }
    }

    internal async Task OnSkipAsync()
    {
        if (_busy || Screen != Screen.Processing || _session.Current is null) return;
        _busy = true;
        try
        {
            await _viewer.ReleaseAsync();
            try
            {
                var outcome = _session.SkipCurrent();
                if (outcome.Vanished)
                    StatusLine = "That file disappeared from the inbox — logged and moved on.";
            }
            catch (CommitError ex)
            {
                _dialogs.Warn(ex.Message, "FileRouter — set-aside failed");
            }
            RefreshDeferredAlert();
            await LoadCurrentAsync();
        }
        finally { _busy = false; }
    }

    internal void OnUndo()
    {
        if (_busy) return;
        if (!_session.CanUndo) { StatusLine = "Nothing to undo."; return; }
        _busy = true;
        try
        {
            try
            {
                var (filed, original, warning) = _session.UndoLast();
                StatusLine = warning.Length > 0
                    ? warning
                    : $"Undid {Path.GetFileName(filed)} → {Path.GetFileName(original)}";
            }
            catch (CommitError ex)
            {
                _dialogs.Warn(ex.Message, "FileRouter — undo failed");
                return;
            }
            if (Screen == Screen.Done)   // undo from Done re-enters the session
                Screen = Screen.Processing;
            RefreshDeferredAlert();
            RefreshCompleter();   // a reverted name may drop out of the suggestions
            _ = LoadCurrentAsync();
        }
        finally { _busy = false; }
    }

    /// <summary>Enter files to the last-used route (when enabled in config).</summary>
    public void OnEnter() => _ = OnEnterAsync();

    internal Task OnEnterAsync()
    {
        if (Screen != Screen.Processing || !_cfg.EnterCommits) return Task.CompletedTask;
        if (_lastRoute is { } i && i < _cfg.Routes.Count) return OnRouteAsync(i);
        StatusLine = "Enter files to the last-used route — press a route button first.";
        return Task.CompletedTask;
    }

    /// <summary>Esc: back to Ready. Nothing is lost — the remaining queue
    /// stays in the inbox.</summary>
    internal void StopSession()
    {
        if (Screen != Screen.Processing || _busy) return;
        StatusLine = "";
        Rescan();
    }

    /// <summary>Live "will be filed as" preview — the same BuildTarget the
    /// commit uses, so an illegal name (colon…) warns before the button.</summary>
    private void UpdatePreview()
    {
        var current = _session.Current;
        if (Screen != Screen.Processing || current is null)
        {
            Preview = "";
            PreviewIsWarning = false;
            return;
        }
        try
        {
            var route = _lastRoute is { } i && i < _cfg.Routes.Count ? _cfg.Routes[i] : null;
            var result = Naming.BuildTarget(
                Path.GetFileName(current), TypedName,
                route?.NamingMode, _session.SessionMode,
                route?.Suffix ?? "", route?.AppendSuffix ?? false,
                _ => false);
            Preview = result.Filename;
            PreviewIsWarning = false;
        }
        catch (ArgumentException ex)
        {
            Preview = "⚠ " + ex.Message;
            PreviewIsWarning = true;
        }
    }

    // -------------------------------------------------------- autocomplete
    private List<string> _allNames = new();

    public ObservableCollection<string> Suggestions { get; } = new();

    private void RefreshCompleter()
    {
        var seeds = Completer.LoadSeedNames(
            string.IsNullOrWhiteSpace(_cfg.NamesFile) ? null : ResolvePath(_cfg.NamesFile, _cfgPath));
        _allNames = Completer.Names(_history, seeds);
        RefreshSuggestions();
    }

    private bool _hasSuggestions;
    public bool HasSuggestions { get => _hasSuggestions; private set => Set(ref _hasSuggestions, value); }

    /// <summary>Top prefix matches for the popup; empty box suggests nothing
    /// (the ranked list would just be noise).</summary>
    private void RefreshSuggestions()
    {
        Suggestions.Clear();
        if (Screen == Screen.Processing && TypedName.Length > 0)
        {
            foreach (var name in _allNames
                         .Where(n => n.StartsWith(TypedName, StringComparison.OrdinalIgnoreCase)
                                     && !n.Equals(TypedName, StringComparison.OrdinalIgnoreCase))
                         .Take(8))
                Suggestions.Add(name);
        }
        HasSuggestions = Suggestions.Count > 0;
    }

    /// <summary>Close the popup (Esc / focus loss); it reopens on typing.</summary>
    internal void DismissSuggestions()
    {
        Suggestions.Clear();
        HasSuggestions = false;
    }

    /// <summary>Tab: complete the top suggestion one word at a time
    /// (Python-parity muscle memory; Enter stays free to commit).</summary>
    internal bool CompleteNextWord()
    {
        if (Suggestions.Count == 0) return false;
        var full = Suggestions[0];
        var len = TypedName.Length;
        if (len >= full.Length) return false;
        var start = len;
        if (full[start] == ' ') start++;
        var idx = full.IndexOf(' ', Math.Min(start, full.Length - 1));
        TypedName = idx < 0 ? full : full[..idx];
        return true;
    }

    /// <summary>Shift+Tab: drop the last completed word.</summary>
    internal void DropLastWord()
    {
        var t = TypedName.TrimEnd();
        var i = t.LastIndexOf(' ');
        TypedName = i < 0 ? "" : t[..i];
    }

    // ------------------------------------------------------------- helpers
    internal static string ResolvePath(string value, string cfgPath) =>
        Path.IsPathRooted(value)
            ? value
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cfgPath))!, value);

    internal static void OpenFolder(string folder)
    {
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder)
            { UseShellExecute = true });
    }

    public void Dispose()
    {
        _watch.Activity -= OnFolderActivity;
        _flash.Dispose();
        _history.Dispose();
    }
}
