namespace FileRouter.Core;

/// <summary>Inbox scanning: which files enter the queue, which are ignored.</summary>
public static class Scanner
{
    public sealed record ScanResult(
        IReadOnlyList<string> Matching, int IgnoredCount, string Error)
    {
        public int Count => Matching.Count;
    }

    private static long SafeSize(string p)
    {
        try { return new FileInfo(p).Length; } catch { return 0; }
    }

    private static long SafeMtime(string p)
    {
        try { return new FileInfo(p).LastWriteTimeUtc.Ticks; } catch { return 0; }
    }

    /// <summary>Snapshot the inbox. Never throws — problems come back in Error.</summary>
    public static ScanResult Scan(string inbox, string sort = "size_desc")
    {
        if (string.IsNullOrWhiteSpace(inbox))
            return new ScanResult(Array.Empty<string>(), 0, "No inbox folder is configured yet.");
        if (!Directory.Exists(inbox))
            return File.Exists(inbox)
                ? new ScanResult(Array.Empty<string>(), 0, $"Inbox path is not a folder: {inbox}")
                : new ScanResult(Array.Empty<string>(), 0, $"Inbox folder does not exist: {inbox}");

        string[] files;
        try { files = Directory.GetFiles(inbox); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ScanResult(Array.Empty<string>(), 0, $"Can't read the inbox folder: {ex.Message}");
        }

        var matching = files
            .Where(f => Naming.InboxRegex().IsMatch(System.IO.Path.GetFileName(f)))
            .ToList();
        var ignored = files.Length - matching.Count;

        matching = sort switch
        {
            "filename_desc" => matching.OrderByDescending(f => System.IO.Path.GetFileName(f).ToLowerInvariant()).ToList(),
            "mtime_asc" => matching.OrderBy(SafeMtime).ToList(),
            "mtime_desc" => matching.OrderByDescending(SafeMtime).ToList(),
            "size_asc" => matching.OrderBy(SafeSize).ThenBy(f => System.IO.Path.GetFileName(f).ToLowerInvariant()).ToList(),
            "size_desc" => matching.OrderByDescending(SafeSize).ThenBy(f => System.IO.Path.GetFileName(f).ToLowerInvariant()).ToList(),
            _ => matching.OrderBy(f => System.IO.Path.GetFileName(f).ToLowerInvariant()).ToList(),
        };
        return new ScanResult(matching, ignored, "");
    }

    /// <summary>Files (any name) sitting in a folder — the set-aside alert
    /// count. Unset/missing/unreadable folders count as 0; never throws.</summary>
    public static int CountFiles(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return 0;
        try { return Directory.GetFiles(folder).Length; } catch { return 0; }
    }
}
