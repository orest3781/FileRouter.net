using FileRouter.Core;
using FileRouter.Wpf.ViewModels;
using FileRouter.Wpf.Windows;

/// <summary>Constructs every secondary window against the real app resources
/// and forces layout — catches XAML/binding wiring errors without showing UI.</summary>
public static class DialogCheck
{
    public static int Run() => SmokeUi.RunSta(Drive,
        "DIALOGS OK — all six construct cleanly",
        "DIALOG FAIL:");

    private static List<string> Drive()
    {
        var errors = new List<string>();
        SmokeUi.Boot();
        var dialogs = new RecordingDialogs();
        var dir = Directory.CreateTempSubdirectory("fr_dialogcheck").FullName;

        void Check(string name, Func<System.Windows.Window> make)
        {
            try
            {
                var w = make();
                w.Measure(new System.Windows.Size(1000, 800));   // force template + bindings
                w.Close();
            }
            catch (Exception ex) { errors.Add($"{name}: {ex.Message}"); }
        }

        Check("Unlock", () => new UnlockWindow(new UnlockViewModel(new Config(), () => { })));
        Check("BulkRename", () => new BulkRenameWindow(new BulkRenameViewModel()));
        Check("MatchMerge", () => new MatchMergeWindow(
            new MatchMergeViewModel(new Config(), _ => { }, dialogs)));
        Check("Settings", () => new SettingsWindow(new SettingsViewModel(new Config(), dialogs)));
        Check("Triage", () => new TriageWindow(new List<MatchMerge.MatchResult>(), new[] { "A", "B" }));

        using (var history = new History(Path.Combine(dir, "history.sqlite")))
        {
            Check("History", () => new HistoryWindow(new HistoryViewModel(history, dialogs)));
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try { Directory.Delete(dir, true); } catch { /* best effort */ }
        return errors;
    }
}
