using FileRouter.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FileRouter.App;

/// <summary>The filing loop: WebView2 PDF pane (left) + control panel (right).
/// The document renders in Edge's built-in PDF viewer; before every commit the
/// viewer is navigated away so Windows can release the file handle.
///
/// Lifecycle: Ready (count + Start) -> Processing -> Done (summary + Rescan).
/// The inbox and the set-aside folder are watched live (FileSystemWatcher,
/// with a 30-second poll backstop for network shares where change
/// notifications don't fire): new arrivals update the Ready count, join a
/// running queue at the tail, and the set-aside alert stays accurate.</summary>
public sealed class MainForm : Form
{
    private Config _cfg;              // replaced by Settings
    private readonly string _cfgPath;
    private History _history;         // re-opened if history_db changes
    private Session _session;

    private readonly WebView2 _viewer = new() { Dock = DockStyle.Fill };
    private readonly Label _progress = new() { AutoSize = true, Font = new Font("Segoe UI", 15, FontStyle.Bold) };
    private readonly Label _filename = new() { AutoSize = true, ForeColor = Color.Gray, MaximumSize = new Size(360, 0) };
    private readonly TextBox _nameBox = new() { Font = new Font("Segoe UI", 13), Width = 340 };
    private readonly Label _preview = new() { AutoSize = true, MaximumSize = new Size(360, 0), Font = new Font("Segoe UI", 10, FontStyle.Bold), MinimumSize = new Size(0, 40) };
    private readonly FlowLayoutPanel _routes = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
    private readonly Button _start = new() { Text = "Start Processing", Width = 340, Height = 48, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
    private readonly Button _rescan = new() { Text = "Rescan inbox", Width = 340, Height = 34 };
    private readonly Button _skip = new() { Text = "Set aside   ·   Ctrl+K", Width = 340, Height = 34 };
    private readonly Button _undo = new() { Text = "Undo last   ·   Ctrl+Shift+Z", Width = 340, Height = 34, Enabled = false };
    private readonly Label _status = new() { ForeColor = Color.FromArgb(185, 119, 14), AutoSize = true, MaximumSize = new Size(360, 0) };
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _deferredAlert = new()
    { ForeColor = Color.FromArgb(185, 119, 14), Font = new Font("Segoe UI", 9, FontStyle.Bold), IsLink = true, Visible = false };

    private FileSystemWatcher? _inboxWatcher;
    private FileSystemWatcher? _deferredWatcher;
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 1500 };
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 30_000 };

    private int? _lastRoute;
    private bool _viewerReady;
    private bool _processing;
    private bool _busy;   // reentrancy guard for commit/skip/undo
    private readonly AutoCompleteStringCollection _completer = new();

    // Ready dashboard: monitored-folder tiles + alert flashing
    private readonly Label _monitorTitle = new() { AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), Visible = false, Margin = new Padding(0, 10, 0, 2) };
    private readonly FlowLayoutPanel _tiles = new() { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Visible = false };
    private readonly System.Windows.Forms.Timer _flash = new() { Interval = 600 };
    private bool _flashOn;
    private bool _inboxAlerting;

    public MainForm(Config cfg, string cfgPath)
    {
        _cfg = cfg;
        _cfgPath = cfgPath;
        var dbPath = ResolvePath(cfg.HistoryDb, cfgPath);
        // Daily point-in-time backup, taken while the file is at rest — BEFORE
        // we open the connection. The audit DB is the only link between a filed
        // document and its original id, so it must have redundancy.
        HistoryBackup.BackupDaily(dbPath,
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "backups"),
            DateTime.Now);
        _history = new History(dbPath);
        _session = new Session(cfg, _history);

        Text = "FileRouter";
        ClientSize = new Size(1280, 860);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        BuildUi();
        KeyDown += OnKeyDown;
        FormClosed += (_, _) => { _debounce.Dispose(); _poll.Dispose(); _flash.Dispose(); _history.Dispose(); };
        Load += async (_, _) => await InitAsync();
    }

    private static string ResolvePath(string value, string cfgPath) =>
        Path.IsPathRooted(value)
            ? value
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cfgPath))!, value);

    // ------------------------------------------------------------------ UI
    private void BuildUi()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(10),
            AutoScroll = true,
        };
        void Row(Control c) { panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.Controls.Add(c); }
        Row(_progress);
        Row(_filename);
        Row(_monitorTitle);
        Row(_tiles);
        Row(new Label { Text = "Name:", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        Row(_nameBox);
        Row(_preview);
        Row(_routes);
        Row(_skip);
        Row(_undo);
        Row(_start);
        Row(_rescan);
        Row(_status);
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // spacer

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
        split.Panel1.Controls.Add(_viewer);
        split.Panel2.Controls.Add(panel);
        Controls.Add(split);
        // set the splitter AFTER the container has its size — setting it in
        // the initializer runs against the default 150px width and misplaces
        Shown += (_, _) => split.SplitterDistance = Math.Max(400, ClientSize.Width - 400);

        _statusStrip.Items.Add(_deferredAlert);
        Controls.Add(_statusStrip);

        // Tools menu — file operations that stand apart from the filing loop.
        var menu = new MenuStrip();
        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.Add("&Unlock PDFs…", null, (_, _) => new UnlockDialog().ShowDialog(this));
        tools.DropDownItems.Add("&Bulk rename…", null, (_, _) => new BulkRenameDialog().ShowDialog(this));
        tools.DropDownItems.Add("&Match and merge…", null, (_, _) =>
            new MatchMergeDialog(_cfg, SaveMergeHeaders).ShowDialog(this));
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add("&Settings…", null, (_, _) => OpenSettings());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("&Export history to spreadsheet…", null, (_, _) => ExportHistory());
        menu.Items.Add(file);
        menu.Items.Add(tools);
        Controls.Add(menu);
        MainMenuStrip = menu;

        // history-ranked name suggestions (recency, then frequency), seeded
        // from names.txt — the speed win for the "type name" loop
        _nameBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _nameBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _nameBox.AutoCompleteCustomSource = _completer;
        _nameBox.PlaceholderText = "Type name…  (blank = file without renaming)";
        _nameBox.TextChanged += (_, _) => { ForceUpper(); UpdatePreview(); };
        _nameBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; OnEnter(); }
        };
        _skip.Click += (_, _) => OnSkip();
        _undo.Click += (_, _) => OnUndo();
        _start.Click += (_, _) => StartProcessing();
        _rescan.Click += (_, _) => Rescan();
        _deferredAlert.Click += (_, _) => OpenFolder(_cfg.Deferred);
    }

    internal string? InitError { get; private set; }

    private async Task InitAsync()
    {
        try
        {
            await _viewer.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            InitError = ex.ToString();
            MessageBox.Show(
                "The PDF viewer (WebView2) failed to start:\n\n" + ex.Message,
                "FileRouter", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _viewerReady = true;
        BuildWatchers();
        _debounce.Tick += (_, _) => { _debounce.Stop(); OnFolderActivity(); };
        _poll.Tick += (_, _) => OnFolderActivity();
        _poll.Start();
        _flash.Tick += (_, _) => { _flashOn = !_flashOn; ApplyFlash(); };
        Rescan();
    }

    // ------------------------------------------------------------ watching
    private void BuildWatchers()
    {
        _inboxWatcher = Watch(_cfg.Inbox);
        _deferredWatcher = Watch(_cfg.Deferred);
    }

    private FileSystemWatcher? Watch(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        var w = new FileSystemWatcher(folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        void Poke(object? s, FileSystemEventArgs e) =>
            BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
        w.Created += Poke;
        w.Deleted += Poke;
        w.Renamed += (s, e) => Poke(s, e);
        return w;
    }

    /// <summary>Debounced watcher/poll tick: refresh the set-aside alert, and
    /// either update the Ready count or feed new arrivals into the queue.</summary>
    private void OnFolderActivity()
    {
        RefreshDeferredAlert();
        var scan = Scanner.Scan(_cfg.Inbox, _cfg.Sort);
        if (scan.Error.Length > 0) return;
        if (_processing && !_session.Done)
        {
            var added = _session.Extend(scan.Matching);
            if (added > 0)
            {
                _progress.Text = $"{_session.Pos + 1} / {_session.Total}";
                ShowStatus($"{added} new file{(added == 1 ? "" : "s")} arrived — added to this session.");
            }
        }
        else if (!_processing)
        {
            ShowReady(scan);
        }
    }

    private void RefreshDeferredAlert()
    {
        var count = Scanner.CountFiles(_cfg.Deferred);
        _deferredAlert.Visible = count > 0;
        if (count > 0)
            _deferredAlert.Text = $"⚠ {count} set-aside file{(count == 1 ? "" : "s")} waiting — click to open";
    }

    // -------------------------------------------------------- dashboard tiles
    /// <summary>Rebuild the monitored-folder tiles (shown on Ready only, and
    /// only for folders holding files or in error), plus the inbox alert
    /// state, and (re)start the flash timer if anything is alerting.</summary>
    private void RefreshDashboard(Scanner.ScanResult inboxScan)
    {
        var statuses = FolderMonitor.All(_cfg.WatchFolders, _cfg.AlertTexts)
            .Where(s => s.HasFiles || s.Error.Length > 0).ToList();

        _tiles.Controls.Clear();
        foreach (var s in statuses) _tiles.Controls.Add(MakeTile(s));

        var show = !_processing && statuses.Count > 0;
        _monitorTitle.Text = _cfg.MonitorTitle;
        _monitorTitle.Visible = _tiles.Visible = show;

        _inboxAlerting = inboxScan.Matching
            .Any(f => FolderMonitor.IsAlerting(Path.GetFileName(f), _cfg.AlertTexts));

        var anyAlert = _inboxAlerting || statuses.Any(s => s.Alerting);
        if (anyAlert && _cfg.FlashAlerts) _flash.Start();
        else { _flash.Stop(); _flashOn = false; }
        ApplyFlash();
    }

    private Control MakeTile(FolderMonitor.FolderStatus s)
    {
        var (back, fore) = TileColors(s, flashing: false);
        var tile = new Panel { Width = 320, Height = 38, Margin = new Padding(0, 2, 0, 2), Cursor = Cursors.Hand, Tag = s, BackColor = back };
        var text = s.Error.Length > 0
            ? $"{s.Label}: {s.Error}"
            : $"{s.Label}: {s.Count}" + (s.Alerting ? "   ⚠" : "");
        var lbl = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), ForeColor = fore, Text = text };
        tile.Controls.Add(lbl);
        var tip = TileTip(s);
        var tooltip = new ToolTip();
        tooltip.SetToolTip(tile, tip);
        tooltip.SetToolTip(lbl, tip);
        void Open(object? _1, EventArgs _2) => OpenFolder(s.Path);
        tile.Click += Open;
        lbl.Click += Open;
        return tile;
    }

    private (Color Back, Color Fore) TileColors(FolderMonitor.FolderStatus s, bool flashing)
    {
        if (s.Alerting && (flashing || !_cfg.FlashAlerts))
            return (Color.FromArgb(192, 57, 43), Color.White);   // steady/flash red
        var back = SystemColors.ControlLight;
        if (!string.IsNullOrEmpty(s.Color))
        {
            try { back = ColorTranslator.FromHtml(s.Color); } catch { /* native */ }
        }
        var lum = 0.299 * back.R + 0.587 * back.G + 0.114 * back.B;
        return (back, lum > 150 ? Color.Black : Color.White);
    }

    private static string TileTip(FolderMonitor.FolderStatus s)
    {
        var lines = new List<string> { s.Path.Length > 0 ? s.Path : "(not set)" };
        if (s.Error.Length > 0) lines.Add(s.Error);
        if (s.Matches.Count > 0)
        {
            lines.Add("Alerts:");
            lines.AddRange(s.Matches.Take(8).Select(m => "  " + m));
            if (s.Matches.Count > 8) lines.Add($"  … +{s.Matches.Count - 8} more");
        }
        lines.Add("Click to open the folder");
        return string.Join("\n", lines);
    }

    /// <summary>Recolor alerting tiles and the inbox count for the flash tick
    /// (no rebuild — cheap enough to run twice a second).</summary>
    private void ApplyFlash()
    {
        foreach (Control tile in _tiles.Controls)
        {
            if (tile.Tag is not FolderMonitor.FolderStatus s) continue;
            var (back, fore) = TileColors(s, _flashOn);
            tile.BackColor = back;
            if (tile.Controls.Count > 0) tile.Controls[0].ForeColor = fore;
        }
        _progress.ForeColor = _inboxAlerting && (_flashOn || !_cfg.FlashAlerts)
            ? Color.FromArgb(192, 57, 43) : SystemColors.ControlText;
    }

    private static void OpenFolder(string folder)
    {
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder)
            { UseShellExecute = true });
    }

    // ------------------------------------------------------------ lifecycle
    internal void Rescan()
    {
        _processing = false;
        var scan = Scanner.Scan(_cfg.Inbox, _cfg.Sort);
        ShowReady(scan);
        RefreshDeferredAlert();
    }

    private void ShowReady(Scanner.ScanResult scan)
    {
        _viewer.CoreWebView2?.Navigate("about:blank");
        _progress.Text = scan.Error.Length > 0 ? "Inbox problem" : $"{scan.Count} files ready";
        _filename.Text = scan.Error.Length > 0
            ? scan.Error
            : (scan.IgnoredCount > 0 ? $"{scan.IgnoredCount} other files ignored" : "");
        _nameBox.Visible = _preview.Visible = _routes.Visible = _skip.Visible = false;
        _start.Visible = _rescan.Visible = true;
        _start.Enabled = scan.Count > 0;
        _undo.Visible = false;
        RefreshDashboard(scan);
    }

    internal void StartProcessing()
    {
        var scan = Scanner.Scan(_cfg.Inbox, _cfg.Sort);
        if (scan.Count == 0) { Rescan(); return; }
        BuildRoutes();
        _session.Start(scan.Matching);
        _lastRoute = null;
        _processing = true;
        _nameBox.Visible = _preview.Visible = _routes.Visible = _skip.Visible = true;
        _undo.Visible = true;
        _start.Visible = _rescan.Visible = false;
        // hide the dashboard while filing
        _monitorTitle.Visible = _tiles.Visible = false;
        _flash.Stop(); _flashOn = false; ApplyFlash();
        RefreshCompleter();
        LoadCurrent();
    }

    private void RefreshCompleter()
    {
        var seeds = Completer.LoadSeedNames(
            string.IsNullOrWhiteSpace(_cfg.NamesFile) ? null : ResolvePath(_cfg.NamesFile, _cfgPath));
        var names = Completer.Names(_history, seeds);
        _completer.Clear();
        _completer.AddRange(names.ToArray());
    }

    private void ExportHistory()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Spreadsheet files (*.csv)|*.csv",
            FileName = "filerouter_history.csv",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var count = _history.ExportCsv(dlg.FileName);
            MessageBox.Show($"Exported {count} rows to {dlg.FileName}", "FileRouter",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (IOException ex)
        {
            MessageBox.Show("Couldn't save it: " + ex.Message, "FileRouter",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void BuildRoutes()
    {
        _routes.Controls.Clear();
        for (var i = 0; i < _cfg.Routes.Count; i++)
        {
            var route = _cfg.Routes[i];
            var index = i;
            var label = route.Label
                + (route.AppendSuffix && route.Suffix.Length > 0 ? $"   ·   {route.Suffix}" : "")
                + (string.IsNullOrEmpty(route.Hotkey) ? "" : $"   ·   {route.Hotkey}");
            var btn = new Button { Text = label, Width = 340, Height = 44, Margin = new Padding(0, 3, 0, 3) };
            if (!string.IsNullOrEmpty(route.Color))
            {
                try
                {
                    var c = ColorTranslator.FromHtml(route.Color);
                    btn.BackColor = c;
                    btn.ForeColor = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150 ? Color.Black : Color.White;
                    btn.FlatStyle = FlatStyle.Flat;
                }
                catch { /* invalid color -> native look */ }
            }
            var problem = Config.ValidateRoute(route);
            if (problem.Length > 0)
            {
                btn.Enabled = false;
                btn.Text = label + "   (unavailable)";
            }
            btn.Click += async (_, _) => await OnRouteAsync(index);
            _routes.Controls.Add(btn);
        }
    }

    private void LoadCurrent()
    {
        var path = _session.Current;
        if (path is null) { ShowDone(); return; }
        _progress.Text = $"{_session.Pos + 1} / {_session.Total}";
        _filename.Text = Path.GetFileName(path);
        _nameBox.Clear();
        _undo.Enabled = _session.CanUndo;
        UpdatePreview();
        if (_viewerReady)
            _viewer.CoreWebView2.Navigate(new Uri(Path.GetFullPath(path)).AbsoluteUri);
        _nameBox.Focus();
    }

    private void ShowDone()
    {
        _processing = false;
        _viewer.CoreWebView2?.Navigate("about:blank");
        _progress.Text = "Session complete";
        _filename.Text = $"{_session.Filed} filed, {_session.Skipped} set aside"
            + (_session.Vanished > 0 ? $", {_session.Vanished} vanished" : "");
        _nameBox.Visible = _preview.Visible = _routes.Visible = _skip.Visible = false;
        _rescan.Visible = true;
        _undo.Visible = true;
        _undo.Enabled = _session.CanUndo;
    }

    // ------------------------------------------------------------- actions
    /// <summary>Navigate the viewer to a blank page and wait, so Edge releases
    /// the PDF file handle before the move.</summary>
    private async Task ReleaseViewerAsync()
    {
        if (!_viewerReady) return;
        var tcs = new TaskCompletionSource();
        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            _viewer.CoreWebView2.NavigationCompleted -= Handler;
            tcs.TrySetResult();
        }
        _viewer.CoreWebView2.NavigationCompleted += Handler;
        _viewer.CoreWebView2.Navigate("about:blank");
        await Task.WhenAny(tcs.Task, Task.Delay(2000));
    }

    internal async Task OnRouteAsync(int index)
    {
        // _busy is set BEFORE the first await: without it, a fast second
        // Enter/Ctrl+1 would start a second commit during ReleaseViewerAsync's
        // yield, capturing the same textbox text and mislabeling the next doc.
        if (_busy || !_processing || _session.Current is null || index >= _cfg.Routes.Count) return;
        _busy = true;
        try
        {
            var typed = _nameBox.Text;
            await ReleaseViewerAsync();
            try
            {
                var outcome = _session.CommitCurrent(typed, _cfg.Routes[index]);
                _lastRoute = index;
                if (outcome.Vanished)
                    ShowStatus("That file disappeared from the inbox — logged and moved on.");
            }
            catch (CommitError ex)
            {
                Warn(ex.Message, "FileRouter — couldn't file it");
                LoadCurrent();   // reload the same doc; nothing moved
                return;
            }
            RefreshDeferredAlert();
            RefreshCompleter();   // the just-used name is now suggestable
            LoadCurrent();
        }
        finally { _busy = false; }
    }

    private void OnEnter()
    {
        if (!_processing || !_cfg.EnterCommits) return;
        if (_lastRoute is { } i && i < _cfg.Routes.Count) _ = OnRouteAsync(i);
        else ShowStatus("Enter files to the last-used route — press a route button first.");
    }

    private async void OnSkip()
    {
        if (_busy || !_processing || _session.Current is null) return;
        _busy = true;
        try
        {
            await ReleaseViewerAsync();
            try
            {
                var outcome = _session.SkipCurrent();
                if (outcome.Vanished)
                    ShowStatus("That file disappeared from the inbox — logged and moved on.");
            }
            catch (CommitError ex)
            {
                Warn(ex.Message, "FileRouter — set-aside failed");
            }
            RefreshDeferredAlert();
            LoadCurrent();
        }
        finally { _busy = false; }
    }

    private void OnUndo()
    {
        if (_busy) return;
        if (!_session.CanUndo) { ShowStatus("Nothing to undo."); return; }
        _busy = true;
        try
        {
            try
            {
                var (filed, original, warning) = _session.UndoLast();
                ShowStatus(warning.Length > 0
                    ? warning
                    : $"Undid {Path.GetFileName(filed)} → {Path.GetFileName(original)}");
            }
            catch (CommitError ex)
            {
                Warn(ex.Message, "FileRouter — undo failed");
                return;
            }
            if (!_processing)   // undo from the Done screen re-enters the session
            {
                _processing = true;
                _nameBox.Visible = _preview.Visible = _routes.Visible = _skip.Visible = true;
                _start.Visible = _rescan.Visible = false;
            }
            RefreshDeferredAlert();
            RefreshCompleter();   // a reverted name may drop out of the suggestions
            LoadCurrent();
        }
        finally { _busy = false; }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.K) { e.Handled = true; OnSkip(); }
        else if (e.Control && e.Shift && e.KeyCode == Keys.Z) { e.Handled = true; OnUndo(); }
        else if (e.Control && e.KeyCode is >= Keys.D1 and <= Keys.D9)
        {
            var idx = e.KeyCode - Keys.D1;
            if (idx < _cfg.Routes.Count) { e.Handled = true; _ = OnRouteAsync(idx); }
        }
    }

    // ------------------------------------------------------------- helpers
    private void ForceUpper()
    {
        if (!_cfg.UppercaseNames) return;
        var pos = _nameBox.SelectionStart;
        var upper = _nameBox.Text.ToUpperInvariant();
        if (upper != _nameBox.Text)
        {
            _nameBox.Text = upper;
            _nameBox.SelectionStart = Math.Min(pos, upper.Length);
        }
    }

    /// <summary>Live "will be filed as" preview — the same BuildTarget the
    /// commit uses, so an illegal name (colon…) warns before the button.</summary>
    private void UpdatePreview()
    {
        var current = _session.Current;
        if (!_processing || current is null) { _preview.Text = ""; return; }
        try
        {
            var route = _lastRoute is { } i && i < _cfg.Routes.Count ? _cfg.Routes[i] : null;
            var result = Naming.BuildTarget(
                Path.GetFileName(current), _nameBox.Text,
                route?.NamingMode, _session.SessionMode,
                route?.Suffix ?? "", route?.AppendSuffix ?? false,
                _ => false);
            _preview.ForeColor = Color.Black;
            _preview.Text = result.Filename;
        }
        catch (ArgumentException ex)
        {
            _preview.ForeColor = Color.Firebrick;
            _preview.Text = "⚠ " + ex.Message;
        }
    }

    private void ShowStatus(string message) => _status.Text = message;

    private void SaveMergeHeaders(Dictionary<string, string> headers)
    {
        _cfg.MergeHeaders = headers;
        Config.Save(_cfg, _cfgPath);
    }

    private void OpenSettings()
    {
        using var dlg = new SettingsDialog(_cfg);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result is { } cfg)
            ApplySettings(cfg);
    }

    /// <summary>Adopt a new config: re-open the DB if its path changed, rebuild
    /// the session and watchers, save, and refresh the Ready view. Settings is
    /// only reachable from Ready, so there is no live session to disturb.</summary>
    private void ApplySettings(Config cfg)
    {
        var oldDb = ResolvePath(_cfg.HistoryDb, _cfgPath);
        var newDb = ResolvePath(cfg.HistoryDb, _cfgPath);
        _cfg = cfg;
        Config.Save(cfg, _cfgPath);
        if (!string.Equals(oldDb, newDb, StringComparison.OrdinalIgnoreCase))
        {
            _history.Dispose();
            _history = new History(newDb);
        }
        _session = new Session(cfg, _history);
        BuildWatchers();   // inbox/deferred may have changed
        Rescan();          // refresh Ready + dashboard with the new config
    }

    // ------------------------------------------------- smoke-test surface
    /// <summary>When true (headless smoke), warnings are recorded instead of
    /// shown as modal dialogs that would block the message loop.</summary>
    internal bool SuppressDialogs { get; set; }
    internal string? LastDialog { get; private set; }

    private void Warn(string message, string title)
    {
        LastDialog = message;
        if (SuppressDialogs) return;
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    internal bool ViewerReady => _viewerReady;
    internal Session SessionForSmoke => _session;
    internal void SetTypedName(string name) => _nameBox.Text = name;
    internal string CurrentPdfUrl => _viewer.CoreWebView2?.Source ?? "";
}
