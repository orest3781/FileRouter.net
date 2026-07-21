namespace FileRouter.Wpf.Services;

/// <summary>Live folder monitoring with the exact semantics the WinForms app
/// proved out: any Created/Deleted/Renamed restarts a 1.5 s debounce (lets a
/// fax finish downloading before we rescan), and a 30 s poll backstops network
/// shares where FileSystemWatcher change notifications never fire (SMB).
///
/// <see cref="Activity"/> is raised on the provided SynchronizationContext
/// (the UI thread in the app) or inline when none is given (tests).</summary>
public sealed class FolderWatchService : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly System.Threading.Timer _debounce;
    private readonly System.Threading.Timer _poll;
    private readonly int _debounceMs;
    private readonly SynchronizationContext? _context;
    private volatile bool _disposed;

    public event Action? Activity;

    public FolderWatchService(int debounceMs = 1500, int pollMs = 30_000,
        SynchronizationContext? context = null)
    {
        _debounceMs = debounceMs;
        _context = context;
        _debounce = new System.Threading.Timer(_ => RaiseActivity());
        _poll = new System.Threading.Timer(_ => RaiseActivity(), null, pollMs, pollMs);
    }

    /// <summary>(Re)build the watcher set. Blank or missing folders are
    /// skipped — a not-yet-created deferred folder must not throw.</summary>
    public void SetFolders(params string?[] folders)
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
            var w = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            w.Created += (_, _) => Poke();
            w.Deleted += (_, _) => Poke();
            w.Renamed += (_, _) => Poke();
            _watchers.Add(w);
        }
    }

    /// <summary>Restart the debounce window; fires <see cref="Activity"/> once
    /// when the burst goes quiet.</summary>
    public void Poke()
    {
        if (_disposed) return;
        _debounce.Change(_debounceMs, Timeout.Infinite);
    }

    private void RaiseActivity()
    {
        if (_disposed) return;
        if (_context is null) Activity?.Invoke();
        else _context.Post(_ => { if (!_disposed) Activity?.Invoke(); }, null);
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        _debounce.Dispose();
        _poll.Dispose();
    }
}
