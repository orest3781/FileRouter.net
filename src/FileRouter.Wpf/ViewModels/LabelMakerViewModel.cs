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
    public string Id { get => _id; set => Set(ref _id, value.ToUpperInvariant().Trim()); }
    public string DestroyDaysText { get => _destroyDaysText; set => Set(ref _destroyDaysText, value); }
    public string NextNumberText { get => _nextNumberText; set => Set(ref _nextNumberText, value); }

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
        });
        RemoveClientCommand = new RelayCommand(() =>
        {
            if (Selected is { } s) Clients.Remove(s);
            Selected = Clients.FirstOrDefault();
        }, () => Selected is not null);
        ResetNumberCommand = new RelayCommand(
            () => { if (Selected is { } s) s.NextNumberText = "1"; },
            () => Selected is not null);
        GenerateCommand = new RelayCommand(Generate, () => Selected is not null);

        Selected = Clients.FirstOrDefault();   // after the commands the setter pokes
    }

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
            GenerateCommand.RaiseCanExecuteChanged();
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

    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    public RelayCommand AddClientCommand { get; }
    public RelayCommand RemoveClientCommand { get; }
    public RelayCommand ResetNumberCommand { get; }
    public RelayCommand GenerateCommand { get; }

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
        if (Selected is not { } s) { Preview = ""; return; }
        var problems = Problems();
        if (problems.Count > 0) { Preview = "⚠ " + problems[0]; return; }
        var created = _today().Date;
        var destroy = created.AddDays(int.Parse(s.DestroyDaysText.Trim()));
        Preview = $"First label:  {BoxLabels.Compose(s.Id, long.Parse(s.NextNumberText.Trim()))}"
            + $"   ·   created {created:yyyy-MM-dd}   ·   destroy after {destroy:yyyy-MM-dd}";
    }

    internal void Generate()
    {
        if (Selected is not { } s) return;
        var problems = Problems();
        if (problems.Count > 0)
        {
            _dialogs.Warn("These need fixing first:\n\n • " + string.Join("\n • ", problems),
                "FileRouter — label maker");
            return;
        }
        var start = long.Parse(s.NextNumberText.Trim());
        var count = int.Parse(LabelCountText.Trim());
        var days = int.Parse(s.DestroyDaysText.Trim());

        var dest = _dialogs.AskSaveFile("PDF files (*.pdf)|*.pdf",
            $"labels_{s.Id}_{start:D8}.pdf");
        if (dest is null) return;

        var items = BoxLabels.Batch(s.Id, start, count, _today(), days);
        try
        {
            BoxLabels.RenderPdf(dest, items);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Warn("Couldn't save it: " + ex.Message, "FileRouter — label maker");
            return;
        }

        s.NextNumberText = (start + count).ToString();
        Persist();
        var sheets = (count + BoxLabels.PerSheet - 1) / BoxLabels.PerSheet;
        Status = $"Saved {count} label{(count == 1 ? "" : "s")} "
            + $"({sheets} sheet{(sheets == 1 ? "" : "s")}) — print at 100% scale.";
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
