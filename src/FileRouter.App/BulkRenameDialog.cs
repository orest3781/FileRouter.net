using FileRouter.Core;
using static FileRouter.Core.BulkRename;

namespace FileRouter.App;

/// <summary>Bulk rename: drop files, describe the change once, watch the live
/// current → new preview, then rename. Includes the "Review files" transform
/// (&lt;received date&gt;-LAST-FIRST) for medical-review batches. Never overwrites;
/// one batch undo.</summary>
public sealed class BulkRenameDialog : Form
{
    private readonly CheckBox _review = new() { Text = "Review files: rename to  <received date>-LAST-FIRST", AutoSize = true };
    private readonly DateTimePicker _received = new() { Format = DateTimePickerFormat.Short, Width = 110, Enabled = false };
    private readonly TextBox _find = new() { Width = 150 };
    private readonly TextBox _replace = new() { Width = 150 };
    private readonly TextBox _prefix = new() { Width = 150 };
    private readonly TextBox _suffix = new() { Width = 150 };
    private readonly ComboBox _case = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly Button _rename = new() { Text = "Rename", AutoSize = true, Enabled = false };
    private readonly Button _undo = new() { Text = "Undo last rename", AutoSize = true, Enabled = false };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(500, 0) };

    private readonly List<string> _files = new();
    private readonly Dictionary<string, string> _overrides = new();   // source -> hand-edited stem
    private List<RenameOutcome> _lastOutcomes = new();
    private bool _refreshing;

    public BulkRenameDialog()
    {
        Text = "FileRouter — Bulk rename";
        ClientSize = new Size(760, 580);
        StartPosition = FormStartPosition.CenterParent;
        AllowDrop = true;
        DragEnter += (_, e) => e!.Effect = e.Data!.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => AddFiles((string[])e!.Data!.GetData(DataFormats.FileDrop)!);
        _case.Items.AddRange(new object[] { "Keep case", "UPPERCASE", "lowercase" });
        _case.SelectedIndex = 0;
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

        var reviewRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        reviewRow.Controls.Add(_review);
        reviewRow.Controls.Add(new Label { Text = "received:", AutoSize = true, Margin = new Padding(8, 6, 2, 0) });
        reviewRow.Controls.Add(_received);
        _review.CheckedChanged += (_, _) => { _received.Enabled = _review.Checked; Refresh2(); };
        _received.ValueChanged += (_, _) => Refresh2();
        Row(reviewRow);

        var form = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        void Field(string label, Control c) { form.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) }); form.Controls.Add(c); }
        Field("Find:", _find);
        Field("Replace with:", _replace);
        Field("Add at start:", _prefix);
        Field("Add at end:", _suffix);
        Field("Letter case:", _case);
        foreach (var tb in new[] { _find, _replace, _prefix, _suffix }) tb.TextChanged += (_, _) => Refresh2();
        _case.SelectedIndexChanged += (_, _) => Refresh2();
        Row(form);

        var pick = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var add = new Button { Text = "Add files…", AutoSize = true };
        var folder = new Button { Text = "Add a folder's files…", AutoSize = true };
        var clear = new Button { Text = "Clear", AutoSize = true };
        add.Click += (_, _) => { using var d = new OpenFileDialog { Multiselect = true, Filter = "All files (*.*)|*.*" }; if (d.ShowDialog(this) == DialogResult.OK) AddFiles(d.FileNames); };
        folder.Click += (_, _) => { using var d = new FolderBrowserDialog(); if (d.ShowDialog(this) == DialogResult.OK) AddFiles(Directory.GetFiles(d.SelectedPath)); };
        clear.Click += (_, _) => { _files.Clear(); _overrides.Clear(); Refresh2(); };
        pick.Controls.AddRange(new Control[] { add, folder, clear });
        Row(pick);

        _grid.Columns.Add("current", "Current name");
        _grid.Columns.Add("becomes", "New name");
        _grid.Columns.Add("note", "");
        _grid.Columns[0].ReadOnly = true;
        _grid.Columns[2].ReadOnly = true;
        _grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _grid.CellEndEdit += (_, e) => OnCellEdited(e!.RowIndex);
        Row(_grid, grow: true);
        Row(new Label { Text = "Double-click a New name to edit it by hand (clear it to go back). Extensions never change.", AutoSize = true, ForeColor = Color.Gray });

        var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _rename.Click += (_, _) => Apply();
        _undo.Click += (_, _) => UndoBatch();
        var close = new Button { Text = "Close", AutoSize = true };
        close.Click += (_, _) => Close();
        bottom.Controls.AddRange(new Control[] { _rename, _undo, _status });
        Row(bottom);
        Row(close);

        Controls.Add(root);
    }

    private RenameOp CurrentOp() => new(
        Find: _find.Text, Replace: _replace.Text, Prefix: _prefix.Text, Suffix: _suffix.Text,
        Case: _case.SelectedIndex switch { 1 => "upper", 2 => "lower", _ => "keep" },
        ReceivedDate: _review.Checked ? _received.Value.ToString("yyyyMMdd") : "");

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths) if (File.Exists(p) && !_files.Contains(p)) _files.Add(p);
        Refresh2();
    }

    private void Refresh2()
    {
        var plans = Plan(_files, CurrentOp(), _overrides);
        _refreshing = true;
        _grid.Rows.Clear();
        var changed = 0;
        foreach (var pr in plans)
        {
            var newName = pr.Changed ? Path.GetFileName(pr.Target) : Path.GetFileName(pr.Source);
            var notes = new List<string>();
            if (pr.Note.Length > 0) notes.Add(pr.Note);
            if (pr.Manual) notes.Add("edited by hand");
            if (!pr.Changed && pr.Note.Length == 0) notes.Add("(no change)");
            var idx = _grid.Rows.Add(Path.GetFileName(pr.Source), newName, string.Join(" — ", notes));
            if (pr.Changed) changed++;
            else _grid.Rows[idx].Cells[1].Style.ForeColor = Color.Gray;
            if (pr.Manual) _grid.Rows[idx].Cells[1].Style.Font = new Font(_grid.Font, FontStyle.Bold);
        }
        _refreshing = false;
        _rename.Text = changed > 0 ? $"Rename {changed} file{(changed == 1 ? "" : "s")}" : "Rename";
        _rename.Enabled = changed > 0;
    }

    private void OnCellEdited(int row)
    {
        if (_refreshing || row < 0 || row >= _files.Count) return;
        var source = _files[row];
        var text = (_grid.Rows[row].Cells[1].Value?.ToString() ?? "").Trim();
        var ext = Path.GetExtension(source);
        if (text.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) text = text[..^ext.Length].Trim();
        if (text.Length == 0) _overrides.Remove(source);
        else _overrides[source] = text;
        Refresh2();
    }

    private void Apply()
    {
        var outcomes = Execute(Plan(_files, CurrentOp(), _overrides));
        _overrides.Clear();
        var renamed = outcomes.Where(o => o.Final != null).ToList();
        var failed = outcomes.Where(o => o.Final == null).ToList();
        var finals = renamed.ToDictionary(o => o.Source, o => o.Final!);
        for (var i = 0; i < _files.Count; i++) if (finals.TryGetValue(_files[i], out var f)) _files[i] = f;
        _lastOutcomes = renamed;
        _undo.Enabled = renamed.Count > 0;
        _find.Clear(); _replace.Clear(); _prefix.Clear(); _suffix.Clear(); _case.SelectedIndex = 0; _review.Checked = false;
        Refresh2();
        _status.Text = failed.Count > 0
            ? $"Renamed {renamed.Count}; {failed.Count} failed — e.g. {Path.GetFileName(failed[0].Source)}: {failed[0].Error}"
            : $"Renamed {renamed.Count} file{(renamed.Count == 1 ? "" : "s")}.";
    }

    private void UndoBatch()
    {
        var problems = Revert(_lastOutcomes);
        var restored = _lastOutcomes.Where(o => o.Final != null).ToDictionary(o => o.Final!, o => o.Source);
        for (var i = 0; i < _files.Count; i++) if (restored.TryGetValue(_files[i], out var s)) _files[i] = s;
        _lastOutcomes = new();
        _undo.Enabled = false;
        Refresh2();
        _status.Text = problems.Count > 0 ? string.Join("; ", problems) : "Original names restored.";
    }
}
