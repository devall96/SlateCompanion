using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace SlateCompanion.UI.Views;

public sealed record Pin(double X, double Z, string Label);

/// <summary>
/// The map: the in-game map and icons, built from the player's own game files. Done things turn blue (or hide) as the save says; Link's live position comes from Savage Wind while this
/// screen is open and the window isn't minimized.
/// </summary>
public sealed class MapScreen : ViewBase
{
    static readonly (string id, string label, string color, string[] types)[] Layers =
    [
        ("shrine", "Shrines", "#FF9A3C", ["shrine"]), ("shrine_dlc", "DLC shrines", "#FFB36B", ["shrine_dlc"]),
        ("korok", "Korok seeds", "#FFD45E", []), ("tower", "Towers", "#39C6FF", ["tower"]),
        ("divine_beast", "Divine Beasts", "#FF6BB0", ["divine_beast"]), ("memory", "Memory spots", "#F7D77A", ["memory"]),
        ("great_fairy", "Great Fairies", "#F59CFF", ["great_fairy"]),
        ("places", "Towns, stables & landmarks", "#C9D8DE", ["village", "stable", "castle", "lab", "landmark", "hestu"]),
        ("pins", "My pins", "#FFFFFF", []),
    ];
    static readonly Dictionary<string, string> GameIcon = new()
    {
        ["shrine"] = "Nt_MapDungeonNewL_03", ["shrine_dlc"] = "Sd_MapDungeonNewLDLC_03", ["tower"] = "Nt_MapTowerNewL_03",
        ["korok"] = "Nt_MapIconKologL", ["castle"] = "Nt_MapCastleL_00", ["stable"] = "Nt_MapHatagoL_00", ["village"] = "Nt_MapCityIcon_00",
        ["lab"] = "Nt_MapReserchCenterL_00", ["hestu"] = "Nt_StampIconLeafL_00", ["pin"] = "Nt_MapPinNewFL_00", ["player"] = "Nt_MapPlayerIconNew_00",
    };
    static readonly HashSet<string> Boxed = ["castle", "stable", "village", "lab"];
    // Each Divine Beast's own icon (Nt_IconSinJuNewMapL_00..03: Rudania, Ruta, Medoh, Naboris).
    static readonly Dictionary<string, string> BeastIcon = new()
    {
        ["RemainsFire"] = "Nt_IconSinJuNewMapL_00", ["RemainsWater"] = "Nt_IconSinJuNewMapL_01",
        ["RemainsWind"] = "Nt_IconSinJuNewMapL_02", ["RemainsElectric"] = "Nt_IconSinJuNewMapL_03",
    };
    // The game has no map icon for these, so they get one of the app's line glyphs: a Sheikah Slate camera for the
    // memory photos, a sparkle for the Great Fairy fountains.
    static readonly Dictionary<string, string> DrawnIcon = new()
    {
        ["memory"] = Ui.Glyphs["compendium"],
        ["great_fairy"] = "M16 4Q17.6 14.4 28 16Q17.6 17.6 16 28Q14.4 17.6 4 16Q14.4 14.4 16 4z M25 4v5M22.5 6.5h5M7 23v4M5 25h4",
    };
    static readonly Color Beast = Color.Parse("#FF6BB0"), Memory = Color.Parse("#F7D77A"), Fairy = Color.Parse("#F59CFF");
    static readonly Color Todo = Color.Parse("#FF9A3C"), DoneC = Color.Parse("#4FD2FF"), Gold = Color.Parse("#FFD45E"), Grey = Color.Parse("#7C8A7A");
    const double KorokIconZoom = 6;

    readonly MapCanvas canvas = new();
    readonly TextBlock coords = Ui.Text("—", "faint mono");
    readonly StackPanel layerList = new() { Spacing = 2 };
    readonly StackPanel details = new() { Spacing = 6 };
    readonly TextBlock liveInfo = Ui.Text("Live position: waiting for Savage Wind", "muted small");
    readonly TextBlock savedInfo = Ui.Text("No save position yet.", "muted small");
    readonly CheckBox follow = new() { Content = "Follow Link" };
    readonly CheckBox hideDone = new() { Content = "Hide what's done" };
    readonly Dictionary<string, List<MapItem>> byLayer = [];
    List<Pin> pins = [];
    DispatcherTimer? liveTimer;
    bool useGame, initialView, polling, benchStarted;
    MapItem? selected;
    Border detailsPanel = null!;

    protected override Control Build()
    {
        canvas.ItemClicked += Select;
        canvas.RightClicked += NewPin;
        canvas.PointerAt += (x, z) => coords.Text = $"x {x:0} · z {z:0}";

        var zoomIn = Ui.Button("+", () => canvas.ZoomBy(0.5), "small");
        var zoomOut = Ui.Button("−", () => canvas.ZoomBy(-0.5), "small");
        var zoomBox = Ui.Stack(4, zoomIn, zoomOut);
        zoomBox.Margin = new Thickness(12); zoomBox.HorizontalAlignment = HorizontalAlignment.Left; zoomBox.VerticalAlignment = VerticalAlignment.Top;
        var coordBox = new Border { Child = coords, Background = new SolidColorBrush(Color.Parse("#D9071116")), BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4), Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        var mapArea = new Grid();
        mapArea.Children.Add(canvas);
        mapArea.Children.Add(zoomBox);
        mapArea.Children.Add(coordBox);

        detailsPanel = Ui.Panel(details, "panel glow");
        detailsPanel.IsVisible = false;
        details.Children.CollectionChanged += (_, _) => detailsPanel.IsVisible = details.Children.Count > 0;
        hideDone.IsChecked = App.Progress.Settings.HideDone;
        hideDone.IsCheckedChanged += (_, _) => { App.Progress.SaveSettings(s => s.HideDone = hideDone.IsChecked == true); Restyle(); };
        follow.IsCheckedChanged += (_, _) => RestartLive();

        var side = Ui.Stack(14,
            Ui.Stack(8, Ui.Text("LAYERS", "h2"), layerList),
            hideDone,
            detailsPanel,
            Ui.Panel(Ui.Stack(6, Ui.Text("Position", "h3"), savedInfo, Ui.Button("Show save spot", () => { if (App.Progress.Position is { } p) canvas.SetView(p[0], p[2], Math.Max(canvas.Zoom, 6)); }, "small"), liveInfo, follow)),
            new Border { Classes = { "note" }, Child = Ui.Text("Right-click the map to drop your own pin. The map is © Nintendo.", "small muted") });
        var sideScroll = new ScrollViewer { Content = new Border { Padding = new Thickness(14), Child = side } };
        var sideBorder = new Border { Width = 300, Child = sideScroll, BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(1, 0, 0, 0), Background = new SolidColorBrush(Color.Parse("#EB071116")) };

        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,300") };
        g.Children.Add(mapArea);
        Grid.SetColumn(sideBorder, 1);
        g.Children.Add(sideBorder);
        pins = App.Store.Get<List<Pin>>("pins") ?? [];
        Setup();
        return g;
    }

    /// <summary>Tiles, icons and items for the current map style.</summary>
    void Setup()
    {
        // The map always comes from the player's own game files (linked at first start).
        useGame = !Program.Demo;
        canvas.ShowGrid = Program.Demo;  // --demo: no map image, a plain grid of the map squares instead
        canvas.Source = new TileSource("game", (z, x, y) => useGame ? Path.Combine(App.MapBuilder.Folder, $"{z}", $"{x}", $"{y}.webp") : "", 3.0 / 256, 8, 1, 10);
        canvas.LiveArrow = useGame ? Icons.Tinted(GameIcon["player"], Colors.White) : null;

        canvas.Items.Clear();
        byLayer.Clear();
        foreach (var (id, _, _, _) in Layers) byLayer[id] = [];
        foreach (var m in App.Data.Markers)
        {
            var layer = Layers.FirstOrDefault(l => l.types.Contains(m.Type)).id;
            if (layer == null) continue;
            var it = new MapItem { X = m.X, Z = m.Z, Id = m.Id, Tag = m };
            byLayer[layer].Add(it); canvas.Items.Add(it);
        }
        foreach (var k in App.Data.Koroks)
        {
            // a dot when zoomed out, the game's icon when zoomed in
            var dot = new MapItem { X = k.X, Z = k.Z, Id = k.Id, Tag = k, Shape = MarkShape.Circle, Size = 8, MaxZoom = useGame ? KorokIconZoom - 0.01 : double.PositiveInfinity };
            byLayer["korok"].Add(dot); canvas.Items.Add(dot);
            if (useGame)
            {
                var icon = new MapItem { X = k.X, Z = k.Z, Id = k.Id, Tag = k, Shape = MarkShape.Icon, Size = 22, MinZoom = KorokIconZoom };
                byLayer["korok"].Add(icon); canvas.Items.Add(icon);
            }
        }
        DrawPins();
        Restyle();
    }

    void DrawPins()
    {
        canvas.Items.RemoveAll(i => i.Tag is Pin);
        byLayer["pins"].Clear();
        foreach (var p in pins)
        {
            var it = new MapItem { X = p.X, Z = p.Z, Id = "pin", Tag = p, Size = 26 };
            if (useGame && Icons.Tinted(GameIcon["pin"], Color.Parse("#FF5D5D")) is { } b) { it.Shape = MarkShape.Icon; it.Icon = b; it.Outline = Icons.Outline(GameIcon["pin"]); }
            else { it.Shape = MarkShape.Circle; it.Fill = Brushes.White; it.Size = 14; }
            byLayer["pins"].Add(it); canvas.Items.Add(it);
        }
    }

    static string KeyOf(MapItem it) => it.Tag switch
    {
        Marker m when m.Flag != null => $"{GameData.KindOf(m.Type)}:{m.Id}",
        Korok k => $"korok:{k.Id}",
        _ => "",
    };

    /// <summary>Colours and visibility from the progress; cheap enough to run on every save.</summary>
    void Restyle()
    {
        var off = App.Progress.Settings.MapLayersOff;
        bool hide = App.Progress.Settings.HideDone;
        foreach (var (layer, items) in byLayer)
            foreach (var it in items)
            {
                string key = KeyOf(it);
                bool done = key != "" && App.Progress.IsDone(key);
                it.Visible = !off.Contains(layer) && !(hide && done);
                Style(it, done);
            }
        if (App.Progress.Position is { } p)
            canvas.Saved = new MapItem { X = p[0], Z = p[2], Id = "saved", Shape = MarkShape.Ring, Size = 22, Fill = Ui.Brush("Orange") };
        savedInfo.Text = App.Progress.Position is { } q ? $"Saved at x {q[0]:0}, z {q[2]:0}" : "No save position yet.";
        DrawLayerList();
        canvas.Redraw();
    }

    void Style(MapItem it, bool done)
    {
        if (it.Tag is Korok)
        {
            if (it.Shape == MarkShape.Icon) { it.Icon = Icons.Tinted(GameIcon["korok"], done ? Grey : Gold); it.Outline = Icons.Outline(GameIcon["korok"]); }
            else it.Fill = new SolidColorBrush(done ? Grey : Gold, done ? 0.5 : 1);
            return;
        }
        if (it.Tag is not Marker m) return;
        if (useGame && LayerIcon(m, done) is { } own)
        {
            it.Shape = MarkShape.Icon;
            it.Icon = own.icon; it.Outline = own.outline;
            it.Size = m.Type == "divine_beast" ? 34 : 24;
            if (it.Icon != null) return;
        }
        if (useGame && GameIcon.TryGetValue(m.Type, out var name))
        {
            it.Shape = MarkShape.Icon;
            it.Size = m.Type switch { "tower" => 32, "shrine" or "shrine_dlc" or "castle" => 30, "lab" => 26, "stable" => 24, _ => 22 };
            if (Boxed.Contains(m.Type)) { it.Icon = Icons.Plain(name); it.Outline = null; }
            else
            {
                var c = m.Type is "shrine" or "shrine_dlc" or "tower" ? (done ? DoneC : Todo) : Color.Parse("#E8F4F8");
                it.Icon = Icons.Tinted(name, c); it.Outline = Icons.Outline(name);
            }
            if (it.Icon != null) return;
        }
        it.Shape = m.Type is "shrine" or "shrine_dlc" ? MarkShape.Diamond : MarkShape.Circle;
        var color = Layers.First(l => l.types.Contains(m.Type)).color;
        it.Fill = new SolidColorBrush(Color.Parse(done ? "#4FD2FF" : color), done ? 0.55 : 1);
        it.Size = m.Type is "divine_beast" ? 20 : m.Type is "shrine" or "shrine_dlc" or "tower" or "great_fairy" ? 16 : 11;
    }

    /// <summary>A Divine Beast's own icon, or the drawn one for a memory spot or Great Fairy (null for the rest).</summary>
    static (Bitmap? icon, Bitmap? outline)? LayerIcon(Marker m, bool done)
    {
        if (m.Type == "divine_beast" && BeastIcon.TryGetValue(m.Id, out var beast))
            return (Icons.Tinted(beast, done ? DoneC : Beast), Icons.Outline(beast));
        if (DrawnIcon.TryGetValue(m.Type, out var path))
            return (Icons.Drawn(path, done ? DoneC : m.Type == "memory" ? Memory : Fairy), Icons.DrawnOutline(path));
        return null;
    }

    /// <summary>The icon a layer's row shows: the same one the map draws for that layer (not yet done).</summary>
    Bitmap? RowIcon(string layer) => layer switch
    {
        "shrine" or "shrine_dlc" or "tower" => Icons.Tinted(GameIcon[layer], Todo),
        "korok" => Icons.Tinted(GameIcon["korok"], Gold),
        "divine_beast" => Icons.Tinted(BeastIcon["RemainsWater"], Beast),
        "memory" => Icons.Drawn(DrawnIcon["memory"], Memory),
        "great_fairy" => Icons.Drawn(DrawnIcon["great_fairy"], Fairy),
        "places" => Icons.Plain(GameIcon["village"]),
        "pins" => Icons.Tinted(GameIcon["pin"], Color.Parse("#FF5D5D")),
        _ => null,
    };

    void DrawLayerList()
    {
        layerList.Children.Clear();
        var off = App.Progress.Settings.MapLayersOff;
        foreach (var (id, label, color, _) in Layers)
        {
            var items = byLayer[id].Where(i => i.Shape != MarkShape.Icon || i.Tag is not Korok).ToList();
            var tracked = items.Where(i => KeyOf(i) != "").ToList();
            string count = id == "pins" ? $"{pins.Count}" : tracked.Count > 0 ? $"{tracked.Count(i => App.Progress.IsDone(KeyOf(i)))}/{tracked.Count}" : $"{items.Count}";
            var cb = new CheckBox { IsChecked = !off.Contains(id) };
            cb.IsCheckedChanged += (_, _) =>
            {
                App.Progress.SaveSettings(s => { s.MapLayersOff.Remove(id); if (cb.IsChecked != true) s.MapLayersOff.Add(id); });
                Restyle();
            };
            // the map's own icon for the layer; a coloured dot only if the icons aren't there
            Control swatch = useGame && RowIcon(id) is { } icon
                ? new Image { Source = icon, Width = id is "memory" or "great_fairy" or "places" ? 20 : 30, Height = id is "memory" or "great_fairy" or "places" ? 20 : 30, Stretch = Stretch.Uniform, Margin = new Thickness(-4) }
                : new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.Parse(color)), BoxShadow = BoxShadows.Parse($"0 0 6 0 {color}") };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,28,*,Auto") };
            g.Children.Add(cb);
            Grid.SetColumn(swatch, 1); if (swatch is Border) swatch.Margin = new Thickness(0, 0, 8, 0); swatch.VerticalAlignment = VerticalAlignment.Center; swatch.HorizontalAlignment = HorizontalAlignment.Center; g.Children.Add(swatch);
            var t = Ui.Text(label, "small"); t.Margin = new Thickness(6, 0, 0, 0); t.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(t, 2); g.Children.Add(t);
            var n = Ui.Text(count, "faint mono"); n.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(n, 3); g.Children.Add(n);
            layerList.Children.Add(g);
        }
    }

    void Select(MapItem it)
    {
        selected = it;
        details.Children.Clear();
        switch (it.Tag)
        {
            case Marker m:
                {
                    string key = KeyOf(it);
                    // memory spots: the photo from the Sheikah Slate album, and the memory's name
                    var memory = m.Type == "memory" ? App.Data.Flags.Memories.FirstOrDefault(x => x.Flag == m.Flag) : null;
                    if (m.Type == "memory" && m.Id.StartsWith("Memory_Demo") && int.TryParse(m.Id.AsSpan(11, 3), out var demo)
                        && Ui.Picture(Assets.Album(demo - 126, 264), 264) is { } photo) details.Children.Add(photo);
                    details.Children.Add(Ui.Text(memory?.Name ?? m.Name, "h3"));
                    details.Children.Add(Ui.Text(GameData.PlaceLabel(m.Type), "muted small"));
                    if (m.Notes != null && m.Type != "memory") details.Children.Add(Ui.Text(m.Notes, "small"));  // (memory notes are internal ids)
                    details.Children.Add(Ui.Text($"x {m.X:0} · y {m.Y:0} · z {m.Z:0}", "faint mono"));
                    if (key != "") details.Children.Add(DoneControl(key));
                    if (m.Type is "shrine" or "shrine_dlc") details.Children.Add(Ui.Button("Open shrine guide", () => App.Main.Go("shrines", m.Id), "small"));
                    break;
                }
            case Korok k:
                details.Children.Add(Ui.Text($"Korok seed {k.Id}", "h3"));
                details.Children.Add(Ui.Text(k.Kind ?? "Korok seed", "muted small"));
                details.Children.Add(Ui.Text($"x {k.X:0} · y {k.Y:0} · z {k.Z:0}", "faint mono"));
                details.Children.Add(DoneControl($"korok:{k.Id}"));
                break;
            case Pin p:
                details.Children.Add(Ui.Text(p.Label, "h3"));
                details.Children.Add(Ui.Text($"x {p.X:0} · z {p.Z:0}", "faint mono"));
                details.Children.Add(Ui.Button("Remove pin", () => { pins.Remove(p); App.Store.Put("pins", pins); DrawPins(); Restyle(); details.Children.Clear(); }, "small"));
                break;
        }
    }

    Control DoneControl(string key)
    {
        if (App.Progress.FromSave(key)) return Ui.Tag("Done in your save", "blue");
        bool done = App.Progress.IsDone(key);
        return Ui.Button(done ? "Untick" : "Mark done", () => { App.Progress.ToggleManual(key); }, "small");
    }

    void NewPin(double x, double z)
    {
        details.Children.Clear();
        var name = new TextBox { Text = "My pin", MaxLength = 60 };
        details.Children.Add(Ui.Text("New pin", "h3"));
        details.Children.Add(Ui.Text($"x {x:0} · z {z:0}", "faint mono"));
        details.Children.Add(name);
        details.Children.Add(Ui.Row(8,
            Ui.Button("Add", () => { pins.Add(new Pin(x, z, string.IsNullOrWhiteSpace(name.Text) ? "Pin" : name.Text.Trim())); App.Store.Put("pins", pins); DrawPins(); Restyle(); details.Children.Clear(); }, "small primary"),
            Ui.Button("Cancel", () => details.Children.Clear(), "small ghost")));
        name.Focus(); name.SelectAll();
    }

    // ------------------------------------------------------------------ shown / hidden, and the live position

    protected override void OnShown(object? arg)
    {
        if (Environment.GetEnvironmentVariable("SLATE_MAP_BENCH") is { Length: > 0 } bench && !benchStarted)
        {
            benchStarted = true;
            Dispatcher.UIThread.Post(async () =>
            {
                App.Main.WindowState = WindowState.Maximized;
                await Task.Delay(3000);
                var s = await canvas.Bench(bench);
                Console.WriteLine(s);
                App.Main.Close();
            }, DispatcherPriority.Background);
        }
        App.Progress.Changed += OnProgress;
        Restyle();
        if (arg is string focus)
        {
            var it = canvas.Items.FirstOrDefault(i => i.Id == focus && i.Shape != MarkShape.Circle) ?? canvas.Items.FirstOrDefault(i => i.Id == focus);
            if (it != null) { Dispatcher.UIThread.Post(() => { canvas.SetView(it.X, it.Z, 6); Select(it); }, DispatcherPriority.Loaded); initialView = true; }
        }
        if (!initialView)
        {
            initialView = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (App.Progress.Position is { } p) canvas.SetView(p[0], p[2], 4); else canvas.FitAll();
            }, DispatcherPriority.Loaded);
        }
        RestartLive();
    }

    public override void OnHidden()
    {
        App.Progress.Changed -= OnProgress;
        liveTimer?.Stop();
        liveTimer = null;
    }

    // give the tile memory back while the map isn't on screen (kept while the window is only in the background)
    protected override void OnLeft() => canvas.ReleaseTiles();

    void OnProgress()
    {
        Restyle();
        if (selected != null && details.Children.Count > 0 && selected.Tag is not Pin) Select(selected);  // the done state shown may have changed
    }

    void RestartLive()
    {
        liveTimer?.Stop();
        if (!App.Progress.Settings.LiveEnabled) { liveInfo.Text = "Live position: off (Saves & settings)"; canvas.Live = null; return; }
        liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        liveTimer.Tick += async (_, _) => await PollLive();
        liveTimer.Start();
        _ = PollLive();
    }

    async Task PollLive()
    {
        if (polling) return;
        polling = true;
        try
        {
            var r = await Task.Run(() => LiveLink.QueryAsync(App.Progress.Settings.LivePort, CancellationToken.None));
            bool on = r["connected"]?.GetValue<bool>() == true && r["x"] != null;
            // connected: every second (half a second when following); not: every 5 seconds
            if (liveTimer != null) liveTimer.Interval = TimeSpan.FromMilliseconds(on ? (follow.IsChecked == true ? 500 : 1000) : 5000);
            if (on)
            {
                double x = r["x"]!.GetValue<double>(), z = r["z"]!.GetValue<double>();
                double? yaw = r["yaw"]?.GetValue<double>(), hour = r["hour"]?.GetValue<double>();
                var prev = canvas.Live;
                if (prev == null || Math.Abs(prev.Value.x - x) > 0.05 || Math.Abs(prev.Value.z - z) > 0.05 || prev.Value.yaw != yaw)
                {
                    canvas.Live = (x, z, yaw);
                    if (follow.IsChecked == true) canvas.SetView(x, z); else canvas.Redraw();
                }
                liveInfo.Text = $"● Live  x {x:0} · z {z:0}" + (hour is double h ? $" · {(int)h:00}:{(int)(h % 1 * 60):00}" : "");
                liveInfo.Foreground = Ui.Brush("Glow");
            }
            else
            {
                if (canvas.Live != null) { canvas.Live = null; canvas.Redraw(); }
                liveInfo.Text = "Live: " + (r["error"]?.GetValue<string>() ?? "waiting for Savage Wind");
                liveInfo.Foreground = Ui.Brush("Muted");
            }
        }
        catch (Exception) { }
        finally { polling = false; }
    }
}
