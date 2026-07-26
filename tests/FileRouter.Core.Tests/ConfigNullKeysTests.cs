using FileRouter.Core;

namespace FileRouter.Core.Tests;

/// <summary>Hand-edited configs are a supported workflow, so an explicit JSON
/// null must behave exactly like an absent key — never a NullReferenceException
/// at load, and never a null field that crashes on the first keystroke.</summary>
public class ConfigNullKeysTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "frnull_" + Guid.NewGuid());

    public ConfigNullKeysTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private Config LoadJson(string json)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".json");
        File.WriteAllText(path, json);
        return Config.Load(path);
    }

    [Fact]
    public void NullCollectionsLoadAsEmptyInsteadOfNull()
    {
        var cfg = LoadJson("""
            {
              "routes": null, "watch_folders": null, "alert_texts": null,
              "saved_passwords": null, "label_clients": null, "merge_headers": null
            }
            """);
        Assert.Empty(cfg.Routes);
        Assert.Empty(cfg.WatchFolders);
        Assert.Empty(cfg.AlertTexts);
        Assert.Empty(cfg.SavedPasswords);
        Assert.Empty(cfg.LabelClients);
        Assert.Empty(cfg.MergeHeaders);
    }

    [Fact]
    public void NullSoundsBlockLoadsAsDefaults()
    {
        var cfg = LoadJson("{ \"sounds\": null }");
        Assert.True(cfg.Sounds.Enabled);
        Assert.Equal("", cfg.Sounds.NewAlert);
        Assert.Equal("none", cfg.Sounds.Filed);
    }

    [Fact]
    public void NullWordSeparatorLoadsAsBlankNotACrash()
    {
        // regression: this threw NullReferenceException out of Config.Load,
        // escaping the ConfigException handler that shows a readable dialog
        Assert.Equal("", LoadJson("{ \"word_separator\": null }").WordSeparator);
    }

    [Fact]
    public void NullStringsFallBackToTheirDeclaredDefaults()
    {
        var cfg = LoadJson("""
            {
              "inbox": null, "deferred": null, "names_file": null,
              "history_db": null, "naming_mode": null, "sort": null,
              "theme": null, "monitor_title": null, "tile_visibility": null,
              "ui_font_family": null, "unlock_suffix": null
            }
            """);
        Assert.Equal("", cfg.Inbox);
        Assert.Equal("", cfg.Deferred);
        Assert.Equal("names.txt", cfg.NamesFile);
        Assert.Equal("history.sqlite", cfg.HistoryDb);
        Assert.Equal("insert", cfg.NamingMode);
        Assert.Equal("size_desc", cfg.Sort);
        Assert.Equal("auto", cfg.Theme);
        Assert.Equal("Monitored folders", cfg.MonitorTitle);
        Assert.Equal("active", cfg.TileVisibility);
        Assert.Equal("", cfg.UiFontFamily);
        Assert.Equal("", cfg.UnlockSuffix);
    }

    [Fact]
    public void NullRouteAndWatchFolderFieldsLoadAsBlanks()
    {
        var cfg = LoadJson("""
            {
              "routes": [ { "label": null, "path": null, "hotkey": null, "suffix": null } ],
              "watch_folders": [ { "label": null, "path": null, "filetypes": null } ],
              "saved_passwords": [ { "label": null, "password": null } ]
            }
            """);
        var route = Assert.Single(cfg.Routes);
        Assert.Equal("", route.Label);
        Assert.Equal("", route.Path);
        Assert.Equal("", route.Hotkey);
        Assert.Equal("", route.Suffix);
        var wf = Assert.Single(cfg.WatchFolders);
        Assert.Equal("", wf.Label);
        Assert.Equal("", wf.Path);
        Assert.Equal("", wf.Filetypes);
        var pw = Assert.Single(cfg.SavedPasswords);
        Assert.Equal("", pw.Label);
        Assert.Equal("", pw.Password);
    }

    [Fact]
    public void ANullRouteEntryIsDroppedRatherThanKept()
    {
        // "routes": [null] would otherwise put a null into a List<Route>
        Assert.Empty(LoadJson("{ \"routes\": [null], \"watch_folders\": [null] }").Routes);
    }

    [Fact]
    public void AnOutOfRangeValueStillReportsReadably()
    {
        // normalizing nulls must not weaken real validation
        Assert.Throws<ConfigException>(() => LoadJson("{ \"naming_mode\": \"sideways\" }"));
        Assert.Throws<ConfigException>(() => LoadJson("{ \"poll_seconds\": 2 }"));
        Assert.Throws<ConfigException>(() => LoadJson("{ \"theme\": \"blue\" }"));
    }
}
