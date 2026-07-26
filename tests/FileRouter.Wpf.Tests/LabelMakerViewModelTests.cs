using FileRouter.Core;
using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf.Tests;

public class LabelMakerViewModelTests : IDisposable
{
    private readonly string _dir =
        Directory.CreateTempSubdirectory("fr_labelvm").FullName;
    private readonly FakeDialogs _dialogs = new();
    private bool _saved;
    private readonly List<string> _opened = new();
    private static readonly DateTime Today = new(2026, 7, 25);

    private LabelMakerViewModel Vm(Config cfg) =>
        new(cfg, () => _saved = true, _dialogs, () => Today, _opened.Add,
            new InlineWorkScheduler());

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void LoadsClientsFromConfigAndSelectsTheFirst()
    {
        var cfg = new Config
        {
            LabelClients =
            {
                new LabelClient { Id = "ABCD", DestroyDays = 30, NextNumber = 42 },
                new LabelClient { Id = "WXYZ", DestroyDays = 90, NextNumber = 7 },
            },
        };
        var vm = Vm(cfg);
        Assert.Equal(2, vm.Clients.Count);
        Assert.Equal("ABCD", vm.Selected!.Id);
        Assert.Contains("ABCD00000042 – ABCD00000051", vm.Preview);   // default count 10
        Assert.Contains("1 sheet", vm.Preview);

        // the live preview card renders the first label of the batch
        Assert.Equal("ABCD00000042", vm.PreviewItem!.Code);
        Assert.Equal(Today, vm.PreviewItem.Created);
        Assert.Equal(Today.AddDays(30), vm.PreviewItem.Destroy);
    }

    [Fact]
    public void PrintSendsTheSheetsAndAdvancesTheNumber()
    {
        var cfg = new Config
        {
            LabelClients = { new LabelClient { Id = "ABCD", DestroyDays = 30, NextNumber = 5 } },
        };
        var vm = Vm(cfg);
        IReadOnlyList<BoxLabels.Item>? sent = null;
        string? job = null;
        vm.PrintSheets = (items, name) => { sent = items; job = name; return true; };

        vm.Print();

        Assert.Equal(10, sent!.Count);
        Assert.Equal("ABCD00000005", sent[0].Code);
        Assert.Contains("ABCD00000005", job);
        Assert.Equal("15", vm.Selected!.NextNumberText);        // 10 labels consumed
        Assert.Equal(15, cfg.LabelClients[0].NextNumber);       // written back...
        Assert.True(_saved);                                    // ...and saved
        Assert.Contains("printer", vm.Status);
        Assert.Empty(_dialogs.Warnings);
    }

    [Fact]
    public void CancellingThePrintDialogChangesNothing()
    {
        var cfg = new Config
        {
            LabelClients = { new LabelClient { Id = "ABCD", NextNumber = 5 } },
        };
        var vm = Vm(cfg);
        vm.PrintSheets = (_, _) => false;   // user backed out

        vm.Print();

        Assert.Equal("5", vm.Selected!.NextNumberText);
        Assert.False(_saved);
        Assert.Equal("", vm.Status);
    }

    [Fact]
    public void SavePdfWritesTheFileAdvancesTheNumberAndPersists()
    {
        var cfg = new Config
        {
            LabelClients = { new LabelClient { Id = "ABCD", DestroyDays = 30, NextNumber = 5 } },
        };
        var vm = Vm(cfg);
        var dest = Path.Combine(_dir, "labels.pdf");
        _dialogs.NextSaveFile = dest;

        vm.SavePdf();

        Assert.True(File.Exists(dest));
        Assert.Equal("15", vm.Selected!.NextNumberText);
        Assert.Equal(15, cfg.LabelClients[0].NextNumber);
        Assert.True(_saved);
        Assert.Equal(dest, Assert.Single(_opened));             // handed to the viewer
        Assert.Contains("1 sheet", vm.Status);
        Assert.Empty(_dialogs.Warnings);
    }

    [Fact]
    public void CancellingTheSaveDialogChangesNothing()
    {
        var cfg = new Config
        {
            LabelClients = { new LabelClient { Id = "ABCD", NextNumber = 5 } },
        };
        var vm = Vm(cfg);
        _dialogs.NextSaveFile = null;   // user pressed Cancel

        vm.SavePdf();

        Assert.Equal("5", vm.Selected!.NextNumberText);
        Assert.False(_saved);
        Assert.Empty(_opened);
    }

    [Fact]
    public void BadInputsWarnInsteadOfGenerating()
    {
        var cfg = new Config { LabelClients = { new LabelClient { Id = "A" } } };
        var vm = Vm(cfg);
        vm.LabelCountText = "0";
        _dialogs.NextSaveFile = Path.Combine(_dir, "never.pdf");

        vm.SavePdf();

        var msg = Assert.Single(_dialogs.Warnings).Message;
        Assert.Contains("2 to 8", msg);          // bad client id
        Assert.Contains("1 to 1000", msg);       // bad count
        Assert.False(File.Exists(Path.Combine(_dir, "never.pdf")));
        Assert.StartsWith("⚠", vm.Preview);      // the preview says so live too
        Assert.Null(vm.PreviewItem);             // and the card goes blank
    }

    [Fact]
    public void PrintWithoutAPrinterHookWarns()
    {
        var cfg = new Config { LabelClients = { new LabelClient { Id = "ABCD" } } };
        var vm = Vm(cfg);
        vm.Print();   // PrintSheets never wired
        Assert.Contains("Printing", Assert.Single(_dialogs.Warnings).Message);
        Assert.False(_saved);
    }

    [Fact]
    public void DuplicateClientIdsAreBlocked()
    {
        var cfg = new Config
        {
            LabelClients =
            {
                new LabelClient { Id = "ABCD" },
                new LabelClient { Id = "ABCD" },
            },
        };
        var vm = Vm(cfg);
        vm.SavePdf();
        Assert.Contains("both called", Assert.Single(_dialogs.Warnings).Message);
    }

    [Fact]
    public void ResetTakesTheNumberBackToOne()
    {
        var cfg = new Config
        {
            LabelClients = { new LabelClient { Id = "ABCD", NextNumber = 4242 } },
        };
        var vm = Vm(cfg);
        vm.ResetNumberCommand.Execute(null);
        Assert.Equal("1", vm.Selected!.NextNumberText);
        Assert.Contains("ABCD00000001", vm.Preview);
    }

    [Fact]
    public void AddAndRemoveManageTheListAndPersistOnDemand()
    {
        var cfg = new Config();
        var vm = Vm(cfg);
        Assert.Null(vm.Selected);
        Assert.False(vm.PrintCommand.CanExecute(null));
        Assert.False(vm.SavePdfCommand.CanExecute(null));

        vm.AddClientCommand.Execute(null);
        vm.Selected!.Id = "abcd";                 // typed lowercase...
        Assert.Equal("ABCD", vm.Selected.Id);     // ...uppercased on the way in

        vm.Persist();
        Assert.Equal("ABCD", Assert.Single(cfg.LabelClients).Id);
        Assert.True(_saved);

        vm.RemoveClientCommand.Execute(null);
        Assert.Empty(vm.Clients);
        vm.Persist();
        Assert.Empty(cfg.LabelClients);
    }

    [Fact]
    public void RemovingARealClientAsksFirstAndDecliningKeepsIt()
    {
        var cfg = new Config
        {
            LabelClients = { new LabelClient { Id = "MEDR", NextNumber = 5000 } },
        };
        var vm = Vm(cfg);

        _dialogs.ConfirmAnswer = false;              // "No" — the counter survives
        vm.RemoveClientCommand.Execute(null);
        Assert.Single(vm.Clients);

        _dialogs.ConfirmAnswer = true;               // "Yes" — deliberate removal
        vm.RemoveClientCommand.Execute(null);
        Assert.Empty(vm.Clients);
    }

    [Fact]
    public void RemovingAJustAddedBlankRowDoesNotNag()
    {
        var vm = Vm(new Config());
        vm.AddClientCommand.Execute(null);
        _dialogs.ConfirmAnswer = false;              // would block if it asked
        vm.RemoveClientCommand.Execute(null);
        Assert.Empty(vm.Clients);                    // removed without a prompt
    }

    [Fact]
    public void AddRequestsFocusOnTheIdBox()
    {
        var vm = Vm(new Config());
        var asked = 0;
        vm.RequestIdFocus += () => asked++;
        vm.AddClientCommand.Execute(null);
        Assert.Equal(1, asked);
    }

    [Fact]
    public void BatchNearTheCeilingIsCaughtBeforeTheDialog()
    {
        var cfg = new Config
        {
            LabelClients = { new LabelClient { Id = "ABCD", NextNumber = 99_999_995 } },
        };
        var vm = Vm(cfg);
        _dialogs.NextSaveFile = Path.Combine(_dir, "never.pdf");

        vm.SavePdf();   // 10 labels would pass 99,999,999

        Assert.Contains("99999999", Assert.Single(_dialogs.Warnings).Message);
        Assert.False(File.Exists(Path.Combine(_dir, "never.pdf")));
    }
}
