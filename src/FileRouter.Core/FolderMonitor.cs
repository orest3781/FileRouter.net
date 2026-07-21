namespace FileRouter.Core;

/// <summary>Computes the live state of a monitored folder for the Ready
/// dashboard: how many matching files it holds and which filenames trip an
/// alert term. Pure filesystem read; never throws.</summary>
public static class FolderMonitor
{
    public sealed record FolderStatus(
        string Label, string Path, string? Color, int Count, string Error,
        IReadOnlyList<string> Matches)
    {
        /// <summary>Tiles only appear while the folder holds matching files.</summary>
        public bool HasFiles => Count > 0;
        public bool Alerting => Matches.Count > 0;
    }

    /// <summary>Extensions (no dot, lower) a folder counts; empty set = any file.</summary>
    public static HashSet<string> ParseFiletypes(string filetypes) =>
        filetypes.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.TrimStart('.').ToLowerInvariant())
            .ToHashSet();

    private static bool TypeMatches(string file, HashSet<string> types) =>
        types.Count == 0 ||
        types.Contains(System.IO.Path.GetExtension(file).TrimStart('.').ToLowerInvariant());

    /// <summary>True if <paramref name="name"/> contains any alert term
    /// (case-insensitive). Blank terms are ignored.</summary>
    public static bool IsAlerting(string name, IEnumerable<string> alertTerms) =>
        alertTerms.Any(t => !string.IsNullOrWhiteSpace(t) &&
            name.Contains(t.Trim(), StringComparison.OrdinalIgnoreCase));

    public static FolderStatus Status(WatchFolder wf, IEnumerable<string> alertTerms)
    {
        var terms = alertTerms.ToList();
        if (string.IsNullOrWhiteSpace(wf.Path))
            return new(wf.Label, wf.Path, wf.Color, 0, "no path configured", Array.Empty<string>());
        if (!Directory.Exists(wf.Path))
            return new(wf.Label, wf.Path, wf.Color, 0, $"folder not available: {wf.Path}", Array.Empty<string>());

        try
        {
            var types = ParseFiletypes(wf.Filetypes);
            var option = wf.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.EnumerateFiles(wf.Path, "*", option)
                .Where(f => TypeMatches(f, types))
                .ToList();
            var matches = files
                .Select(System.IO.Path.GetFileName)
                .Where(n => n is not null && IsAlerting(n, terms))
                .Select(n => n!)
                .ToList();
            return new(wf.Label, wf.Path, wf.Color, files.Count, "", matches);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(wf.Label, wf.Path, wf.Color, 0, $"can't read folder: {ex.Message}", Array.Empty<string>());
        }
    }

    /// <summary>Status for every configured watch folder, in order.</summary>
    public static List<FolderStatus> All(IEnumerable<WatchFolder> folders, IEnumerable<string> alertTerms)
    {
        var terms = alertTerms.ToList();
        return folders.Select(f => Status(f, terms)).ToList();
    }
}
