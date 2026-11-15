using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace SlateCompanion.UI;

/// <summary>Where map tiles come from (files built from the game) and their scale: zoom z draws 2^z * PixelsPerUnitAtZero pixels per game unit.</summary>
public sealed record TileSource(string Name, Func<int, int, int, string> Locate, double PixelsPerUnitAtZero, int MaxNativeZoom, double MinZoom, double MaxZoom);

public enum MarkShape { Icon, Circle, Diamond, Ring }

/// <summary>Something drawn on the map at game coordinates.</summary>
public sealed class MapItem
{
    public required double X { get; init; }
    public required double Z { get; init; }
    public required string Id { get; init; }
    public Bitmap? Icon { get; set; }
    public Bitmap? Outline { get; set; }
    public IBrush? Fill { get; set; }
    public MarkShape Shape { get; set; } = MarkShape.Circle;
    public double Size { get; set; } = 14;
    public double MinZoom { get; set; } = double.NegativeInfinity;  // hidden below this zoom
    public double MaxZoom { get; set; } = double.PositiveInfinity;
    public bool Visible { get; set; } = true;
    public object? Tag { get; init; }
}

/// <summary>
/// The map. Draws the tiles on screen (a lower-zoom tile, scaled, until the sharp one has loaded) and the visible
/// items, and redraws only when something changes: a pan or zoom, a tile arriving, Link moving.
/// </summary>
public sealed class MapCanvas : Control
{
    const int TileSize = 256, CacheTiles = 220;
    TileSource? source;
    public double CenterX { get; private set; }
    public double CenterZ { get; private set; }
    public double Zoom { get; private set; } = 3;
    public List<MapItem> Items { get; } = [];
    public MapItem? Saved { get; set; }
    public (double x, double z, double? yaw)? Live { get; set; }
    public Bitmap? LiveArrow { get; set; }
    public bool ShowGrid { get; set; }

    public event Action<MapItem>? ItemClicked;
    public event Action<double, double>? RightClicked;
    public event Action<double, double>? PointerAt;

    readonly Dictionary<(int, int, int), Bitmap> cache = [];
    readonly LinkedList<(int, int, int)> lru = new();
    readonly HashSet<(int, int, int)> pending = [];
    readonly HashSet<(int, int, int)> missing = [];
    readonly SemaphoreSlim loads = new(4, 4);
    bool redrawQueued;
    Point? press, last;
    bool dragged;
    int generation;

    static readonly IBrush Background = new SolidColorBrush(Color.Parse("#0B181C"));
    static readonly IBrush Edge = new SolidColorBrush(Color.Parse("#05090C"));
    static readonly Pen DarkPen = new(new SolidColorBrush(Color.Parse("#CC000000")), 1.5);

    public MapCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.MediumQuality);
    }

    public TileSource? Source
    {
        get => source;
        set
        {
            source = value;
            generation++;
            foreach (var b in cache.Values) b.Dispose();
            cache.Clear(); lru.Clear(); pending.Clear(); missing.Clear();
            if (value != null) Zoom = Math.Clamp(Zoom, value.MinZoom, value.MaxZoom);
            InvalidateVisual();
        }
    }

    double Ppu => source == null ? 0.05 : Math.Pow(2, Zoom) * source.PixelsPerUnitAtZero;

    public Point ToScreen(double x, double z) => new((x - CenterX) * Ppu + Bounds.Width / 2, (z - CenterZ) * Ppu + Bounds.Height / 2);
    public (double x, double z) ToWorld(Point p) => ((p.X - Bounds.Width / 2) / Ppu + CenterX, (p.Y - Bounds.Height / 2) / Ppu + CenterZ);

    public void SetView(double x, double z, double? zoom = null)
    {
        CenterX = Math.Clamp(x, -7000, 7000);
        CenterZ = Math.Clamp(z, -6000, 6000);
        if (zoom != null && source != null) Zoom = Math.Clamp(zoom.Value, source.MinZoom, source.MaxZoom);
        Redraw();
    }

    /// <summary>Fit the whole of Hyrule in view.</summary>
    public void FitAll()
    {
        if (source == null || Bounds.Width < 10) { SetView(0, 0, 3); return; }
        double z = Math.Log2(Math.Min(Bounds.Width / 12000, Bounds.Height / 10000) / source.PixelsPerUnitAtZero);
        SetView(0, 0, Math.Floor(z * 2) / 2);
    }

    public void Redraw()
    {
        if (redrawQueued) return;
        redrawQueued = true;
        Dispatcher.UIThread.Post(() => { redrawQueued = false; InvalidateVisual(); }, DispatcherPriority.Render);
    }

    // ------------------------------------------------------------------ drawing

    public static int Renders;

    public override void Render(DrawingContext ctx)
    {
        Renders++;
        var size = Bounds.Size;
        ctx.FillRectangle(Edge, new Rect(size));
        if (source == null) return;
        double ppu = Ppu;
        int tz = (int)Math.Clamp(Math.Round(Zoom), 0, source.MaxNativeZoom);
        double unitsPerTile = TileSize / (Math.Pow(2, tz) * source.PixelsPerUnitAtZero);
        var (x0, z0) = ToWorld(new Point(0, 0));
        var (x1, z1) = ToWorld(new Point(size.Width, size.Height));
        int tx0 = (int)Math.Floor((Math.Max(x0, -6000) + 6000) / unitsPerTile), tx1 = (int)Math.Floor((Math.Min(x1, 5999.9) + 6000) / unitsPerTile);
        int ty0 = (int)Math.Floor((Math.Max(z0, -5000) + 5000) / unitsPerTile), ty1 = (int)Math.Floor((Math.Min(z1, 4999.9) + 5000) / unitsPerTile);
        var mapRect = new Rect(ToScreen(-6000, -5000), ToScreen(6000, 5000));
        ctx.FillRectangle(Background, mapRect);
        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
            {
                // Snapped to whole pixels so neighbouring tiles meet without a seam.
                var a = ToScreen(tx * unitsPerTile - 6000, ty * unitsPerTile - 5000);
                var c2 = ToScreen((tx + 1) * unitsPerTile - 6000, (ty + 1) * unitsPerTile - 5000);
                var dst = new Rect(new Point(Math.Floor(a.X), Math.Floor(a.Y)), new Point(Math.Ceiling(c2.X), Math.Ceiling(c2.Y)));
                if (Get(tz, tx, ty) is Bitmap bmp) { ctx.DrawImage(bmp, new Rect(0, 0, TileSize, TileSize), dst); continue; }
                // Until it loads, draw the matching part of a lower-zoom tile, scaled up.
                for (int up = 1; up <= Math.Min(4, tz); up++)
                {
                    int f = 1 << up;
                    if (Peek(tz - up, tx / f, ty / f) is not Bitmap parent) continue;
                    double part = (double)TileSize / f;
                    ctx.DrawImage(parent, new Rect(tx % f * part, ty % f * part, part, part), dst);
                    break;
                }
            }

        if (ShowGrid)
        {
            // the map's 1000-unit squares, lettered like the game's map grid (no map image)
            var line = new Pen(new SolidColorBrush(Color.Parse("#2839C6FF")), 1);
            var edge = new Pen(new SolidColorBrush(Color.Parse("#6039C6FF")), 1.5);
            for (int gx = -6000; gx <= 6000; gx += 1000) ctx.DrawLine(gx is -6000 or 6000 ? edge : line, ToScreen(gx, -5000), ToScreen(gx, 5000));
            for (int gz = -5000; gz <= 5000; gz += 1000) ctx.DrawLine(gz is -5000 or 5000 ? edge : line, ToScreen(-6000, gz), ToScreen(6000, gz));
        }

        // items: cheap culling by screen position
        foreach (var it in Items)
        {
            if (!it.Visible || Zoom < it.MinZoom || Zoom > it.MaxZoom) continue;
            var p = ToScreen(it.X, it.Z);
            double r = it.Size / 2;
            if (p.X < -r || p.Y < -r || p.X > size.Width + r || p.Y > size.Height + r) continue;
            DrawItem(ctx, it, p);
        }
        if (Saved != null) DrawItem(ctx, Saved, ToScreen(Saved.X, Saved.Z));
        if (Live is { } live)
        {
            var p = ToScreen(live.x, live.z);
            ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#4039C6FF")), new Pen(new SolidColorBrush(Color.Parse("#7039C6FF")), 2), p, 18, 18);
            if (LiveArrow != null && live.yaw is double yaw)
            {
                // yaw 0 faces +z (south on the map); the arrow image points north
                using (ctx.PushTransform(Matrix.CreateTranslation(-p.X, -p.Y) * Matrix.CreateRotation((180 - yaw) * Math.PI / 180) * Matrix.CreateTranslation(p.X, p.Y)))
                    ctx.DrawImage(LiveArrow, new Rect(p.X - 13, p.Y - 13, 26, 26));
            }
            else ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#39C6FF")), new Pen(Brushes.White, 2.5), p, 7, 7);
        }
    }

    static void DrawItem(DrawingContext ctx, MapItem it, Point p)
    {
        double s = it.Size, r = s / 2;
        switch (it.Shape)
        {
            case MarkShape.Icon when it.Icon != null:
                if (it.Outline != null) ctx.DrawImage(it.Outline, new Rect(p.X - r - 2, p.Y - r - 2, s + 4, s + 4));
                ctx.DrawImage(it.Icon, new Rect(p.X - r, p.Y - r, s, s));
                break;
            case MarkShape.Diamond:
                var g = new StreamGeometry();
                using (var c = g.Open()) { c.BeginFigure(new Point(p.X, p.Y - r), true); c.LineTo(new Point(p.X + r, p.Y)); c.LineTo(new Point(p.X, p.Y + r)); c.LineTo(new Point(p.X - r, p.Y)); c.EndFigure(true); }
                ctx.DrawGeometry(it.Fill, DarkPen, g);
                break;
            case MarkShape.Ring:
                ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#38FF9A3C")), new Pen(it.Fill ?? Brushes.Orange, 3), p, r, r);
                break;
            default:
                ctx.DrawEllipse(it.Fill, DarkPen, p, r, r);
                break;
        }
    }

    // ------------------------------------------------------------------ tiles

    Bitmap? Peek(int z, int x, int y) => cache.TryGetValue((z, x, y), out var b) ? b : null;

    Bitmap? Get(int z, int x, int y)
    {
        var key = (z, x, y);
        if (cache.TryGetValue(key, out var b)) { lru.Remove(key); lru.AddFirst(key); return b; }
        if (!missing.Contains(key) && pending.Add(key)) _ = Load(key, generation);
        return null;
    }

    async Task Load((int z, int x, int y) key, int gen)
    {
        var src = source!;
        await loads.WaitAsync();
        Bitmap? bmp = null;
        try
        {
            bmp = await Task.Run(() =>
            {
                var loc = src.Locate(key.z, key.x, key.y);
                return File.Exists(loc) ? new Bitmap(loc) : null;
            });
        }
        catch (Exception) { }
        finally { loads.Release(); }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            pending.Remove(key);
            if (gen != generation) { bmp?.Dispose(); return; }
            if (bmp == null) { missing.Add(key); return; }
            cache[key] = bmp;
            lru.AddFirst(key);
            while (lru.Count > CacheTiles) { var old = lru.Last!.Value; lru.RemoveLast(); if (cache.Remove(old, out var o)) o.Dispose(); }
            Redraw();
        });
    }

    // ------------------------------------------------------------------ input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        press = last = pt.Position;
        dragged = false;
        if (pt.Properties.IsRightButtonPressed)
        {
            var (x, z) = ToWorld(pt.Position);
            RightClicked?.Invoke(x, z);
            press = null;
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (press != null && last != null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var d = p - last.Value;
            if (!dragged && Math.Abs(p.X - press.Value.X) + Math.Abs(p.Y - press.Value.Y) > 4) dragged = true;
            if (dragged) { CenterX -= d.X / Ppu; CenterZ -= d.Y / Ppu; Redraw(); }
            last = p;
        }
        var (x, z) = ToWorld(p);
        PointerAt?.Invoke(x, z);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        if (press != null && !dragged)
        {
            var hit = HitTest(e.GetPosition(this));
            if (hit != null) ItemClicked?.Invoke(hit);
        }
        press = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (source == null) return;
        var p = e.GetPosition(this);
        var (wx, wz) = ToWorld(p);
        Zoom = Math.Clamp(Zoom + (e.Delta.Y > 0 ? 0.5 : -0.5), source.MinZoom, source.MaxZoom);
        // keep the point under the cursor where it is
        CenterX = wx - (p.X - Bounds.Width / 2) / Ppu;
        CenterZ = wz - (p.Y - Bounds.Height / 2) / Ppu;
        Redraw();
        e.Handled = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty) Redraw();
    }

    public void ZoomBy(double d)
    {
        if (source == null) return;
        Zoom = Math.Clamp(Zoom + d, source.MinZoom, source.MaxZoom);
        Redraw();
    }

    MapItem? HitTest(Point p)
    {
        MapItem? best = null; double bestD = double.MaxValue;
        foreach (var it in Items)
        {
            if (!it.Visible || Zoom < it.MinZoom || Zoom > it.MaxZoom) continue;
            var s = ToScreen(it.X, it.Z);
            double d = Math.Abs(s.X - p.X) + Math.Abs(s.Y - p.Y);
            if (d <= Math.Max(10, it.Size * 0.6) && d < bestD) { best = it; bestD = d; }
        }
        return best;
    }

    public void ReleaseTiles()
    {
        foreach (var b in cache.Values) b.Dispose();
        cache.Clear(); lru.Clear();
    }
}
