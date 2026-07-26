using FileRouter.Core;
using FileRouter.Wpf.Services;

namespace FileRouter.Wpf.Tests;

/// <summary>The per-file alert memory must survive anything that SKIPS the
/// watch-folder sweep. A skipped sweep is not evidence the alert went away, so
/// re-showing the tiles must not replay it as a fresh arrival.</summary>
public class AlertMemoryTests
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
                Label = "Failed faxes", Path = path, Filetypes = "pdf", Color = "#c0392b",
            });
            cfg.AlertTexts.Add("URGENT");
            extraTweak?.Invoke(cfg);
        });
        watched = path!;
        return fx;
    }

    [Fact]
    public void HidingAndShowingTilesDoesNotReplayAKnownAlert()
    {
        using var fx = WithWatchFolder(out var watched);
        fx.Shell.Initialize();
        File.WriteAllText(Path.Combine(watched, "URGENT.pdf"), "x");
        fx.Shell.OnFolderActivity();          // the genuine arrival: chimes once
        Assert.Contains(fx.Sounds.Played, p => p.Evt == SoundEvent.NewAlert);
        fx.Sounds.Played.Clear();
        fx.Shell.HideToast();

        fx.Shell.TileVisibilityIndex = 2;     // Hidden - sweep is skipped
        fx.Shell.TileVisibilityIndex = 0;     // back to Active

        // regression: the skipped sweep used to wipe the memory, so coming back
        // chimed and toasted a file that had been sitting there all along
        Assert.DoesNotContain(fx.Sounds.Played, p => p.Evt == SoundEvent.NewAlert);
        Assert.False(fx.Shell.ToastVisible);
    }

    [Fact]
    public void AnAlertArrivingWhileHiddenIsAnnouncedWhenTilesComeBack()
    {
        using var fx = WithWatchFolder(out var watched);
        fx.Shell.Initialize();
        fx.Shell.TileVisibilityIndex = 2;     // Hidden before anything arrives
        fx.Sounds.Played.Clear();

        File.WriteAllText(Path.Combine(watched, "URGENT.pdf"), "x");
        fx.Shell.OnFolderActivity();          // not swept, so nothing yet
        Assert.DoesNotContain(fx.Sounds.Played, p => p.Evt == SoundEvent.NewAlert);

        fx.Shell.TileVisibilityIndex = 0;     // now we look - it IS new to us
        Assert.Contains(fx.Sounds.Played, p => p.Evt == SoundEvent.NewAlert);
    }

    [Fact]
    public void AFilingSessionDoesNotReplayAKnownAlert()
    {
        using var fx = WithWatchFolder(out var watched);
        fx.Shell.Initialize();
        File.WriteAllText(Path.Combine(watched, "URGENT.pdf"), "x");
        fx.Shell.OnFolderActivity();
        fx.Sounds.Played.Clear();
        fx.Shell.HideToast();

        fx.AddInboxFile();
        fx.Shell.OnFolderActivity();
        fx.Shell.StartProcessing();
        fx.Shell.OnFolderActivity();          // the poll keeps ticking while filing
        fx.Shell.StopSession();               // Esc - back to Ready, re-sweeps
        fx.Shell.OnFolderActivity();

        Assert.DoesNotContain(fx.Sounds.Played, p => p.Evt == SoundEvent.NewAlert);
        Assert.False(fx.Shell.ToastVisible);
    }

    [Fact]
    public void AnAlertThatLeavesAndComesBackChimesAgain()
    {
        using var fx = WithWatchFolder(out var watched);
        fx.Shell.Initialize();
        var file = Path.Combine(watched, "URGENT.pdf");
        File.WriteAllText(file, "x");
        fx.Shell.OnFolderActivity();
        fx.Sounds.Played.Clear();

        File.Delete(file);                    // somebody dealt with it
        fx.Shell.OnFolderActivity();
        Assert.False(fx.Shell.HasActiveAlert);

        File.WriteAllText(file, "x");         // and it comes back
        fx.Shell.OnFolderActivity();
        Assert.Contains(fx.Sounds.Played, p => p.Evt == SoundEvent.NewAlert);
    }

    [Fact]
    public void InboxAlertsAreRememberedIndependentlyOfWatchFolders()
    {
        using var fx = WithWatchFolder(out var watched);
        fx.AddInboxFile("20240115--URGENT1.pdf");
        fx.Shell.Initialize();                // seeds the inbox alert silently
        fx.Sounds.Played.Clear();

        // hiding tiles must not make the inbox alert look new either
        fx.Shell.TileVisibilityIndex = 2;
        fx.Shell.TileVisibilityIndex = 0;
        Assert.DoesNotContain(fx.Sounds.Played, p => p.Evt == SoundEvent.NewAlert);

        File.WriteAllText(Path.Combine(watched, "URGENT-new.pdf"), "x");
        fx.Shell.OnFolderActivity();          // a real new folder alert still lands
        Assert.Contains(fx.Sounds.Played, p => p.Evt == SoundEvent.NewAlert);
    }
}
