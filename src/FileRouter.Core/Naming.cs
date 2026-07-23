using System.Text.RegularExpressions;

namespace FileRouter.Core;

/// <summary>
/// Filename construction. Pure functions, no filesystem access — collision
/// checks go through an injected predicate so callers decide what "exists"
/// means and tests never touch a disk.
///
/// The one mechanical rule: the result ends in exactly one ".pdf". The typed
/// name is otherwise used verbatim — no sanitization, no case folding.
/// Assembly order: name per mode -> route suffix -> collision counter -> .pdf
/// </summary>
public static partial class Naming
{
    public const string PdfExt = ".pdf";
    public const string ModeInsert = "insert";
    public const string ModeReplace = "replace";
    public static readonly string[] Modes = { ModeInsert, ModeReplace };

    // Inbox contract: any PDF with "--" in the stem (something on each side).
    // Insert mode splices the typed name at the FIRST "--"; the classic
    // YYYYMMDD--ID fax names are just one instance of the pattern.
    [GeneratedRegex(@"^.+--.+\.pdf$", RegexOptions.IgnoreCase)]
    public static partial Regex InboxRegex();

    // Characters Windows can't put in a filename, plus control chars. The
    // colon is the dangerous one: "SMITH:JOHN" is not rejected by the move —
    // Windows writes the bytes into an NTFS alternate data stream of a 0-byte
    // file "SMITH", so the commit "succeeds" while the document silently
    // vanishes. Reject the whole class up front so any illegal name fails
    // readably with the file left in place.
    [GeneratedRegex("""[<>:"/\\|?*\x00-\x1F]""")]
    private static partial Regex ReservedCharsRegex();

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public sealed record NameResult(
        string Filename,          // final name, including .pdf
        string CollisionSuffix,   // "" or " (2)", " (3)", ...  (Explorer style)
        string SuffixApplied,     // "" or the route suffix appended verbatim
        string ModeUsed);         // "insert" | "replace"

    /// <summary>Strip ONE trailing ".pdf" (case-insensitive). Nothing else.</summary>
    public static string StripPdfExt(string text) =>
        text.EndsWith(PdfExt, StringComparison.OrdinalIgnoreCase)
            ? text[..^PdfExt.Length]
            : text;

    /// <summary>True when the typed name commits without renaming
    /// (blank/whitespace, or just ".pdf", which strips to nothing).</summary>
    public static bool IsBlankName(string typedName) =>
        string.IsNullOrWhiteSpace(StripPdfExt(typedName));

    /// <summary>Route's own naming_mode wins; absent means inherit global.</summary>
    public static string ResolveMode(string? routeMode, string globalMode)
    {
        var mode = routeMode ?? globalMode;
        if (Array.IndexOf(Modes, mode) < 0)
            throw new ArgumentException($"Unknown naming mode: '{mode}'");
        return mode;
    }

    /// <summary>Filename STEM after applying the typed name per mode. A blank
    /// name preserves the original stem in both modes.</summary>
    public static string ApplyName(string originalFilename, string typedName, string mode)
    {
        if (Array.IndexOf(Modes, mode) < 0)
            throw new ArgumentException($"Unknown naming mode: '{mode}'");
        var name = StripPdfExt(typedName);
        if (string.IsNullOrWhiteSpace(name))
            return StripPdfExt(originalFilename);
        if (mode == ModeReplace)
            return name;
        // insert: the typed name replaces the FIRST "--" (ABC--123 -> ABC-NAME-123)
        var stem = StripPdfExt(originalFilename);
        var split = stem.IndexOf("--", StringComparison.Ordinal);
        if (split <= 0 || split + 2 >= stem.Length)
            throw new ArgumentException(
                $"Insert mode needs '--' in the filename, got '{originalFilename}'");
        return $"{stem[..split]}-{name}-{stem[(split + 2)..]}";
    }

    /// <summary>Throw if <paramref name="stem"/> can't be a Windows filename.
    /// Legal names — spaces, apostrophes, hyphens, unicode — pass untouched.
    /// (Trailing dots/spaces in the STEM are fine: the ".pdf" that always
    /// follows keeps them mid-name, where Windows preserves them.)</summary>
    public static void RejectIllegal(string stem)
    {
        var bad = ReservedCharsRegex().Match(stem);
        if (bad.Success)
            throw new ArgumentException(
                $"The name can't contain '{bad.Value}' — Windows forbids the " +
                "characters  < > : \" / \\ | ? *  in filenames.");
        var deviceName = stem.Split('.')[0];
        if (ReservedNames.Contains(deviceName))
            throw new ArgumentException(
                $"\"{stem}\" is a reserved Windows device name — pick another.");
    }

    /// <summary>Assemble the final target filename for a commit. The
    /// <paramref name="exists"/> predicate is called with candidate filenames
    /// (including .pdf) and returns true while a candidate is taken; the
    /// collision counter starts at " (2)" and goes after the route suffix.</summary>
    public static NameResult BuildTarget(
        string originalFilename, string typedName,
        string? routeMode, string globalMode,
        string routeSuffix, bool appendSuffix,
        Func<string, bool> exists)
    {
        var mode = ResolveMode(routeMode, globalMode);
        var stem = ApplyName(originalFilename, typedName, mode);

        var suffixApplied = "";
        if (appendSuffix && !string.IsNullOrEmpty(routeSuffix))
        {
            suffixApplied = routeSuffix;
            stem += suffixApplied;
        }

        RejectIllegal(stem);  // colon etc. -> readable error, file stays put

        var collisionSuffix = "";
        var filename = stem + PdfExt;
        var counter = 2;
        while (exists(filename))
        {
            collisionSuffix = $" ({counter})";
            filename = stem + collisionSuffix + PdfExt;
            counter++;
        }
        return new NameResult(filename, collisionSuffix, suffixApplied, mode);
    }
}
