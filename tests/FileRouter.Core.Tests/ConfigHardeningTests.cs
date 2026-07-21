using FileRouter.Core;

namespace FileRouter.Core.Tests;

/// <summary>A read-only or locked config file must never crash the app —
/// load failures become readable ConfigExceptions, save failures a bool.</summary>
public class ConfigHardeningTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "frcfg_" + Guid.NewGuid());

    public ConfigHardeningTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void LoadWrapsIoErrorsAsConfigException()
    {
        var path = Path.Combine(_dir, "locked.json");
        File.WriteAllText(path, "{}");
        using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var ex = Assert.Throws<ConfigException>(() => Config.Load(path));
        Assert.Contains("locked.json", ex.Message);
    }

    [Fact]
    public void TrySaveReportsFailureInsteadOfThrowing()
    {
        var dest = Path.Combine(_dir, "sub", "nope", "config.json");   // missing dirs
        var ok = Config.TrySave(new Config(), dest, out var error);
        Assert.False(ok);
        Assert.NotEqual("", error);
    }

    [Fact]
    public void TrySaveSucceedsNormally()
    {
        var dest = Path.Combine(_dir, "config.json");
        Assert.True(Config.TrySave(new Config(), dest, out var error));
        Assert.Equal("", error);
        Assert.True(File.Exists(dest));
    }
}
