using FileRouter.Core;
using FileRouter.Wpf.Services;

namespace FileRouter.Wpf.Tests;

/// <summary>Nothing the filing loop can throw may disappear in silence. An
/// audit write that fails after the move says where the document went; anything
/// else surfaces as a warning and reaches crash.log.</summary>
public class UnexpectedErrorTests
{
    [Fact]
    public void AFailedAuditWriteTellsTheUserWhereTheDocumentWent()
    {
        using var fx = new ShellFixture();
        var src = fx.AddInboxFile("20240115--111111.pdf");
        fx.AddInboxFile("20240115--222222.pdf");
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();

        fx.Shell.History.Dispose();          // audit DB dies mid-session
        fx.Shell.RouteCommand.Execute(0);

        // the move happened, so the user must be told - not left guessing
        var warning = Assert.Single(fx.Dialogs.Warnings);
        Assert.Contains("20240115--111111.pdf", warning.Message + fx.Shell.StatusLine);
        Assert.Contains(fx.RouteDir, warning.Message);
        Assert.False(File.Exists(src));
        Assert.Single(Directory.GetFiles(fx.RouteDir));
    }

    [Fact]
    public void AFailedAuditWriteMovesOnToTheNextDocument()
    {
        using var fx = new ShellFixture();
        fx.AddInboxFile("20240115--111111.pdf");
        fx.AddInboxFile("20240115--222222.pdf");
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();

        fx.Shell.History.Dispose();
        fx.Shell.RouteCommand.Execute(0);

        // regression: the queue used to stay put, so pressing again logged the
        // already-filed document as <vanished>
        Assert.Equal("20240115--222222.pdf", fx.Shell.CurrentFilename);
        Assert.Equal("2 / 2", fx.Shell.ProgressLine);
        Assert.Equal(1, fx.Shell.Session.Filed);
        Assert.Equal(0, fx.Shell.Session.Vanished);
    }

    [Fact]
    public void AFailedAuditWriteChimesTheErrorSound()
    {
        using var fx = new ShellFixture();
        fx.AddInboxFile("20240115--111111.pdf");
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();

        fx.Shell.History.Dispose();
        fx.Shell.RouteCommand.Execute(0);

        Assert.Contains(fx.Sounds.Played, p => p.Evt == SoundEvent.Error);
    }

    [Fact]
    public void AFailedAuditWriteOnSetAsideIsReportedToo()
    {
        using var fx = new ShellFixture();
        fx.AddInboxFile("20240115--111111.pdf");
        fx.AddInboxFile("20240115--222222.pdf");
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();

        fx.Shell.History.Dispose();
        fx.Shell.SkipCommand.Execute(null);

        Assert.Single(fx.Dialogs.Warnings);
        Assert.Equal(1, fx.Shell.Session.Skipped);
        Assert.Equal("20240115--222222.pdf", fx.Shell.CurrentFilename);
    }

    [Fact]
    public void AnUnexpectedExceptionIsReportedInsteadOfSwallowed()
    {
        using var fx = new ShellFixture();
        fx.AddInboxFile("20240115--111111.pdf");
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();

        Exception? logged = null;
        fx.Shell.UnexpectedError += ex => logged = ex;
        // the viewer blowing up mid-commit stands in for any unforeseen fault
        fx.Viewer.ThrowOnRelease = new InvalidOperationException("viewer exploded");
        fx.Shell.RouteCommand.Execute(0);

        Assert.NotNull(logged);
        Assert.Equal("viewer exploded", logged!.Message);
        Assert.Contains(fx.Dialogs.Warnings, w => w.Message.Contains("viewer exploded"));
    }

    [Fact]
    public void TheBusyGuardIsReleasedAfterAnUnexpectedFailure()
    {
        using var fx = new ShellFixture();
        fx.AddInboxFile("20240115--111111.pdf");
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();

        fx.Viewer.ThrowOnRelease = new InvalidOperationException("boom");
        fx.Shell.RouteCommand.Execute(0);
        fx.Viewer.ThrowOnRelease = null;

        // a fault must not wedge the session shut
        fx.Shell.RouteCommand.Execute(0);
        Assert.Single(Directory.GetFiles(fx.RouteDir));
    }
}
