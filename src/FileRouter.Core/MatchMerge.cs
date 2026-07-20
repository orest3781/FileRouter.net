using System.Text;
using System.Text.RegularExpressions;

namespace FileRouter.Core;

/// <summary>
/// Match &amp; merge: pair PDFs with a roster spreadsheet by name, then merge
/// each person's Control ID into the filename. Renames go through
/// <see cref="BulkRename"/> so collision counters, never-overwrite, and
/// per-file fail-soft all apply here too.
/// </summary>
public static partial class MatchMerge
{
    [GeneratedRegex(@"^\d{8}-(?<rest>.+)$")]
    private static partial Regex DatedStemRegex();

    [GeneratedRegex(@"-\d+$")]
    private static partial Regex TrailingIdRegex();

    private static string Norm(string name) =>
        string.Join(' ', name.Replace('_', ' ').ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public sealed record Candidate(string ControlId, IReadOnlyDictionary<string, string> Row);

    public sealed class Roster
    {
        public required IReadOnlyList<string> Headers { get; init; }
        public Dictionary<(string, string), List<Candidate>> People { get; } = new();

        public IReadOnlyList<Candidate> Lookup(string last, string first) =>
            People.TryGetValue((Norm(last), Norm(first)), out var c)
                ? c : Array.Empty<Candidate>();
    }

    /// <summary>Read a CSV roster (Excel-style UTF-8 BOM tolerated). Throws
    /// <see cref="RosterException"/> with a dialog-ready message on any
    /// problem. Rows missing a name or id are ignored; duplicate rows with the
    /// same name AND id collapse to one candidate.</summary>
    public static Roster LoadRoster(string path, string firstHeader,
        string lastHeader, string controlHeader)
    {
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RosterException($"Couldn't read the spreadsheet: {ex.Message}");
        }

        var rows = ParseCsv(text);
        if (rows.Count == 0)
            throw new RosterException("The spreadsheet is empty.");
        var headers = rows[0].Select(h => h.Trim()).ToList();

        var missing = new[] { firstHeader, lastHeader, controlHeader }
            .Where(h => !headers.Contains(h)).ToList();
        if (missing.Count > 0)
            throw new RosterException(
                "These headers aren't in the spreadsheet: " + string.Join(", ", missing) +
                ".\nHeaders found: " + (headers.Count > 0 ? string.Join(", ", headers) : "(none)"));

        var fi = headers.IndexOf(firstHeader);
        var li = headers.IndexOf(lastHeader);
        var ci = headers.IndexOf(controlHeader);

        var roster = new Roster { Headers = headers };
        foreach (var cells in rows.Skip(1))
        {
            string Cell(int i) => i < cells.Count ? cells[i].Trim() : "";
            var first = Cell(fi);
            var last = Cell(li);
            var control = Cell(ci);
            if (first.Length == 0 || last.Length == 0 || control.Length == 0) continue;

            var row = new Dictionary<string, string>();
            for (var i = 0; i < headers.Count; i++) row[headers[i]] = Cell(i);

            var key = (Norm(last), Norm(first));
            if (!roster.People.TryGetValue(key, out var list))
                roster.People[key] = list = new();
            if (list.All(c => c.ControlId != control))
                list.Add(new Candidate(control, row));
        }
        return roster;
    }

    /// <summary>Possible (last, first) readings of a stem, most likely first.
    /// A trailing "-&lt;id&gt;" is ignored for the reading.</summary>
    public static List<(string Last, string First)> NameCandidates(string stem)
    {
        var readings = new List<(string, string)>();
        var dated = DatedStemRegex().Match(TrailingIdRegex().Replace(stem, ""));
        if (dated.Success)
        {
            var parts = dated.Groups["rest"].Value.Split('-');
            for (var i = parts.Length - 1; i >= 1; i--)
                readings.Add((string.Join('-', parts[..i]), string.Join('-', parts[i..])));
        }
        var review = BulkRename.ParseReviewStem(stem);
        if (review is { } r && !readings.Contains((r.Last, r.First)))
            readings.Add((r.Last, r.First));
        return readings;
    }

    public sealed record MatchResult(
        string Source, string Status, string Last = "", string First = "",
        IReadOnlyList<Candidate>? Candidates = null, string NewStem = "");

    public static string MergedStem(string stem, string controlId) => $"{stem}-{controlId}";

    /// <summary>Classify every file, in input order. Touches nothing.</summary>
    public static List<MatchResult> MatchFiles(IEnumerable<string> paths, Roster roster)
    {
        var results = new List<MatchResult>();
        foreach (var source in paths)
        {
            var stem = Path.GetFileNameWithoutExtension(source);
            var readings = NameCandidates(stem);
            if (readings.Count == 0)
            {
                results.Add(new MatchResult(source, "no_name"));
                continue;
            }
            (string Last, string First, IReadOnlyList<Candidate> C)? hit = null;
            foreach (var (last, first) in readings)
            {
                var found = roster.Lookup(last, first);
                if (found.Count > 0) { hit = (last, first, found); break; }
            }
            if (hit is null)
            {
                results.Add(new MatchResult(source, "no_match", readings[0].Last, readings[0].First));
                continue;
            }
            var (hl, hf, candidates) = hit.Value;
            if (candidates.Any(c => stem.EndsWith($"-{c.ControlId}", StringComparison.Ordinal)))
                results.Add(new MatchResult(source, "already", hl, hf, candidates));
            else if (candidates.Count == 1)
                results.Add(new MatchResult(source, "merge", hl, hf, candidates,
                    MergedStem(stem, candidates[0].ControlId)));
            else
                results.Add(new MatchResult(source, "ambiguous", hl, hf, candidates));
        }
        return results;
    }

    /// <summary>Rename every unambiguous match, with the full bulk-rename safety.</summary>
    public static List<BulkRename.RenameOutcome> ExecuteMerges(IEnumerable<MatchResult> results)
    {
        var toDo = results.Where(r => r.Status == "merge").ToList();
        var overrides = toDo.ToDictionary(r => r.Source, r => r.NewStem);
        var plans = BulkRename.Plan(toDo.Select(r => r.Source), new BulkRename.RenameOp(), overrides);
        return BulkRename.Execute(plans);
    }

    /// <summary>A single triage decision: merge this control id into the file.</summary>
    public static List<BulkRename.RenameOutcome> MergeOne(string source, string controlId)
    {
        var overrides = new Dictionary<string, string>
        {
            [source] = MergedStem(Path.GetFileNameWithoutExtension(source), controlId),
        };
        var plans = BulkRename.Plan(new[] { source }, new BulkRename.RenameOp(), overrides);
        return BulkRename.Execute(plans);
    }

    /// <summary>Minimal RFC-4180-ish CSV: handles quoted fields with embedded
    /// commas, quotes ("") and newlines. Good enough for Excel exports.</summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else switch (c)
            {
                case '"': inQuotes = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new();
                    break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows.Where(r => r.Count > 1 || (r.Count == 1 && r[0].Length > 0)).ToList();
    }
}

public sealed class RosterException : Exception
{
    public RosterException(string message) : base(message) { }
}
