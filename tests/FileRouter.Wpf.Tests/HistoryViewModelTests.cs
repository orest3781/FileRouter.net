using FileRouter.Core;
using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf.Tests;

public class HistoryViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "frhist_" + Guid.NewGuid());
    private readonly History _history;
    private readonly FakeDialogs _dialogs = new();

    public HistoryViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        _history = new History(Path.Combine(_dir, "history.sqlite"));
    }

    public void Dispose()
    {
        _history.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var i = 0; i < 10; i++)
        {
            try { Directory.Delete(_dir, true); return; } catch { Thread.Sleep(50); }
        }
    }

    private void Seed(int count, string route = "Invoices")
    {
        for (var i = 0; i < count; i++)
            _history.LogCommit($"c:\\in\\{i}.pdf", $"{i}.pdf", $"NAME {i}.pdf",
                $"NAME {i}", "insert", "", route, "c:\\out", tagged: false, "");
    }

    [Fact]
    public void LoadsNewestFiveHundredWithFooter()
    {
        Seed(600);
        var vm = new HistoryViewModel(_history, _dialogs);
        Assert.Equal(500, vm.Rows.Count);
        Assert.Equal("NAME 599", vm.Rows[0].Name);   // newest first
        Assert.True(vm.CanShowAll);
        Assert.Equal("Showing the latest 500 of 600 filings", vm.FooterText);
    }

    [Fact]
    public void ShowAllLoadsEverything()
    {
        Seed(600);
        var vm = new HistoryViewModel(_history, _dialogs);
        vm.ShowAllCommand.Execute(null);
        Assert.Equal(600, vm.Rows.Count);
        Assert.False(vm.CanShowAll);
        Assert.Equal("600 of 600 filings shown", vm.FooterText);
    }

    [Fact]
    public void SmallTablesNeedNoShowAll()
    {
        Seed(3);
        var vm = new HistoryViewModel(_history, _dialogs);
        Assert.Equal(3, vm.Rows.Count);
        Assert.False(vm.CanShowAll);
    }

    [Fact]
    public void FilterNarrowsAcrossColumns()
    {
        Seed(20);
        _history.LogCommit("c:\\in\\x.pdf", "x.pdf", "SMITH JOHN.pdf",
            "SMITH JOHN", "replace", "", "Statements", "c:\\out", tagged: false, "");
        var vm = new HistoryViewModel(_history, _dialogs);

        vm.Filter = "smith";
        Assert.Single(vm.Rows);
        vm.Filter = "statements";
        Assert.Single(vm.Rows);
        vm.Filter = "";
        Assert.Equal(21, vm.Rows.Count);
    }

    [Fact]
    public void RevertedRowsAreFlagged()
    {
        var id = _history.LogCommit("c:\\in\\x.pdf", "x.pdf", "Y.pdf", "Y",
            "insert", "", "Invoices", "c:\\out", tagged: false, "");
        _history.MarkReverted(id);
        var vm = new HistoryViewModel(_history, _dialogs);
        Assert.True(Assert.Single(vm.Rows).Reverted);
    }

    [Fact]
    public void ExportGoesThroughTheDialogService()
    {
        Seed(2);
        var vm = new HistoryViewModel(_history, _dialogs);
        var dest = Path.Combine(_dir, "out.csv");
        _dialogs.NextSaveFile = dest;
        vm.ExportCommand.Execute(null);
        Assert.True(File.Exists(dest));
        Assert.Single(_dialogs.Infos);
    }
}
