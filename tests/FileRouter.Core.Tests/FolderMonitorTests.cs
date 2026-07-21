using FileRouter.Core;

namespace FileRouter.Core.Tests;

public class FolderMonitorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fmtest_" + Guid.NewGuid());
    public FolderMonitorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private WatchFolder Wf(string sub, string filetypes = "", bool recursive = false)
    {
        var p = Path.Combine(_dir, sub);
        Directory.CreateDirectory(p);
        return new WatchFolder { Label = sub, Path = p, Filetypes = filetypes, Recursive = recursive };
    }

    private static void Touch(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), "x");
    }

    [Fact]
    public void CountsFilesAnyTypeWhenBlank()
    {
        var wf = Wf("a");
        Touch(wf.Path, "one.pdf");
        Touch(wf.Path, "two.txt");
        var s = FolderMonitor.Status(wf, Array.Empty<string>());
        Assert.Equal(2, s.Count);
        Assert.True(s.HasFiles);
        Assert.Equal("", s.Error);
    }

    [Fact]
    public void FiletypeFilterRestrictsCount()
    {
        var wf = Wf("a", filetypes: "pdf");
        Touch(wf.Path, "one.pdf");
        Touch(wf.Path, "two.txt");
        Touch(wf.Path, "three.PDF");   // case-insensitive
        Assert.Equal(2, FolderMonitor.Status(wf, Array.Empty<string>()).Count);
    }

    [Fact]
    public void FiletypesAcceptCommaSpaceAndDots()
    {
        var types = FolderMonitor.ParseFiletypes(".pdf, txt ;.TIF");
        Assert.Equal(new[] { "pdf", "tif", "txt" }, types.OrderBy(x => x));
    }

    [Fact]
    public void RecursiveCountsSubfolders()
    {
        var wf = Wf("a", recursive: true);
        Touch(wf.Path, "top.pdf");
        Touch(Path.Combine(wf.Path, "sub"), "nested.pdf");
        Assert.Equal(2, FolderMonitor.Status(wf, Array.Empty<string>()).Count);
    }

    [Fact]
    public void NonRecursiveIgnoresSubfolders()
    {
        var wf = Wf("a", recursive: false);
        Touch(wf.Path, "top.pdf");
        Touch(Path.Combine(wf.Path, "sub"), "nested.pdf");
        Assert.Equal(1, FolderMonitor.Status(wf, Array.Empty<string>()).Count);
    }

    [Fact]
    public void EmptyFolderHasNoFiles()
    {
        var s = FolderMonitor.Status(Wf("empty"), Array.Empty<string>());
        Assert.False(s.HasFiles);
        Assert.Equal(0, s.Count);
    }

    [Fact]
    public void MissingFolderIsReadableError()
    {
        var wf = new WatchFolder { Label = "gone", Path = Path.Combine(_dir, "nope") };
        var s = FolderMonitor.Status(wf, Array.Empty<string>());
        Assert.Contains("not available", s.Error);
        Assert.False(s.HasFiles);
    }

    [Fact]
    public void AlertMatchesByContainsCaseInsensitive()
    {
        var wf = Wf("a");
        Touch(wf.Path, "20240101--URGENT-scan.pdf");
        Touch(wf.Path, "normal.pdf");
        var s = FolderMonitor.Status(wf, new[] { "urgent", "STAT" });
        Assert.True(s.Alerting);
        Assert.Equal(new[] { "20240101--URGENT-scan.pdf" }, s.Matches);
    }

    [Fact]
    public void NoAlertWhenNothingMatches()
    {
        var wf = Wf("a");
        Touch(wf.Path, "normal.pdf");
        Assert.False(FolderMonitor.Status(wf, new[] { "URGENT" }).Alerting);
    }

    [Fact]
    public void BlankAlertTermsIgnored()
    {
        var wf = Wf("a");
        Touch(wf.Path, "anything.pdf");
        Assert.False(FolderMonitor.Status(wf, new[] { "", "   " }).Alerting);
    }

    [Fact]
    public void AllReturnsOnePerFolderInOrder()
    {
        var a = Wf("aaa");
        var b = Wf("bbb");
        Touch(a.Path, "x.pdf");
        var all = FolderMonitor.All(new[] { a, b }, Array.Empty<string>());
        Assert.Equal(new[] { "aaa", "bbb" }, all.Select(s => s.Label));
    }

    [Fact]
    public void DashboardConfigFieldsRoundTrip()
    {
        var path = Path.Combine(_dir, "c.json");
        var cfg = new Config
        {
            MonitorTitle = "Work queues",
            FlashAlerts = false,
            AlertTexts = new() { "URGENT", "STAT" },
            WatchFolders = new()
            {
                new WatchFolder { Label = "Failed", Path = "S:/x", Recursive = true, Filetypes = "pdf,txt", Color = "#c0392b" },
            },
        };
        Config.Save(cfg, path);
        var back = Config.Load(path);
        Assert.Equal("Work queues", back.MonitorTitle);
        Assert.False(back.FlashAlerts);
        Assert.Equal(new[] { "URGENT", "STAT" }, back.AlertTexts);
        Assert.Single(back.WatchFolders);
        Assert.True(back.WatchFolders[0].Recursive);
        Assert.Equal("pdf,txt", back.WatchFolders[0].Filetypes);
        Assert.Equal("#c0392b", back.WatchFolders[0].Color);
    }
}
