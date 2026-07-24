using FileRouter.Core;
using FileRouter.Wpf.Theme;
using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf.Tests;

public class DashboardTests
{
    private static ShellFixture WithWatchFolder(out string watched,
        Action<Config>? extraTweak = null)
    {
        string? path = null;
        var fx = new ShellFixture(cfg =>
        {
            path = Path.Combine(cfg.Inbox, "..", "watched");
            Directory.CreateDirectory(path);
            cfg.WatchFolders.Add(new WatchFolder
            {
                Label = "Failed faxes",
                Path = path,
                Filetypes = "pdf",
                Color = "#c0392b",
            });
            cfg.AlertTexts.Add("URGENT");
            cfg.MonitorTitle = "Needs attention";
            extraTweak?.Invoke(cfg);
        });
        watched = path!;
        return fx;
    }

    [Fact]
    public void EmptyWatchedFolderShowsNoTile()
    {
        using var fx = WithWatchFolder(out _);
        fx.Shell.Initialize();
        Assert.Empty(fx.Shell.Tiles);
        Assert.False(fx.Shell.DashboardVisible);
    }

    [Fact]
    public void FolderWithFilesGetsATileWithCountAndConfiguredColor()
    {
        using var fx = WithWatchFolder(out var watched);
        File.WriteAllText(Path.Combine(watched, "a.pdf"), "x");
        File.WriteAllText(Path.Combine(watched, "b.pdf"), "x");
        File.WriteAllText(Path.Combine(watched, "ignored.txt"), "x");   // filetype filter
        fx.Shell.Initialize();

        var tile = Assert.Single(fx.Shell.Tiles);
        Assert.Equal("Failed faxes: 2", tile.Display);
        Assert.Equal("Failed faxes", tile.Label);
        Assert.Equal("2", tile.CountText);   // the grid card's big number
        Assert.Equal(new Rgb(192, 57, 43), tile.Back);
        Assert.Equal(ThemePalette.IdealForeground(new Rgb(192, 57, 43)), tile.Fore);
        Assert.True(fx.Shell.DashboardVisible);
        Assert.Equal("Needs attention", fx.Shell.MonitorTitle);
        Assert.False(fx.Shell.FlashRunning);   // nothing alerting
    }

    [Fact]
    public void MissingWatchedFolderShowsAnErrorTile()
    {
        using var fx = WithWatchFolder(out var watched);
        Directory.Delete(watched);
        fx.Shell.Initialize();
        var tile = Assert.Single(fx.Shell.Tiles);
        Assert.StartsWith("Failed faxes: folder not available", tile.Display);
        Assert.Equal("⚠", tile.CountText);
    }

    [Fact]
    public void AlertingTileFlashesRed()
    {
        using var fx = WithWatchFolder(out var watched);
        File.WriteAllText(Path.Combine(watched, "URGENT-callback.pdf"), "x");
        fx.Shell.Initialize();

        var tile = Assert.Single(fx.Shell.Tiles);
        Assert.True(tile.Alerting);
        Assert.Contains("⚠", tile.Display);
        Assert.Equal("1 ⚠", tile.CountText);
        Assert.True(fx.Shell.FlashRunning);

        var baseBack = tile.Back;
        fx.Shell.FlashTick();   // on
        Assert.Equal(ThemePalette.Light.Danger, tile.Back);
        Assert.Equal(ThemePalette.Light.DangerText, tile.Fore);
        fx.Shell.FlashTick();   // off
        Assert.Equal(baseBack, tile.Back);
    }

    [Fact]
    public void SubfolderAlertNamesTheSubfolderOnTheCard()
    {
        using var fx = WithWatchFolder(out var watched,
            cfg => cfg.WatchFolders[0].Recursive = true);
        Directory.CreateDirectory(Path.Combine(watched, "retries"));
        File.WriteAllText(Path.Combine(watched, "retries", "URGENT-fax.pdf"), "x");
        fx.Shell.Initialize();

        var tile = Assert.Single(fx.Shell.Tiles);
        Assert.True(tile.Alerting);
        Assert.Equal("in retries", tile.SubfolderNote);
        Assert.True(tile.HasSubfolderNote);
        Assert.Contains(Path.Combine("retries", "URGENT-fax.pdf"), tile.Tooltip);
    }

    [Fact]
    public void TopLevelAlertShowsNoSubfolderNote()
    {
        using var fx = WithWatchFolder(out var watched);
        File.WriteAllText(Path.Combine(watched, "URGENT.pdf"), "x");
        fx.Shell.Initialize();
        var tile = Assert.Single(fx.Shell.Tiles);
        Assert.True(tile.Alerting);
        Assert.False(tile.HasSubfolderNote);
        Assert.Equal("", tile.SubfolderNote);
    }

    [Fact]
    public void SteadyHighlightWhenFlashingIsDisabled()
    {
        using var fx = WithWatchFolder(out var watched, cfg => cfg.FlashAlerts = false);
        File.WriteAllText(Path.Combine(watched, "URGENT.pdf"), "x");
        fx.Shell.Initialize();

        var tile = Assert.Single(fx.Shell.Tiles);
        Assert.False(fx.Shell.FlashRunning);              // no blinking...
        Assert.Equal(ThemePalette.Light.Danger, tile.Back);   // ...steady red
    }

    [Fact]
    public void InvalidConfiguredColorFallsBackToThemeTile()
    {
        using var fx = WithWatchFolder(out var watched,
            cfg => cfg.WatchFolders[0].Color = "not-a-color");
        File.WriteAllText(Path.Combine(watched, "a.pdf"), "x");
        fx.Shell.Initialize();
        Assert.Equal(ThemePalette.Light.TileDefaultBg, Assert.Single(fx.Shell.Tiles).Back);
    }

    [Fact]
    public void InboxAlertTermTurnsTheCountRedWhileFlashing()
    {
        using var fx = new ShellFixture(cfg => cfg.AlertTexts.Add("555"));
        fx.AddInboxFile("20240115--555123.pdf");
        fx.Shell.Initialize();

        Assert.True(fx.Shell.InboxAlerting);
        Assert.True(fx.Shell.FlashRunning);
        Assert.False(fx.Shell.CountAlertOn);   // flash starts dark
        fx.Shell.FlashTick();
        Assert.True(fx.Shell.CountAlertOn);
    }

    [Fact]
    public void AllModeKeepsAnEmptyFolderOnTheDashboard()
    {
        using var fx = WithWatchFolder(out _, cfg => cfg.TileVisibility = "all");
        fx.Shell.Initialize();

        var tile = Assert.Single(fx.Shell.Tiles);
        Assert.Equal("0", tile.CountText);
        Assert.False(tile.Alerting);
        Assert.True(fx.Shell.DashboardVisible);
        Assert.False(fx.Shell.FlashRunning);
    }

    [Fact]
    public void HiddenModeRemovesTilesEvenWithAlertingFiles()
    {
        using var fx = WithWatchFolder(out var watched,
            cfg => cfg.TileVisibility = "hidden");
        File.WriteAllText(Path.Combine(watched, "URGENT.pdf"), "x");
        fx.Shell.Initialize();

        Assert.Empty(fx.Shell.Tiles);
        Assert.False(fx.Shell.DashboardVisible);
        Assert.False(fx.Shell.FlashRunning);   // silenced folders don't flash
    }

    [Fact]
    public void ChangingTileVisibilityRefreshesLiveAndPersists()
    {
        using var fx = WithWatchFolder(out var watched);
        File.WriteAllText(Path.Combine(watched, "a.pdf"), "x");
        fx.Shell.Initialize();
        Assert.Single(fx.Shell.Tiles);           // active + file present
        Assert.True(fx.Shell.TileControlsVisible);

        fx.Shell.TileVisibilityIndex = 2;        // Hidden
        Assert.Empty(fx.Shell.Tiles);
        Assert.False(fx.Shell.DashboardVisible);
        Assert.Equal("hidden", Config.Load(fx.CfgPath).TileVisibility);

        fx.Shell.TileVisibilityIndex = 1;        // All
        Assert.Single(fx.Shell.Tiles);
        Assert.Equal("all", Config.Load(fx.CfgPath).TileVisibility);

        fx.Shell.TileVisibilityIndex = 0;        // back to Active only
        Assert.Single(fx.Shell.Tiles);           // still has a file
        Assert.Equal("active", Config.Load(fx.CfgPath).TileVisibility);
    }

    [Fact]
    public void UnknownTileVisibilityReadsAsActive()
    {
        using var fx = WithWatchFolder(out var watched,
            cfg => cfg.TileVisibility = "banana");
        fx.Shell.Initialize();
        Assert.Empty(fx.Shell.Tiles);            // not "all" — empty stays hidden
        Assert.Equal(0, fx.Shell.TileVisibilityIndex);

        File.WriteAllText(Path.Combine(watched, "a.pdf"), "x");
        fx.Shell.OnFolderActivity();
        Assert.Single(fx.Shell.Tiles);           // not "hidden" — files show
    }

    [Fact]
    public void TileControlsHideWithoutWatchFoldersAndWhileFiling()
    {
        using var bare = new ShellFixture();
        bare.Shell.Initialize();
        Assert.False(bare.Shell.TileControlsVisible);   // nothing to control

        using var fx = WithWatchFolder(out _);
        fx.AddInboxFile();
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();
        Assert.False(fx.Shell.TileControlsVisible);     // hidden while filing
    }

    [Fact]
    public void StartingASessionHidesTheDashboardAndStopsTheFlash()
    {
        using var fx = WithWatchFolder(out var watched);
        File.WriteAllText(Path.Combine(watched, "URGENT.pdf"), "x");
        fx.AddInboxFile();
        fx.Shell.Initialize();
        Assert.True(fx.Shell.FlashRunning);

        fx.Shell.StartProcessing();
        Assert.False(fx.Shell.DashboardVisible);
        Assert.False(fx.Shell.FlashRunning);
    }
}
