using FileRouter.Core;

namespace FileRouter.App;

/// <summary>Unlock PDFs: add password-protected PDFs, give the password, and
/// unlock them. By default the original file itself is unlocked (verified,
/// then atomically replaced); tick "keep a copy" to write &lt;name&gt;_unlocked.pdf
/// and leave the original untouched.</summary>
public sealed class UnlockDialog : Form
{
    private readonly ListBox _files = new() { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, Width = 220 };
    private readonly CheckBox _show = new() { Text = "Show", AutoSize = true };
    private readonly CheckBox _keepCopy = new() { Text = "Keep a copy (…_unlocked.pdf), don't touch the original", AutoSize = true };
    private readonly TextBox _results = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly List<string> _paths = new();

    public UnlockDialog()
    {
        Text = "FileRouter — Unlock PDFs";
        ClientSize = new Size(560, 560);
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
            root.RowStyles.Add(new RowStyle(grow ? SizeType.Percent : SizeType.AutoSize, grow ? 50 : 0));
            root.Controls.Add(c);
        }

        Row(new Label { Text = "PDFs to unlock (drag them here, or Add files…):", AutoSize = true });
        Row(_files, grow: true);

        var fileBtns = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var add = new Button { Text = "Add files…", AutoSize = true };
        var remove = new Button { Text = "Remove selected", AutoSize = true };
        var clear = new Button { Text = "Clear", AutoSize = true };
        add.Click += (_, _) => Browse();
        remove.Click += (_, _) => { foreach (int i in _files.SelectedIndices.Cast<int>().OrderByDescending(x => x)) _paths.RemoveAt(i); RefreshList(); };
        clear.Click += (_, _) => { _paths.Clear(); RefreshList(); };
        fileBtns.Controls.AddRange(new Control[] { add, remove, clear });
        Row(fileBtns);

        var pwRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        pwRow.Controls.Add(new Label { Text = "Password:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 4, 0) });
        pwRow.Controls.Add(_password);
        pwRow.Controls.Add(_show);
        _show.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !_show.Checked;
        Row(pwRow);
        Row(_keepCopy);

        Row(new Label { Text = "Results:", AutoSize = true });
        Row(_results, grow: true);

        var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "Close", AutoSize = true };
        var unlock = new Button { Text = "Unlock", AutoSize = true };
        close.Click += (_, _) => Close();
        unlock.Click += (_, _) => DoUnlock();
        bottom.Controls.AddRange(new Control[] { close, unlock });
        Row(bottom);

        Controls.Add(root);
    }

    private void Browse()
    {
        using var dlg = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Multiselect = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) AddFiles(dlg.FileNames);
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
            if (File.Exists(p) && p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && !_paths.Contains(p))
                _paths.Add(p);
        RefreshList();
    }

    private void RefreshList()
    {
        _files.BeginUpdate();
        _files.Items.Clear();
        foreach (var p in _paths) _files.Items.Add(Path.GetFileName(p));
        _files.EndUpdate();
    }

    private void DoUnlock()
    {
        if (_paths.Count == 0) { _results.Text = "Add at least one PDF first."; return; }
        var suffix = _keepCopy.Checked ? "_unlocked" : "";
        var lines = new List<string>();
        foreach (var path in _paths.ToList())
        {
            var r = Unlock.UnlockPdf(path, _password.Text, suffix: suffix);
            var name = Path.GetFileName(path);
            if (r.Ok)
                lines.Add(r.InPlace ? $"✓ {name} — unlocked" : $"✓ {name}  →  {Path.GetFileName(r.NewPath!)}");
            else if (r.Status == "not_encrypted")
                lines.Add($"•  {name} — {r.Message}");
            else
                lines.Add($"✗  {name} — {r.Message}");
        }
        _results.Text = string.Join(Environment.NewLine, lines);
    }
}
