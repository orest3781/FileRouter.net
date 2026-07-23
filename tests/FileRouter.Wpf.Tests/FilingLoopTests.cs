using FileRouter.Core;
using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf.Tests;

/// <summary>The filing loop, headless: real Session + History + temp folders,
/// fake viewer. This is the coverage the WinForms app only had via the manual
/// smoke tool.</summary>
public class FilingLoopTests
{
    private static ShellFixture Started(params string[] files)
    {
        var fx = new ShellFixture();
        foreach (var f in files) fx.AddInboxFile(f);
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();
        return fx;
    }

    [Fact]
    public async Task CommitRenamesMovesAndAdvances()
    {
        using var fx = Started("20240115--111111.pdf", "20240116--222222.pdf");
        Assert.Equal("1 / 2", fx.Shell.ProgressLine);
        Assert.Equal("20240115--111111.pdf", fx.Shell.CurrentFilename);

        fx.Shell.TypedName = "SMITH JOHN";
        await fx.Shell.OnRouteAsync(0);

        Assert.True(File.Exists(Path.Combine(fx.RouteDir, "20240115-SMITH JOHN-111111.pdf")));
        Assert.False(File.Exists(Path.Combine(fx.Inbox, "20240115--111111.pdf")));
        Assert.Equal("2 / 2", fx.Shell.ProgressLine);
        Assert.Equal("", fx.Shell.TypedName);           // cleared for the next doc
        Assert.True(fx.Viewer.Releases >= 1);           // handle released BEFORE move
        Assert.Contains(fx.Viewer.Shown, p => p.EndsWith("20240116--222222.pdf"));
    }

    [Fact]
    public async Task AnyDoubleDashNameFlowsThroughTheWholeLoop()
    {
        // the -- contract is general: not just YYYYMMDD--ID fax names
        using var fx = Started("REFERRAL--ACME CLINIC.pdf");
        Assert.Equal("1 / 1", fx.Shell.ProgressLine);   // it entered the queue

        fx.Shell.TypedName = "SMITH JOHN";    // fixture config defaults to insert mode
        Assert.Equal("REFERRAL-SMITH JOHN-ACME CLINIC.pdf", fx.Shell.Preview);

        await fx.Shell.OnRouteAsync(0);
        Assert.True(File.Exists(Path.Combine(fx.RouteDir, "REFERRAL-SMITH JOHN-ACME CLINIC.pdf")));
    }

    [Fact]
    public async Task BlankCommitKeepsTheOriginalName()
    {
        using var fx = Started("20240115--111111.pdf");
        await fx.Shell.OnRouteAsync(0);
        Assert.True(File.Exists(Path.Combine(fx.RouteDir, "20240115--111111.pdf")));
        Assert.Equal(Screen.Done, fx.Shell.Screen);
    }

    [Fact]
    public async Task SkipMovesToDeferredAndRaisesTheAlert()
    {
        using var fx = Started("20240115--111111.pdf");
        await fx.Shell.OnSkipAsync();
        Assert.True(File.Exists(Path.Combine(fx.Deferred, "20240115--111111.pdf")));
        Assert.True(fx.Shell.HasDeferred);
        Assert.Equal(Screen.Done, fx.Shell.Screen);
        Assert.Equal("1 set aside", fx.Shell.DetailLine.Split(", ")[1]);
    }

    [Fact]
    public async Task UndoRestoresTheFileAndRewindsTheQueue()
    {
        using var fx = Started("20240115--111111.pdf", "20240116--222222.pdf");
        fx.Shell.TypedName = "DOE JANE";
        await fx.Shell.OnRouteAsync(0);
        Assert.Equal("2 / 2", fx.Shell.ProgressLine);

        fx.Shell.OnUndo();
        await fx.Shell.RouteCommand.Completion;

        Assert.True(File.Exists(Path.Combine(fx.Inbox, "20240115--111111.pdf")));
        Assert.Empty(Directory.GetFiles(fx.RouteDir));
        Assert.Equal("1 / 2", fx.Shell.ProgressLine);
        Assert.False(fx.Shell.CanUndo);
        Assert.Contains("Undid", fx.Shell.StatusLine);
    }

    [Fact]
    public async Task UndoFromDoneReentersTheSession()
    {
        using var fx = Started("20240115--111111.pdf");
        await fx.Shell.OnRouteAsync(0);
        Assert.Equal(Screen.Done, fx.Shell.Screen);

        fx.Shell.OnUndo();
        Assert.Equal(Screen.Processing, fx.Shell.Screen);
        Assert.Equal("20240115--111111.pdf", fx.Shell.CurrentFilename);
    }

    [Fact]
    public async Task DoubleFireCommitsExactlyOnce()
    {
        using var fx = Started("20240115--111111.pdf", "20240116--222222.pdf");
        fx.Shell.TypedName = "AAA";
        fx.Viewer.HoldRelease = new TaskCompletionSource();

        var first = fx.Shell.OnRouteAsync(0);
        fx.Shell.TypedName = "BBB";          // a fast second press mid-release
        var second = fx.Shell.OnRouteAsync(0);
        fx.Viewer.HoldRelease.SetResult();
        await Task.WhenAll(first, second);

        var filed = Directory.GetFiles(fx.RouteDir);
        Assert.Single(filed);
        Assert.Contains("AAA", Path.GetFileName(filed[0]));   // first-captured name won
        Assert.Equal(2, fx.Shell.Session.Total);
        Assert.Equal(1, fx.Shell.Session.Filed);
    }

    [Fact]
    public async Task ReentrancyGuardAlsoCoversTheCommandLayer()
    {
        using var fx = Started("20240115--111111.pdf", "20240116--222222.pdf");
        fx.Viewer.HoldRelease = new TaskCompletionSource();

        fx.Shell.RouteCommand.Execute(0);
        Assert.False(fx.Shell.RouteCommand.CanExecute(0));   // UI buttons grey out
        fx.Shell.RouteCommand.Execute(0);
        fx.Viewer.HoldRelease.SetResult();
        await fx.Shell.RouteCommand.Completion;

        Assert.Single(Directory.GetFiles(fx.RouteDir));
    }

    [Fact]
    public void PreviewShowsTheExactFinalName()
    {
        using var fx = Started("20240115--111111.pdf");
        fx.Shell.TypedName = "SMITH JOHN";
        Assert.Equal("20240115-SMITH JOHN-111111.pdf", fx.Shell.Preview);
        Assert.False(fx.Shell.PreviewIsWarning);
    }

    [Fact]
    public void IllegalNameWarnsInThePreviewBeforeAnyButton()
    {
        using var fx = Started("20240115--111111.pdf");
        fx.Shell.TypedName = "A:B";
        Assert.True(fx.Shell.PreviewIsWarning);
        Assert.StartsWith("⚠", fx.Shell.Preview);
    }

    [Fact]
    public void TypedNameIsUppercasedWhenConfigured()
    {
        using var fx = Started("20240115--111111.pdf");
        fx.Shell.TypedName = "smith john";
        Assert.Equal("SMITH JOHN", fx.Shell.TypedName);
    }

    [Fact]
    public async Task EnterCommitsToTheLastUsedRoute()
    {
        using var fx = Started("20240115--111111.pdf", "20240116--222222.pdf");
        await fx.Shell.OnRouteAsync(0);          // establishes the last route
        fx.Shell.TypedName = "JONES AMY";
        await fx.Shell.OnEnterAsync();
        Assert.True(File.Exists(Path.Combine(fx.RouteDir, "20240116-JONES AMY-222222.pdf")));
    }

    [Fact]
    public async Task EnterWithNoLastRouteJustHints()
    {
        using var fx = Started("20240115--111111.pdf");
        await fx.Shell.OnEnterAsync();
        Assert.Contains("press a route button first", fx.Shell.StatusLine);
        Assert.Empty(Directory.GetFiles(fx.RouteDir));
    }

    [Fact]
    public void NewArrivalsJoinTheRunningQueueTail()
    {
        using var fx = Started("20240115--111111.pdf");
        Assert.Equal(1, fx.Shell.Session.Total);
        fx.AddInboxFile("20240117--333333.pdf");
        fx.Shell.OnFolderActivity();
        Assert.Equal(2, fx.Shell.Session.Total);
        Assert.Contains("added to this session", fx.Shell.StatusLine);
    }

    [Fact]
    public void StopReturnsToReadyWithNothingLost()
    {
        using var fx = Started("20240115--111111.pdf", "20240116--222222.pdf");
        fx.Shell.StopSession();
        Assert.Equal(Screen.Ready, fx.Shell.Screen);
        Assert.Equal(2, Directory.GetFiles(fx.Inbox).Length);
        Assert.Equal("2 files ready", fx.Shell.CountLine);
    }

    [Fact]
    public async Task DisabledRouteNeverCommits()
    {
        var fx = new ShellFixture(cfg =>
            cfg.Routes.Add(new Route { Label = "Broken", Path = Path.Combine(cfg.Inbox, "missing-dir") }));
        using var _ = fx;
        fx.AddInboxFile("20240115--111111.pdf");
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();

        Assert.False(fx.Shell.Routes[1].Enabled);
        Assert.NotNull(fx.Shell.Routes[1].DisabledReason);
        await fx.Shell.OnRouteAsync(1);
        Assert.True(File.Exists(Path.Combine(fx.Inbox, "20240115--111111.pdf")));   // untouched
    }

    [Fact]
    public void SuggestionsComeFromSeedsRankedAndPrefixFiltered()
    {
        var fx = new ShellFixture();
        using var _ = fx;
        fx.WriteNamesFile("SMITH JOHN", "SANDERS PAT", "JONES AMY");
        fx.AddInboxFile("20240115--111111.pdf");
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();

        fx.Shell.TypedName = "S";
        Assert.Contains("SMITH JOHN", fx.Shell.Suggestions);
        Assert.Contains("SANDERS PAT", fx.Shell.Suggestions);
        Assert.DoesNotContain("JONES AMY", fx.Shell.Suggestions);
        Assert.True(fx.Shell.HasSuggestions);
    }

    [Fact]
    public void TabCompletesOneWordAtATime()
    {
        var fx = new ShellFixture();
        using var _ = fx;
        fx.WriteNamesFile("SMITH JOHN");
        fx.AddInboxFile("20240115--111111.pdf");
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();

        fx.Shell.TypedName = "SM";
        Assert.True(fx.Shell.CompleteNextWord());
        Assert.Equal("SMITH", fx.Shell.TypedName);
        Assert.True(fx.Shell.CompleteNextWord());
        Assert.Equal("SMITH JOHN", fx.Shell.TypedName);

        fx.Shell.DropLastWord();
        Assert.Equal("SMITH", fx.Shell.TypedName);
    }

    [Fact]
    public void TabCompletesWordAtATimeWithAWordSeparatorToo()
    {
        // with word_separator "-", history names look like SMITH-JOHN and a
        // space-splitting completer would swallow the whole name in one Tab
        var fx = new ShellFixture(cfg => cfg.WordSeparator = "-");
        using var _ = fx;
        fx.WriteNamesFile("SMITH JOHN MICHAEL");   // seeds may still use spaces
        fx.AddInboxFile("20240115--111111.pdf");
        fx.Shell.Initialize();
        fx.Shell.StartProcessing();

        fx.Shell.TypedName = "SM";
        Assert.Contains("SMITH-JOHN-MICHAEL", fx.Shell.Suggestions);   // polished

        Assert.True(fx.Shell.CompleteNextWord());
        Assert.Equal("SMITH", fx.Shell.TypedName);
        Assert.True(fx.Shell.CompleteNextWord());
        Assert.Equal("SMITH-JOHN", fx.Shell.TypedName);
        Assert.True(fx.Shell.CompleteNextWord());
        Assert.Equal("SMITH-JOHN-MICHAEL", fx.Shell.TypedName);
        Assert.False(fx.Shell.CompleteNextWord());   // nothing left to add

        fx.Shell.DropLastWord();
        Assert.Equal("SMITH-JOHN", fx.Shell.TypedName);
        fx.Shell.DropLastWord();
        Assert.Equal("SMITH", fx.Shell.TypedName);
        fx.Shell.DropLastWord();
        Assert.Equal("", fx.Shell.TypedName);
    }

    [Fact]
    public async Task CommittedNamesBecomeSuggestions()
    {
        using var fx = Started("20240115--111111.pdf", "20240116--222222.pdf");
        fx.Shell.TypedName = "WALKER SUE";
        await fx.Shell.OnRouteAsync(0);

        fx.Shell.TypedName = "WAL";
        Assert.Contains("WALKER SUE", fx.Shell.Suggestions);
    }
}
