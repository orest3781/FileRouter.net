using FileRouter.Core;

namespace FileRouter.App;

/// <summary>Edit every config field the app needs so first-time setup doesn't
/// mean hand-editing JSON. General options + the routes table + the
/// monitored-folders table + the alert terms. On OK, <see cref="Result"/>
/// holds the new Config (unknown keys and merge headers preserved).</summary>
public sealed class SettingsDialog : Form
{
    private readonly Config _cfg;
    public Config? Result { get; private set; }

    private readonly TextBox _inbox = new() { Width = 320 };
    private readonly TextBox _deferred = new() { Width = 320 };
    private readonly TextBox _namesFile = new() { Width = 320 };
    private readonly TextBox _historyDb = new() { Width = 320 };
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ComboBox _sort = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly CheckBox _tag = new() { Text = "Write route label into PDF keywords/subject", AutoSize = true };
    private readonly CheckBox _enter = new() { Text = "Enter files to the last-used route", AutoSize = true };
    private readonly CheckBox _upper = new() { Text = "TYPE NAMES IN CAPITALS automatically", AutoSize = true };
    private readonly CheckBox _flash = new() { Text = "Flash alerts (uncheck for a steady highlight)", AutoSize = true };
    private readonly TextBox _monitorTitle = new() { Width = 320 };
    private readonly TextBox _alerts = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Width = 320, Height = 60 };
    private readonly DataGridView _routes = Grid();
    private readonly DataGridView _watch = Grid();

    public SettingsDialog(Config cfg)
    {
        _cfg = cfg;
        Text = "FileRouter — Settings";
        ClientSize = new Size(720, 720);
        StartPosition = FormStartPosition.CenterParent;
        BuildUi();
        Load += (_, _) => Populate();
    }

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, Height = 130, AllowUserToAddRows = true,
        RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
    };

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10), AutoScroll = true };
        void Row(Control c) { root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.Controls.Add(c); }

        var g = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        void Field(string label, Control c) { g.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }); g.Controls.Add(WithBrowse(c, label)); }
        _mode.Items.AddRange(Naming.Modes.Cast<object>().ToArray());
        _sort.Items.AddRange(Config.Sorts.Cast<object>().ToArray());
        Field("Inbox folder:", _inbox);
        Field("Set-aside folder:", _deferred);
        Field("Name suggestions file:", _namesFile);
        Field("History file:", _historyDb);
        Field("Naming mode:", _mode);
        Field("Work through files:", _sort);
        Field("Monitored-folders heading:", _monitorTitle);
        Field("Alert terms (one per line):", _alerts);
        Row(new GroupBox { Text = "General", AutoSize = true, Dock = DockStyle.Fill, Controls = { g }, Padding = new Padding(6) });
        Row(_tag); Row(_enter); Row(_upper); Row(_flash);

        _routes.Columns.Add(TextCol("Label")); _routes.Columns.Add(TextCol("Path"));
        _routes.Columns.Add(TextCol("Hotkey")); _routes.Columns.Add(TextCol("Suffix"));
        _routes.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Suffix on" });
        _routes.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Mode", DataSource = new[] { "(inherit)", "insert", "replace" } });
        _routes.Columns.Add(TextCol("Color"));
        Row(new Label { Text = "Routes (the filing buttons):", AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        Row(_routes);

        _watch.Columns.Add(TextCol("Label")); _watch.Columns.Add(TextCol("Path"));
        _watch.Columns.Add(TextCol("File types"));
        _watch.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Subfolders" });
        _watch.Columns.Add(TextCol("Color"));
        Row(new Label { Text = "Monitored folders (dashboard tiles):", AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        Row(_watch);

        var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => OnOk();
        bottom.Controls.AddRange(new Control[] { cancel, ok });
        Row(bottom);
        AcceptButton = ok; CancelButton = cancel;
        Controls.Add(root);
    }

    private static DataGridViewTextBoxColumn TextCol(string header) => new() { HeaderText = header };

    /// <summary>Wrap a folder textbox with a "…" browse button.</summary>
    private Control WithBrowse(Control c, string label)
    {
        if (c != _inbox && c != _deferred) return c;
        var host = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        var btn = new Button { Text = "…", Width = 30 };
        btn.Click += (_, _) => { using var d = new FolderBrowserDialog(); if (d.ShowDialog(this) == DialogResult.OK) ((TextBox)c).Text = d.SelectedPath; };
        host.Controls.Add(c);
        host.Controls.Add(btn);
        return host;
    }

    private void Populate()
    {
        _inbox.Text = _cfg.Inbox; _deferred.Text = _cfg.Deferred;
        _namesFile.Text = _cfg.NamesFile; _historyDb.Text = _cfg.HistoryDb;
        _mode.SelectedItem = _cfg.NamingMode; _sort.SelectedItem = _cfg.Sort;
        _tag.Checked = _cfg.TagWithRoute; _enter.Checked = _cfg.EnterCommits;
        _upper.Checked = _cfg.UppercaseNames; _flash.Checked = _cfg.FlashAlerts;
        _monitorTitle.Text = _cfg.MonitorTitle;
        _alerts.Text = string.Join(Environment.NewLine, _cfg.AlertTexts);

        foreach (var r in _cfg.Routes)
        {
            var i = _routes.Rows.Add(r.Label, r.Path, r.Hotkey, r.Suffix, r.AppendSuffix,
                r.NamingMode ?? "(inherit)", r.Color ?? "");
            _routes.Rows[i].Tag = r;   // keep the original to preserve unknown keys
        }
        foreach (var w in _cfg.WatchFolders)
        {
            var i = _watch.Rows.Add(w.Label, w.Path, w.Filetypes, w.Recursive, w.Color ?? "");
            _watch.Rows[i].Tag = w;
        }
    }

    private static string Cell(DataGridViewRow row, int col) =>
        row.Cells[col].Value?.ToString()?.Trim() ?? "";

    private void OnOk()
    {
        var routes = new List<Route>();
        foreach (DataGridViewRow row in _routes.Rows)
        {
            if (row.IsNewRow || Cell(row, 0).Length == 0) continue;
            var route = row.Tag as Route ?? new Route();
            route.Label = Cell(row, 0);
            route.Path = Cell(row, 1);
            route.Hotkey = Cell(row, 2);
            route.Suffix = Cell(row, 3);
            route.AppendSuffix = row.Cells[4].Value is true;
            var mode = Cell(row, 5);
            route.NamingMode = mode is "insert" or "replace" ? mode : null;
            var color = Cell(row, 6);
            route.Color = color.Length > 0 ? color : null;
            routes.Add(route);
        }

        var watch = new List<WatchFolder>();
        foreach (DataGridViewRow row in _watch.Rows)
        {
            if (row.IsNewRow || Cell(row, 0).Length == 0) continue;
            var wf = row.Tag as WatchFolder ?? new WatchFolder();
            wf.Label = Cell(row, 0);
            wf.Path = Cell(row, 1);
            wf.Filetypes = Cell(row, 2);
            wf.Recursive = row.Cells[3].Value is true;
            var color = Cell(row, 4);
            wf.Color = color.Length > 0 ? color : null;
            watch.Add(wf);
        }

        Result = new Config
        {
            Inbox = _inbox.Text.Trim(),
            Deferred = _deferred.Text.Trim(),
            NamesFile = _namesFile.Text.Trim(),
            HistoryDb = _historyDb.Text.Trim().Length > 0 ? _historyDb.Text.Trim() : "history.sqlite",
            NamingMode = (string)_mode.SelectedItem!,
            Sort = (string)_sort.SelectedItem!,
            TagWithRoute = _tag.Checked,
            EnterCommits = _enter.Checked,
            UppercaseNames = _upper.Checked,
            FlashAlerts = _flash.Checked,
            MonitorTitle = _monitorTitle.Text.Trim().Length > 0 ? _monitorTitle.Text.Trim() : "Monitored folders",
            AlertTexts = _alerts.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList(),
            Routes = routes,
            WatchFolders = watch,
            MergeHeaders = _cfg.MergeHeaders,   // preserved (not edited here)
            Extras = _cfg.Extras,               // unknown top-level keys preserved
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
