using FileRouter.Wpf.Services;

namespace FileRouter.Wpf.Tests;

public class PasswordVaultTests
{
    [Fact]
    public void ProtectRoundTrips()
    {
        var stored = PasswordVault.Protect("hunter2");
        Assert.True(PasswordVault.IsProtected(stored));
        Assert.StartsWith("dpapi:", stored);
        Assert.DoesNotContain("hunter2", stored);
        Assert.Equal("hunter2", PasswordVault.Reveal(stored));
    }

    [Fact]
    public void LegacyPlaintextPassesThrough()
    {
        Assert.False(PasswordVault.IsProtected("plain-old-password"));
        Assert.Equal("plain-old-password", PasswordVault.Reveal("plain-old-password"));
    }

    [Fact]
    public void CorruptProtectedValueRevealsEmptyNotCrash()
    {
        Assert.Equal("", PasswordVault.Reveal("dpapi:not-base64!!"));
        Assert.Equal("", PasswordVault.Reveal("dpapi:AAAA"));
    }
}
