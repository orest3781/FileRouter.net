namespace FileRouter.Core;

/// <summary>Name suggestions for the filing loop's name box: history names
/// (recency, then frequency) first, then any unseen seeds from names.txt.</summary>
public static class Completer
{
    /// <summary>names.txt: one name per line, blanks and '#' comments ignored.
    /// A missing/unreadable file is fine — returns empty.</summary>
    public static List<string> LoadSeedNames(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new();
        try
        {
            return File.ReadAllLines(path)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToList();
        }
        catch (IOException) { return new(); }
    }

    /// <summary>Ranked history names, then seeds not already present.</summary>
    public static List<string> Names(History history, IEnumerable<string> seeds)
    {
        var ranked = history.RankedNames();
        var seen = new HashSet<string>(ranked);
        var result = new List<string>(ranked);
        result.AddRange(seeds.Where(seen.Add));
        return result;
    }
}
