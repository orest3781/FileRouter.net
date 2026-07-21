using System.Security.Cryptography;
using System.Text;

namespace FileRouter.Wpf.Services;

/// <summary>Saved Unlock passwords, DPAPI-protected per Windows user. The
/// Python original stored these in plaintext; legacy plaintext values still
/// read fine and are re-protected the next time Settings saves.</summary>
public static class PasswordVault
{
    private const string Prefix = "dpapi:";

    public static bool IsProtected(string stored) =>
        stored.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Protect(string plain) =>
        Prefix + Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    /// <summary>Plaintext passes through (legacy configs); a protected value
    /// that can't be decrypted (different user/machine) becomes "".</summary>
    public static string Reveal(string stored)
    {
        if (!IsProtected(stored)) return stored;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                Convert.FromBase64String(stored[Prefix.Length..]), null,
                DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return "";
        }
    }
}
