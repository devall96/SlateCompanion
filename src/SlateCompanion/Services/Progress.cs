using System.Buffers.Binary;
using Avalonia.Threading;

namespace SlateCompanion;

public sealed record HistoryPoint(string Key, DateTime T, string? Source, string? Slot, int Shrines, int ShrinesDlc, int Towers,
    int Koroks, int Memories, int Beasts, Dictionary<string, double>? Stats);

public sealed record Counts(int Shrines, int ShrinesDlc, int Towers, int Koroks, int Memories, int Beasts);

/// <summary>
/// The player's progress: what the linked save says, plus anything ticked by hand. Keys are "&lt;kind&gt;:&lt;id&gt;",
/// e.g. "shrine:Dungeon000", "korok:A01", "sideQuests:&lt;id&gt;". The save is read on a worker thread when the game
/// writes it; only the save in use is watched. Changed is raised on the UI thread.
/// </summary>
public sealed class Progress : IDisposable
{
    readonly Store store;
    readonly GameData data;
    readonly SaveWatcher watcher = new();
    readonly (string key, string flag)[] tracked;
    readonly (string key, string flag, string type)[] statFlags;
    readonly SemaphoreSlim reading = new(1, 1);
    public AppSettings Settings { get; private set; }

    public bool Linked { get; private set; }
    public SaveSource? Source { get; private set; }
    public List<SaveSource> Sources { get; private set; } = [];
    public string? Slot { get; private set; }
    public string? SlotPath { get; private set; }  // the slot folder read (game_data.sav, caption.sav, caption.jpg)
    public DateTime? Modified { get; private set; }
    public string? Platform { get; private set; }
    public string? Error { get; private set; }
    public HashSet<string> SaveDone { get; private set; } = [];
    public HashSet<string> Manual { get; }
    public double[]? Position { get; private set; }
    public Dictionary<string, double> Stats { get; private set; } = [];
    public event Action? Changed;

    public Progress(Store store, GameData data)
    {
        this.store = store;
        this.data = data;
        Settings = store.LoadSettings();
        Manual = new HashSet<string>(store.Get<List<string>>("checks") ?? []);
        var t = new List<(string, string)>();
        foreach (var m in data.Markers) if (m.Flag != null) t.Add(($"{GameData.KindOf(m.Type)}:{m.Id}", m.Flag));
        foreach (var k in data.Koroks) t.Add(($"korok:{k.Id}", k.Flag));
        void Group(string g, IEnumerable<(string id, string? flag)> items) { foreach (var (id, flag) in items) if (flag != null) t.Add(($"{g}:{id}", flag)); }
        Group("mainQuests", data.Flags.MainQuests.Select(q => (q.Id, q.Flag)));
        Group("sideQuests", data.Flags.SideQuests.Select(q => (q.Id, q.Flag)));
        Group("shrineQuests", data.Flags.ShrineQuests.Select(q => (q.Id, q.Flag)));
        Group("divineBeasts", data.Flags.DivineBeasts.Select(q => (q.Id, q.Flag)));
        Group("memories", data.Flags.Memories.Select(q => (q.Id, q.Flag)));
        tracked = [.. t];
        var s = new List<(string, string, string)>();
        foreach (var st in data.Flags.Stats) if (st.Flag != null) s.Add((st.Key, st.Flag, st.Type ?? "s32"));
        statFlags = [.. s];
        watcher.Changed += () => _ = RefreshAsync();
    }

    public bool IsDone(string key) => SaveDone.Contains(key) || Manual.Contains(key);
    public bool FromSave(string key) => SaveDone.Contains(key);

    public void ToggleManual(string key)
    {
        if (!Manual.Add(key)) Manual.Remove(key);
        store.Put("checks", Manual.ToList());
        Changed?.Invoke();
    }

    public void SaveSettings(Action<AppSettings> change)
    {
        change(Settings);
        store.SaveSettings(Settings);
    }

    /// <summary>Find saves and re-read the one in use. Safe to call from any thread.</summary>
    public async Task RefreshAsync()
    {
        await reading.WaitAsync();
        try { await Task.Run(Read); }
        finally { reading.Release(); }
        Dispatcher.UIThread.Post(() => Changed?.Invoke());
    }

    void Read()
    {
        try
        {
            Sources = SaveLocator.Find(Settings);
            static DateTime Newest(SaveSource s) => SaveLocator.ListSlots(s.Folder).Select(x => x.Modified).DefaultIfEmpty().Max();
            var src = Sources.FirstOrDefault(s => s.Id == Settings.SelectedSource) ?? Sources.OrderByDescending(Newest).FirstOrDefault();
            Source = src;
            watcher.Watch(src?.Folder);
            var slots = src == null ? [] : SaveLocator.ListSlots(src.Folder);
            var chosen = Settings.SelectedSlot == null ? slots.FirstOrDefault() : slots.FirstOrDefault(x => x.Slot == Settings.SelectedSlot) ?? slots.FirstOrDefault();
            Linked = chosen != null;
            if (chosen == null) { SaveDone = []; Error = null; return; }

            // Read only: the file is opened shared, read in one go and closed (SaveFile.Load).
            var game = SaveFile.Load(Path.Combine(chosen.Path, "game_data.sav"));
            SaveFile? caption = null;
            var cap = Path.Combine(chosen.Path, "caption.sav");
            if (File.Exists(cap)) { try { caption = SaveFile.Load(cap); } catch (InvalidDataException) { } }

            var done = new HashSet<string>();
            foreach (var (key, flag) in tracked)
            {
                var v = game.Get(flag) ?? caption?.Get(flag);
                if (v != null && v.Words.Any(w => w != 0)) done.Add(key);
            }
            var stats = new Dictionary<string, double>();
            foreach (var (key, flag, type) in statFlags)
            {
                var v = game.Get(flag) ?? caption?.Get(flag);
                if (v == null || v.Words.Length == 0) continue;
                uint w = v.Words[0];
                stats[key] = type switch
                {
                    "f32" => BitConverter.UInt32BitsToSingle(w),
                    "bool" => w != 0 ? 1 : 0,
                    _ => unchecked((int)w),
                };
            }
            var pos = game.Get("PlayerSavePos");
            Position = pos is { Words.Length: >= 3 } ? [BitConverter.UInt32BitsToSingle(pos.Words[0]), BitConverter.UInt32BitsToSingle(pos.Words[1]), BitConverter.UInt32BitsToSingle(pos.Words[2])] : null;
            SaveDone = done;
            Stats = stats;
            Slot = chosen.Slot;
            SlotPath = chosen.Path;
            Modified = chosen.Modified;
            Platform = game.BigEndian ? "Wii U" : "Switch";
            Error = null;
            RecordHistory();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Error = e.Message;
        }
    }

    public Counts Count()
    {
        int sh = 0, dlc = 0, tw = 0, mem = 0, bst = 0, kor = 0;
        foreach (var m in data.Markers)
        {
            if (m.Flag == null || !IsDone($"{GameData.KindOf(m.Type)}:{m.Id}")) continue;
            switch (m.Type) { case "shrine": sh++; break; case "shrine_dlc": dlc++; break; case "tower": tw++; break; case "memory": mem++; break; case "divine_beast": bst++; break; }
        }
        foreach (var k in data.Koroks) if (IsDone($"korok:{k.Id}")) kor++;
        return new Counts(sh, dlc, tw, kor, mem, bst);
    }

    void RecordHistory()
    {
        var hist = store.Get<List<HistoryPoint>>("history") ?? [];
        var key = $"{Source?.Id}|{Slot}|{Modified:o}";
        if (hist.Count > 0 && hist[^1].Key == key) return;
        var c = Count();
        hist.Add(new HistoryPoint(key, Modified ?? DateTime.UtcNow, Source?.Id, Slot, c.Shrines, c.ShrinesDlc, c.Towers, c.Koroks, c.Memories, c.Beasts, Stats));
        if (hist.Count > 2000) hist.RemoveRange(0, hist.Count - 2000);
        store.Put("history", hist);
    }

    public List<HistoryPoint> History() => (store.Get<List<HistoryPoint>>("history") ?? []).Where(h => h.Source == Source?.Id).ToList();

    public void Dispose() => watcher.Dispose();

    // (kept for callers that need a raw little-endian float)
    public static float F32(uint w) => BinaryPrimitives.ReadSingleLittleEndian(BitConverter.GetBytes(w));
}
