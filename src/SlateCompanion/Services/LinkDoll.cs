using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace SlateCompanion;

/// <summary>
/// The paper doll's picture: Link wearing what he has equipped, drawn from the player's own game files by
/// <see cref="ModelRender.RenderDoll"/> (his body, the armor, weapon, shield and bow models, one pass, one pose). It's
/// drawn once per outfit, in the background, and kept as a PNG next to the other things built from the game files
/// (gamemap/doll/), so changing gear back and forth costs nothing the second time. Nothing of it ships with the app.
/// </summary>
public static class LinkDoll
{
    const int Version = 1;  // (raise when the renderer's look changes: old pictures are drawn again)
    const int Size = 768;
    static readonly Dictionary<string, Task<string?>> running = [];
    static readonly Lock gate = new();

    static string Folder => Path.Combine(App.MapBuilder.Folder, "doll");

    static string? Romfs => MapBuilder.FindRomfs(App.Progress.Settings.GameFilesFolder, App.Progress.Settings.SavageWindFolders);

    public static bool Available => !Program.Demo && Romfs != null;

    /// <summary>OpenGL couldn't start this session: the still picture is used instead.</summary>
    public static bool GpuFailed { get; set; }

    // the posed outfits for the 3D view, the last few kept (switching gear back and forth is instant)
    static readonly Dictionary<string, Task<OutfitMesh?>> meshes = [];
    static readonly LinkedList<string> meshOrder = new();

    /// <summary>The outfit as one posed mesh with its textures, for the live 3D view (built in the background).</summary>
    public static Task<OutfitMesh?> MeshAsync(Outfit o)
    {
        var key = Key(o);
        lock (gate)
        {
            if (meshes.TryGetValue(key, out var t)) { meshOrder.Remove(key); meshOrder.AddFirst(key); return t; }
            var romfs = Romfs;
            t = Task.Run(() =>
            {
                if (romfs == null) return null;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { var m = ModelRender.BuildOutfit(romfs, o); Log.Info($"3D Link: outfit built in {sw.ElapsedMilliseconds} ms: {o}"); return m; }
                catch (Exception e) { Log.Warn($"3D Link: outfit couldn't be built ({o}): {e.Message}"); lock (gate) meshes.Remove(key); return (OutfitMesh?)null; }
            });
            meshes[key] = t; meshOrder.AddFirst(key);
            while (meshOrder.Count > 4) { meshes.Remove(meshOrder.Last!.Value); meshOrder.RemoveLast(); }
            return t;
        }
    }

    static string Key(Outfit o)
    {
        var s = $"{Version}|{o.Head}|{o.Upper}|{o.Lower}|{o.Weapon}|{o.Shield}|{o.Bow}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..16].ToLowerInvariant();
    }

    /// <summary>The picture if it's drawn already (else null: call <see cref="DrawAsync"/>).</summary>
    public static Bitmap? Cached(Outfit o)
    {
        var p = Path.Combine(Folder, Key(o) + ".png");
        try { return File.Exists(p) ? new Bitmap(p) : null; } catch (Exception) { return null; }
    }

    /// <summary>Draws it (once per outfit, even when asked twice) and returns the PNG's path, or null if it couldn't.</summary>
    public static Task<string?> DrawAsync(Outfit o)
    {
        var key = Key(o);
        lock (gate)
        {
            if (running.TryGetValue(key, out var t)) return t;
            t = Task.Run(() => Draw(o, key));
            running[key] = t;
            return t;
        }
    }

    static string? Draw(Outfit o, string key)
    {
        var romfs = Romfs;
        if (romfs == null) return null;
        var path = Path.Combine(Folder, key + ".png");
        if (File.Exists(path)) return path;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var bmp = ModelRender.RenderDoll(romfs, o, Size);
            Directory.CreateDirectory(Folder);
            using (var data = bmp.Encode(SKEncodedImageFormat.Png, 100))
            using (var f = File.Create(path + ".tmp")) data.SaveTo(f);
            File.Move(path + ".tmp", path, true);
            Log.Info($"Paper doll drawn in {sw.ElapsedMilliseconds} ms: {o}");
            return path;
        }
        catch (Exception e)
        {
            Log.Warn($"Paper doll couldn't be drawn ({o}): {e.Message}");
            return null;
        }
        finally { lock (gate) running.Remove(key); }
    }
}
