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
    string? folder;
    public event Action? Changed;

    public void Watch(string? dir)
    {
        lock (gate)
        {
            if (string.Equals(dir, folder, StringComparison.OrdinalIgnoreCase)) return;
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
            watcher.EnableRaisingEvents = true;
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
