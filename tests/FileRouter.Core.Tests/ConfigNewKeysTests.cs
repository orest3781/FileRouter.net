using FileRouter.Core;

namespace FileRouter.Core.Tests;

/// <summary>The Python-parity keys: exact JSON names, defaults, validation.</summary>
public class ConfigNewKeysTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "frnk_" + Guid.NewGuid());

    public ConfigNewKeysTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private Config RoundTrip(Config cfg)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".json");
        Config.Save(cfg, path);
        return Config.Load(path);
    }

    private Config LoadJson(string json)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".json");
        File.WriteAllText(path, json);
        return Config.Load(path);
    }

    [Fact]
    public void NewKeysRoundTripWithExactPythonNames()
    {
        var cfg = new Config
        {
            UiFontFamily = "Verdana",
            UiFontSize = 13,
            WordSeparator = "-",
            UnlockSuffix = "_unlocked",
            SavedPasswords = { new SavedPassword { Label = "Payer A", Password = "dpapi:abc" } },
        };
        var path = Path.Combine(_dir, "t.json");
        Config.Save(cfg, path);
        var json = File.ReadAllText(path);
        Assert.Contains("\"ui_font_family\"", json);
        Assert.Contains("\"ui_font_size\"", json);
        Assert.Contains("\"word_separator\"", json);
        Assert.Contains("\"unlock_suffix\"", json);
        Assert.Contains("\"saved_passwords\"", json);

        var back = Config.Load(path);
        Assert.Equal("Verdana", back.UiFontFamily);
        Assert.Equal(13, back.UiFontSize);
        Assert.Equal("-", back.WordSeparator);
        Assert.Equal("_unlocked", back.UnlockSuffix);
        var pw = Assert.Single(back.SavedPasswords);
        Assert.Equal("Payer A", pw.Label);
        Assert.Equal("dpapi:abc", pw.Password);
    }

    [Fact]
    public void DefaultsAreBenign()
    {
        var cfg = RoundTrip(new Config());
        Assert.Equal("", cfg.UiFontFamily);
        Assert.Equal(0, cfg.UiFontSize);
        Assert.Equal("", cfg.WordSeparator);
        Assert.Equal("", cfg.UnlockSuffix);
        Assert.Empty(cfg.SavedPasswords);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(73)]
    [InlineData(-1)]
    public void FontSizeOutsideRangeIsRejected(int size)
    {
        var ex = Assert.Throws<ConfigException>(() =>
            LoadJson($"{{ \"ui_font_size\": {size} }}"));
        Assert.Contains("ui_font_size", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(72)]
    public void FontSizeZeroOrInRangeIsAccepted(int size) =>
        Assert.Equal(size, LoadJson($"{{ \"ui_font_size\": {size} }}").UiFontSize);

    [Fact]
    public void SeparatorContainingASpaceIsRejected()
    {
        // a space in the separator would re-trigger substitution forever
        var ex = Assert.Throws<ConfigException>(() =>
            LoadJson("{ \"word_separator\": \" - \" }"));
        Assert.Contains("word_separator", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("_")]
    public void SaneSeparatorsAreAccepted(string sep) =>
        Assert.Equal(sep, LoadJson($"{{ \"word_separator\": \"{sep}\" }}").WordSeparator);

    [Theory]
    [InlineData("auto")]
    [InlineData("light")]
    [InlineData("dark")]
    public void ThemeAcceptsTheThreeModes(string mode) =>
        Assert.Equal(mode, LoadJson($"{{ \"theme\": \"{mode}\" }}").Theme);

    [Fact]
    public void ThemeDefaultsToAutoAndRejectsGarbage()
    {
        Assert.Equal("auto", RoundTrip(new Config()).Theme);
        var ex = Assert.Throws<ConfigException>(() => LoadJson("{ \"theme\": \"blue\" }"));
        Assert.Contains("theme", ex.Message);
    }
}
