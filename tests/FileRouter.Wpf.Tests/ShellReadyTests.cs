using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf.Tests;

public class ShellReadyTests
{
    [Fact]
    public void FreshShellShowsReadyCount()
    {
        using var fx = new ShellFixture();
        fx.AddInboxFile("20240115--111111.pdf");
        fx.AddInboxFile("20240116--222222.pdf");
        fx.Shell.Initialize();

        Assert.Equal(Screen.Ready, fx.Shell.Screen);
        Assert.Equal("2 files ready", fx.Shell.CountLine);
        Assert.Equal("2", fx.Shell.BigCount);
        Assert.Equal("PDFs in the inbox", fx.Shell.CountCaption);
        Assert.True(fx.Shell.StartEnabled);
        Assert.True(fx.Shell.Viewer0Blanked(fx.Viewer));
    }

    [Fact]
    public void NonMatchingFilesAreCountedAsIgnored()
    {
        using var fx = new ShellFixture();
        fx.AddInboxFile("20240115--111111.pdf");
        File.WriteAllText(Path.Combine(fx.Inbox, "not-a-fax.pdf"), "x");
        fx.Shell.Initialize();

        Assert.Equal("1 file ready", fx.Shell.CountLine);
        Assert.Equal("1 other file ignored", fx.Shell.DetailLine);
    }

    [Fact]
    public void EmptyInboxDisablesStart()
    {
        using var fx = new ShellFixture();
        fx.Shell.Initialize();
        Assert.Equal("0 files ready", fx.Shell.CountLine);
        Assert.False(fx.Shell.StartEnabled);
        Assert.False(fx.Shell.StartCommand.CanExecute(null));
    }

    [Fact]
    public void DeferredFilesRaiseTheSetAsideAlert()
    {
        using var fx = new ShellFixture();
        File.WriteAllText(Path.Combine(fx.Deferred, "waiting.pdf"), "x");
        fx.Shell.Initialize();
        Assert.True(fx.Shell.HasDeferred);
        Assert.Contains("1 set-aside file waiting", fx.Shell.DeferredAlert);
    }

    [Fact]
    public void FolderActivityRefreshesTheReadyCount()
    {
        using var fx = new ShellFixture();
        fx.Shell.Initialize();
        Assert.Equal("0 files ready", fx.Shell.CountLine);

        fx.AddInboxFile();
        fx.Shell.OnFolderActivity();
        Assert.Equal("1 file ready", fx.Shell.CountLine);
        Assert.True(fx.Shell.StartEnabled);
    }

    [Fact]
    public void InboxProblemIsReadableNotFatal()
    {
        using var fx = new ShellFixture(cfg => cfg.Inbox = Path.Combine(cfg.Inbox, "missing"));
        fx.Shell.Initialize();
        Assert.Equal("Inbox problem", fx.Shell.CountLine);
        Assert.Equal("⚠", fx.Shell.BigCount);
        Assert.False(fx.Shell.StartEnabled);
        Assert.NotEqual("", fx.Shell.DetailLine);
    }
}

internal static class ShellAsserts
{
    /// <summary>Ready always blanks the viewer at least once.</summary>
    public static bool Viewer0Blanked(this ShellViewModel _, FakeViewer viewer) =>
        viewer.Blanks >= 1;
}
