namespace FileRouter.Core;

/// <summary>
/// The commit pipeline: name -> move. Plus skip and undo. Files are only ever
/// MOVED — never deleted, never overwritten. Every failure raises CommitError
/// with a message fit for a dialog box, and the source stays put.
///
/// (PDF metadata tagging from the Python version is deferred — it needs a
/// managed PDF library. The safe-move + audit is the load-bearing part.)
/// </summary>
public static class Commit
{
    public sealed record CommitOutcome(
        bool Vanished, string? NewPath, Naming.NameResult? NameResult);

    public sealed record SkipOutcome(
        bool Vanished, string? NewPath, string CollisionSuffix);

    /// <summary>File.Move with a last-instant collision guard. Windows won't
    /// overwrite on move, but check explicitly and treat 'exists' as a race.</summary>
    private static void MoveNeverOverwrite(string src, string target)
    {
        if (File.Exists(target))
            throw new FileExistsRace($"{Path.GetFileName(target)} appeared at the destination mid-commit");
        try
        {
            File.Move(src, target);   // .NET Move does not overwrite by default
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CommitError($"Couldn't move {Path.GetFileName(src)} to " +
                                  $"{Path.GetDirectoryName(target)}:\n{ex.Message}");
        }
    }

    public static CommitOutcome CommitFile(
        string src, string typedName, Route route, string globalMode)
    {
        if (!File.Exists(src))
            return new CommitOutcome(true, null, null);

        var destDir = route.Path ?? "";
        if (!Directory.Exists(destDir))
            throw new CommitError($"Destination folder is not available: " +
                                  $"{(destDir.Length > 0 ? destDir : "(not set)")}");

        Naming.NameResult Build() => Naming.BuildTarget(
            Path.GetFileName(src), typedName, route.NamingMode, globalMode,
            route.Suffix, route.AppendSuffix,
            name => File.Exists(Path.Combine(destDir, name)));

        Naming.NameResult result;
        try { result = Build(); }
        catch (ArgumentException ex) { throw new CommitError(ex.Message); }

        try
        {
            MoveNeverOverwrite(src, Path.Combine(destDir, result.Filename));
        }
        catch (FileExistsRace)
        {
            // Collision race: something claimed the name after Build. Retry once.
            result = Build();
            try { MoveNeverOverwrite(src, Path.Combine(destDir, result.Filename)); }
            catch (FileExistsRace ex) { throw new CommitError(ex.Message); }
        }
        return new CommitOutcome(false, Path.Combine(destDir, result.Filename), result);
    }

    public static SkipOutcome SkipFile(string src, string deferredDir)
    {
        if (!File.Exists(src))
            return new SkipOutcome(true, null, "");
        if (string.IsNullOrWhiteSpace(deferredDir) || !Directory.Exists(deferredDir))
            throw new CommitError($"Set-aside folder is not available: " +
                                  $"{(string.IsNullOrWhiteSpace(deferredDir) ? "(not set)" : deferredDir)}");

        // blank name + empty route == keep the original filename, collision-counted
        var result = Naming.BuildTarget(
            Path.GetFileName(src), "", null, Naming.ModeInsert, "", false,
            name => File.Exists(Path.Combine(deferredDir, name)));
        MoveNeverOverwrite(src, Path.Combine(deferredDir, result.Filename));
        return new SkipOutcome(false, Path.Combine(deferredDir, result.Filename),
            result.CollisionSuffix);
    }

    /// <summary>Reverse one commit/skip: move the file back to its original
    /// name. Raises CommitError if the undo can't be done — the filed copy
    /// stays put.</summary>
    public static void UndoAction(string filedPath, string originalPath)
    {
        if (!File.Exists(filedPath))
            throw new CommitError($"Can't undo: {Path.GetFileName(filedPath)} is no longer there");
        if (File.Exists(originalPath))
            throw new CommitError($"Can't undo: {Path.GetFileName(originalPath)} already exists again");
        var parent = Path.GetDirectoryName(originalPath);
        if (parent is null || !Directory.Exists(parent))
            throw new CommitError($"Can't undo: inbox folder is gone: {parent}");
        MoveNeverOverwrite(filedPath, originalPath);
    }

    private sealed class FileExistsRace : Exception
    {
        public FileExistsRace(string message) : base(message) { }
    }
}

public sealed class CommitError : Exception
{
    public CommitError(string message) : base(message) { }
}
