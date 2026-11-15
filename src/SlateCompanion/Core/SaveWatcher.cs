namespace SlateCompanion;

/// <summary>
/// Watches one save folder; a burst of writes (the game saves several files) is reported once, 1.5 s after the last,
/// so the game has finished writing before anything is read. Watching opens no files.
/// </summary>
public sealed class SaveWatcher : IDisposable
{
    readonly Lock gate = new();
    FileSystemWatcher? watcher;
    Timer? debounce;
    string? folder, wanted;
    int suspended;
    public event Action? Changed;

    public void Watch(string? dir)
    {
        lock (gate)
        {
            wanted = dir;
            if (suspended > 0) return;  // (started again when the save change is done)
            if (watcher != null && string.Equals(dir, folder, StringComparison.OrdinalIgnoreCase)) return;
            watcher?.Dispose();
            watcher = null;
            folder = dir;
            if (dir == null || !Directory.Exists(dir)) return;
            watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                InternalBufferSize = 8192,
            };
            watcher.Changed += (_, e) => Touched(e.FullPath);
            watcher.Created += (_, e) => Touched(e.FullPath);
            watcher.Renamed += (_, e) => Touched(e.FullPath);
            // the folder went away (renamed or deleted under it) or the buffer overflowed: drop this watcher and let
            // the refresh that follows start a new one
            watcher.Error += (_, _) => { lock (gate) { watcher?.Dispose(); watcher = null; folder = null; } Changed?.Invoke(); };
            watcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>
    /// Stops watching while saves are changed. On Windows a watched save_0 keeps its parent "save" folder from being
    /// renamed (Access denied), which made Import all and Restore of the save in use always fail; and a watcher left
    /// on a folder that was swapped out sees nothing again. Watching starts afresh when this is disposed.
    /// </summary>
    public IDisposable Suspend()
    {
        lock (gate)
        {
            suspended++;
            watcher?.Dispose(); watcher = null; folder = null;
            debounce?.Dispose(); debounce = null;
        }
        return new Resumer(this);
    }

    sealed class Resumer(SaveWatcher w) : IDisposable
    {
        int done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref done, 1) != 0) return;
            string? dir;
            lock (w.gate) { w.suspended--; dir = w.wanted; }
            w.Watch(dir);
        }
    }

    void Touched(string path)
    {
        if (!path.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)) return;
        lock (gate)
        {
            debounce?.Dispose();
            debounce = new Timer(_ => Changed?.Invoke(), null, 1500, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        lock (gate) { watcher?.Dispose(); debounce?.Dispose(); watcher = null; }
    }
}
