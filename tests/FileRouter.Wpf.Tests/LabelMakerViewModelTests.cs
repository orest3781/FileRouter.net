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
        new(cfg, () => _saved = true, _dialogs, () => Today, _opened.Add);

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
        Assert.Contains("ABCD00000042", vm.Preview);
        Assert.Contains("created 2026-07-25", vm.Preview);
        Assert.Contains("destroy after 2026-08-24", vm.Preview);
    }

    [Fact]
    public void GenerateWritesThePdfAdvancesTheNumberAndPersists()
    {
        var cfg = new Config
        {
            LabelClients = { new LabelClient { Id = "ABCD", DestroyDays = 30, NextNumber = 5 } },
        };
        var vm = Vm(cfg);
        var dest = Path.Combine(_dir, "labels.pdf");
        _dialogs.NextSaveFile = dest;

        vm.Generate();

        Assert.True(File.Exists(dest));
        Assert.Equal("15", vm.Selected!.NextNumberText);        // 10 labels consumed
        Assert.Equal(15, cfg.LabelClients[0].NextNumber);       // written back...
        Assert.True(_saved);                                    // ...and saved
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

        vm.Generate();

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

        vm.Generate();

        var msg = Assert.Single(_dialogs.Warnings).Message;
        Assert.Contains("2 to 8", msg);          // bad client id
        Assert.Contains("1 to 1000", msg);       // bad count
        Assert.False(File.Exists(Path.Combine(_dir, "never.pdf")));
        Assert.StartsWith("⚠", vm.Preview);      // the preview says so live too
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
        vm.Generate();
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
        Assert.False(vm.GenerateCommand.CanExecute(null));

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
    public void BatchNearTheCeilingIsCaughtBeforeTheDialog()
    {
        var cfg = new Config
        {
            LabelClients = { new LabelClient { Id = "ABCD", NextNumber = 99_999_995 } },
        };
        var vm = Vm(cfg);
        _dialogs.NextSaveFile = Path.Combine(_dir, "never.pdf");

        vm.Generate();   // 10 labels would pass 99,999,999

        Assert.Contains("99999999", Assert.Single(_dialogs.Warnings).Message);
        Assert.False(File.Exists(Path.Combine(_dir, "never.pdf")));
    }
}
