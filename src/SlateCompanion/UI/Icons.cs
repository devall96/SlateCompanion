using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace SlateCompanion.UI;

/// <summary>
/// Icons extracted from the player's game files (gamemap/icons/*.png, built by MapBuilder). The game draws most map
/// icons as white masks it tints; here they're tinted once per colour and cached, with a dark outline copy for contrast.
/// </summary>
public static class Icons
{
    static readonly Dictionary<(string, uint), Bitmap?> tinted = [];
    static readonly Dictionary<string, Bitmap?> plain = [];

    public static string Folder => Path.Combine(App.MapBuilder.Folder, "icons");
    public static bool Available => !Program.Demo && App.MapBuilder.State == "ready" && Directory.Exists(Folder);

    public static void Reset()
    {
        foreach (var b in tinted.Values) b?.Dispose();
        foreach (var b in plain.Values) b?.Dispose();
        tinted.Clear(); plain.Clear();
    }

    /// <summary>The icon as the game stores it (for icons with their own light tile and dark symbol).</summary>
    public static Bitmap? Plain(string name)
    {
        if (plain.TryGetValue(name, out var b)) return b;
        var path = Path.Combine(Folder, name + ".png");
        try { b = File.Exists(path) ? new Bitmap(path) : null; } catch (Exception) { b = null; }
        return plain[name] = b;
    }

    /// <summary>The mask filled with one colour, keeping its alpha.</summary>
    public static Bitmap? Tinted(string name, Color color)
    {
        var key = (name, color.ToUInt32());
        if (tinted.TryGetValue(key, out var b)) return b;
        b = null;
        var path = Path.Combine(Folder, name + ".png");
        if (File.Exists(path))
        {
            try
            {
                using var src = SKBitmap.Decode(path);
                using var dst = new SKBitmap(new SKImageInfo(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
                for (int y = 0; y < src.Height; y++)
                    for (int x = 0; x < src.Width; x++)
                    {
                        var s = src.GetPixel(x, y);
                        dst.SetPixel(x, y, new SKColor(color.R, color.G, color.B, (byte)(s.Alpha * color.A / 255)));
                    }
                using var data = dst.Encode(SKEncodedImageFormat.Png, 100);
                b = new Bitmap(new MemoryStream(data.ToArray()));
            }
            catch (Exception) { b = null; }
        }
        return tinted[key] = b;
    }

    public static Bitmap? Outline(string name) => Tinted(name, Color.FromArgb(200, 0, 0, 0));
}
