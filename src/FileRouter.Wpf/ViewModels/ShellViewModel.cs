using FileRouter.Core;
using FileRouter.Wpf.Mvvm;
using FileRouter.Wpf.Services;

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

    public ShellViewModel(Config cfg, string cfgPath, IPdfViewer viewer,
        IDialogService dialogs, FolderWatchService watch)
    {
        _cfg = cfg;
        _cfgPath = cfgPath;
        _viewer = viewer;
        _dialogs = dialogs;
        _watch = watch;

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

        _watch.Activity += OnFolderActivity;
    }

    internal Config Cfg => _cfg;
    internal Session Session => _session;
    internal History History => _history;

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

    /// <summary>Dashboard tiles + alert flash land in the dashboard task; the
    /// Ready refresh path already routes through here so that task is additive.</summary>
    private void RefreshDashboard(Scanner.ScanResult inboxScan) { }

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

    // ------------------------------------------------- processing (minimal)
    // The filing loop (commit/skip/undo/preview) is built out in the
    // processing-screen task; Start/advance/Done live here so every screen is
    // reachable from the first shell commit onward.

    private string _progressLine = "";
    public string ProgressLine { get => _progressLine; private set => Set(ref _progressLine, value); }

    private string _currentFilename = "";
    public string CurrentFilename { get => _currentFilename; private set => Set(ref _currentFilename, value); }

    private string _statusLine = "";
    public string StatusLine { get => _statusLine; internal set => Set(ref _statusLine, value); }

    private void RaiseProgress() => ProgressLine = $"{_session.Pos + 1} / {_session.Total}";

    internal void StartProcessing()
    {
        var scan = Scanner.Scan(_cfg.Inbox, _cfg.Sort);
        if (scan.Count == 0) { Rescan(); return; }
        _session.Start(scan.Matching);
        Screen = Screen.Processing;
        StatusLine = "";
        _ = LoadCurrentAsync();
    }

    internal async Task LoadCurrentAsync()
    {
        var path = _session.Current;
        if (path is null) { ShowDone(); return; }
        RaiseProgress();
        CurrentFilename = Path.GetFileName(path);
        await _viewer.ShowAsync(path);
    }

    private void ShowDone()
    {
        Screen = Screen.Done;
        _viewer.Blank();
        CountLine = "Session complete";
        DetailLine = $"{_session.Filed} filed, {_session.Skipped} set aside"
            + (_session.Vanished > 0 ? $", {_session.Vanished} vanished" : "");
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
        _history.Dispose();
    }
}
