using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using SkiaSharp;

namespace SlateCompanion;

/// <summary>
/// Builds Slate Companion's map from the player's own game files, once: the game's map squares
/// (romfs/UI/MapTex/MainField/MapTex_&lt;col&gt;-&lt;row&gt;.sbmaptex, 3000x3000 BC1 per 1000x1000 units) become a WebP tile
/// pyramid at 3 pixels per unit (zoom 8), and the map screen's icons (Layout/Common.sblarc) become PNGs. Everything goes
/// to the app's own folder; the game files are only read. While playing, the app just serves the finished files.
/// </summary>
public sealed class MapBuilder
{
    const string Cols = "ZABCDEFGHIJK";          // x from -6000, 1000 units each
    const int Rows = 10;                         // z from -5000
    const int Square = 3000;                     // pixels per square at zoom 8
    const int MapW = 12 * Square, MapH = Rows * Square;
    public const int MaxZoom = 8, Tile = 256;
    static readonly byte[] Background = [0x0b, 0x18, 0x1c];

    readonly string outDir;
    readonly Lock gate = new();
    public string State { get; private set; } = "none";   // none | building | ready | error
    public double Progress { get; private set; }
    public string Message { get; private set; } = "";
    CancellationTokenSource? cts;

    public MapBuilder(string appFolder)
    {
        outDir = Path.Combine(appFolder, "gamemap");
        if (File.Exists(Path.Combine(outDir, "meta.json"))) State = "ready";
    }

    public string Folder => outDir;

    public object Status()
    {
        object? meta = null;
        try { if (State == "ready") meta = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(outDir, "meta.json"))); }
        catch (Exception e) when (e is IOException or JsonException) { }
        return new { state = State, progress = Math.Round(Progress, 3), message = Message, meta };
    }

    /// <summary>The romfs folder of a game dump: contains UI/MapTex/MainField and Pack/Bootup.pack.</summary>
    public static bool IsRomfs(string? dir) =>
        dir != null && File.Exists(Path.Combine(dir, "UI", "MapTex", "MainField", "MapTex_A-0.sbmaptex")) &&
        File.Exists(Path.Combine(dir, "Pack", "Bootup.pack"));

    /// <summary>A romfs from the player's choice, or from a Savage Wind folder (its extracted/romfs).</summary>
    public static string? FindRomfs(string? chosen, IEnumerable<string> savageWindFolders)
    {
        foreach (var c in new[] { chosen, chosen == null ? null : Path.Combine(chosen, "romfs"), chosen == null ? null : Path.Combine(chosen, "extracted", "romfs") })
            if (IsRomfs(c)) return c;
        foreach (var sw in savageWindFolders)
        {
            var r = Path.Combine(sw, "extracted", "romfs");
            if (IsRomfs(r)) return r;
        }
        return null;
    }

    /// <summary>The game's cooking pot (Item_CookSet), rendered once from the player's files for the cooking screen.</summary>
    public static bool RenderPot(string romfs, string path)
    {
        try
        {
            using var bmp = ModelRender.RenderActor(romfs, "Item_CookSet", 768, "threequarter");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var data = bmp.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
            using (var f = File.Create(path + ".tmp")) data.SaveTo(f);
            File.Move(path + ".tmp", path, true);
            Log.Info("Cooking pot drawn from the game files");
            return true;
        }
        catch (Exception e) { Log.Warn("Cooking pot couldn't be drawn: " + e.Message); return false; }
    }

    public const int AssetsVersion = 2;  // 1: map and map icons; 2: + item icons, pictures and the game's text

    Tuple<string, bool>? completeFor;  // (a reference, so a read from another thread is never half-written)

    /// <summary>
    /// Built with everything the current version uses (an older build has only the map). Asked on every icon, name
    /// and picture lookup, so meta.json is read once per build state, not every time.
    /// </summary>
    public bool Complete
    {
        get
        {
            var state = State;
            if (completeFor is { } c && c.Item1 == state) return c.Item2;
            bool complete;
            try
            {
                using var meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "meta.json")));
                complete = state == "ready" && meta.RootElement.GetProperty("version").GetInt32() >= AssetsVersion;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException) { complete = false; }
            completeFor = Tuple.Create(state, complete);
            return complete;
        }
    }

    public bool Start(string romfs, string lang = "USen")
    {
        lock (gate)
        {
            if (State == "building") return false;
            State = "building"; Progress = 0; Message = "Starting";
            cts = new CancellationTokenSource();
        }
        var token = cts.Token;
        var t = new Thread(() => Run(romfs, lang, token)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "map build" };
        t.Start();
        return true;
    }

    public void Cancel() => cts?.Cancel();

    void Run(string romfs, string lang, CancellationToken ct)
    {
        var tmp = outDir + ".building";
        var sw = Stopwatch.StartNew();
        try
        {
            // A build that failed before can leave its folder behind with a file still held (a virus scanner reading
            // the new tiles): it must not stop this one. If it can't be removed, this build uses a folder of its own.
            foreach (var pattern in new[] { ".building*", ".old*" })
                foreach (var stale in Directory.GetDirectories(Path.GetDirectoryName(outDir)!, Path.GetFileName(outDir) + pattern)) TryDelete(stale);
            if (Directory.Exists(tmp)) tmp = $"{outDir}.building-{DateTime.Now:yyyyMMddHHmmss}";
            Directory.CreateDirectory(tmp);
            Message = "Reading the map icons";
            var icons = ExtractIcons(romfs, Path.Combine(tmp, "icons"));
            Message = "Reading the item pictures";
            int items = ExtractItems(romfs, Path.Combine(tmp, "items"), ct);
            int pictures = CopyAll(Path.Combine(romfs, "UI", "PictureBook"), Path.Combine(tmp, "pictures"), "*.jpg");
            int album = CopyAll(Path.Combine(romfs, "UI", "Album"), Path.Combine(tmp, "album"), "*.jpg");
            Message = "Drawing the cooking pot";
            RenderPot(romfs, Path.Combine(tmp, "cook", "pot.png"));
            Message = "Reading the game's text";
            var text = GameText.FromPack(romfs, lang, "ActorType/", "QuestMsg/", "StaticMsg/LocationMarker", "Tips/");
            File.WriteAllText(Path.Combine(tmp, "text.json"), JsonSerializer.Serialize(text));
            Message = "Building the map";
            long bytes = BuildTiles(romfs, tmp, ct);
            var meta = new
            {
                version = AssetsVersion,
                language = lang,
                items,
                pictures,
                album,
                texts = text.Count,
                built = DateTime.UtcNow,
                seconds = (int)sw.Elapsed.TotalSeconds,
                tileSize = Tile,
                maxNativeZoom = MaxZoom,
                pixelsPerUnit = 3,
                width = MapW,
                height = MapH,
                bytes,
                icons,
            };
            File.WriteAllText(Path.Combine(tmp, "meta.json"), JsonSerializer.Serialize(meta));
            ct.ThrowIfCancellationRequested();
            // The working map is moved aside, not deleted in place: a delete that fails half-way (a tile still open,
            // antivirus) would leave neither the old map nor the new one.
            Message = "Putting the new map in place";
            var old = outDir + ".old";
            TryDelete(old);
            if (Directory.Exists(old)) old = $"{outDir}.old-{DateTime.Now:yyyyMMddHHmmss}";
            if (Directory.Exists(outDir)) MoveWithRetry(outDir, old);
            try { PutInPlace(tmp, outDir); }
            catch (Exception) { if (Directory.Exists(old) && !Directory.Exists(outDir)) MoveWithRetry(old, outDir); throw; }
            TryDelete(old);
            Progress = 1; Message = $"Built in {sw.Elapsed.TotalSeconds:0} s"; State = "ready";
        }
        catch (OperationCanceledException)
        {
            TryDelete(tmp);
            Message = "Cancelled"; State = File.Exists(Path.Combine(outDir, "meta.json")) ? "ready" : "none";
        }
        catch (Exception e)
        {
            TryDelete(tmp);
            Message = e.Message; State = "error";
            Log.Error("Building from the game files failed", e);
        }
        finally
        {
            squares.Clear();
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);  // give the build's memory back at once
        }
    }

    /// <summary>A folder deleted, read-only files too, trying again for a few seconds while something holds a file in it.</summary>
    static void TryDelete(string dir)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    if (File.GetAttributes(f).HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dir, true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 5) { Log.Warn($"Couldn't delete {Log.Scrub(dir)} (it's tried again at the next build): {e.Message}"); return; }
                Thread.Sleep(400 + attempt * 300);
            }
        }
    }

    /// <summary>
    /// A folder renamed. Windows refuses while anything holds a file inside it open (a virus scanner or the search
    /// indexer reading the thousands of new map tiles), with "Access to the path ... is denied", whatever the user's
    /// rights: so it's tried again for about 20 seconds.
    /// </summary>
    static void MoveWithRetry(string from, string to)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { Directory.Move(from, to); return; }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < 11 && Directory.Exists(from) && !Directory.Exists(to))
            {
                if (attempt == 0) Log.Info($"Renaming {Log.Scrub(from)} was refused ({e.Message}); trying again");
                Thread.Sleep(500 + attempt * 250);
            }
        }
    }

    /// <summary>
    /// The finished build put where the app reads it: renamed (with retries), or, if the folder stays held, copied
    /// file by file (a file being scanned can still be read) and the build folder removed as best it can.
    /// </summary>
    static void PutInPlace(string build, string dest)
    {
        try { MoveWithRetry(build, dest); return; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn($"The new map folder couldn't be renamed ({e.Message}); copying it instead"); }
        try
        {
            foreach (var f in Directory.EnumerateFiles(build, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dest, Path.GetRelativePath(build, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(f, to, true);
            }
        }
        catch (Exception) { TryDelete(dest); throw; }  // (never a half-copied map: the old one goes back)
        TryDelete(build);
    }

    // ------------------------------------------------------------------ item icons and pictures

    /// <summary>Every inventory icon (UI/StockItem/&lt;actor&gt;.sbitemico: one 144x144 RGBA8 texture) as items/&lt;actor&gt;.png.</summary>
    static int ExtractItems(string romfs, string dir, CancellationToken ct)
    {
        var src = Path.Combine(romfs, "UI", "StockItem");
        if (!Directory.Exists(src)) return 0;
        Directory.CreateDirectory(dir);
        int n = 0;
        var files = Directory.GetFiles(src, "*.sbitemico");
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), CancellationToken = ct }, f =>
        {
            try
            {
                var tex = GameAssets.Textures(GameAssets.Yaz0(File.ReadAllBytes(f))).FirstOrDefault();
                if (tex == null) return;
                var px = GameAssets.DecodeRgba(tex);
                using var img = SKImage.FromPixelCopy(new SKImageInfo(tex.Width, tex.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul), px);
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(dir, Path.GetFileNameWithoutExtension(f) + ".png"), data.ToArray());
                Interlocked.Increment(ref n);
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException or IndexOutOfRangeException or ArgumentException) { }
        });
        return n;
    }

    static int CopyAll(string from, string to, string pattern)
    {
        if (!Directory.Exists(from)) return 0;
        Directory.CreateDirectory(to);
        int n = 0;
        foreach (var f in Directory.GetFiles(from, pattern))
        {
            var dest = Path.Combine(to, Path.GetFileName(f));
            File.Copy(f, dest, true);
            // (a dump's files can be read-only, and a copy keeps that: it would stop the folder being deleted later)
            if (File.GetAttributes(dest).HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(dest, FileAttributes.Normal);
            n++;
        }
        return n;
    }

    // ------------------------------------------------------------------ icons

    static List<string> ExtractIcons(string romfs, string dir)
    {
        Directory.CreateDirectory(dir);
        var pack = GameAssets.Sarc(File.ReadAllBytes(Path.Combine(romfs, "Pack", "Bootup.pack")));
        if (!pack.TryGetValue("Layout/Common.sblarc", out var blarc)) return [];
        var layout = GameAssets.Sarc(blarc);
        if (!layout.TryGetValue("timg/__Combined.bntx", out var bntx)) return [];
        var names = new List<string>();
        foreach (var t in GameAssets.Textures(bntx))
        {
            var name = t.Name.Split('^', '+')[0];
            if (!(name.Contains("Map", StringComparison.Ordinal) || name.Contains("StampIcon", StringComparison.Ordinal) ||
                  name.StartsWith("DLC_WarpMarker", StringComparison.Ordinal))) continue;
            byte[] px;
            try { px = GameAssets.DecodeRgba(t); } catch (NotSupportedException) { continue; }
            using var img = SKImage.FromPixelCopy(new SKImageInfo(t.Width, t.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul), px);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), data.ToArray());
            names.Add(name);
        }
        return names;
    }

    // ------------------------------------------------------------------ tiles

    /// <summary>Decoded squares as BC1 blocks in row-major order (750 x 750 x 8 bytes), loaded as rows are reached.</summary>
    readonly Dictionary<(int c, int r), byte[]?> squares = [];

    byte[]? SquareBlocks(string romfs, int c, int r)
    {
        lock (squares)
        {
            if (squares.TryGetValue((c, r), out var have)) return have;
        }
        byte[]? blocks = null;
        var path = Path.Combine(romfs, "UI", "MapTex", "MainField", $"MapTex_{Cols[c]}-{r}.sbmaptex");
        if (File.Exists(path))
        {
            var tex = GameAssets.Textures(GameAssets.Yaz0(File.ReadAllBytes(path)))[0];
            if (tex.Kind == 0x1A && tex.Width == Square && tex.Height == Square) blocks = GameAssets.Linear(tex, 8, true);
        }
        lock (squares) squares[(c, r)] = blocks;
        return blocks;
    }

    void DropRowsBefore(int r)
    {
        lock (squares)
            foreach (var k in squares.Keys.Where(k => k.r < r).ToList()) squares.Remove(k);
    }

    /// <summary>RGBA pixels of the zoom-8 region (x0, y0, w, h), from the squares' BC1 blocks.</summary>
    void Region(string romfs, int x0, int y0, int w, int h, byte[] rgba)
    {
        for (int i = 0; i < w * h; i++) { rgba[i * 4] = Background[0]; rgba[i * 4 + 1] = Background[1]; rgba[i * 4 + 2] = Background[2]; rgba[i * 4 + 3] = 255; }
        Span<int> pal = stackalloc int[16];
        int bx0 = x0 / 4, by0 = y0 / 4, bx1 = (x0 + w - 1) / 4, by1 = (y0 + h - 1) / 4;
        for (int by = by0; by <= by1; by++)
        {
            int gy = by * 4;
            if (gy >= MapH) break;
            int r = gy / Square, ly = (gy % Square) / 4;
            int lastC = -1;
            byte[]? blocks = null;
            for (int bx = bx0; bx <= bx1; bx++)
            {
                int gx = bx * 4;
                if (gx >= MapW) break;
                int c = gx / Square, lx = (gx % Square) / 4;
                if (c != lastC) { blocks = SquareBlocks(romfs, c, r); lastC = c; }
                if (blocks == null) continue;
                int o = (ly * (Square / 4) + lx) * 8;
                GameAssets.Bc1Palette(blocks, o, pal, false);
                uint bits = BinaryPrimitives.ReadUInt32LittleEndian(blocks.AsSpan(o + 4));
                for (int i = 0; i < 16; i++)
                {
                    int px = gx + (i & 3) - x0, py = gy + (i >> 2) - y0;
                    if ((uint)px >= (uint)w || (uint)py >= (uint)h) continue;
                    int sel = (int)(bits >> (2 * i)) & 3, d = (py * w + px) * 4;
                    rgba[d] = (byte)pal[sel * 4]; rgba[d + 1] = (byte)pal[sel * 4 + 1]; rgba[d + 2] = (byte)pal[sel * 4 + 2];
                }
            }
        }
    }

    static void Fill(byte[] rgba)
    {
        for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = Background[0]; rgba[i + 1] = Background[1]; rgba[i + 2] = Background[2]; rgba[i + 3] = 255; }
    }

    static void Half(byte[] src, int sw, int sh, byte[] dst)
    {
        int dw = sw / 2, dh = sh / 2;
        for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
                for (int k = 0; k < 3; k++)
                {
                    int a = ((2 * y) * sw + 2 * x) * 4 + k, b = a + sw * 4;
                    dst[(y * dw + x) * 4 + k] = (byte)((src[a] + src[a + 4] + src[b] + src[b + 4] + 2) >> 2);
                }
        for (int i = 3; i < dst.Length; i += 4) dst[i] = 255;
    }

    static long Save(string root, int z, int x, int y, byte[] rgba, int w = Tile, int h = Tile)
    {
        var dir = Path.Combine(root, z.ToString(), x.ToString());
        Directory.CreateDirectory(dir);
        using var img = SKImage.FromPixelCopy(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque), rgba);
        using var data = img.Encode(SKEncodedImageFormat.Webp, 82);
        var bytes = data.ToArray();
        File.WriteAllBytes(Path.Combine(dir, y + ".webp"), bytes);
        return bytes.Length;
    }

    long BuildTiles(string romfs, string root, CancellationToken ct)
    {
        long total = 0;
        // Zoom 7 tiles each cover a 512x512 zoom-8 region: decode it once, save its four zoom-8 tiles and itself, and
        // put a 128x128 copy in a two-row strip; each full strip becomes a row of zoom-6 tiles and a band of the
        // zoom-5 canvas (4544 x 3776), from which zoom 5 down to 0 are made at the end.
        int t7x = (MapW + 511) / 512, t7y = (MapH + 511) / 512;
        int z6w = t7x * 128;
        var strip = new byte[z6w * Tile * 4];
        int z5w = z6w / 2, z5h = (t7y + 1) / 2 * 128;
        var z5 = new byte[z5w * z5h * 4];
        var z6 = strip;
        Fill(strip); Fill(z5);
        int workers = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        int done = 0, all = t7x * t7y;
        for (int ty = 0; ty < t7y; ty++)
        {
            ct.ThrowIfCancellationRequested();
            DropRowsBefore(ty * 512 / Square);
            int row = ty;
            int next = -1;
            var threads = new Thread[workers];
            long rowBytes = 0;
            Exception? failure = null;  // a worker's error, raised here after the row (unhandled on a thread, it would end the app)
            for (int k = 0; k < workers; k++)
            {
                threads[k] = new Thread(() =>
                {
                    var region = new byte[512 * 512 * 4];
                    var tile = new byte[Tile * Tile * 4];
                    var half = new byte[Tile * Tile * 4];
                    var quarter = new byte[128 * 128 * 4];
                    long mine = 0;
                    int tx;
                    try
                    {
                    while ((tx = Interlocked.Increment(ref next)) < t7x && !ct.IsCancellationRequested && Volatile.Read(ref failure) == null)
                    {
                        Region(romfs, tx * 512, row * 512, 512, 512, region);
                        for (int q = 0; q < 4; q++)
                        {
                            int qx = q & 1, qy = q >> 1;
                            if (tx * 512 + qx * Tile >= MapW || row * 512 + qy * Tile >= MapH) continue;
                            for (int y = 0; y < Tile; y++)
                                Buffer.BlockCopy(region, ((qy * Tile + y) * 512 + qx * Tile) * 4, tile, y * Tile * 4, Tile * 4);
                            mine += Save(root, 8, tx * 2 + qx, row * 2 + qy, tile);
                        }
                        Half(region, 512, 512, half);
                        mine += Save(root, 7, tx, row, half);
                        Half(half, Tile, Tile, quarter);
                        for (int y = 0; y < 128; y++)
                            Buffer.BlockCopy(quarter, y * 128 * 4, z6, (((row % 2) * 128 + y) * z6w + tx * 128) * 4, 128 * 4);
                        Interlocked.Increment(ref done);
                        Progress = 0.9 * done / all;
                    }
                    }
                    catch (Exception e) { Interlocked.CompareExchange(ref failure, e, null); }
                    Interlocked.Add(ref rowBytes, mine);
                }) { IsBackground = true, Priority = ThreadPriority.Lowest };
                threads[k].Start();
            }
            foreach (var th in threads) th.Join();
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
            total += rowBytes;
            // Each square read leaves ~15 MB of large arrays behind; give them back now rather than let them pile up.
            if (row % 4 == 3)
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
            }
            Message = $"Building the map ({done * 100 / all}%)";
            if (row % 2 == 1 || row == t7y - 1)
            {
                // the strip holds zoom-6 tile row row/2: save it, then halve it into the zoom-5 canvas
                var tile = new byte[Tile * Tile * 4];
                for (int tx6 = 0; tx6 < (z6w + Tile - 1) / Tile; tx6++)
                {
                    Fill(tile);
                    for (int y = 0; y < Tile; y++)
                        Buffer.BlockCopy(strip, (y * z6w + tx6 * Tile) * 4, tile, y * Tile * 4, Math.Min(Tile, z6w - tx6 * Tile) * 4);
                    total += Save(root, 6, tx6, row / 2, tile);
                }
                var band = new byte[z5w * 128 * 4];
                Half(strip, z6w, Tile, band);
                Buffer.BlockCopy(band, 0, z5, (row / 2) * 128 * z5w * 4, band.Length);
                Fill(strip);
            }
        }
        ct.ThrowIfCancellationRequested();
        squares.Clear();

        // Zoom 5 down to 0 from the canvas, halving each time.
        Message = "Building the zoomed-out map";
        var canvas = z5; int cw = z5w, ch = z5h;
        for (int z = 5; z >= 0; z--)
        {
            ct.ThrowIfCancellationRequested();
            int nx = (cw + Tile - 1) / Tile, ny = (ch + Tile - 1) / Tile;
            var tile = new byte[Tile * Tile * 4];
            for (int ty = 0; ty < ny; ty++)
                for (int tx = 0; tx < nx; tx++)
                {
                    for (int i = 0; i < tile.Length; i += 4) { tile[i] = Background[0]; tile[i + 1] = Background[1]; tile[i + 2] = Background[2]; tile[i + 3] = 255; }
                    for (int y = 0; y < Tile && ty * Tile + y < ch; y++)
                    {
                        int wcopy = Math.Min(Tile, cw - tx * Tile);
                        Buffer.BlockCopy(canvas, ((ty * Tile + y) * cw + tx * Tile) * 4, tile, y * Tile * 4, wcopy * 4);
                    }
                    total += Save(root, z, tx, ty, tile);
                }
            if (z == 0) break;
            int hw = Math.Max(1, cw / 2), hh = Math.Max(1, ch / 2);
            var smaller = new byte[hw * hh * 4];
            Half(canvas, cw, ch, smaller);
            canvas = smaller; cw = hw; ch = hh;
            Progress = 0.9 + 0.1 * (6 - z) / 6;
        }
        return total;
    }
}
