using System.Collections.ObjectModel;
using FileRouter.Core;
using FileRouter.Wpf.Mvvm;
using FileRouter.Wpf.Services;

namespace FileRouter.Wpf.ViewModels;

/// <summary>One displayed audit row.</summary>
public sealed record HistoryRow(
    string When, string Original, string FiledAs, string Name,
    string Route, bool Reverted)
{
    public static HistoryRow From(IReadOnlyDictionary<string, object> r)
    {
        var whenRaw = r["ts_utc"] as string ?? "";
        var when = DateTime.TryParse(whenRaw, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal, out var utc)
            ? utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : whenRaw;
        return new HistoryRow(
            when,
            r["original_name"] as string ?? "",
            r["new_name"] as string ?? "",
            r["name_entered"] as string ?? "",
            r["route_label"] as string ?? "",
            Convert.ToInt64(r["reverted"]) != 0);
    }

    public bool Matches(string filter) =>
        Original.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || FiledAs.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Route.Contains(filter, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The in-app audit viewer — newest first, lazy 500-row load with
/// Show all, live substring filter, CSV export. Every SQLite touch runs off
/// the UI thread: the history DB can live on a share where a single query
/// (or a busy_timeout wait) takes seconds, and filtering must stay pure
/// in-memory — no query per keystroke.</summary>
public sealed class HistoryViewModel : ObservableObject
{
    public const int InitialLoad = 500;

    private readonly History _history;
    private readonly IDialogService _dialogs;
    private readonly IWorkScheduler _scheduler;
    private List<HistoryRow> _loaded = new();
    private long _total;
    private bool _showedAll;

    public ObservableCollection<HistoryRow> Rows { get; } = new();
    public RelayCommand ShowAllCommand { get; }
    public RelayCommand ExportCommand { get; }

    public HistoryViewModel(History history, IDialogService dialogs,
        IWorkScheduler? scheduler = null)
    {
        _history = history;
        _dialogs = dialogs;
        _scheduler = scheduler ?? new TaskWorkScheduler();
        ShowAllCommand = new RelayCommand(() => _ = LoadAsync(all: true), () => !_showedAll);
        ExportCommand = new RelayCommand(() => _ = ExportAsync());
        _ = LoadAsync(all: false);
    }

    private string _filter = "";
    public string Filter
    {
        get => _filter;
        set { if (Set(ref _filter, value)) Refresh(); }
    }

    private string _footerText = "";
    public string FooterText { get => _footerText; private set => Set(ref _footerText, value); }

    public bool CanShowAll => !_showedAll;

    internal async Task LoadAsync(bool all)
    {
        var history = _history;
        var (rows, total) = await _scheduler.Run(() =>
        {
            var loaded = (all ? history.Rows() : history.Rows(InitialLoad))
                .Select(HistoryRow.From).ToList();
            return (loaded, (long)history.Count());
        });
        _loaded = rows;
        _total = total;   // cached — the filter never re-queries
        _showedAll = all || rows.Count < InitialLoad;
        ShowAllCommand.RaiseCanExecuteChanged();
        Raise(nameof(CanShowAll));
        Refresh();
    }

    /// <summary>Pure in-memory: reapply the filter to the loaded rows.</summary>
    private void Refresh()
    {
        var visible = Filter.Length == 0
            ? _loaded
            : _loaded.Where(r => r.Matches(Filter)).ToList();
        Rows.Clear();
        foreach (var r in visible) Rows.Add(r);

        FooterText = _showedAll
            ? $"{Rows.Count} of {_total} filings shown"
            : $"Showing the latest {Rows.Count} of {_total} filings";
    }

    internal async Task ExportAsync()
    {
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
}
