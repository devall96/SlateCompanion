using Avalonia;
using Avalonia.Controls;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Path = System.IO.Path;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SlateCompanion.UI;
using SlateCompanion.UI.Views;

namespace SlateCompanion;

/// <summary>A screen of the app. Built once; told when it is shown and hidden so it only works while on screen.</summary>
public abstract class ViewBase : UserControl
{
    bool built, active;
    object? pendingArg;
    protected abstract Control Build();

    /// <summary>
    /// The screen is opened (with what to show, e.g. a shrine to focus). While the window is paused it only remembers
    /// the argument and starts its work on <see cref="Resume"/>, so OnShown and OnHidden always come in pairs.
    /// </summary>
    public void Show(object? arg, bool paused = false)
    {
        if (!built) { Content = Build(); built = true; }
        if (paused) { pendingArg = arg; return; }
        if (active) OnHidden();
        active = true;
        pendingArg = null;
        OnShown(arg);
    }

    /// <summary>The screen stops its work: left for another screen (<paramref name="leaving"/>) or the window is paused.</summary>
    public void Hide(bool leaving)
    {
        if (active) { active = false; OnHidden(); }
        if (leaving) { pendingArg = null; OnLeft(); }
    }

    /// <summary>Back from a pause: picks up where it was, without replaying the argument it was first opened with.</summary>
    public void Resume()
    {
        if (!built || active) return;
        var arg = pendingArg;
        pendingArg = null;
        active = true;
        OnShown(arg);
    }

    protected virtual void OnShown(object? arg) { }
    public virtual void OnHidden() { }
    /// <summary>Another screen took over (or the window was minimized): let go of memory that's quick to get back.</summary>
    protected virtual void OnLeft() { }
}

public sealed class MainWindow : Window
{
    static readonly (string id, string label)[] Nav =
    [
        ("home", "Adventure log"), ("launch", "Play Savage Wind"), ("map", "Map"), ("quests", "Quests & walkthrough"), ("shrines", "Shrines"),
        ("koroks", "Korok seeds"), ("inventory", "Inventory"), ("cooking", "Cooking"), ("compendium", "Hyrule Compendium"), ("armor", "Armor & upgrades"),
        ("saves", "Save files"), ("guides", "Guides & tips"), ("zeldadungeon", "Zelda Dungeon"),
    ];

    readonly Dictionary<string, Button> runes = [];
    readonly Dictionary<string, ViewBase> views = [];
    readonly ContentControl host = new() { Margin = new Thickness(0) };
    readonly TextBlock syncText = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
    readonly Ellipse syncDot = new() { Width = 9, Height = 9 };
    readonly Border toast;
    readonly TextBlock toastText = new();
    readonly SearchOverlay search;
    readonly SetupOverlay setup;
    readonly Border updateBar = new() { IsVisible = false, Background = new SolidColorBrush(Color.Parse("#2639C6FF")), BorderBrush = Ui.Brush("LineHi"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(18, 8) };
    ViewBase? current;
    string currentId = "home";
    DispatcherTimer? toastTimer;
    DateTime? lastStamp;

    public MainWindow()
    {
        Title = "Slate Companion";
        Width = 1400; Height = 900; MinWidth = 900; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://SlateCompanion/Assets/icon.ico"))); } catch (Exception) { }

        // rail
        var rail = new StackPanel { Spacing = 4, Margin = new Thickness(0, 14), HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (id, label) in Nav) rail.Children.Add(Rune(id, label));
        var railGrid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        railGrid.Children.Add(new ScrollViewer { Content = rail, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden });
        var settingsRune = Rune("settings", "Saves & settings");
        settingsRune.Margin = new Thickness(0, 0, 0, 14);
        settingsRune.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetRow(settingsRune, 1);
        railGrid.Children.Add(settingsRune);
        var railBorder = new Border
        {
            Width = 76, Child = railGrid, BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(0, 0, 1, 0),
            Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = { new(Color.Parse("#F20B1A21"), 0), new(Color.Parse("#F2071116"), 1) } },
        };

        // top bar
        var brand = Ui.Row(12, Ui.Eye(40), Ui.Stack(0, Ui.Text("SLATE COMPANION", "h3", 14), Ui.Text("Unofficial companion for Breath of the Wild", "faint", 11)));
        ((TextBlock)((StackPanel)brand.Children[1]).Children[0]).LetterSpacing = 3;
        var searchBtn = new Button { Content = Ui.Row(8, Ui.Glyph("search", 15), Ui.Text("Search", "muted"), Ui.Tag("Ctrl K")), CornerRadius = new CornerRadius(17), Padding = new Thickness(12, 5) };
        searchBtn.Click += (_, _) => search!.Open();
        var pill = new Button { Content = Ui.Row(8, syncDot, syncText), CornerRadius = new CornerRadius(17), Padding = new Thickness(12, 6), MaxWidth = 520 };
        pill.Click += (_, _) => Go("settings");
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(18, 10) };
        top.Children.Add(brand);
        Grid.SetColumn(searchBtn, 1); searchBtn.Margin = new Thickness(0, 0, 12, 0); top.Children.Add(searchBtn);
        Grid.SetColumn(pill, 2); top.Children.Add(pill);
        var topBorder = new Border { Child = top, BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Background = new SolidColorBrush(Color.Parse("#CC0B1A21")) };

        var screen = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        screen.Children.Add(topBorder);
        Grid.SetRow(updateBar, 1);
        screen.Children.Add(updateBar);
        Grid.SetRow(host, 2);
        screen.Children.Add(host);

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("76,*") };
        layout.Children.Add(railBorder);
        Grid.SetColumn(screen, 1);
        layout.Children.Add(screen);

        toast = new Border
        {
            Child = toastText, Background = Ui.Brush("Panel"), BorderBrush = Ui.Brush("Glow"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 10), Margin = new Thickness(0, 0, 0, 22),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, IsVisible = false,
        };
        search = new SearchOverlay(Go);
        // (a web page is a native window drawn above everything of the app's: it steps aside while the search is open)
        search.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) (views.GetValueOrDefault("zeldadungeon") as ZeldaDungeonView)?.Cover(search.IsVisible); };
        // Nothing works without the player's game files: until they're linked and built, this covers the app.
        setup = new SetupOverlay(() => { views.Clear(); Go("home"); Toast("Built from your game files"); });

        var root = new Grid();
        root.Children.Add(new HexBackground());
        root.Children.Add(layout);
        root.Children.Add(toast);
        root.Children.Add(search);
        root.Children.Add(setup);
        Content = root;
        setup.Show();

        KeyDown += (_, e) =>
        {
            if (setup.IsVisible) return;
            if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { search.Open(); e.Handled = true; }
            else if (e.Key == Key.Escape && search.IsVisible) { search.Close(); e.Handled = true; }
        };

        // Drop a save folder, a game_data.sav or a .zip anywhere on the window.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Link);
        AddHandler(DragDrop.DropEvent, (_, e) => OnDrop(e));

        // Minimized or in the background (another window has the focus, e.g. the game): the screen stops all its
        // work (timers, polling, redrawing) and catches up when it's back. The map's live position can be kept moving
        // in the background for a second screen (Settings: off by default).
        PropertyChanged += (_, e) =>
        {
            if (e.Property != WindowStateProperty) return;
            if (WindowState == WindowState.Minimized) Pause();
            else if (e.OldValue is WindowState.Minimized && IsActive) Resume();
        };
        Deactivated += (_, _) => { if (!(currentId == "map" && App.Progress.Settings.LiveInBackground)) Pause(); };
        Activated += (_, _) => Resume();

        App.Progress.Changed += () => { if (!paused) RenderPill(); };
        pillTimer.Tick += (_, _) => { if (!paused) RenderPill(); };
        pillTimer.Start();
        RenderPill();
        ShowLauncher(App.Progress.Settings.UseLauncher);

        var start = ArgView() ?? App.Progress.Settings.LastView;
        Opened += (_, _) => Go((Nav.Any(n => n.id == start) || start == "settings") && !(start == "launch" && !App.Progress.Settings.UseLauncher) ? start : "home");
        Opened += async (_, _) => await CheckForUpdate();
        if (Environment.GetEnvironmentVariable("SLATE_TRACE") == "1")
        {
            // Counts layout passes and redraws every 5 s, to catch anything that keeps the window busy.
            int layouts = 0;
            LayoutUpdated += (_, _) => layouts++;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            t.Tick += (_, _) => { Console.Error.WriteLine($"[trace] layouts {layouts} hexRenders {HexBackground.Renders} mapRenders {MapCanvas.Renders}"); layouts = 0; HexBackground.Renders = 0; MapCanvas.Renders = 0; };
            t.Start();
        }
        int si = Array.IndexOf(Program.Args, "--screenshots");
        if (si >= 0 && si + 1 < Program.Args.Length) Opened += async (_, _) => await Screenshots(Program.Args[si + 1]);
    }

    /// <summary>--screenshots &lt;folder&gt;: opens every screen in turn, saves a picture of each, then quits (for checking the UI).</summary>
    async Task Screenshots(string folder)
    {
        Directory.CreateDirectory(folder);
        await Task.Delay(2500);  // the save is read and the window laid out
        if (setup.IsVisible)
        {
            // a profile without game files: the first-start screen is all there is to capture
            var size0 = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            using var rtb0 = new Avalonia.Media.Imaging.RenderTargetBitmap(size0);
            rtb0.Render(this);
            rtb0.Save(Path.Combine(folder, "welcome.png"));
            Close();
            return;
        }
        // each screen with something picked, so the details (and any game pictures) show
        var bear = App.Data.Compendium.FirstOrDefault(c => c.Name.Contains("bear"))?.Id;
        var samples = new Dictionary<string, object?>
        {
            ["map"] = Program.Demo ? null : "Memory_Demo126_0",["quests"] = ("walkthrough", "vah-medoh"), ["shrines"] = App.Data.Markers.First(m => m.Type == "shrine").Id,
            ["cooking"] = ("ingredient", "Item_Fruit_A"), ["compendium"] = bear, ["armor"] = App.Data.Armor.Armor.FirstOrDefault(a => a.Name.Contains("Hylian Hood"))?.Id,
        };
        foreach (var id in Nav.Select(n => n.id).Where(id => id != "zeldadungeon").Append("settings"))  // (not a website)
        {
            Go(id, samples.GetValueOrDefault(id));
            await Task.Delay(id == "map" ? 3500 : 900);
            var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
            rtb.Render(this);
            rtb.Save(Path.Combine(folder, id + ".png"));
        }
        if (Environment.GetEnvironmentVariable("SLATE_TEST_ERROR") == "1")
        {
            // the UI's error handler: a mistake in a screen's code becomes a crash report and the error window
            Dispatcher.UIThread.Post(() => throw new InvalidOperationException("Test error from --screenshots (SLATE_TEST_ERROR=1)"));
            await Task.Delay(1500);
            if (ErrorWindow.Last is { } w)
            {
                using var er = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)w.Bounds.Width, (int)w.Bounds.Height));
                er.Render(w);
                er.Save(Path.Combine(folder, "error.png"));
                w.Close();
            }
        }
        Close();
    }

    /// <summary>
    /// Once at start (installed copies, unless turned off in Settings): one request to GitHub. A newer version shows a
    /// bar with Update and restart; nothing is downloaded until it's pressed.
    /// </summary>
    async Task CheckForUpdate()
    {
        var s = App.Progress.Settings;
        if (!s.CheckUpdatesOnStart || Program.Demo || Program.Args.Contains("--screenshots") || !Updater.Installed) return;
        try
        {
            var v = await Updater.CheckAsync();
            Log.Info(v == null ? "Update check: this is the newest version" : $"Update check: {v} is available");
            if (v == null || v == s.SkippedVersion) return;
            var status = Ui.Text($"Slate Companion {v} is available (you have {Updater.Version}).", "small");
            Button? now = null;
            now = Ui.Button("Update and restart", async () =>
            {
                now!.IsEnabled = false;
                try { await Updater.DownloadAndRestartAsync(p => Dispatcher.UIThread.Post(() => status.Text = $"Downloading Slate Companion {v}… {p}%")); }
                catch (Exception e) { now.IsEnabled = true; ErrorWindow.Show("Update failed", $"Slate Companion {v} couldn't be installed. Your current version still works.", e); }
            }, "primary small");
            var later = Ui.Button("Later", () => updateBar.IsVisible = false, "ghost small");
            var skip = Ui.Button("Skip this version", () => { App.Progress.SaveSettings(x => x.SkippedVersion = v); updateBar.IsVisible = false; }, "ghost small");
            var notes = Ui.Button("What's new", () => ErrorWindow.OpenPath($"{Updater.Repo}/releases/tag/v{v}"), "ghost small");
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            g.Children.Add(Ui.Row(10, Ui.Glyph("settings", 16), status));
            var right = Ui.Row(8, notes, skip, later, now); Grid.SetColumn(right, 1); g.Children.Add(right);
            updateBar.Child = g;
            updateBar.IsVisible = true;
        }
        catch (Exception e) { Log.Warn("Update check failed: " + e.Message); }
    }

    bool paused;
    readonly DispatcherTimer pillTimer = new() { Interval = TimeSpan.FromSeconds(30) };  // "saved 5 min ago" ages

    /// <summary>The screen stops its work (it's minimized or in the background).</summary>
    void Pause()
    {
        if (Program.Args.Contains("--screenshots")) return;
        // (also when already paused: losing the focus usually comes just before the minimize)
        paused = true;
        pillTimer.Stop();
        // minimized: the screen also lets go of memory it can get back quickly (the map's tiles); just in the
        // background (the game has the focus), it keeps it, so coming back shows the map at once
        current?.Hide(leaving: WindowState == WindowState.Minimized);
    }

    /// <summary>Back in front: the screen picks up again and shows what changed meanwhile.</summary>
    void Resume()
    {
        if (!paused || WindowState == WindowState.Minimized) return;
        paused = false;
        current?.Resume();
        pillTimer.Start();
        RenderPill();
    }

    public bool Paused => paused;

    /// <summary>The Play screen (Savage Wind launcher) is optional: its rune shows only when it's on.</summary>
    public void ShowLauncher(bool on)
    {
        if (runes.TryGetValue("launch", out var r)) r.IsVisible = on;
        if (!on && currentId == "launch") Go("home");
    }

    static string? ArgView()
    {
        int i = Array.IndexOf(Program.Args, "--view");
        return i >= 0 && i + 1 < Program.Args.Length ? Program.Args[i + 1] : null;
    }

    Button Rune(string id, string label)
    {
        var b = new Button { Content = Ui.Glyph(id) };
        b.Classes.Add("rune");
        ToolTip.SetTip(b, label);
        ToolTip.SetPlacement(b, PlacementMode.Right);
        b.Click += (_, _) => Go(id);
        runes[id] = b;
        return b;
    }

    public void Go(string id, object? arg = null)
    {
        if (id == "stats") id = "home";  // (the stats are on the Adventure log now)
        current?.Hide(leaving: true);
        if (!views.TryGetValue(id, out var v))
        {
            v = id switch
            {
                "map" => new MapScreen(),
                "launch" => new LaunchView(),
                "inventory" => new InventoryView(),
                "saves" => new SavesView(),
                "quests" => new QuestsView(),
                "shrines" => new ShrinesView(),
                "koroks" => new KoroksView(),
                "cooking" => new CookingView(),
                "compendium" => new CompendiumView(),
                "armor" => new ArmorView(),
                "guides" => new GuidesView(),
                "zeldadungeon" => new ZeldaDungeonView(),
                "settings" => new SettingsView(),
                _ => new HomeView(),
            };
            views[id] = v;
        }
        foreach (var (k, b) in runes) b.Classes.Set("active", k == id);
        current = v;
        currentId = id;
        host.Content = v;
        v.Show(arg, paused);
        if (App.Progress.Settings.LastView != id) App.Progress.SaveSettings(s => s.LastView = id);
    }

    void RenderPill()
    {
        var p = App.Progress;
        if (p.Error != null) { syncText.Text = "Save unreadable: " + p.Error; syncDot.Fill = Ui.Brush("Orange"); return; }
        if (!p.Linked) { syncText.Text = "No save linked — add one"; syncDot.Fill = Ui.Brush("Faint"); return; }
        syncDot.Fill = Ui.Brush("Glow");
        syncText.Text = $"{p.Source?.Label.Replace(" (in use)", "")} · slot {p.Slot} · saved {Ui.Ago(p.Modified)}";
        if (lastStamp != null && lastStamp != p.Modified) Toast("Save updated — progress refreshed");
        lastStamp = p.Modified;
    }

    public void Toast(string text)
    {
        toastText.Text = text;
        toast.IsVisible = true;
        toastTimer?.Stop();
        toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
        toastTimer.Tick += (_, _) => { toast.IsVisible = false; toastTimer?.Stop(); };
        toastTimer.Start();
    }

    async void OnDrop(DragEventArgs e)
    {
        if (e.Handled || e.DataTransfer.TryGetValue(Drag.Item) != null) return;  // (the app's own drags: an item or ingredient let go elsewhere)
        var items = e.DataTransfer.TryGetFiles();
        var path = items?.Select(i => i.TryGetLocalPath()).FirstOrDefault(p => p != null);
        if (path == null) { Toast("Drop a save folder, a game_data.sav or a .zip."); return; }
        try
        {
            string folder = await Task.Run(() => AddSave(path));
            App.Progress.SaveSettings(s => { s.SelectedSource = "custom:" + folder; s.SelectedSlot = null; });
            await App.Progress.RefreshAsync();
            Toast(path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "Save copied in from the zip" : "Save linked — it updates as you play");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Toast("Could not read that: " + ex.Message);
        }
    }

    /// <summary>A dropped or picked path: zip → copied in; folder or .sav → linked where it is (read only).</summary>
    public static string AddSave(string path)
    {
        string folder;
        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) folder = Importer.FromZip(App.Store.Folder, path);
        else
        {
            var dir = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
            if (SaveLocator.LooksLikeSavageWind(dir))
            {
                App.Progress.SaveSettings(s => { if (!s.SavageWindFolders.Contains(dir, StringComparer.OrdinalIgnoreCase)) s.SavageWindFolders.Add(dir); });
                return SaveLocator.FindProfileFolder(Path.Combine(dir, "save")) ?? throw new InvalidDataException("That Savage Wind folder has no saves yet.");
            }
            folder = SaveLocator.FindProfileFolder(dir) ?? (Path.GetDirectoryName(dir) is string up && SaveLocator.HasSlots(up) ? up : null)
                     ?? throw new InvalidDataException("No Breath of the Wild save (game_data.sav) was found there.");
        }
        App.Progress.SaveSettings(s => { if (!s.CustomFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) s.CustomFolders.Add(folder); });
        return folder;
    }
}

/// <summary>The slate's surface: a faint hex lattice and a cold glow, drawn once per size.</summary>
public sealed class HexBackground : Control
{
    public static int Renders;
    public override void Render(DrawingContext ctx)
    {
        Renders++;
        var b = Bounds;
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#071116")), new Rect(b.Size));
        var glow = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, -0.2, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, -0.2, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
            GradientStops = { new(Color.Parse("#1F39C6FF"), 0), new(Color.Parse("#0039C6FF"), 1) },
        };
        ctx.FillRectangle(glow, new Rect(b.Size));
        var pen = new Pen(new SolidColorBrush(Color.Parse("#0D39C6FF")), 1);
        const double w = 56, h = 98;
        for (double y = -h; y < b.Height + h; y += h)
            for (double x = 0; x < b.Width + w; x += w)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x + 28, y), false);
                    c.LineTo(new Point(x + 56, y + 16)); c.LineTo(new Point(x + 56, y + 48)); c.LineTo(new Point(x + 28, y + 64));
                    c.LineTo(new Point(x, y + 48)); c.LineTo(new Point(x, y + 16)); c.LineTo(new Point(x + 28, y));
                    c.BeginFigure(new Point(x + 28, y + 64), false); c.LineTo(new Point(x + 28, y + 98));
                }
                ctx.DrawGeometry(null, pen, g);
            }
    }
}
