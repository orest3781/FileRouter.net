using System.Text.RegularExpressions;

namespace FileRouter.Core;

/// <summary>
/// Bulk filename editing: plan -> preview -> execute -> (optional) revert.
/// Operations transform the STEM only; the extension is preserved. Nothing is
/// ever overwritten: a taken target name (on disk, or claimed by an earlier
/// file in the same batch) gets the app's " (2)" counter. Every rename is
/// per-file fail-soft.
/// </summary>
public static partial class BulkRename
{
    // Generation tokens, longest-first so VIII wins over VII over VI over V.
    private const string Gen =
        @"(?:JUNIOR|SENIOR|JR\.?|SR\.?|VIII|VII|VI|IX|IV|III|II|X|V|2ND|3RD|4TH|5TH)";

    // Surname particles: separator-joined multi-part last names (VAN_DYKE,
    // DE_LA_CRUZ) are recognized when led by these. Backtracking keeps
    // two-token names right; three full tokens without a particle stay
    // ambiguous and are skipped.
    private const string Particle =
        @"(?:VANDER|VANDEN|VANDE|VAN|VON|DELLA|DEL|DEN|DER|DE|DI|DA|DOS|DAS|DO|DU" +
        @"|LA|LE|LOS|MAC|MC|SAINT|SANTA|SAN|ST|TER|TEN|EL|BIN|IBN)";

    [GeneratedRegex(
        @"^(?<last>(?:" + Particle + @"[_ ]+)*[A-Za-z'\-]+)[_ ]+" +
        @"(?:(?<gen>" + Gen + @")[_ ]+)?" +
        @"(?<first>[A-Za-z'\-]+)" +
        @"(?:[_ ]+(?<gen2>" + Gen + @"))?" +
        @"(?:[_ ]+[A-Za-z])*" +                          // middle initial(s)
        @"[_ ]+(?<m>\d{1,2})[_ ](?<d>\d{1,2})[_ ](?<y>(?:19|20)\d{2})(?:[_ ]|$)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ReviewRegex();

    public sealed record RenameOp(
        string Find = "", string Replace = "",
        string Prefix = "", string Suffix = "",
        string Case = "keep",       // keep | upper | lower
        string ReceivedDate = "");  // YYYYMMDD -> review-file rebuild

    public sealed record PlannedRename(
        string Source, string Target, bool Changed, string Note = "", bool Manual = false);

    public sealed record RenameOutcome(string Source, string? Final, string Error = "");

    /// <summary>(last, first) from a review-file filename stem, or null when
    /// the stem doesn't follow the layout. A multi-part last name comes back
    /// space-joined: "VAN DYKE".</summary>
    public static (string Last, string First)? ParseReviewStem(string stem)
    {
        var m = ReviewRegex().Match(stem);
        if (!m.Success) return null;
        var month = int.Parse(m.Groups["m"].Value);
        var day = int.Parse(m.Groups["d"].Value);
        if (month is < 1 or > 12 || day is < 1 or > 31) return null;
        var last = string.Join(' ',
            m.Groups["last"].Value.Replace('_', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return (last, m.Groups["first"].Value);
    }

    /// <summary>Order: review-file rebuild -> find/replace -> affixes -> case.
    /// Returns null when review mode is on and the stem doesn't match the
    /// layout (the caller skips the file, readably).</summary>
    public static string? TransformStem(string stem, RenameOp op)
    {
        var outp = stem;
        if (!string.IsNullOrEmpty(op.ReceivedDate))
        {
            var parts = ParseReviewStem(outp);
            if (parts is null) return null;
            outp = $"{op.ReceivedDate}-{parts.Value.Last.ToUpperInvariant()}" +
                   $"-{parts.Value.First.ToUpperInvariant()}";
        }
        if (!string.IsNullOrEmpty(op.Find))
            outp = outp.Replace(op.Find, op.Replace);
        outp = $"{op.Prefix}{outp}{op.Suffix}";
        return op.Case switch
        {
            "upper" => outp.ToUpperInvariant(),
            "lower" => outp.ToLowerInvariant(),
            _ => outp,
        };
    }

    private static bool SameFile(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Compute the batch, in input order. Touches nothing on disk
    /// beyond existence checks. <paramref name="overrides"/> maps a source
    /// path to a hand-edited target STEM that beats the operation.</summary>
    public static List<PlannedRename> Plan(
        IEnumerable<string> paths, RenameOp op,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        var planned = new List<PlannedRename>();
        var taken = new Dictionary<string, HashSet<string>>();

        foreach (var source in paths)
        {
            var dir = Path.GetDirectoryName(source) ?? "";
            var ext = Path.GetExtension(source);
            var stem = Path.GetFileNameWithoutExtension(source);

            var manual = overrides is not null && overrides.ContainsKey(source);
            var newStem = manual ? overrides![source] : TransformStem(stem, op);

            if (newStem is null)
            {
                planned.Add(new PlannedRename(source, source, false,
                    "doesn't match the review-file layout — skipped"));
                continue;
            }
            if (string.IsNullOrWhiteSpace(newStem))
            {
                planned.Add(new PlannedRename(source, source, false,
                    "new name would be empty — skipped", manual));
                continue;
            }
            var candidate = Path.Combine(dir, newStem + ext);
            if (SameFile(Path.GetFileName(candidate), Path.GetFileName(source)))
            {
                planned.Add(new PlannedRename(source, source, false, "", manual));
                continue;
            }

            if (!taken.TryGetValue(dir.ToLowerInvariant(), out var claimed))
                taken[dir.ToLowerInvariant()] = claimed = new(StringComparer.OrdinalIgnoreCase);

            bool Free(string p) =>
                !claimed.Contains(Path.GetFileName(p)) &&
                (!File.Exists(p) || SameFile(p, source));

            var final = candidate;
            var note = "";
            var counter = 2;
            while (!Free(final))
            {
                final = Path.Combine(dir, $"{newStem} ({counter}){ext}");
                counter++;
            }
            if (!SameFile(final, candidate))
                note = "name was taken — using a counter";
            claimed.Add(Path.GetFileName(final));
            planned.Add(new PlannedRename(source, final, true, note, manual));
        }
        return planned;
    }

    /// <summary>Rename everything changed, per-file fail-soft. A target that
    /// appeared since planning gets the counter bumped at the last instant.</summary>
    public static List<RenameOutcome> Execute(IEnumerable<PlannedRename> plans)
    {
        var outcomes = new List<RenameOutcome>();
        foreach (var pr in plans)
        {
            if (!pr.Changed) continue;
            var dir = Path.GetDirectoryName(pr.Target) ?? "";
            var ext = Path.GetExtension(pr.Target);
            var stem = Path.GetFileNameWithoutExtension(pr.Target);
            var target = pr.Target;
            var counter = 2;
            while (File.Exists(target) && !SameFile(target, pr.Source))
            {
                target = Path.Combine(dir, $"{stem} ({counter}){ext}");
                counter++;
            }
            try
            {
                File.Move(pr.Source, target);
                outcomes.Add(new RenameOutcome(pr.Source, target));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                outcomes.Add(new RenameOutcome(pr.Source, null, ex.Message));
            }
        }
        return outcomes;
    }

    /// <summary>Undo a batch (newest first). Returns readable problems, empty
    /// if all names were restored.</summary>
    public static List<string> Revert(IReadOnlyList<RenameOutcome> outcomes)
    {
        var problems = new List<string>();
        for (var i = outcomes.Count - 1; i >= 0; i--)
        {
            var o = outcomes[i];
            if (o.Final is null) continue;
            if (File.Exists(o.Source) && !SameFile(o.Source, o.Final))
            {
                problems.Add(
                    $"{Path.GetFileName(o.Source)} exists again — left as " +
                    Path.GetFileName(o.Final));
                continue;
            }
            try { File.Move(o.Final, o.Source); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"Couldn't restore {Path.GetFileName(o.Source)}: {ex.Message}");
            }
        }
        return problems;
    }
}
