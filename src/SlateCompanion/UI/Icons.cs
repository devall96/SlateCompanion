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

    /// <summary>
    /// For the few map layers the game has no icon for (memory spots, Great Fairy fountains): one of the app's own
    /// line glyphs (a 32×32 SVG path) drawn into a 64-pixel bitmap, so it sits on the map like the game's icons.
    /// </summary>
    public static Bitmap? Drawn(string svgPath, Color color, float width = 3.6f)
    {
        var key = ("drawn:" + svgPath + width, color.ToUInt32());
        if (tinted.TryGetValue(key, out var b)) return b;
        try
        {
            using var path = SKPath.ParseSvgPathData(svgPath);
            using var surface = SKSurface.Create(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));
            var c = surface.Canvas;
            c.Clear(SKColors.Transparent);
            c.Scale(2);
            using var paint = new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round, Color = new SKColor(color.R, color.G, color.B, color.A),
            };
            c.DrawPath(path, paint);
            using var img = surface.Snapshot();
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            b = new Bitmap(new MemoryStream(data.ToArray()));
        }
        catch (Exception) { b = null; }
        return tinted[key] = b;
    }

    /// <summary>The dark edge drawn behind a <see cref="Drawn"/> icon.</summary>
    public static Bitmap? DrawnOutline(string svgPath) => Drawn(svgPath, Color.FromArgb(200, 0, 0, 0), 6.5f);
}
