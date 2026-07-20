namespace FileRouter.Core;

/// <summary>Session state: the queue walk, counts, undo stack, and history
/// writes. No UI — the whole processing loop is testable headless.</summary>
public sealed class Session
{
    public const string SkipLabel = "<skip>";
    public const string VanishedLabel = "<vanished>";
    public const int UndoDepth = 20;

    private sealed record UndoEntry(long RowId, int QueueIndex, string FiledPath,
        string OriginalPath, bool WasSkip,
        bool Tagged = false, string OldKeywords = "", string OldSubject = "");

    private readonly Config _cfg;
    private readonly History _history;
    private readonly LinkedList<UndoEntry> _undo = new();

    public string SessionMode { get; set; }
    public List<string> Queue { get; private set; } = new();
    public int Pos { get; private set; }
    public int Filed { get; private set; }
    public int Skipped { get; private set; }
    public int Vanished { get; private set; }
    public List<long> RowIds { get; } = new();

    public Session(Config cfg, History history)
    {
        _cfg = cfg;
        _history = history;
        SessionMode = cfg.NamingMode;
    }

    public void Start(IEnumerable<string> queue)
    {
        Queue = queue.ToList();
        Pos = Filed = Skipped = Vanished = 0;
        _undo.Clear();
        RowIds.Clear();
        SessionMode = _cfg.NamingMode;
    }

    public string? Current => Pos < Queue.Count ? Queue[Pos] : null;
    public bool Done => Current is null;
    public int Total => Queue.Count;
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Append newly arrived inbox files to the END of a running queue.
    /// Files already known are ignored. Returns how many were added.</summary>
    public int Extend(IEnumerable<string> paths)
    {
        var known = new HashSet<string>(Queue);
        var added = paths.Where(p => known.Add(p)).ToList();
        Queue.AddRange(added);
        return added.Count;
    }

    public Commit.CommitOutcome CommitCurrent(string typedName, Route route)
    {
        var src = Current ?? throw new CommitError("No document is loaded.");
        var outcome = Commit.CommitFile(src, typedName, route, SessionMode, _cfg.TagWithRoute);
        if (outcome.Vanished) { LogVanished(src); return outcome; }

        var result = outcome.NameResult!;
        var rowId = _history.LogCommit(
            src, Path.GetFileName(src), result.Filename,
            Naming.IsBlankName(typedName) ? "" : typedName, result.ModeUsed,
            result.SuffixApplied, route.Label, route.Path, outcome.Tagged,
            result.CollisionSuffix);
        RowIds.Add(rowId);
        Push(new UndoEntry(rowId, Pos, outcome.NewPath!, src, false,
            outcome.Tagged, outcome.OldKeywords, outcome.OldSubject));
        Filed++;
        Pos++;
        return outcome;
    }

    public Commit.SkipOutcome SkipCurrent()
    {
        var src = Current ?? throw new CommitError("No document is loaded.");
        var outcome = Commit.SkipFile(src, _cfg.Deferred);
        if (outcome.Vanished) { LogVanished(src); return outcome; }

        var rowId = _history.LogCommit(
            src, Path.GetFileName(src), Path.GetFileName(outcome.NewPath!),
            "", SessionMode, "", SkipLabel, _cfg.Deferred, tagged: false,
            outcome.CollisionSuffix);
        RowIds.Add(rowId);
        Push(new UndoEntry(rowId, Pos, outcome.NewPath!, src, true));
        Skipped++;
        Pos++;
        return outcome;
    }

    public (string FiledPath, string OriginalPath, string Warning) UndoLast()
    {
        if (_undo.Count == 0) throw new CommitError("Nothing to undo.");
        var entry = _undo.Last!.Value;
        var warning = Commit.UndoAction(entry.FiledPath, entry.OriginalPath,
            entry.Tagged, entry.OldKeywords, entry.OldSubject);
        _undo.RemoveLast();
        _history.MarkReverted(entry.RowId);
        if (entry.WasSkip) Skipped--; else Filed--;
        Pos = entry.QueueIndex;   // the restored file is current again
        return (entry.FiledPath, entry.OriginalPath, warning);
    }

    private void Push(UndoEntry e)
    {
        _undo.AddLast(e);
        while (_undo.Count > UndoDepth) _undo.RemoveFirst();
    }

    private void LogVanished(string src)
    {
        var rowId = _history.LogCommit(
            src, Path.GetFileName(src), Path.GetFileName(src), "", SessionMode,
            "", VanishedLabel, "", tagged: false, "");
        RowIds.Add(rowId);
        Vanished++;
        Pos++;
    }
}
