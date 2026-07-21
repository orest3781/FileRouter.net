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

/// <summary>The in-app audit viewer — the one user-facing feature the Python
/// original had that the WinForms port never grew. Newest first, lazy 500-row
/// load with Show all, live substring filter, CSV export.</summary>
public sealed class HistoryViewModel : ObservableObject
{
    public const int InitialLoad = 500;

    private readonly History _history;
    private readonly IDialogService _dialogs;
    private List<HistoryRow> _loaded;
    private bool _showedAll;

    public ObservableCollection<HistoryRow> Rows { get; } = new();
    public RelayCommand ShowAllCommand { get; }
    public RelayCommand ExportCommand { get; }

    public HistoryViewModel(History history, IDialogService dialogs)
    {
        _history = history;
        _dialogs = dialogs;
        _loaded = _history.Rows(InitialLoad).Select(HistoryRow.From).ToList();
        _showedAll = _loaded.Count < InitialLoad;
        ShowAllCommand = new RelayCommand(ShowAll, () => !_showedAll);
        ExportCommand = new RelayCommand(Export);
        Refresh();
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

    private void ShowAll()
    {
        _loaded = _history.Rows().Select(HistoryRow.From).ToList();
        _showedAll = true;
        ShowAllCommand.RaiseCanExecuteChanged();
        Raise(nameof(CanShowAll));
        Refresh();
    }

    private void Refresh()
    {
        var visible = Filter.Length == 0
            ? _loaded
            : _loaded.Where(r => r.Matches(Filter)).ToList();
        Rows.Clear();
        foreach (var r in visible) Rows.Add(r);

        var total = _history.Count();
        FooterText = _showedAll
            ? $"{Rows.Count} of {total} filings shown"
            : $"Showing the latest {Rows.Count} of {total} filings";
    }

    private void Export()
    {
        var dest = _dialogs.AskSaveFile("Spreadsheet files (*.csv)|*.csv", "filerouter_history.csv");
        if (dest is null) return;
        try
        {
            var count = _history.ExportCsv(dest);
            _dialogs.Info($"Exported {count} rows to {dest}", "FileRouter");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Warn("Couldn't save it: " + ex.Message, "FileRouter");
        }
    }
}
