using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf.Tests;

public class DoneAndExportTests
{
    [Fact]
    public async Task DoneSummarizesFiledAndSetAside()
    {
        using var fx = new ShellFixture();
        fx.AddInboxFile("20240115--111111.pdf");
        fx.AddInboxFile("20240116--222222.pdf");
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();

        fx.Shell.TypedName = "SMITH JOHN";
        await fx.Shell.OnRouteAsync(0);
        await fx.Shell.OnSkipAsync();

        Assert.Equal(Screen.Done, fx.Shell.Screen);
        Assert.Equal("Session complete", fx.Shell.CountLine);
        Assert.Equal("1 filed, 1 set aside", fx.Shell.DetailLine);
    }

    [Fact]
    public async Task FolderActivityOnDoneNotifiesWithoutClobberingTheSummary()
    {
        using var fx = new ShellFixture();
        fx.AddInboxFile("20240115--111111.pdf");
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();
        fx.Shell.TypedName = "SMITH JOHN";
        await fx.Shell.OnRouteAsync(0);
        Assert.Equal(Screen.Done, fx.Shell.Screen);

        // the last commit's own file event fires the watcher moments after
        // the summary appears — it must not replace the summary text
        fx.Shell.OnFolderActivity();
        Assert.Equal("Session complete", fx.Shell.CountLine);
        Assert.Equal("1 filed, 0 set aside", fx.Shell.DetailLine);
        Assert.Equal("", fx.Shell.StatusLine);   // empty inbox -> no note

        // a NEW arrival while on Done gets a quiet note, summary intact
        fx.AddInboxFile("20240116--222222.pdf");
        fx.Shell.OnFolderActivity();
        Assert.Equal(Screen.Done, fx.Shell.Screen);
        Assert.Equal("Session complete", fx.Shell.CountLine);
        Assert.Equal("1 filed, 0 set aside", fx.Shell.DetailLine);
        Assert.Equal("1 file waiting in the inbox.", fx.Shell.StatusLine);

        // Back to inbox picks it up
        fx.Shell.RescanCommand.Execute(null);
        Assert.Equal(Screen.Ready, fx.Shell.Screen);
        Assert.Equal("1 file ready", fx.Shell.CountLine);
    }

    [Fact]
    public async Task VanishedFilesAppearInTheSummary()
    {
        using var fx = new ShellFixture();
        var path = fx.AddInboxFile("20240115--111111.pdf");
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();

        File.Delete(path);   // yanked mid-session
        await fx.Shell.OnRouteAsync(0);

        Assert.Equal(Screen.Done, fx.Shell.Screen);
        Assert.Equal("0 filed, 0 set aside, 1 vanished", fx.Shell.DetailLine);
    }

    [Fact]
    public async Task ExportWritesTheAuditCsv()
    {
        using var fx = new ShellFixture();
        fx.AddInboxFile("20240115--111111.pdf");
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();
        fx.Shell.TypedName = "DOE JANE";
        await fx.Shell.OnRouteAsync(0);

        var dest = Path.Combine(fx.Dir, "export.csv");
        fx.Dialogs.NextSaveFile = dest;
        fx.Shell.ExportHistory();

        Assert.True(File.Exists(dest));
        Assert.Contains("DOE JANE", File.ReadAllText(dest));
        Assert.Single(fx.Dialogs.Infos);
    }

    [Fact]
    public void ExportFailureWarnsInsteadOfCrashing()
    {
        using var fx = new ShellFixture();
        fx.Dialogs.NextSaveFile = Path.Combine(fx.Dir, "no-such-dir", "export.csv");
        fx.Shell.ExportHistory();
        Assert.Single(fx.Dialogs.Warnings);
        Assert.Empty(fx.Dialogs.Infos);
    }

    [Fact]
    public void ExportCancelledDoesNothing()
    {
        using var fx = new ShellFixture();
        fx.Dialogs.NextSaveFile = null;
        fx.Shell.ExportHistory();
        Assert.Empty(fx.Dialogs.Warnings);
        Assert.Empty(fx.Dialogs.Infos);
    }
}
