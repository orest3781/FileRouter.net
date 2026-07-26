using System.Collections.ObjectModel;
using System.IO;
using FileRouter.Core;
using FileRouter.Wpf.Mvvm;
using FileRouter.Wpf.Services;

namespace FileRouter.Wpf.ViewModels;

/// <summary>An editable label-maker client row.</summary>
public sealed class LabelClientVm : ObservableObject
{
    private string _id = "", _destroyDaysText = "30", _nextNumberText = "1";

    /// <summary>Uppercased as typed — the barcode alphabet is A-Z 0-9.</summary>
    public string Id
    {
        get => _id;
        set { if (Set(ref _id, value.ToUpperInvariant().Trim())) Raise(nameof(Summary)); }
    }

    public string DestroyDaysText
    {
        get => _destroyDaysText;
        set { if (Set(ref _destroyDaysText, value)) Raise(nameof(Summary)); }
    }

    public string NextNumberText
    {
        get => _nextNumberText;
        set { if (Set(ref _nextNumberText, value)) Raise(nameof(Summary)); }
    }

    /// <summary>Hover detail for the client list row.</summary>
    public string Summary =>
        $"{DestroyDaysText}-day retention   ·   next label {NextNumberText}";

    public Dictionary<string, System.Text.Json.JsonElement> Extras { get; init; } = new();

    public static LabelClientVm From(LabelClient c) => new()
    {
        Id = c.Id,
        DestroyDaysText = c.DestroyDays.ToString(),
        NextNumberText = c.NextNumber.ToString(),
        Extras = new Dictionary<string, System.Text.Json.JsonElement>(c.Extras),
    };

    public LabelClient ToClient() => new()
    {
        Id = Id,
        DestroyDays = int.TryParse(DestroyDaysText.Trim(), out var d) ? d : 30,
        NextNumber = long.TryParse(NextNumberText.Trim(), out var n) ? n : 1,
        Extras = new Dictionary<string, System.Text.Json.JsonElement>(Extras),
    };
}

/// <summary>Tools → Label maker: print-ready box labels, ten per US-letter
/// sheet. Each client keeps its own destruction offset and running number;
/// generating a batch advances the number and persists it.</summary>
public sealed class LabelMakerViewModel : ObservableObject
{
    private readonly Config _cfg;
    private readonly Action _saveConfig;
    private readonly IDialogService _dialogs;
    private readonly Func<DateTime> _today;
    private readonly Action<string> _openFile;

    public ObservableCollection<LabelClientVm> Clients { get; } = new();

    public LabelMakerViewModel(Config cfg, Action saveConfig, IDialogService dialogs,
        Func<DateTime>? today = null, Action<string>? openFile = null)
    {
        _cfg = cfg;
        _saveConfig = saveConfig;
        _dialogs = dialogs;
        _today = today ?? (() => DateTime.Now);
        _openFile = openFile ?? (p => System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(p) { UseShellExecute = true }));

        foreach (var c in cfg.LabelClients) Hook(Clients.AddReturn(LabelClientVm.From(c)));

        AddClientCommand = new RelayCommand(() =>
        {
            var vm = Hook(Clients.AddReturn(new LabelClientVm()));
            Selected = vm;
            RequestIdFocus?.Invoke();   // the id box is the only next step
        });
        RemoveClientCommand = new RelayCommand(() =>
        {
            if (Selected is not { } s) return;
            // the running number is what keeps box numbers unique — removing
            // it is the one destructive act in this window, so it confirms
            // (a just-added blank row goes quietly)
            var pristine = s.Id.Length == 0 && s.NextNumberText.Trim() is "" or "1";
            if (!pristine && !_dialogs.Confirm(
                    $"Remove \"{s.Id}\"?\n\nIts running label number ({s.NextNumberText}) "
                    + "will be lost — re-adding the client starts back at 1.",
                    "Sendu — label maker"))
                return;
            Clients.Remove(s);
            Selected = Clients.FirstOrDefault();
        }, () => Selected is not null);
        ResetNumberCommand = new RelayCommand(
            () => { if (Selected is { } s) s.NextNumberText = "1"; },
            () => Selected is not null);
        PrintCommand = new RelayCommand(Print, () => Selected is not null);
        SavePdfCommand = new RelayCommand(SavePdf, () => Selected is not null);

        Selected = Clients.FirstOrDefault();   // after the commands the setter pokes
    }

    /// <summary>Sends composed sheets to a printer; returns false when the
    /// user cancels the print dialog. Supplied by the window (WPF PrintDialog
    /// + FixedDocument); tests inject a recorder.</summary>
    internal Func<IReadOnlyList<BoxLabels.Item>, string, bool>? PrintSheets { get; set; }

    /// <summary>The window's print path reports failures through the same
    /// dialog service the view model uses.</summary>
    internal IDialogService Dialogs => _dialogs;

    /// <summary>Raised after Add so the view can put the caret in the
    /// client-id box — typing the id is the only sensible next step.</summary>
    public event Action? RequestIdFocus;

    private LabelClientVm Hook(LabelClientVm vm)
    {
        vm.PropertyChanged += (_, _) => RefreshPreview();
        return vm;
    }

    private LabelClientVm? _selected;
    public LabelClientVm? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            RemoveClientCommand.RaiseCanExecuteChanged();
            ResetNumberCommand.RaiseCanExecuteChanged();
            PrintCommand.RaiseCanExecuteChanged();
            SavePdfCommand.RaiseCanExecuteChanged();
            RefreshPreview();
        }
    }

    private string _labelCountText = "10";
    public string LabelCountText
    {
        get => _labelCountText;
        set { if (Set(ref _labelCountText, value)) RefreshPreview(); }
    }

    private string _preview = "";
    public string Preview { get => _preview; private set => Set(ref _preview, value); }

    /// <summary>The first label of the batch, rendered live by the window's
    /// preview control; null while the inputs have a problem.</summary>
    private BoxLabels.Item? _previewItem;
    public BoxLabels.Item? PreviewItem { get => _previewItem; private set => Set(ref _previewItem, value); }

    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    public RelayCommand AddClientCommand { get; }
    public RelayCommand RemoveClientCommand { get; }
    public RelayCommand ResetNumberCommand { get; }
    public RelayCommand PrintCommand { get; }
    public RelayCommand SavePdfCommand { get; }

    /// <summary>Everything wrong with the current inputs, one line each.</summary>
    internal List<string> Problems()
    {
        var problems = new List<string>();
        if (Selected is not { } s) { problems.Add("Add a client first."); return problems; }

        var idProblem = BoxLabels.ValidateClientId(s.Id);
        if (idProblem.Length > 0) problems.Add(idProblem);
        if (Clients.Count(c => c.Id == s.Id) > 1)
            problems.Add($"Two clients are both called \"{s.Id}\".");
        if (!int.TryParse(s.DestroyDaysText.Trim(), out var days) || days is < 0 or > 3650)
            problems.Add("Keep days must be a number from 0 to 3650.");
        if (!long.TryParse(s.NextNumberText.Trim(), out var next) || next < 1 || next > BoxLabels.MaxNumber)
            problems.Add($"Next label number must be 1 to {BoxLabels.MaxNumber}.");
        if (!int.TryParse(LabelCountText.Trim(), out var count) || count is < 1 or > 1000)
            problems.Add("Labels to print must be 1 to 1000.");
        else if (long.TryParse(s.NextNumberText.Trim(), out var n)
                 && n >= 1 && n + count - 1 > BoxLabels.MaxNumber)
            problems.Add("That batch would run past label 99999999 — reset the number.");
        return problems;
    }

    private void RefreshPreview()
    {
        if (Selected is not { } s) { Preview = ""; PreviewItem = null; return; }
        var problems = Problems();
        if (problems.Count > 0) { Preview = "⚠ " + problems[0]; PreviewItem = null; return; }
        var created = _today().Date;
        var destroy = created.AddDays(int.Parse(s.DestroyDaysText.Trim()));
        var start = long.Parse(s.NextNumberText.Trim());
        var count = int.Parse(LabelCountText.Trim());
        var sheets = (count + BoxLabels.PerSheet - 1) / BoxLabels.PerSheet;
        PreviewItem = new BoxLabels.Item(BoxLabels.Compose(s.Id, start), created, destroy);
        Preview = count == 1
            ? $"Prints {BoxLabels.Compose(s.Id, start)}   ·   1 sheet"
            : $"Prints {BoxLabels.Compose(s.Id, start)} – {BoxLabels.Compose(s.Id, start + count - 1)}"
              + $"   ·   {sheets} sheet{(sheets == 1 ? "" : "s")}";
    }

    /// <summary>Validated batch for the current inputs, or null after warning.</summary>
    private (List<BoxLabels.Item> Items, LabelClientVm Client, long Start, int Count)? BuildBatch()
    {
        if (Selected is not { } s) return null;
        var problems = Problems();
        if (problems.Count > 0)
        {
            _dialogs.Warn("These need fixing first:\n\n • " + string.Join("\n • ", problems),
                "Sendu — label maker");
            return null;
        }
        var start = long.Parse(s.NextNumberText.Trim());
        var count = int.Parse(LabelCountText.Trim());
        var items = BoxLabels.Batch(s.Id, start, count, _today(),
            int.Parse(s.DestroyDaysText.Trim()));
        return (items, s, start, count);
    }

    /// <summary>The batch went out — advance the counter and save it.</summary>
    private void Advance(LabelClientVm client, long start, int count, string status)
    {
        client.NextNumberText = (start + count).ToString();
        Persist();
        Status = status;
    }

    internal void Print()
    {
        if (BuildBatch() is not { } b) return;
        if (PrintSheets is null)
        {
            _dialogs.Warn("Printing isn't available here.", "Sendu — label maker");
            return;
        }
        if (!PrintSheets(b.Items, $"Sendu labels {b.Items[0].Code}")) return;   // cancelled
        var sheets = (b.Count + BoxLabels.PerSheet - 1) / BoxLabels.PerSheet;
        Advance(b.Client, b.Start, b.Count,
            $"Sent {b.Count} label{(b.Count == 1 ? "" : "s")} "
            + $"({sheets} sheet{(sheets == 1 ? "" : "s")}) to the printer.");
    }

    internal void SavePdf()
    {
        if (BuildBatch() is not { } b) return;
        var dest = _dialogs.AskSaveFile("PDF files (*.pdf)|*.pdf",
            $"labels_{b.Client.Id}_{b.Start:D8}.pdf");
        if (dest is null) return;
        try
        {
            BoxLabels.RenderPdf(dest, b.Items);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Warn("Couldn't save it: " + ex.Message, "Sendu — label maker");
            return;
        }
        var sheets = (b.Count + BoxLabels.PerSheet - 1) / BoxLabels.PerSheet;
        Advance(b.Client, b.Start, b.Count,
            $"Saved {b.Count} label{(b.Count == 1 ? "" : "s")} "
            + $"({sheets} sheet{(sheets == 1 ? "" : "s")}) — print at 100% scale.");
        try { _openFile(dest); } catch { /* viewer trouble isn't a label problem */ }
    }

    /// <summary>Write the edited client list back to config.json.</summary>
    internal void Persist()
    {
        _cfg.LabelClients = Clients.Select(c => c.ToClient()).ToList();
        _saveConfig();
    }
}

file static class CollectionExtensions
{
    public static T AddReturn<T>(this ObservableCollection<T> list, T item)
    {
        list.Add(item);
        return item;
    }
}
