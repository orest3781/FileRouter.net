using System.Text.Json;
using FileRouter.Core;
using FileRouter.Wpf.Services;
using FileRouter.Wpf.ViewModels;

namespace FileRouter.Wpf.Tests;

public class SettingsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "frset_" + Guid.NewGuid());
    private readonly FakeDialogs _dialogs = new();

    public SettingsViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private Config LoadFromJson(string json)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".json");
        File.WriteAllText(path, json);
        return Config.Load(path);
    }

    [Fact]
    public void UnknownKeysAndToolStateSurviveOkByConstruction()
    {
        // the Python settings dialog wiped anything it didn't carry by hand —
        // the clone-then-patch build makes that class of bug impossible
        var cfg = LoadFromJson("""
            {
              "inbox": "c:/faxes",
              "custom_top_level_key": {"kept": true},
              "merge_headers": {"first": "FirstName"},
              "saved_passwords": [{"label": "Payer", "password": "dpapi:abc"}],
              "routes": [{"label": "Invoices", "path": "c:/inv", "custom_route_key": 7}]
            }
            """);
        var vm = new SettingsViewModel(cfg, _dialogs);
        vm.Inbox = "c:/faxes-new";
        _dialogs.ConfirmAnswer = true;   // path warnings -> save anyway

        Assert.True(vm.TryBuildResult());
        var result = vm.Result!;
        Assert.Equal("c:/faxes-new", result.Inbox);
        Assert.True(result.Extras.ContainsKey("custom_top_level_key"));
        Assert.Equal("FirstName", result.MergeHeaders["first"]);
        Assert.Equal("dpapi:abc", Assert.Single(result.SavedPasswords).Password);
        Assert.True(Assert.Single(result.Routes).Extras.ContainsKey("custom_route_key"));

        // and the original object was never mutated
        Assert.Equal("c:/faxes", cfg.Inbox);
    }

    [Fact]
    public void DuplicateEffectiveHotkeysBlockOk()
    {
        var cfg = new Config
        {
            Routes =
            {
                new Route { Label = "A", Path = _dir, Hotkey = "Ctrl+3" },
                new Route { Label = "B", Path = _dir, Hotkey = "ctrl+3" },
            },
        };
        var vm = new SettingsViewModel(cfg, _dialogs);
        Assert.False(vm.TryBuildResult());
        Assert.Contains("Ctrl+3", Assert.Single(_dialogs.Warnings).Message);
    }

    [Fact]
    public void FallbackHotkeyCollisionsAreCaughtToo()
    {
        // route 0's fallback is Ctrl+1; route 1 explicitly claims Ctrl+1
        var cfg = new Config
        {
            Routes =
            {
                new Route { Label = "A", Path = _dir },
                new Route { Label = "B", Path = _dir, Hotkey = "Ctrl+1" },
            },
        };
        var vm = new SettingsViewModel(cfg, _dialogs);
        Assert.False(vm.TryBuildResult());
    }

    [Fact]
    public void DuplicateLabelsUnparseableHotkeyAndBadColorBlockOk()
    {
        var cfg = new Config
        {
            Routes =
            {
                new Route { Label = "Same", Path = _dir, Hotkey = "NotAKey+X", Color = "nope" },
                new Route { Label = "same", Path = _dir, Hotkey = "Ctrl+2" },
            },
        };
        var vm = new SettingsViewModel(cfg, _dialogs);
        Assert.False(vm.TryBuildResult());
        var msg = Assert.Single(_dialogs.Warnings).Message;
        Assert.Contains("both called", msg);
        Assert.Contains("hotkey", msg);
        Assert.Contains("not a color", msg);
    }

    [Fact]
    public void BadFontSizeBlocksOk()
    {
        var vm = new SettingsViewModel(new Config(), _dialogs) { UiFontSizeText = "5" };
        Assert.False(vm.TryBuildResult());
        Assert.Contains("6 to 72", Assert.Single(_dialogs.Warnings).Message);
    }

    [Fact]
    public void SeparatorWithSpaceBlocksOk()
    {
        var vm = new SettingsViewModel(new Config(), _dialogs) { WordSeparator = " - " };
        Assert.False(vm.TryBuildResult());
    }

    [Fact]
    public void UnreachableRouteIsAWarningNotAnError()
    {
        var cfg = new Config
        {
            Inbox = _dir,
            Routes = { new Route { Label = "A", Path = Path.Combine(_dir, "missing") } },
        };
        var vm = new SettingsViewModel(cfg, _dialogs);

        _dialogs.ConfirmAnswer = false;   // decline "Save anyway?"
        Assert.False(vm.TryBuildResult());

        _dialogs.ConfirmAnswer = true;
        Assert.True(vm.TryBuildResult());
        Assert.NotNull(vm.Result);
    }

    [Fact]
    public void PlaintextPasswordsGetProtectedOnSave()
    {
        var cfg = new Config
        {
            Inbox = _dir,
            SavedPasswords = { new SavedPassword { Label = "Old", Password = "plain" } },
        };
        var vm = new SettingsViewModel(cfg, _dialogs);
        Assert.True(vm.TryBuildResult());
        var saved = Assert.Single(vm.Result!.SavedPasswords);
        Assert.True(PasswordVault.IsProtected(saved.Password));
        Assert.Equal("plain", PasswordVault.Reveal(saved.Password));
    }

    [Fact]
    public void AddPasswordStoresProtected()
    {
        var vm = new SettingsViewModel(new Config { Inbox = _dir }, _dialogs);
        vm.AddPassword("Payer B", "s3cret");
        Assert.True(vm.TryBuildResult());
        var saved = Assert.Single(vm.Result!.SavedPasswords);
        Assert.Equal("Payer B", saved.Label);
        Assert.Equal("s3cret", PasswordVault.Reveal(saved.Password));
    }

    [Fact]
    public void FilingExampleTracksModeCaseAndSeparator()
    {
        var vm = new SettingsViewModel(new Config { Inbox = _dir }, _dialogs);
        Assert.Contains("20240115-SMITH JOHN-12345.pdf", vm.FilingExample);

        vm.WordSeparator = "-";
        Assert.Contains("20240115-SMITH-JOHN-12345.pdf", vm.FilingExample);

        vm.InsertMode = false;
        Assert.Contains("SMITH-JOHN.pdf", vm.FilingExample);

        vm.UppercaseNames = false;
        Assert.Contains("Smith-John.pdf", vm.FilingExample);
    }

    [Fact]
    public void FilingExampleWarnsOnAnIllegalSeparatorInsteadOfThrowing()
    {
        var vm = new SettingsViewModel(new Config { Inbox = _dir }, _dialogs)
        { WordSeparator = ":" };
        Assert.StartsWith("⚠", vm.FilingExample);
    }

    [Fact]
    public void DuplicateHotkeyGetsALiveNote()
    {
        var cfg = new Config
        {
            Routes =
            {
                new Route { Label = "Invoices", Path = _dir },          // fallback Ctrl+1
                new Route { Label = "Statements", Path = _dir, Hotkey = "Ctrl+2" },
            },
        };
        var vm = new SettingsViewModel(cfg, _dialogs);
        Assert.Equal("", vm.Routes[1].HotkeyNote);

        vm.Routes[1].Hotkey = "Ctrl+1";   // now collides with route 0's fallback
        Assert.Contains("already used by \"Invoices\"", vm.Routes[1].HotkeyNote);
        Assert.True(vm.Routes[1].HasHotkeyNote);

        vm.Routes[1].Hotkey = "Ctrl+2";
        Assert.Equal("", vm.Routes[1].HotkeyNote);
    }

    [Fact]
    public void RoutePreviewMatchesTheProcessingButtonComposition()
    {
        var cfg = new Config
        {
            Routes =
            {
                new Route
                {
                    Label = "Invoices", Path = _dir, Color = "#2e7d32",
                    Suffix = "_INVOICE", AppendSuffix = true,
                },
            },
        };
        var vm = new SettingsViewModel(cfg, _dialogs);
        var r = vm.Routes[0];
        Assert.Equal("Invoices   ·   _INVOICE   ·   Ctrl+1", r.PreviewLabel);
        Assert.Equal(new FileRouter.Wpf.Theme.Rgb(46, 125, 50), r.PreviewBack);
        Assert.True(FileRouter.Wpf.Theme.ThemePalette.ContrastRatio(
            r.PreviewFore, r.PreviewBack) >= 4.5);

        r.AppendSuffix = false;   // live: suffix drops out of the preview
        Assert.Equal("Invoices   ·   Ctrl+1", r.PreviewLabel);
    }

    [Fact]
    public void HistoryDbBrowseUsesTheOpenStylePicker()
    {
        var vm = new SettingsViewModel(new Config { Inbox = _dir }, _dialogs);
        _dialogs.NextFilePath = Path.Combine(_dir, "audit.sqlite");
        vm.BrowseHistoryDbCommand.Execute(null);
        Assert.Equal(Path.Combine(_dir, "audit.sqlite"), vm.HistoryDb);

        _dialogs.NextFilePath = null;   // cancel keeps the old value
        vm.BrowseHistoryDbCommand.Execute(null);
        Assert.Equal(Path.Combine(_dir, "audit.sqlite"), vm.HistoryDb);
    }

    [Fact]
    public void PathNotesSurfaceProblemsLive()
    {
        var vm = new SettingsViewModel(new Config(), _dialogs);
        Assert.Contains("no inbox folder set", vm.InboxNote);

        vm.Inbox = Path.Combine(_dir, "missing");
        Assert.Contains("doesn't exist", vm.InboxNote);

        vm.Inbox = _dir;
        Assert.Equal("", vm.InboxNote);

        vm.HistoryDb = "history.sqlite";
        Assert.Contains("relative", vm.HistoryDbNote);
        vm.HistoryDb = Path.Combine(_dir, "new-audit.sqlite");
        Assert.Contains("new database will be created", vm.HistoryDbNote);
    }

    [Fact]
    public void WatchFoldersReorderWithTheCommands()
    {
        var cfg = new Config
        {
            Inbox = _dir,
            WatchFolders =
            {
                new WatchFolder { Label = "A", Path = _dir },
                new WatchFolder { Label = "B", Path = _dir },
            },
        };
        var vm = new SettingsViewModel(cfg, _dialogs);
        vm.SelectedWatch = vm.WatchFolders[1];
        Assert.False(vm.WatchDownCommand.CanExecute(null));
        Assert.True(vm.WatchUpCommand.CanExecute(null));

        vm.WatchUpCommand.Execute(null);
        Assert.Equal("B", vm.WatchFolders[0].Label);
        Assert.True(vm.TryBuildResult());
        Assert.Equal(new[] { "B", "A" }, vm.Result!.WatchFolders.Select(w => w.Label));
    }

    [Fact]
    public void AddPasswordRefusesBlankFields()
    {
        var vm = new SettingsViewModel(new Config { Inbox = _dir }, _dialogs);
        Assert.False(vm.AddPassword("", "secret"));
        Assert.False(vm.AddPassword("Payer", ""));
        Assert.Empty(vm.Passwords);
        Assert.True(vm.AddPassword("Payer", "secret"));
        Assert.Single(vm.Passwords);
    }

    [Fact]
    public void WatchFolderProblemNotesSurfaceLive()
    {
        var vm = new SettingsViewModel(new Config
        {
            Inbox = _dir,
            WatchFolders = { new WatchFolder { Label = "W", Path = Path.Combine(_dir, "missing") } },
        }, _dialogs);
        Assert.Contains("doesn't exist", vm.WatchFolders[0].Problem);

        vm.WatchFolders[0].Path = _dir;
        Assert.Equal("", vm.WatchFolders[0].Problem);
        vm.WatchFolders[0].Path = "";
        Assert.Contains("no folder", vm.WatchFolders[0].Problem);
    }

    [Fact]
    public void CreateWatchFolderMakesTheDirectoryAndClearsTheNote()
    {
        var missing = Path.Combine(_dir, "new", "deep");
        var vm = new SettingsViewModel(new Config
        {
            Inbox = _dir,
            WatchFolders = { new WatchFolder { Label = "W", Path = missing } },
        }, _dialogs);
        vm.SelectedWatch = vm.WatchFolders[0];
        Assert.Contains("doesn't exist", vm.SelectedWatch.Problem);

        vm.CreateWatchFolderCommand.Execute(null);
        Assert.True(Directory.Exists(missing));
        Assert.Equal("", vm.SelectedWatch.Problem);
        Assert.Empty(_dialogs.Warnings);
    }

    [Fact]
    public void OpenFolderOnAMissingPathWarnsInsteadOfThrowing()
    {
        var vm = new SettingsViewModel(new Config
        {
            Inbox = _dir,
            Routes = { new Route { Label = "A", Path = Path.Combine(_dir, "nope") } },
        }, _dialogs);
        vm.SelectedRoute = vm.Routes[0];
        vm.OpenRouteFolderCommand.Execute(null);
        Assert.Single(_dialogs.Warnings);
    }

    [Fact]
    public void DuplicateRouteCopiesEverythingButTheHotkey()
    {
        var cfg = new Config
        {
            Inbox = _dir,
            Routes =
            {
                new Route
                {
                    Label = "Invoices", Path = _dir, Hotkey = "Ctrl+5",
                    Suffix = "_INV", AppendSuffix = true, Color = "#2e7d32",
                    NamingMode = "replace",
                },
                new Route { Label = "Other", Path = _dir },
            },
        };
        var vm = new SettingsViewModel(cfg, _dialogs);
        vm.SelectedRoute = vm.Routes[0];
        vm.DuplicateRouteCommand.Execute(null);

        Assert.Equal(3, vm.Routes.Count);
        var copy = vm.Routes[1];               // inserted right after the original
        Assert.Same(copy, vm.SelectedRoute);   // and selected for tweaking
        Assert.Equal("Invoices copy", copy.Label);
        Assert.Equal(_dir, copy.Path);
        Assert.Equal("_INV", copy.Suffix);
        Assert.True(copy.AppendSuffix);
        Assert.Equal("#2e7d32", copy.Color);
        Assert.Equal("replace", copy.NamingMode);
        Assert.Equal("", copy.Hotkey);         // a copied hotkey would collide
        Assert.Equal("Ctrl+2", copy.GestureText);   // fallback for its slot
    }

    [Fact]
    public void GestureTextTracksReordering()
    {
        var cfg = new Config
        {
            Inbox = _dir,
            Routes =
            {
                new Route { Label = "A", Path = _dir },
                new Route { Label = "B", Path = _dir },
            },
        };
        var vm = new SettingsViewModel(cfg, _dialogs);
        Assert.Equal("Ctrl+1", vm.Routes[0].GestureText);
        Assert.Equal("Ctrl+2", vm.Routes[1].GestureText);

        vm.Routes.Move(1, 0);   // same path drag-drop reordering uses
        Assert.Equal("B", vm.Routes[0].Label);
        Assert.Equal("Ctrl+1", vm.Routes[0].GestureText);
        Assert.Equal("Ctrl+2", vm.Routes[1].GestureText);
    }

    [Fact]
    public void TilePreviewShowsTheRealFolderState()
    {
        var watched = Path.Combine(_dir, "watched");
        Directory.CreateDirectory(watched);
        File.WriteAllText(Path.Combine(watched, "a.pdf"), "x");
        File.WriteAllText(Path.Combine(watched, "URGENT-fax.pdf"), "x");

        var vm = new SettingsViewModel(new Config
        {
            Inbox = _dir,
            AlertTexts = { "URGENT" },
            WatchFolders =
            {
                new WatchFolder { Label = "Failed", Path = watched, Color = "#1565c0" },
            },
        }, _dialogs);
        vm.SelectedWatch = vm.WatchFolders[0];

        Assert.True(vm.TilePreviewVisible);
        Assert.Equal("Failed", vm.TilePreviewLabel);
        Assert.Equal("2 ⚠", vm.TilePreviewCount);
        Assert.Equal(FileRouter.Wpf.Theme.ThemePalette.Light.Danger, vm.TilePreviewBack);
        Assert.Contains("alerting right now", vm.TilePreviewHint);

        // clearing the alert terms live drops the alert state and the color
        vm.AlertTextsText = "";
        Assert.Equal("2", vm.TilePreviewCount);
        Assert.Equal(new FileRouter.Wpf.Theme.Rgb(21, 101, 192), vm.TilePreviewBack);
        Assert.Equal("", vm.TilePreviewHint);
    }

    [Fact]
    public void TilePreviewExplainsEmptyAndMissingFolders()
    {
        var vm = new SettingsViewModel(new Config
        {
            Inbox = _dir,
            WatchFolders = { new WatchFolder { Label = "W", Path = Path.Combine(_dir, "gone") } },
        }, _dialogs);
        vm.SelectedWatch = vm.WatchFolders[0];
        Assert.Equal("⚠", vm.TilePreviewCount);
        Assert.Contains("not available", vm.TilePreviewHint);

        vm.SelectedWatch.Path = _dir;   // exists, empty of matching files? _dir has dirs only
        Assert.Contains("only appears", vm.TilePreviewHint);

        vm.SelectedWatch = null;
        Assert.False(vm.TilePreviewVisible);
    }

    [Fact]
    public void ThemeModeRoundTripsThroughTheRadiosIntoTheResult()
    {
        var vm = new SettingsViewModel(new Config { Inbox = _dir }, _dialogs);
        Assert.True(vm.ThemeAuto);

        vm.ThemeDark = true;
        Assert.True(vm.ThemeDark);
        Assert.False(vm.ThemeAuto);
        Assert.True(vm.TryBuildResult());
        Assert.Equal("dark", vm.Result!.Theme);

        var vm2 = new SettingsViewModel(vm.Result, _dialogs);
        Assert.True(vm2.ThemeDark);
    }

    [Fact]
    public void AlertTermsParseFromLinesAndCommas()
    {
        var vm = new SettingsViewModel(new Config { Inbox = _dir }, _dialogs)
        {
            AlertTextsText = "URGENT\nSTAT, callback\n\n",
        };
        Assert.True(vm.TryBuildResult());
        Assert.Equal(new[] { "URGENT", "STAT", "callback" }, vm.Result!.AlertTexts);
    }

    [Fact]
    public void ResultSurvivesAConfigRoundTripOnDisk()
    {
        var vm = new SettingsViewModel(new Config { Inbox = _dir }, _dialogs)
        {
            UiFontFamily = "Verdana",
            UiFontSizeText = "16",
            WordSeparator = "-",
        };
        Assert.True(vm.TryBuildResult());
        var path = Path.Combine(_dir, "saved.json");
        Config.Save(vm.Result!, path);
        var back = Config.Load(path);
        Assert.Equal("Verdana", back.UiFontFamily);
        Assert.Equal(16, back.UiFontSize);
        Assert.Equal("-", back.WordSeparator);
    }
}

public class ApplySettingsTests
{
    [Fact]
    public void ChangedDbPathReopensHistoryWithFreshBackupDir()
    {
        using var fx = new ShellFixture();
        fx.Shell.Initialize();
        var newDbDir = Path.Combine(fx.Dir, "elsewhere");
        Directory.CreateDirectory(newDbDir);
        var newDb = Path.Combine(newDbDir, "audit.sqlite");

        var clone = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(fx.Cfg))!;
        clone.HistoryDb = newDb;
        fx.Shell.ApplySettings(clone);

        Assert.Equal(newDb, fx.Shell.History.Path);
        Assert.True(File.Exists(newDb));
        Assert.Equal(FileRouter.Wpf.ViewModels.Screen.Ready, fx.Shell.Screen);
    }

    [Fact]
    public void WordSeparatorTakesEffectImmediately()
    {
        using var fx = new ShellFixture();
        fx.AddInboxFile("20240115--111111.pdf");
        fx.Shell.Initialize();

        var clone = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(fx.Cfg))!;
        clone.WordSeparator = "-";
        fx.Shell.ApplySettings(clone);

        fx.Shell.StartProcessing();
        fx.Shell.TypedName = "SMITH JOHN";
        Assert.Equal("SMITH-JOHN", fx.Shell.TypedName);
    }
}
