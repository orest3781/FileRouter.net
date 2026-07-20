using FileRouter.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FileRouter.App;

/// <summary>Match &amp; merge: load a roster CSV, map its headers, drop PDFs in,
/// and merge each person's Control ID into the filename. Unambiguous matches
/// merge in one click; names matching several roster rows go to Triage, where
/// the PDF opens in Edge next to the full candidate rows.</summary>
public sealed class MatchMergeDialog : Form
{
    private readonly Config _cfg;
    private readonly Action<Dictionary<string, string>> _saveHeaders;

    private readonly TextBox _rosterPath = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly ComboBox _first = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ComboBox _last = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ComboBox _control = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly Button _merge = new() { Text = "Merge", AutoSize = true, Enabled = false };
    private readonly Button _triage = new() { Text = "Triage…", AutoSize = true, Enabled = false };
    private readonly Button _undo = new() { Text = "Undo last merge", AutoSize = true, Enabled = false };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(560, 0) };

    private readonly List<string> _files = new();
    private MatchMerge.Roster? _roster;
    private List<MatchMerge.MatchResult> _results = new();
    private List<BulkRename.RenameOutcome> _outcomes = new();
    private readonly WebView2 _sharedViewer;

    public MatchMergeDialog(Config cfg, Action<Dictionary<string, string>> saveHeaders)
    {
        _cfg = cfg;
        _saveHeaders = saveHeaders;
        _sharedViewer = new WebView2();   // one viewer, reused by triage
        Text = "FileRouter — Match and merge";
        ClientSize = new Size(780, 600);
        StartPosition = FormStartPosition.CenterParent;
        AllowDrop = true;
        DragEnter += (_, e) => e!.Effect = e.Data!.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => AddFiles((string[])e!.Data!.GetData(DataFormats.FileDrop)!);
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
        void Row(Control c, bool grow = false)
        {
            root.RowStyles.Add(new RowStyle(grow ? SizeType.Percent : SizeType.AutoSize, grow ? 100 : 0));
            root.Controls.Add(c);
        }

        var rosterRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Fill };
        rosterRow.Controls.Add(new Label { Text = "Roster:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 4, 0) });
        rosterRow.Controls.Add(_rosterPath);
        var loadBtn = new Button { Text = "Load spreadsheet (CSV)…", AutoSize = true };
        loadBtn.Click += (_, _) => BrowseRoster();
        rosterRow.Controls.Add(loadBtn);
        rosterRow.SetColumnSpan(_rosterPath, 1);
        Row(rosterRow);

        var headerRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        void H(string label, ComboBox c) { headerRow.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(4, 6, 2, 0) }); headerRow.Controls.Add(c); c.SelectedIndexChanged += (_, _) => ReloadRoster(); }
        H("First name:", _first); H("Last name:", _last); H("Control ID:", _control);
        Row(headerRow);

        var fileRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var add = new Button { Text = "Add PDFs…", AutoSize = true };
        var folder = new Button { Text = "Add a folder's PDFs…", AutoSize = true };
        var clear = new Button { Text = "Clear", AutoSize = true };
        add.Click += (_, _) => { using var d = new OpenFileDialog { Multiselect = true, Filter = "PDF files (*.pdf)|*.pdf" }; if (d.ShowDialog(this) == DialogResult.OK) AddFiles(d.FileNames); };
        folder.Click += (_, _) => { using var d = new FolderBrowserDialog(); if (d.ShowDialog(this) == DialogResult.OK) AddFiles(Directory.GetFiles(d.SelectedPath, "*.pdf")); };
        clear.Click += (_, _) => { _files.Clear(); Refresh2(); };
        fileRow.Controls.AddRange(new Control[] { add, folder, clear });
        Row(fileRow);

        _grid.Columns.Add("file", "File");
        _grid.Columns.Add("becomes", "Becomes");
        _grid.Columns.Add("note", "");
        _grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        Row(_grid, grow: true);

        var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _merge.Click += (_, _) => DoMerge();
        _triage.Click += (_, _) => DoTriage();
        _undo.Click += (_, _) => UndoBatch();
        var close = new Button { Text = "Close", AutoSize = true };
        close.Click += (_, _) => Close();
        bottom.Controls.AddRange(new Control[] { _merge, _triage, _undo, _status });
        Row(bottom);
        Row(close);

        Controls.Add(root);
    }

    private void BrowseRoster()
    {
        using var d = new OpenFileDialog { Filter = "Spreadsheet files (*.csv)|*.csv|All files (*.*)|*.*" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _rosterPath.Text = d.FileName;
        string[] headers;
        try { headers = ReadCsvHeaders(d.FileName); }
        catch (Exception ex) { _status.Text = "Couldn't read the spreadsheet: " + ex.Message; return; }

        void Fill(ComboBox c, string key, params string[] needles)
        {
            c.Items.Clear();
            c.Items.AddRange(headers);
            var saved = _cfg.MergeHeaders.TryGetValue(key, out var s) && headers.Contains(s) ? s : null;
            c.SelectedItem = saved ?? headers.FirstOrDefault(h => needles.Any(n => h.ToLowerInvariant().Contains(n))) ?? headers.FirstOrDefault();
        }
        Fill(_first, "first", "first"); Fill(_last, "last", "last"); Fill(_control, "control", "control", "id");
        ReloadRoster();
    }

    private static string[] ReadCsvHeaders(string path)
    {
        using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
        var line = reader.ReadLine() ?? "";
        return line.Split(',').Select(h => h.Trim().Trim('"')).ToArray();
    }

    private void ReloadRoster()
    {
        if (_rosterPath.Text.Length == 0 || _first.SelectedItem is null) return;
        var headers = new Dictionary<string, string>
        {
            ["first"] = _first.Text, ["last"] = _last.Text, ["control"] = _control.Text,
        };
        try { _roster = MatchMerge.LoadRoster(_rosterPath.Text, headers["first"], headers["last"], headers["control"]); }
        catch (RosterException ex) { _roster = null; _status.Text = ex.Message; Refresh2(); return; }
        _saveHeaders(headers);
        _status.Text = $"Roster loaded: {_roster.People.Count} people.";
        Refresh2();
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths) if (File.Exists(p) && p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && !_files.Contains(p)) _files.Add(p);
        Refresh2();
    }

    private void Refresh2()
    {
        _results = _roster is null ? new() : MatchMerge.MatchFiles(_files, _roster);
        _grid.Rows.Clear();
        int merges = 0, ambiguous = 0;
        var display = _roster is null
            ? _files.Select(f => new MatchMerge.MatchResult(f, "no_roster")).ToList()
            : _results;
        foreach (var r in display)
        {
            string becomes = "", note = "";
            switch (r.Status)
            {
                case "merge": becomes = r.NewStem + Path.GetExtension(r.Source); merges++; break;
                case "ambiguous": note = $"{r.Candidates!.Count} candidates — decide in Triage"; ambiguous++; break;
                case "already": note = "already has the id"; break;
                case "no_match": note = "no roster match"; break;
                case "no_name": note = "no name found in the filename"; break;
                case "no_roster": note = "load a roster first"; break;
            }
            var idx = _grid.Rows.Add(Path.GetFileName(r.Source), becomes, note);
            if (becomes.Length == 0) _grid.Rows[idx].Cells[2].Style.ForeColor = Color.Gray;
        }
        _merge.Text = merges > 0 ? $"Merge {merges} matched" : "Merge";
        _merge.Enabled = merges > 0;
        _triage.Text = ambiguous > 0 ? $"Triage {ambiguous} ambiguous…" : "Triage…";
        _triage.Enabled = ambiguous > 0;
    }

    private void DoMerge() => Absorb(MatchMerge.ExecuteMerges(_results));

    private void DoTriage()
    {
        var items = _results.Where(r => r.Status == "ambiguous").ToList();
        if (items.Count == 0 || _roster is null) return;
        using var dlg = new TriageDialog(_sharedViewer, items, _roster.Headers);
        dlg.ShowDialog(this);
        Absorb(dlg.Outcomes);
    }

    private void Absorb(List<BulkRename.RenameOutcome> outcomes)
    {
        var renamed = outcomes.Where(o => o.Final != null).ToList();
        var finals = renamed.ToDictionary(o => o.Source, o => o.Final!);
        for (var i = 0; i < _files.Count; i++) if (finals.TryGetValue(_files[i], out var f)) _files[i] = f;
        _outcomes.AddRange(renamed);
        _undo.Enabled = _outcomes.Count > 0;
        Refresh2();
        var failed = outcomes.Count(o => o.Final == null);
        _status.Text = failed > 0 ? $"Merged {renamed.Count}; {failed} failed."
            : renamed.Count > 0 ? $"Merged {renamed.Count} file{(renamed.Count == 1 ? "" : "s")}." : _status.Text;
    }

    private void UndoBatch()
    {
        var problems = BulkRename.Revert(_outcomes);
        var restored = _outcomes.Where(o => o.Final != null).ToDictionary(o => o.Final!, o => o.Source);
        for (var i = 0; i < _files.Count; i++) if (restored.TryGetValue(_files[i], out var s)) _files[i] = s;
        _outcomes = new();
        _undo.Enabled = false;
        Refresh2();
        _status.Text = problems.Count > 0 ? string.Join("; ", problems) : "Original names restored.";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _sharedViewer.Dispose();
        base.OnFormClosed(e);
    }
}

/// <summary>One ambiguous file at a time: the PDF in Edge (left) and every
/// candidate's full roster row (right). Pick the row the document belongs to.</summary>
internal sealed class TriageDialog : Form
{
    private readonly WebView2 _viewer;
    private readonly List<MatchMerge.MatchResult> _items;
    private readonly IReadOnlyList<string> _headers;
    private readonly Label _progress = new() { AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
    private readonly Label _file = new() { AutoSize = true, MaximumSize = new Size(380, 0) };
    private readonly DataGridView _candidates = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Button _use = new() { Text = "Use selected id", AutoSize = true, Enabled = false };
    private readonly Label _note = new() { AutoSize = true, ForeColor = Color.FromArgb(185, 119, 14), MaximumSize = new Size(380, 0) };
    private int _index;
    private bool _viewerReady;

    public List<BulkRename.RenameOutcome> Outcomes { get; } = new();

    public TriageDialog(WebView2 viewer, List<MatchMerge.MatchResult> items, IReadOnlyList<string> headers)
    {
        _viewer = viewer;
        _items = items;
        _headers = headers;
        Text = "FileRouter — Triage";
        ClientSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterParent;
        BuildUi();
        Load += async (_, _) => { await _viewer.EnsureCoreWebView2Async(); _viewerReady = true; ShowCurrent(); };
    }

    private void BuildUi()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
        _viewer.Dock = DockStyle.Fill;
        if (_viewer.Parent is not null) _viewer.Parent.Controls.Remove(_viewer);
        split.Panel1.Controls.Add(_viewer);

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
        void Row(Control c, bool grow = false) { panel.RowStyles.Add(new RowStyle(grow ? SizeType.Percent : SizeType.AutoSize, grow ? 100 : 0)); panel.Controls.Add(c); }
        Row(_progress);
        Row(_file);
        Row(new Label { Text = "Which row does this document belong to?", AutoSize = true });
        Row(_candidates, grow: true);
        Row(_note);
        _use.Click += (_, _) => UseSelected();
        _candidates.SelectionChanged += (_, _) => _use.Enabled = _candidates.SelectedRows.Count == 1;
        Row(_use);
        var skip = new Button { Text = "Skip this file", AutoSize = true };
        skip.Click += (_, _) => { _index++; ShowCurrent(); };
        Row(skip);
        var close = new Button { Text = "Stop triage", AutoSize = true };
        close.Click += (_, _) => Close();
        Row(close);
        split.Panel2.Controls.Add(panel);

        Controls.Add(split);
        Shown += (_, _) => split.SplitterDistance = Math.Max(400, ClientSize.Width - 420);
    }

    private MatchMerge.MatchResult? Current => _index < _items.Count ? _items[_index] : null;

    private void ShowCurrent()
    {
        var r = Current;
        if (r is null) { Close(); return; }
        _progress.Text = $"{_index + 1} / {_items.Count}";
        _file.Text = Path.GetFileName(r.Source);
        _note.Text = "";
        if (_viewerReady) _viewer.CoreWebView2.Navigate(new Uri(Path.GetFullPath(r.Source)).AbsoluteUri);

        _candidates.Columns.Clear();
        foreach (var h in _headers) _candidates.Columns.Add(h, h);
        _candidates.Rows.Clear();
        foreach (var c in r.Candidates!)
            _candidates.Rows.Add(_headers.Select(h => c.Row.TryGetValue(h, out var v) ? v : "").Cast<object>().ToArray());
        _use.Enabled = false;
    }

    private async void UseSelected()
    {
        var r = Current;
        if (r is null || _candidates.SelectedRows.Count != 1) return;
        var candidate = r.Candidates![_candidates.SelectedRows[0].Index];

        // release Edge's handle on the file before renaming
        if (_viewerReady)
        {
            var tcs = new TaskCompletionSource();
            void H(object? s, CoreWebView2NavigationCompletedEventArgs e) { _viewer.CoreWebView2.NavigationCompleted -= H; tcs.TrySetResult(); }
            _viewer.CoreWebView2.NavigationCompleted += H;
            _viewer.CoreWebView2.Navigate("about:blank");
            await Task.WhenAny(tcs.Task, Task.Delay(2000));
        }
        var outcomes = MatchMerge.MergeOne(r.Source, candidate.ControlId);
        if (outcomes[0].Final is null) { _note.Text = "Couldn't rename: " + outcomes[0].Error; ShowCurrent(); return; }
        Outcomes.AddRange(outcomes);
        _index++;
        ShowCurrent();
    }
}
