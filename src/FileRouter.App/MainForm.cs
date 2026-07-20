using FileRouter.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FileRouter.App;

/// <summary>The filing loop: WebView2 PDF pane (left) + control panel (right).
/// The document renders in Edge's built-in PDF viewer; before every commit the
/// viewer is navigated away so Windows can release the file handle.</summary>
public sealed class MainForm : Form
{
    private readonly Config _cfg;
    private readonly string _cfgPath;
    private readonly History _history;
    private readonly Session _session;

    private readonly WebView2 _viewer = new() { Dock = DockStyle.Fill };
    private readonly Label _progress = new() { AutoSize = true, Font = new Font("Segoe UI", 15, FontStyle.Bold) };
    private readonly Label _filename = new() { AutoSize = true, ForeColor = Color.Gray, MaximumSize = new Size(360, 0) };
    private readonly TextBox _nameBox = new() { Dock = DockStyle.Top, Font = new Font("Segoe UI", 13) };
    private readonly FlowLayoutPanel _routes = new() { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
    private readonly Button _skip = new() { Text = "Set aside   ·   Ctrl+K", Dock = DockStyle.Top, Height = 34 };
    private readonly Button _undo = new() { Text = "Undo last   ·   Ctrl+Shift+Z", Dock = DockStyle.Top, Height = 34, Enabled = false };
    private readonly Label _status = new() { Dock = DockStyle.Top, ForeColor = Color.FromArgb(185, 119, 14), AutoSize = false, Height = 40 };

    private int? _lastRoute;
    private bool _viewerReady;

    public MainForm(Config cfg, string cfgPath)
    {
        _cfg = cfg;
        _cfgPath = cfgPath;
        var dbPath = ResolvePath(cfg.HistoryDb, cfgPath);
        _history = new History(dbPath);
        _session = new Session(cfg, _history);

        Text = "FileRouter";
        Width = 1280;
        Height = 860;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        BuildUi();
        KeyDown += OnKeyDown;
        FormClosed += (_, _) => _history.Dispose();

        Load += async (_, _) => await StartAsync();
    }

    private static string ResolvePath(string value, string cfgPath)
    {
        if (Path.IsPathRooted(value)) return value;
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cfgPath))!, value);
    }

    private void BuildUi()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 840 };
        split.Panel1.Controls.Add(_viewer);

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        _nameBox.PlaceholderText = "Type name…  (blank = file without renaming)";
        _nameBox.TextChanged += (_, _) => ForceUpper();
        _nameBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; OnEnter(); }
        };

        // top-to-bottom: status, undo, skip, routes, name, filename, progress
        panel.Controls.Add(_status);
        panel.Controls.Add(_undo);
        panel.Controls.Add(_skip);
        panel.Controls.Add(_routes);
        panel.Controls.Add(new Label { Text = "Name:", Dock = DockStyle.Top, Height = 20 });
        panel.Controls.Add(_nameBox);
        panel.Controls.Add(_filename);
        panel.Controls.Add(_progress);
        // Dock=Top stacks in reverse add-order, so add in reverse of visual order:
        panel.Controls.SetChildIndex(_progress, 0);
        panel.Controls.SetChildIndex(_filename, 1);
        panel.Controls.SetChildIndex(_nameBox, 2);

        split.Panel2.Controls.Add(panel);
        _skip.Click += (_, _) => OnSkip();
        _undo.Click += (_, _) => OnUndo();
        Controls.Add(split);
    }

    private async Task StartAsync()
    {
        await _viewer.EnsureCoreWebView2Async();
        _viewerReady = true;

        var scan = Scanner.Scan(_cfg.Inbox, _cfg.Sort);
        if (scan.Error.Length > 0)
        {
            MessageBox.Show(scan.Error, "FileRouter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (scan.Count == 0)
        {
            _progress.Text = "Inbox is empty";
            return;
        }
        BuildRoutes();
        _session.Start(scan.Matching);
        LoadCurrent();
    }

    private void BuildRoutes()
    {
        _routes.Controls.Clear();
        for (var i = 0; i < _cfg.Routes.Count; i++)
        {
            var route = _cfg.Routes[i];
            var index = i;
            var label = route.Label + (route.AppendSuffix && route.Suffix.Length > 0
                ? $"   ·   {route.Suffix}" : "") +
                (string.IsNullOrEmpty(route.Hotkey) ? "" : $"   ·   {route.Hotkey}");
            var btn = new Button { Text = label, Width = 340, Height = 44, Margin = new Padding(0, 3, 0, 3) };
            if (!string.IsNullOrEmpty(route.Color))
            {
                try
                {
                    var c = ColorTranslator.FromHtml(route.Color);
                    btn.BackColor = c;
                    var lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                    btn.ForeColor = lum > 150 ? Color.Black : Color.White;
                    btn.FlatStyle = FlatStyle.Flat;
                }
                catch { /* invalid color -> native look */ }
            }
            var problem = Config.ValidateRoute(route);
            if (problem.Length > 0) { btn.Enabled = false; }
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
        if (_viewerReady)
            _viewer.CoreWebView2.Navigate(new Uri(Path.GetFullPath(path)).AbsoluteUri);
        _nameBox.Focus();
    }

    private void ShowDone()
    {
        _viewer.CoreWebView2?.Navigate("about:blank");
        _progress.Text = "Session complete";
        _filename.Text = $"{_session.Filed} filed, {_session.Skipped} set aside" +
            (_session.Vanished > 0 ? $", {_session.Vanished} vanished" : "");
        _undo.Enabled = _session.CanUndo;
    }

    /// <summary>Navigate the viewer to a blank page and wait for it, so Edge
    /// releases the PDF file handle before we move the file.</summary>
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

    private async Task OnRouteAsync(int index)
    {
        if (_session.Current is null || index >= _cfg.Routes.Count) return;
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
            MessageBox.Show(ex.Message, "FileRouter — couldn't file it",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LoadCurrent();   // reload the same doc; nothing moved
            return;
        }
        LoadCurrent();
    }

    private void OnEnter()
    {
        if (!_cfg.EnterCommits) return;
        if (_lastRoute is { } i && i < _cfg.Routes.Count)
            _ = OnRouteAsync(i);
        else
            ShowStatus("Enter files to the last-used route — press a route button first.");
    }

    private async void OnSkip()
    {
        if (_session.Current is null) return;
        await ReleaseViewerAsync();
        try
        {
            var outcome = _session.SkipCurrent();
            if (outcome.Vanished)
                ShowStatus("That file disappeared from the inbox — logged and moved on.");
        }
        catch (CommitError ex)
        {
            MessageBox.Show(ex.Message, "FileRouter — set-aside failed",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        LoadCurrent();
    }

    private void OnUndo()
    {
        if (!_session.CanUndo) { ShowStatus("Nothing to undo."); return; }
        try
        {
            var (filed, original) = _session.UndoLast();
            ShowStatus($"Undid {Path.GetFileName(filed)} → {Path.GetFileName(original)}");
        }
        catch (CommitError ex)
        {
            MessageBox.Show(ex.Message, "FileRouter — undo failed",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        LoadCurrent();
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

    private void ShowStatus(string message) => _status.Text = message;
}
