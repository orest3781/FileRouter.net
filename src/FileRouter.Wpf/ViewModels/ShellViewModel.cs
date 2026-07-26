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
    private readonly IWorkScheduler _scheduler;
    private readonly ISoundService _sounds;
    private readonly System.Threading.Timer _flash;

    public ShellViewModel(Config cfg, string cfgPath, IPdfViewer viewer,
        IDialogService dialogs, FolderWatchService watch,
        SynchronizationContext? uiContext = null, Func<ThemePalette>? palette = null,
        IWorkScheduler? scheduler = null, ISoundService? sounds = null)
    {
        _cfg = cfg;
        _cfgPath = cfgPath;
        _viewer = viewer;
        _dialogs = dialogs;
        _watch = watch;
        _uiContext = uiContext;
        _palette = palette ?? (() => ThemeManager.Current);
        _scheduler = scheduler ?? new TaskWorkScheduler();
        _sounds = sounds ?? new NullSoundService();
        _flash = new System.Threading.Timer(_ =>
        {
            if (_uiContext is null) FlashTick();
            else _uiContext.Post(_ => FlashTick(), null);
        });
        _lastActionTimer = new System.Threading.Timer(_ =>
        {
            if (_uiContext is null) HideLastAction();
            else _uiContext.Post(_ => HideLastAction(), null);
        });
        _toastTimer = new System.Threading.Timer(_ =>
        {
            if (_uiContext is null) HideToast();
            else _uiContext.Post(_ => HideToast(), null);
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
        OpenInboxCommand = new RelayCommand(() => OpenFolder(_cfg.Inbox));
        OpenToastCommand = new RelayCommand(() => { OpenFolder(_toastFolder); HideToast(); });
        RouteCommand = new AsyncRelayCommand<int>(OnRouteAsync);
        SkipCommand = new AsyncRelayCommand(OnSkipAsync);
        UndoCommand = new AsyncRelayCommand(OnUndoAsync, () => _session.CanUndo);
        StopCommand = new RelayCommand(StopSession);
        ExportHistoryCommand = new RelayCommand(ExportHistory);

        // Without these the async commands swallow every unexpected exception:
        // a failed filing would look like a button that simply did nothing.
        RouteCommand.OnError += ReportUnexpected;
        SkipCommand.OnError += ReportUnexpected;
        UndoCommand.OnError += ReportUnexpected;

        _watch.Activity += OnFolderActivity;
    }

    /// <summary>Raised for an exception no handler anticipated, so the window can
    /// put it in crash.log. The user is warned either way.</summary>
    public event Action<Exception>? UnexpectedError;

    private void ReportUnexpected(Exception ex)
    {
        UnexpectedError?.Invoke(ex);
        _dialogs.Warn(
            "Something went wrong and the last action may not have finished.\n\n" +
            ex.Message + "\n\nDetails were written to crash.log.", "Sendu");
    }

    /// <summary>Tell the user a document moved but went unrecorded, and keep
    /// going — the queue has already advanced past it.</summary>
    private void ReportAuditFailure(AuditError ex, string title)
    {
        if (_cfg.Sounds.Enabled) _sounds.Play(SoundEvent.Error, _cfg.Sounds.Error);
        UnexpectedError?.Invoke(ex);
        _dialogs.Warn(ex.Message, title);
        StatusLine = $"{Path.GetFileName(ex.NewPath)} moved, but the history " +
                     "database didn't record it — see the warning.";
    }

    internal Config Cfg => _cfg;
    internal string CfgPath => _cfgPath;
    internal Session Session => _session;
    internal History History => _history;

    /// <summary>True for the moment ApplySettings is re-opening the history
    /// DB at a new path — _history is unusable until the swap lands.</summary>
    internal bool HistorySwapping { get; private set; }

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
                Raise(nameof(TileControlsVisible));
            }
        }
    }

    public bool IsReady => Screen == Screen.Ready;
    public bool IsProcessing => Screen == Screen.Processing;
    public bool IsDone => Screen == Screen.Done;

    // -------------------------------------------------------- ready state
    private string _countLine = "";
    public string CountLine { get => _countLine; private set => Set(ref _countLine, value); }

    /// <summary>The big number over the Start button: inbox PDF count, or ⚠
    /// when the inbox can't be read (DetailLine carries the reason).</summary>
    private string _bigCount = "";
    public string BigCount { get => _bigCount; private set => Set(ref _bigCount, value); }

    private string _countCaption = "";
    public string CountCaption { get => _countCaption; private set => Set(ref _countCaption, value); }

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
    public RelayCommand OpenInboxCommand { get; }
    public RelayCommand OpenToastCommand { get; }

    /// <summary>Called once by the window after the viewer init attempt:
    /// start watching and take the first scan.</summary>
    public void Initialize() => _ = InitializeAsync();

    internal async Task InitializeAsync()
    {
        var cfg = _cfg;
        // Directory.Exists + watcher registration are network round trips on
        // an SMB share — never on the UI thread
        await _scheduler.Run(() => _watch.SetFolders(cfg.Inbox, cfg.Deferred));
        Rescan();
    }

    public void Rescan()
    {
        Screen = Screen.Ready;
        _viewer.Blank();
        _ = RefreshFoldersAsync(showErrors: true);
    }

    // ---------------------------------------------------- folder snapshots
    // Every folder read happens OFF the UI thread: over SMB a single
    // enumeration can take seconds, and a blocked UI thread here doesn't just
    // freeze the app — it starves the global mouse hook.
    private sealed record FolderSnapshot(
        Scanner.ScanResult Scan, Scanner.DeferredInfo Deferred,
        List<FolderMonitor.FolderStatus>? Statuses);

    private bool _refreshBusy;
    private bool _refreshPending;

    /// <summary>Gather (thread pool) → apply (UI). Overlapping requests
    /// coalesce: one runs, the latest waiter reruns after.</summary>
    internal async Task RefreshFoldersAsync(bool showErrors = false)
    {
        if (_refreshBusy) { _refreshPending = true; return; }
        _refreshBusy = true;
        try
        {
            do
            {
                _refreshPending = false;
                // while filing the dashboard is hidden, and in Hidden mode the
                // user asked for silence — either way skip the watch-folder
                // sweep (one network enumeration per watched folder, per
                // debounce, is pure churn on SMB)
                var mode = TileMode;
                var wantStatuses = Screen != Screen.Processing && mode != "hidden";
                var cfg = _cfg;
                var snap = await _scheduler.Run(() => new FolderSnapshot(
                    Scanner.Scan(cfg.Inbox, cfg.Sort, cfg.NamingMode),
                    Scanner.DeferredSummary(cfg.Deferred),
                    wantStatuses
                        ? FolderMonitor.All(cfg.WatchFolders, cfg.AlertTexts)
                            .Where(s => mode == "all" || s.HasFiles || s.Error.Length > 0)
                            .ToList()
                        : null));
                ApplySnapshot(snap, showErrors);
            } while (_refreshPending);
        }
        finally { _refreshBusy = false; }
    }

    private void ApplySnapshot(FolderSnapshot snap, bool showErrors)
    {
        ApplyDeferred(snap.Deferred);
        if (snap.Scan.Error.Length > 0 && !showErrors && Screen != Screen.Ready)
            return;   // a transient share hiccup must not wipe the screen

        if (Screen == Screen.Processing && !_session.Done)
        {
            if (snap.Scan.Error.Length > 0) return;
            var added = _session.Extend(snap.Scan.Matching);
            if (added > 0)
            {
                RaiseProgress();
                StatusLine = $"{added} new file{(added == 1 ? "" : "s")} arrived — added to this session.";
            }
        }
        else if (Screen == Screen.Ready)
        {
            ShowReady(snap);
        }
        else
        {
            // Done: notify, don't yank — the session summary stays put
            if (snap.Scan.Error.Length == 0 && snap.Scan.Count > 0)
                StatusLine = $"{snap.Scan.Count} file{(snap.Scan.Count == 1 ? "" : "s")} waiting in the inbox.";
        }
    }

    private void ShowReady(FolderSnapshot snap)
    {
        var scan = snap.Scan;
        _viewer.Blank();
        CountLine = scan.Error.Length > 0
            ? "Inbox problem"
            : $"{scan.Count} file{(scan.Count == 1 ? "" : "s")} ready";
        BigCount = scan.Error.Length > 0 ? "⚠" : scan.Count.ToString();
        CountCaption = scan.Error.Length > 0
            ? "inbox problem"
            : $"PDF{(scan.Count == 1 ? "" : "s")} in the inbox";
        DetailLine = scan.Error.Length > 0
            ? scan.Error
            : (scan.IgnoredCount > 0
                ? $"{scan.IgnoredCount} other file{(scan.IgnoredCount == 1 ? "" : "s")} ignored"
                : "");
        StartEnabled = scan.Count > 0;
        RefreshDashboard(scan, snap.Statuses);
    }

    private void ApplyDeferred(Scanner.DeferredInfo info)
    {
        if (info.Count == 0) { DeferredAlert = ""; return; }
        var age = info.OldestAgeDays switch
        {
            0 => "",
            1 => "   ·   oldest 1 day",
            var d => $"   ·   oldest {d} days",
        };
        DeferredAlert = $"⚠ {info.Count} set-aside file{(info.Count == 1 ? "" : "s")} waiting"
            + age + "   —   click to open";
    }

    private async Task RefreshDeferredAsync()
    {
        var deferred = _cfg.Deferred;
        ApplyDeferred(await _scheduler.Run(() => Scanner.DeferredSummary(deferred)));
    }

    // ----------------------------------------------------------- dashboard
    public ObservableCollection<TileViewModel> Tiles { get; } = new();

    private string _monitorTitle = "";
    public string MonitorTitle { get => _monitorTitle; private set => Set(ref _monitorTitle, value); }

    private bool _dashboardVisible;
    public bool DashboardVisible { get => _dashboardVisible; private set => Set(ref _dashboardVisible, value); }

    /// <summary>True when monitoring is on (Active-only mode, folders
    /// configured) and nothing needs attention — the dashboard says so
    /// instead of silently vanishing the whole section.</summary>
    private bool _allQuiet;
    public bool AllQuiet { get => _allQuiet; private set => Set(ref _allQuiet, value); }

    /// <summary>The config's tile_visibility normalized: unknown values (hand
    /// edits, future keys) read as the default so the dashboard never
    /// silently disappears on a typo.</summary>
    private string TileMode => _cfg.TileVisibility switch
    {
        "all" or "hidden" => _cfg.TileVisibility,
        _ => "active",
    };

    /// <summary>Header-bar dropdown: 0 = Active only, 1 = All (even empty),
    /// 2 = Hidden. Persisted, and refreshes the dashboard live; Hidden also
    /// skips the watch-folder sweep entirely — a real saving on SMB.</summary>
    public int TileVisibilityIndex
    {
        get => TileMode switch { "all" => 1, "hidden" => 2, _ => 0 };
        set
        {
            var mode = value switch { 1 => "all", 2 => "hidden", _ => "active" };
            if (TileMode == mode) return;
            _cfg.TileVisibility = mode;
            Raise();
            SaveConfigNow();
            _ = RefreshFoldersAsync();
        }
    }

    /// <summary>The dropdown only appears on Ready, and only when there are
    /// monitored folders to control.</summary>
    public bool TileControlsVisible => IsReady && _cfg.WatchFolders.Count > 0;

    private bool _inboxAlerting;
    public bool InboxAlerting { get => _inboxAlerting; private set => Set(ref _inboxAlerting, value); }

    private bool _flashOn;
    internal bool FlashRunning { get; private set; }

    /// <summary>The Ready count goes alert-red while an inbox filename trips an
    /// alert term (flashing, or steady when flash_alerts is off).</summary>
    public bool CountAlertOn => InboxAlerting && (_flashOn || !_cfg.FlashAlerts);

    private List<string> _tileSignature = new();
    private ThemePalette? _tilePalette;

    /// <summary>Rebuild the monitored-folder tiles (shown on Ready only, and
    /// only for folders holding files or in error), the inbox alert state, and
    /// (re)start the 600 ms flash if anything is alerting. Statuses arrive
    /// pre-gathered off-thread. Tiles are only REBUILT when something actually
    /// changed — the backstop poll must not replay the fade-in on identical
    /// tiles (the "blink").</summary>
    private void RefreshDashboard(Scanner.ScanResult inboxScan,
        List<FolderMonitor.FolderStatus>? swept)
    {
        var statuses = swept ?? new List<FolderMonitor.FolderStatus>();
        var p = _palette();
        var signature = statuses.Select(s =>
            $"{s.Label}|{s.Path}|{s.Color}|{s.Count}|{s.Error}|{s.Alerting}"
            + $"|{string.Join(",", s.AlertFolders)}").ToList();
        if (!ReferenceEquals(p, _tilePalette) || !signature.SequenceEqual(_tileSignature))
        {
            _tileSignature = signature;
            _tilePalette = p;
            Tiles.Clear();
            foreach (var s in statuses) Tiles.Add(new TileViewModel(s, p));
        }

        MonitorTitle = _cfg.MonitorTitle;
        DashboardVisible = Screen == Screen.Ready && statuses.Count > 0;
        AllQuiet = Screen == Screen.Ready && statuses.Count == 0
            && _cfg.WatchFolders.Count > 0 && TileMode == "active";

        InboxAlerting = inboxScan.Matching
            .Any(f => FolderMonitor.IsAlerting(Path.GetFileName(f), _cfg.AlertTexts));

        var anyAlert = InboxAlerting || statuses.Any(s => s.Alerting);
        HasActiveAlert = anyAlert;
        DetectNewAlerts(inboxScan, swept);

        if (anyAlert && _cfg.FlashAlerts)
        {
            // don't reset a running flash — every poll would otherwise
            // visibly stutter the blink phase
            if (!FlashRunning)
            {
                FlashRunning = true;
                _flash.Change(600, 600);
            }
        }
        else
        {
            StopFlash();
        }
        ApplyFlashAll();
    }

    // ------------------------------------------------------------ new alerts
    // A per-file memory: which alerting files we've already reacted to. The
    // sound, the toast, and the window flash all fire ONCE per newly-arrived
    // alerting file — never on a later poll re-seeing the same one.
    //
    // The inbox and the watch folders are remembered SEPARATELY because they
    // are not always scanned together: Hidden mode skips the watch-folder
    // sweep. A skipped sweep is not evidence the alert went away, so it must
    // leave that half of the memory alone — otherwise re-showing the tiles
    // would replay every standing alert as if it had just landed.
    private readonly HashSet<string> _seenInboxAlerts = new();
    private readonly HashSet<string> _seenFolderAlerts = new();
    private bool _alertsSeeded;

    /// <summary>True while any alerting file exists — drives the taskbar badge.</summary>
    private bool _hasActiveAlert;
    public bool HasActiveAlert { get => _hasActiveAlert; private set => Set(ref _hasActiveAlert, value); }

    /// <summary>Raised when a genuinely new alerting file appears (not on
    /// startup, not on re-poll) — the window flashes its taskbar button.</summary>
    public event Action? AlertArrived;

    private sealed record AlertItem(string Key, string Label, string File, string Folder);

    /// <param name="swept">Watch-folder statuses, or null when the sweep was
    /// skipped — in which case the folder half of the memory is left intact.</param>
    private void DetectNewAlerts(Scanner.ScanResult inboxScan,
        List<FolderMonitor.FolderStatus>? swept)
    {
        // watch folders first, so the toast names one of those over an inbox
        // hit when several land at once
        var current = new List<AlertItem>();
        if (swept is not null)
            foreach (var s in swept.Where(s => s.Alerting))
                foreach (var m in s.Matches)
                    current.Add(new AlertItem($"{s.Path}\0{m}", s.Label,
                        Path.GetFileName(m), s.Path));
        var folderCount = current.Count;

        foreach (var f in inboxScan.Matching)
        {
            var nm = Path.GetFileName(f);
            if (FolderMonitor.IsAlerting(nm, _cfg.AlertTexts))
                current.Add(new AlertItem($"inbox\0{nm}", "the inbox", nm, _cfg.Inbox));
        }

        // first sweep after launch/rescan: adopt what's already there silently —
        // the red tiles are visible; the chime is for things that arrive later
        if (_alertsSeeded)
        {
            var fresh = current
                .Where((a, i) => !(i < folderCount ? _seenFolderAlerts : _seenInboxAlerts)
                    .Contains(a.Key))
                .ToList();
            if (fresh.Count > 0) RaiseNewAlerts(fresh);
        }
        _alertsSeeded = true;

        Remember(_seenInboxAlerts, current.Skip(folderCount));
        if (swept is not null) Remember(_seenFolderAlerts, current.Take(folderCount));
    }

    private static void Remember(HashSet<string> memory, IEnumerable<AlertItem> live)
    {
        memory.Clear();
        foreach (var a in live) memory.Add(a.Key);
    }

    private void RaiseNewAlerts(List<AlertItem> fresh)
    {
        if (_cfg.Sounds.Enabled) _sounds.Play(SoundEvent.NewAlert, _cfg.Sounds.NewAlert);

        var first = fresh[0];
        ShowToast(
            fresh.Count == 1 ? "Alert" : $"{fresh.Count} new alerts",
            fresh.Count == 1 ? $"{first.File} — in {first.Label}" : $"newest: {first.File}",
            first.Folder);
        AlertArrived?.Invoke();
    }

    // ---------------------------------------------------------------- toast
    // A transient bottom-corner card when a new alert arrives — reaches you
    // even if the dashboard tile is scrolled off or the window is unfocused.
    internal const int ToastMs = 7000;
    private readonly System.Threading.Timer _toastTimer;
    private string _toastFolder = "";

    private bool _toastVisible;
    public bool ToastVisible { get => _toastVisible; private set => Set(ref _toastVisible, value); }

    private string _toastText = "";
    public string ToastText { get => _toastText; private set => Set(ref _toastText, value); }

    private string _toastDetail = "";
    public string ToastDetail { get => _toastDetail; private set => Set(ref _toastDetail, value); }

    private void ShowToast(string text, string detail, string folder)
    {
        ToastText = text;
        ToastDetail = detail;
        _toastFolder = folder;
        ToastVisible = true;
        _toastTimer.Change(ToastMs, Timeout.Infinite);
    }

    internal void HideToast()
    {
        ToastVisible = false;
        _toastTimer.Change(Timeout.Infinite, Timeout.Infinite);
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
    /// either update the Ready count or feed new arrivals into the queue.
    /// All disk reads happen off-thread inside RefreshFoldersAsync.</summary>
    internal void OnFolderActivity() => _ = RefreshFoldersAsync();

    // ------------------------------------------------------ processing state
    private string _progressLine = "";
    public string ProgressLine { get => _progressLine; private set => Set(ref _progressLine, value); }

    private string _currentFilename = "";
    public string CurrentFilename { get => _currentFilename; private set => Set(ref _currentFilename, value); }

    private string _statusLine = "";
    public string StatusLine { get => _statusLine; internal set => Set(ref _statusLine, value); }

    // -------------------------------------------------- last-action card
    // A transient confirmation after every commit/set-aside, colored like the
    // route that was pressed — the at-a-glance "yes, it went where I meant".
    internal const int LastActionMs = 4000;
    private readonly System.Threading.Timer _lastActionTimer;

    private bool _lastActionVisible;
    public bool LastActionVisible { get => _lastActionVisible; private set => Set(ref _lastActionVisible, value); }

    private string _lastActionText = "";
    public string LastActionText { get => _lastActionText; private set => Set(ref _lastActionText, value); }

    private string _lastActionDetail = "";
    public string LastActionDetail { get => _lastActionDetail; private set => Set(ref _lastActionDetail, value); }

    private Rgb _lastActionBack;
    public Rgb LastActionBack { get => _lastActionBack; private set => Set(ref _lastActionBack, value); }

    private Rgb _lastActionFore;
    public Rgb LastActionFore { get => _lastActionFore; private set => Set(ref _lastActionFore, value); }

    private void ShowLastAction(string text, string detail, Rgb back)
    {
        LastActionText = text;
        LastActionDetail = detail;
        LastActionBack = back;
        LastActionFore = ThemePalette.IdealForeground(back);
        LastActionVisible = true;
        _lastActionTimer.Change(LastActionMs, Timeout.Infinite);
    }

    internal void HideLastAction()
    {
        LastActionVisible = false;
        _lastActionTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public ObservableCollection<RouteButtonViewModel> Routes { get; } = new();

    /// <summary>Raised whenever the route set is rebuilt so the window can
    /// re-register the hotkey bindings.</summary>
    public event Action? RoutesRebuilt;

    /// <summary>Raised when the name box should take focus (new document).</summary>
    public event Action? RequestNameFocus;

    public AsyncRelayCommand<int> RouteCommand { get; }
    public AsyncRelayCommand SkipCommand { get; }
    public AsyncRelayCommand UndoCommand { get; }
    public RelayCommand StopCommand { get; }

    private int? _lastRoute;
    private bool _busy;   // cross-command reentrancy guard (commit/skip/undo)

    /// <summary>The same polishing the name box applies to typing — used on
    /// completer names too, so suggestions match and complete in their FINAL
    /// form (with the word separator, uppercased).</summary>
    private string Polish(string s)
    {
        if (_cfg.UppercaseNames) s = s.ToUpperInvariant();
        if (_cfg.WordSeparator.Length > 0) s = s.Replace(" ", _cfg.WordSeparator);
        return s;
    }

    /// <summary>What separates "words" in the name box: the configured word
    /// separator when set, else a space.</summary>
    private string WordBoundary =>
        _cfg.WordSeparator.Length > 0 ? _cfg.WordSeparator : " ";

    private string _typedName = "";
    public string TypedName
    {
        get => _typedName;
        set
        {
            var polished = Polish(value);
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

    internal void StartProcessing() => _ = StartProcessingAsync();

    internal async Task StartProcessingAsync()
    {
        if (_busy) return;
        var cfg = _cfg;
        // the scan AND the destination probes (ProbeWritable touches every
        // route folder — a network round trip each) run off the UI thread
        var (scan, problems) = await _scheduler.Run(() =>
            (Scanner.Scan(cfg.Inbox, cfg.Sort, cfg.NamingMode),
             cfg.Routes.Select(Config.ValidateRoute).ToList()));
        if (Screen == Screen.Processing) return;   // a double Start raced us
        if (scan.Count == 0) { Rescan(); return; }
        BuildRoutes(problems);
        _session.Start(scan.Matching);
        _lastRoute = null;
        Screen = Screen.Processing;
        StatusLine = "";
        HideLastAction();
        // hide the dashboard while filing
        DashboardVisible = false;
        AllQuiet = false;
        StopFlash();
        ApplyFlashAll();
        await RefreshCompleterAsync();
        await LoadCurrentAsync();
    }

    private void BuildRoutes(IReadOnlyList<string> problems)
    {
        Routes.Clear();
        var p = _palette();
        for (var i = 0; i < _cfg.Routes.Count; i++)
            Routes.Add(new RouteButtonViewModel(i, _cfg.Routes[i], p,
                i < problems.Count ? problems[i] : ""));
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
        StatusLine = "";   // mid-session notes don't belong under the summary
        RaiseUndoState();
        if (_cfg.Sounds.Enabled) _sounds.Play(SoundEvent.Filed, _cfg.Sounds.Filed);
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
                var route = _cfg.Routes[index];
                // the move itself can be a copy+delete across SMB shares —
                // never on the UI thread
                var outcome = await _scheduler.Run(() => _session.CommitCurrent(typed, route));
                _lastRoute = index;
                MarkEnterRoute();
                if (outcome.Vanished)
                {
                    StatusLine = "That file disappeared from the inbox — logged and moved on.";
                }
                else
                {
                    var back = ThemePalette.ParseColor(route.Color) ?? _palette().Success;
                    ShowLastAction($"✓  Filed to {route.Label}",
                        Path.GetFileName(outcome.NewPath!), back);
                }
            }
            catch (AuditError ex)
            {
                // the move already happened and the queue moved with it — report
                // and carry on rather than reloading a document that isn't there
                _lastRoute = index;
                MarkEnterRoute();
                HideLastAction();
                ReportAuditFailure(ex, "Sendu — filed, but not recorded");
            }
            catch (CommitError ex)
            {
                if (_cfg.Sounds.Enabled) _sounds.Play(SoundEvent.Error, _cfg.Sounds.Error);
                _dialogs.Warn(ex.Message, "Sendu — couldn't file it");
                await LoadCurrentAsync();   // reload the same doc; nothing moved
                return;
            }
            await RefreshDeferredAsync();
            await RefreshCompleterAsync();   // the just-used name is now suggestable
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
                var outcome = await _scheduler.Run(() => _session.SkipCurrent());
                if (outcome.Vanished)
                    StatusLine = "That file disappeared from the inbox — logged and moved on.";
                else
                {
                    ShowLastAction("✓  Set aside for later",
                        Path.GetFileName(outcome.NewPath!), _palette().Warning);
                    if (_cfg.Sounds.Enabled) _sounds.Play(SoundEvent.SetAside, _cfg.Sounds.SetAside);
                }
            }
            catch (AuditError ex)
            {
                HideLastAction();
                ReportAuditFailure(ex, "Sendu — set aside, but not recorded");
            }
            catch (CommitError ex)
            {
                if (_cfg.Sounds.Enabled) _sounds.Play(SoundEvent.Error, _cfg.Sounds.Error);
                _dialogs.Warn(ex.Message, "Sendu — set-aside failed");
            }
            await RefreshDeferredAsync();
            await LoadCurrentAsync();
        }
        finally { _busy = false; }
    }

    internal void OnUndo() => _ = OnUndoAsync();

    internal async Task OnUndoAsync()
    {
        if (_busy) return;
        if (!_session.CanUndo) { StatusLine = "Nothing to undo."; return; }
        _busy = true;
        try
        {
            try
            {
                var (filed, original) = await _scheduler.Run(() => _session.UndoLast());
                StatusLine = $"Undid {Path.GetFileName(filed)} → {Path.GetFileName(original)}";
                HideLastAction();   // the card must never claim an undone filing
            }
            catch (AuditError ex)
            {
                // the file is back; only the history row is stale
                HideLastAction();
                ReportAuditFailure(ex, "Sendu — undone, but still logged as filed");
            }
            catch (CommitError ex)
            {
                _dialogs.Warn(ex.Message, "Sendu — undo failed");
                return;
            }
            if (Screen == Screen.Done)   // undo from Done re-enters the session
                Screen = Screen.Processing;
            await RefreshDeferredAsync();
            await RefreshCompleterAsync();   // a reverted name may drop out
            await LoadCurrentAsync();
        }
        finally { _busy = false; }
    }

    /// <summary>Show the ⏎ badge on the one button Enter would press now —
    /// the answer to "which button is Enter?" at a glance.</summary>
    private void MarkEnterRoute()
    {
        var want = _cfg.EnterCommits ? _lastRoute : null;
        for (var i = 0; i < Routes.Count; i++)
            Routes[i].IsEnterTarget = i == want;
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

    public RelayCommand ExportHistoryCommand { get; }

    /// <summary>Raised after new settings are adopted (window re-applies the
    /// font resources and hotkey bindings).</summary>
    public event Action? SettingsApplied;

    /// <summary>Persist the live config now (tool windows saving their own
    /// state: merge headers, remembered passwords).</summary>
    internal void SaveConfigNow()
    {
        if (!Config.TrySave(_cfg, _cfgPath, out var error))
            _dialogs.Warn(error, "Sendu — settings not saved");
    }

    internal void SaveMergeHeaders(Dictionary<string, string> headers)
    {
        _cfg.MergeHeaders = headers;
        SaveConfigNow();
    }

    /// <summary>Adopt a new config: re-open the DB if its path changed (with a
    /// fresh daily backup for the NEW db — a gap in the WinForms port), save
    /// (warning, not crashing, on a read-only file), rebuild watchers, refresh
    /// Ready. Settings is only reachable from Ready, so no live session.</summary>
    internal void ApplySettings(Config cfg) => _ = ApplySettingsAsync(cfg);

    internal async Task ApplySettingsAsync(Config cfg)
    {
        var oldDb = ResolvePath(_cfg.HistoryDb, _cfgPath);
        var newDb = ResolvePath(cfg.HistoryDb, _cfgPath);
        _cfg = cfg;
        if (!Config.TrySave(cfg, _cfgPath, out var error))
            _dialogs.Warn(error, "Sendu — settings not saved");
        if (!string.Equals(oldDb, newDb, StringComparison.OrdinalIgnoreCase))
        {
            // the backup copies the whole DB file — off the UI thread, it can
            // live on a share. While the swap runs, _history points at a
            // disposed instance: HistorySwapping gates the two UI entry
            // points (History window, CSV export) that could touch it.
            var old = _history;
            HistorySwapping = true;
            try
            {
                _history = await _scheduler.Run(() =>
                {
                    old.Dispose();
                    HistoryBackup.BackupDaily(newDb,
                        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(newDb))!, "backups"),
                        DateTime.Now);
                    return new History(newDb);
                });
            }
            finally { HistorySwapping = false; }
        }
        _session = new Session(cfg, _history);
        await _scheduler.Run(() => _watch.SetFolders(cfg.Inbox, cfg.Deferred));
        _watch.SetPollInterval(cfg.PollSeconds * 1000);   // adopt a changed cadence live
        Raise(nameof(UppercaseNames));
        Raise(nameof(TileVisibilityIndex));
        Raise(nameof(TileControlsVisible));
        SettingsApplied?.Invoke();
        Rescan();
    }

    /// <summary>File → Export history: the whole audit table as a spreadsheet
    /// (with the formula-injection guard History applies).</summary>
    internal void ExportHistory() => _ = ExportHistoryAsync();

    internal async Task ExportHistoryAsync()
    {
        if (HistorySwapping) return;   // mid-swap, the handle is being replaced
        var dest = _dialogs.AskSaveFile("Spreadsheet files (*.csv)|*.csv", "filerouter_history.csv");
        if (dest is null) return;
        try
        {
            var history = _history;
            var count = await _scheduler.Run(() => history.ExportCsv(dest));
            _dialogs.Info($"Exported {count} rows to {dest}", "Sendu");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Warn("Couldn't save it: " + ex.Message, "Sendu");
        }
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

    private async Task RefreshCompleterAsync()
    {
        var namesFile = string.IsNullOrWhiteSpace(_cfg.NamesFile)
            ? null : ResolvePath(_cfg.NamesFile, _cfgPath);
        var history = _history;
        // seed-file read + SQLite query off-thread; polished once here so a
        // seed list with spaces still suggests correctly with a separator set
        try
        {
            _allNames = await _scheduler.Run(() =>
                Completer.Names(history, Completer.LoadSeedNames(namesFile))
                    .Select(Polish).Distinct().ToList());
        }
        catch (Exception)
        {
            // Suggestions are a convenience, and this runs after EVERY commit.
            // An unreadable history (locked share, a DB that just died) must
            // not abort the filing loop on its way to the next document — keep
            // the names we already have and move on.
            return;
        }
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

    /// <summary>Down arrow: take the WHOLE top suggestion in one keystroke
    /// (Tab stays word-at-a-time; Enter stays free to commit).</summary>
    internal bool AcceptTopSuggestion()
    {
        if (Suggestions.Count == 0) return false;
        TypedName = Suggestions[0];
        return true;
    }

    /// <summary>Tab: complete the top suggestion one word at a time, honoring
    /// the configured word separator as the boundary (Python-parity muscle
    /// memory; Enter stays free to commit).</summary>
    internal bool CompleteNextWord()
    {
        if (Suggestions.Count == 0) return false;
        var full = Suggestions[0];
        var sep = WordBoundary;
        var len = TypedName.Length;
        if (len >= full.Length) return false;
        var start = len;
        if (start + sep.Length <= full.Length
            && string.CompareOrdinal(full, start, sep, 0, sep.Length) == 0)
            start += sep.Length;
        var idx = start < full.Length
            ? full.IndexOf(sep, start, StringComparison.Ordinal)
            : -1;
        TypedName = idx < 0 ? full : full[..idx];
        return true;
    }

    /// <summary>Shift+Tab: drop the last completed word (same boundary).
    /// False when the box is already empty — the keystroke then traverses
    /// focus backward normally instead of being swallowed.</summary>
    internal bool DropLastWord()
    {
        if (TypedName.Length == 0) return false;
        var sep = WordBoundary;
        var t = TypedName;
        if (t.EndsWith(sep, StringComparison.Ordinal)) t = t[..^sep.Length];
        var i = t.LastIndexOf(sep, StringComparison.Ordinal);
        TypedName = i < 0 ? "" : t[..i];
        return true;
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
        _lastActionTimer.Dispose();
        _toastTimer.Dispose();
        _history.Dispose();
    }
}
