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
    // made once, not on every frame
    static readonly IBrush LiveFill = new SolidColorBrush(Color.Parse("#4039C6FF")), LiveDot = new SolidColorBrush(Color.Parse("#39C6FF")), RingFill = new SolidColorBrush(Color.Parse("#38FF9A3C"));
    static readonly Pen LivePen = new(new SolidColorBrush(Color.Parse("#7039C6FF")), 2), LiveDotPen = new(Brushes.White, 2.5);
    static readonly Dictionary<double, StreamGeometry> Diamonds = [];
    static readonly Dictionary<IBrush, Pen> RingPens = [];

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
            ctx.DrawEllipse(LiveFill, LivePen, p, 18, 18);
            if (LiveArrow != null && live.yaw is double yaw)
            {
                // yaw 0 faces +z (south on the map); the arrow image points north
                using (ctx.PushTransform(Matrix.CreateTranslation(-p.X, -p.Y) * Matrix.CreateRotation((180 - yaw) * Math.PI / 180) * Matrix.CreateTranslation(p.X, p.Y)))
                    ctx.DrawImage(LiveArrow, new Rect(p.X - 13, p.Y - 13, 26, 26));
            }
            else ctx.DrawEllipse(LiveDot, LiveDotPen, p, 7, 7);
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
                // one shape per size around (0,0), moved into place
                if (!Diamonds.TryGetValue(r, out var g))
                {
                    g = new StreamGeometry();
                    using (var c = g.Open()) { c.BeginFigure(new Point(0, -r), true); c.LineTo(new Point(r, 0)); c.LineTo(new Point(0, r)); c.LineTo(new Point(-r, 0)); c.EndFigure(true); }
                    Diamonds[r] = g;
                }
                using (ctx.PushTransform(Matrix.CreateTranslation(p.X, p.Y))) ctx.DrawGeometry(it.Fill, DarkPen, g);
                break;
            case MarkShape.Ring:
                var ringBrush = it.Fill ?? Brushes.Orange;
                if (!RingPens.TryGetValue(ringBrush, out var ringPen)) RingPens[ringBrush] = ringPen = new Pen(ringBrush, 3);
                ctx.DrawEllipse(RingFill, ringPen, p, r, r);
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

    // ------------------------------------------------------------------ benchmark (SLATE_MAP_BENCH=<csv>)

    /// <summary>
    /// A scripted pan and zoom, one step per frame the window asks for (RequestAnimationFrame), so a slow map shows as
    /// longer frames: zoomed out across Hyrule, the middle zoom, then zoomed in, each twice. Every frame's interval
    /// goes to the CSV; the summary (average, 1% and 0.1% lows, p50/p99/p99.9/max, frames over 25/33/50/100 ms,
    /// stutter) to the log. Returns the summary.
    /// </summary>
    public async Task<string> Bench(string csvPath)
    {
        var top = TopLevel.GetTopLevel(this)!;
        var frames = new List<(string phase, double ms)>();
        double lastTs = -1;
        async Task Phase(string name, double zoom, int count, double stepPx)
        {
            ZoomTo(zoom);
            await Task.Delay(1500);  // the tiles for this zoom load first: this measures drawing, not file reads
            var done = new TaskCompletionSource();
            int i = 0;
            lastTs = -1;
            void Step(TimeSpan ts)
            {
                double now = ts.TotalMilliseconds;
                if (lastTs >= 0) frames.Add((name, now - lastTs));
                lastTs = now;
                if (i++ >= count) { done.SetResult(); return; }
                // a slow circle, so new map comes into view in every direction
                double a = i * 2 * Math.PI / 120;
                CenterX += Math.Cos(a) * stepPx / Ppu; CenterZ += Math.Sin(a) * stepPx / Ppu;
                ViewChanged();
                top.RequestAnimationFrame(Step);
            }
            top.RequestAnimationFrame(Step);
            await done.Task;
        }
        SetView(0, 0);
        for (int run = 0; run < 2; run++)
        {
            await Phase("whole", 4.5, 240, 14);
            await Phase("middle", 6, 240, 14);
            await Phase("close", 7.5, 240, 14);
        }
        var sb = new System.Text.StringBuilder("phase,frame_ms\n");
        foreach (var (p, ms) in frames) sb.Append(p).Append(',').Append(ms.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        File.WriteAllText(csvPath, sb.ToString());
        var lines = new List<string>();
        foreach (var g in frames.GroupBy(f => f.phase).Append(frames.GroupBy(_ => "all").First()))
        {
            var ms = g.Select(f => f.ms).OrderBy(x => x).ToArray();
            double P(double q) => ms[Math.Min(ms.Length - 1, (int)Math.Floor(q * ms.Length))];
            double Low(double frac) { int n = Math.Max(1, (int)(ms.Length * frac)); return 1000 / ms.Skip(ms.Length - n).Average(); }
            var seq = g.Select(f => f.ms).ToArray();
            int stutter = seq.Zip(seq.Skip(1), (a, b) => Math.Abs(a - b) > 4 ? 1 : 0).Sum();
            lines.Add($"{g.Key,-6} frames {ms.Length}, avg {1000 / ms.Average():0.0} fps, 1% low {Low(0.01):0.0}, 0.1% low {Low(0.001):0.0}, " +
                      $"p50 {P(0.5):0.0} ms, p99 {P(0.99):0.0}, p99.9 {P(0.999):0.0}, max {ms[^1]:0.0}, " +
                      $">25ms {ms.Count(x => x > 25)}, >33 {ms.Count(x => x > 33)}, >50 {ms.Count(x => x > 50)}, >100 {ms.Count(x => x > 100)}, stutter {stutter}");
        }
        var summary = string.Join("\n", lines);
        Log.Info("Map benchmark:\n" + summary);
        return summary;
    }

    void ZoomTo(double z)
    {
        if (source == null) return;
        Zoom = Math.Clamp(z, source.MinZoom, source.MaxZoom);
        ViewChanged();
    }

    /// <summary>The view moved or zoomed (nothing on the map changed).</summary>
    void ViewChanged() => Redraw();

    public void ReleaseTiles()
    {
        foreach (var b in cache.Values) b.Dispose();
        cache.Clear(); lru.Clear();
    }
}
