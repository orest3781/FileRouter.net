using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FileRouter.Core;
using FileRouter.Wpf.Services;

namespace FileRouter.Wpf.Windows;

/// <summary>One ambiguous file at a time: the PDF in Edge (left) and every
/// candidate's full roster row (right). Pick the row the document belongs to.
/// The viewer's handle is released before every rename — same contract as the
/// filing loop.</summary>
public partial class TriageWindow : Window
{
    private readonly List<MatchMerge.MatchResult> _items;
    private readonly IReadOnlyList<string> _headers;
    private readonly WebViewPdfViewer _pdf;
    private int _index;

    public List<BulkRename.RenameOutcome> Outcomes { get; } = new();

    public TriageWindow(List<MatchMerge.MatchResult> items, IReadOnlyList<string> headers)
    {
        InitializeComponent();
        _items = items;
        _headers = headers;
        _pdf = new WebViewPdfViewer(Viewer);
        foreach (var h in _headers)
            Candidates.Columns.Add(new DataGridTextColumn
            {
                Header = h,
                Binding = new Binding($"[{h}]"),
            });
        Loaded += async (_, _) =>
        {
            await _pdf.InitAsync();
            await ShowCurrentAsync();
        };
    }

    private MatchMerge.MatchResult? Current => _index < _items.Count ? _items[_index] : null;

    private async Task ShowCurrentAsync()
    {
        var r = Current;
        if (r is null) { Close(); return; }
        Progress.Text = $"{_index + 1} / {_items.Count}";
        FileName.Text = Path.GetFileName(r.Source);
        Note.Text = "";
        await _pdf.ShowAsync(r.Source);

        Candidates.ItemsSource = r.Candidates!
            .Select(c => _headers.ToDictionary(h => h, h => c.Row.TryGetValue(h, out var v) ? v : ""))
            .ToList();
        UseButton.IsEnabled = false;
    }

    private void OnCandidateSelected(object sender, SelectionChangedEventArgs e) =>
        UseButton.IsEnabled = Candidates.SelectedIndex >= 0;

    private async void OnUseSelected(object sender, RoutedEventArgs e)
    {
        var r = Current;
        if (r is null || Candidates.SelectedIndex < 0) return;
        var candidate = r.Candidates![Candidates.SelectedIndex];

        await _pdf.ReleaseAsync();   // Edge lets go of the file before the rename
        var outcomes = MatchMerge.MergeOne(r.Source, candidate.ControlId);
        if (outcomes[0].Final is null)
        {
            Note.Text = "Couldn't rename: " + outcomes[0].Error;
            await ShowCurrentAsync();
            return;
        }
        Outcomes.AddRange(outcomes);
        _index++;
        await ShowCurrentAsync();
    }

    private async void OnSkip(object sender, RoutedEventArgs e)
    {
        _index++;
        await ShowCurrentAsync();
    }

    private void OnStop(object sender, RoutedEventArgs e) => Close();
}
