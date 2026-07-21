using FileRouter.Core;
using FileRouter.Wpf.Services;
using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf.Tests;

/// <summary>A complete headless shell over real temp folders, a real Session,
/// and a real SQLite history — only the viewer and dialogs are fakes. This is
/// the app the WinForms version could only exercise through the smoke tool.</summary>
public sealed class ShellFixture : IDisposable
{
    public string Dir { get; }
    public string Inbox { get; }
    public string Deferred { get; }
    public string RouteDir { get; }
    public Config Cfg { get; }
    public string CfgPath { get; }
    public FakeViewer Viewer { get; } = new();
    public FakeDialogs Dialogs { get; } = new();
    public FolderWatchService Watch { get; }
    public ShellViewModel Shell { get; }

    public ShellFixture(Action<Config>? tweak = null)
    {
        Dir = Path.Combine(Path.GetTempPath(), "frshell_" + Guid.NewGuid());
        Inbox = Path.Combine(Dir, "inbox");
        Deferred = Path.Combine(Dir, "deferred");
        RouteDir = Path.Combine(Dir, "routed");
        Directory.CreateDirectory(Inbox);
        Directory.CreateDirectory(Deferred);
        Directory.CreateDirectory(RouteDir);
        CfgPath = Path.Combine(Dir, "config.json");
        Cfg = new Config
        {
            Inbox = Inbox,
            Deferred = Deferred,
            Sort = "filename_asc",
            Routes = { new Route { Label = "Filed", Path = RouteDir, Color = "#2e7d32" } },
        };
        tweak?.Invoke(Cfg);
        // huge intervals: tests drive OnFolderActivity directly, deterministically
        Watch = new FolderWatchService(debounceMs: 600_000, pollMs: 600_000);
        Shell = new ShellViewModel(Cfg, CfgPath, Viewer, Dialogs, Watch,
            uiContext: null, palette: () => Theme.ThemePalette.Light);
    }

    /// <summary>Drop a file matching the inbox pattern (YYYYMMDD--ID.pdf).</summary>
    public string AddInboxFile(string name = "", string content = "pdf")
    {
        if (name.Length == 0) name = $"20240115--{Random.Shared.Next(100000, 999999)}.pdf";
        var path = Path.Combine(Inbox, name);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        Shell.Dispose();
        Watch.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var i = 0; i < 10; i++)
        {
            try { Directory.Delete(Dir, true); return; } catch { Thread.Sleep(50); }
        }
    }
}
