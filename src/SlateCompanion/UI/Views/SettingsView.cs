using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SlateCompanion.UI;

namespace SlateCompanion.UI.Views;

/// <summary>Saves & settings: the saves found, adding one, the Savage Wind live link, and the in-game map and assets.</summary>
public sealed class SettingsView : ViewBase
{
    static readonly Dictionary<string, string> KindName = new() { ["savagewind"] = "Savage Wind", ["cemu"] = "Cemu", ["ryujinx"] = "Ryujinx", ["yuzu"] = "Switch emulator", ["custom"] = "Added by you" };
    readonly StackPanel savesList = new() { Spacing = 8 };
    readonly ContentControl mapBox = new();
    readonly TextBlock liveResult = Ui.Text("", "muted small");
    readonly TextBox pathBox = new() { PlaceholderText = "…or paste a folder or file path", MinWidth = 360 };
    DispatcherTimer? mapPoll;
    ScrollViewer scroll = null!;

    protected override Control Build()
    {
        var p = App.Progress;
        var add = Ui.Panel(Ui.Stack(10, Ui.Text("ADD A SAVE", "h2"),
            Ui.Text("Slate Companion only reads saves. It never writes to them, so it can't damage your progress. A folder or save file you pick stays in sync while you play; a .zip is copied in. You can also drop any of them on this window.", "muted"),
            Ui.Row(10, Ui.Button("Choose a folder…", PickFolder, "primary"), Ui.Button("Choose a save file or .zip…", PickFile), Ui.Button("Search again", () => _ = Refresh(true), "ghost")),
            Ui.Row(10, pathBox, Ui.Button("Add", () => Add(pathBox.Text?.Trim().Trim('"')))),
            Ui.Text(OperatingSystem.IsWindows() ? "Savage Wind: pick its folder (the one with savagewind.exe). Every save profile in it shows up."
                : "Savage Wind: pick its AppImage with Choose a save file: its saves (every profile) are found from it.", "faint")), "panel glow");

        var liveOn = new CheckBox { Content = "Show Link's live position", IsChecked = p.Settings.LiveEnabled };
        liveOn.IsCheckedChanged += (_, _) => p.SaveSettings(s => s.LiveEnabled = liveOn.IsChecked == true);
        var bg = new CheckBox { Content = "Keep it moving while Slate Companion is in the background (a second screen)", IsChecked = p.Settings.LiveInBackground };
        bg.IsCheckedChanged += (_, _) => p.SaveSettings(s => s.LiveInBackground = bg.IsChecked == true);
        var port = new NumericUpDown { Value = p.Settings.LivePort, Minimum = 1, Maximum = 65535, Increment = 1, FormatString = "0", Width = 140 };
        port.ValueChanged += (_, _) => p.SaveSettings(s => s.LivePort = (int)(port.Value ?? 7777));
        var live = Ui.Panel(Ui.Stack(10, Ui.Text("LIVE POSITION (SAVAGE WIND)", "h2"),
            Ui.Text("Shows where Link is on the map while you play, by asking Savage Wind over its local, read-only connection (this computer only). In Savage Wind Configuration turn on Slate Companion; it uses port 7777.", "muted"),
            Ui.Row(10, liveOn, Ui.Text("Port", "muted"), port, Ui.Button("Test", async () =>
            {
                liveResult.Text = "Asking Savage Wind…";
                var r = await Task.Run(() => LiveLink.QueryAsync(p.Settings.LivePort, CancellationToken.None));
                liveResult.Text = r["connected"]?.GetValue<bool>() == true && r["x"] != null
                    ? $"Connected. Link is at x {r["x"]!.GetValue<double>():0}, y {r["y"]!.GetValue<double>():0}, z {r["z"]!.GetValue<double>():0}."
                    : "Not connected: " + (r["error"]?.GetValue<string>() ?? "no answer");
            }, "small")), bg, liveResult));


        var page = Ui.Stack(16, Ui.Header("Saves & settings"),
            new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Children = { add, Wrap(live, 1) } },
            Ui.Panel(mapBox, "panel"),
            Ui.Panel(LauncherPanel(), "panel"),
            Ui.Panel(UpdatesPanel(), "panel"),
            Ui.Text("SAVES FOUND", "h2"), savesList,
            Ui.Text("ABOUT", "h2"),
            Ui.Panel(Ui.Text($"Slate Companion {typeof(Program).Assembly.GetName().Version?.ToString(3)} · an unofficial fan-made companion for The Legend of Zelda: Breath of the Wild. Not affiliated with Nintendo; game names, the map and icons are © Nintendo and come from your own copy of the game. Your ticks, pins and history are kept in {App.Store.Folder}.", "muted small")));
        add.Margin = new Thickness(0, 0, 14, 0);
        scroll = new ScrollViewer { Content = new Border { Padding = new Thickness(24, 20), Child = page } };
        return scroll;

        static Control Wrap(Control c, int col) { Grid.SetColumn(c, col); return c; }
    }

    /// <summary>The optional Savage Wind launcher (the Play screen).</summary>
    static Control LauncherPanel()
    {
        var on = new ToggleSwitch { IsChecked = App.Progress.Settings.UseLauncher, OnContent = "Shown", OffContent = "Hidden" };
        on.IsCheckedChanged += (_, _) => { App.Progress.SaveSettings(s => s.UseLauncher = on.IsChecked == true); App.Main.ShowLauncher(on.IsChecked == true); };
        var minimize = new CheckBox { Content = "Minimize Slate Companion when the game starts (it then sleeps; the map's live position pauses)", IsChecked = App.Progress.Settings.MinimizeOnPlay };
        minimize.IsCheckedChanged += (_, _) => App.Progress.SaveSettings(s => s.MinimizeOnPlay = minimize.IsChecked == true);
        var t = Ui.Text("The Play screen: starts Savage Wind (with Clear Skies if you use it) and lists your copies of it. Savage Wind opens as it always does, and Slate Companion never installs, updates or changes it. Hidden, Slate Companion never starts Savage Wind.", "muted small");
        t.TextWrapping = TextWrapping.Wrap;
        var gpu = new CheckBox { Content = "Draw with the graphics card (faster map; off: software drawing). Takes effect the next time Slate Companion starts.", IsChecked = App.Progress.Settings.GpuDrawing };
        gpu.IsCheckedChanged += (_, _) => App.Progress.SaveSettings(s => s.GpuDrawing = gpu.IsChecked == true);
        var stack = Ui.Stack(8, Ui.Text("SAVAGE WIND LAUNCHER", "h2"), t, on, minimize, Ui.Text("DRAWING", "h2"), gpu);
        if (DesktopEntries.Supported)
        {
            var menu = new CheckBox { Content = "Add to applications: Slate Companion and your Savage Wind AppImages in the app menu (off removes them)", IsChecked = App.Progress.Settings.AddToApplications };
            menu.IsCheckedChanged += (_, _) =>
            {
                App.Progress.SaveSettings(s => s.AddToApplications = menu.IsChecked == true);
                if (menu.IsChecked == true) App.SyncDesktopEntries(); else DesktopEntries.RemoveAll();
            };
            stack.Children.Add(Ui.Text("APP MENU", "h2"));
            stack.Children.Add(menu);
        }
        return stack;
    }

    /// <summary>Updates, from GitHub, only when asked (installed copies only).</summary>
    static Control UpdatesPanel()
    {
        var status = Ui.Text(Updater.Installed ? "" : "Updates work in the installed app (Setup or AppImage). This copy isn't installed.", "muted small");
        var check = new Button { Content = "Check for updates" };
        var update = new Button { Content = "Update and restart", IsVisible = false };
        update.Classes.Add("primary");
        check.IsEnabled = Updater.Installed;
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false; status.Text = "Checking GitHub…";
            try
            {
                var v = await Updater.CheckAsync();
                status.Text = v == null ? "You have the newest version." : $"Version {v} is available.";
                update.IsVisible = v != null;
            }
            catch (Exception e) { status.Text = "Couldn't check: " + e.Message; }
            check.IsEnabled = true;
        };
        update.Click += async (_, _) =>
        {
            update.IsEnabled = check.IsEnabled = false;
            try { await Updater.DownloadAndRestartAsync(p => Dispatcher.UIThread.Post(() => status.Text = $"Downloading… {p}%")); }
            catch (Exception e) { status.Text = "Update failed: " + e.Message; update.IsEnabled = check.IsEnabled = true; ErrorWindow.Show("Update failed", "The new version couldn't be installed. Your current version still works.", e); }
        };
        var auto = new CheckBox { Content = "Check for a new version when Slate Companion opens", IsChecked = App.Progress.Settings.CheckUpdatesOnStart, IsEnabled = Updater.Installed };
        auto.IsCheckedChanged += (_, _) => App.Progress.SaveSettings(s => { s.CheckUpdatesOnStart = auto.IsChecked == true; s.SkippedVersion = null; });
        return Ui.Stack(8, Ui.Text("UPDATES", "h2"), Ui.Text($"Slate Companion {Updater.Version}. One small request to GitHub; nothing is downloaded until you press Update.", "muted small"),
            auto, Ui.Row(10, check, update), status,
            Ui.Row(10, Ui.Button("Open logs folder", () => ErrorWindow.OpenPath(Log.Folder), "small ghost"), Ui.Button("Report a problem", () => ErrorWindow.OpenPath(ErrorWindow.IssuesUrl), "small ghost")));
    }

    protected override void OnShown(object? arg)
    {
        App.Progress.Changed += DrawSaves;
        DrawSaves();
        DrawMap();
        if (arg as string == "map") Dispatcher.UIThread.Post(() => mapBox.BringIntoView(), DispatcherPriority.Loaded);
    }

    public override void OnHidden()
    {
        App.Progress.Changed -= DrawSaves;
        mapPoll?.Stop();
    }

    async Task Refresh(bool toast)
    {
        await App.Progress.RefreshAsync();
        if (toast) App.Main.Toast($"{App.Progress.Sources.Count} save location{(App.Progress.Sources.Count == 1 ? "" : "s")} found");
    }

    void DrawSaves()
    {
        savesList.Children.Clear();
        var p = App.Progress;
        if (p.Sources.Count == 0) { savesList.Children.Add(Ui.Empty("No Breath of the Wild saves found yet. Choose a folder above, or drop a save on this window.")); return; }
        foreach (var s in p.Sources)
        {
            bool sel = p.Source?.Id == s.Id;
            var chips = new WrapPanel();
            foreach (var slot in SaveLocator.ListSlots(s.Folder))
            {
                var b = Ui.Button($"Slot {slot.Slot} · {Ui.Ago(slot.Modified)}", () => Select(s.Id, slot.Slot), "chip");
                b.Classes.Set("on", sel && p.Slot == slot.Slot && p.Settings.SelectedSlot != null);
                b.Margin = new Thickness(0, 4, 6, 0);
                chips.Children.Add(b);
            }
            var newest = Ui.Button("Newest", () => Select(s.Id, null), "chip");
            newest.Classes.Set("on", sel && p.Settings.SelectedSlot == null);
            newest.Margin = new Thickness(0, 4, 6, 0);
            chips.Children.Add(newest);
            var title = Ui.Row(8, Ui.Text(s.Label, "h3"));
            if (sel) title.Children.Add(Ui.Tag("linked", "done"));
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*,Auto") };
            g.Children.Add(Ui.Tag(KindName.GetValueOrDefault(s.Kind, s.Kind), s.Kind == "savagewind" ? "blue" : ""));
            var mid = Ui.Stack(2, title, Ui.Text(s.Folder, "faint mono"), chips); Grid.SetColumn(mid, 1); g.Children.Add(mid);
            var open = Ui.Button("Open folder", () => Open(s.Folder), "small ghost"); Grid.SetColumn(open, 2); g.Children.Add(open);
            var panel = Ui.Panel(g, sel ? "panel glow" : "panel");
            savesList.Children.Add(panel);
        }
    }

    void Select(string id, string? slot)
    {
        App.Progress.SaveSettings(s => { s.SelectedSource = id; s.SelectedSlot = slot; });
        _ = App.Progress.RefreshAsync();
        App.Main.Toast("Save linked");
    }

    void DrawMap()
    {
        var b = App.MapBuilder;
        var romfs = MapBuilder.FindRomfs(App.Progress.Settings.GameFilesFolder, App.Progress.Settings.SavageWindFolders);
        var s = Ui.Stack(8, Ui.Text("FROM YOUR GAME FILES", "h2"),
            Ui.Text("Slate Companion can take the map, its icons, every item picture, the compendium photos, your memory photos and the in-game names, descriptions and quest log from your own copy of the game. It is built once (under a minute) and kept in Slate Companion's folder; your game files are only read, and nothing is ever sent anywhere.", "muted"));
        if (b.State == "building")
        {
            var meter = Ui.Meter(b.Progress, 1); meter.Width = 420;
            s.Children.Add(Ui.Row(10, meter, Ui.Text($"{b.Progress:P0}", "mono small"), Ui.Button("Cancel", () => b.Cancel(), "small ghost")));
            s.Children.Add(Ui.Text(b.Message, "faint"));
            mapPoll ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            mapPoll.Tick -= OnMapPoll; mapPoll.Tick += OnMapPoll; mapPoll.Start();
        }
        else
        {
            mapPoll?.Stop();
            if (b.State == "ready" && !b.Complete) s.Children.Add(new Border { Classes = { "note" }, Child = Ui.Text("This build only has the map. Rebuild to add item pictures, compendium photos and the in-game text.", "small") });
            s.Children.Add(b.State == "ready" ? Ui.Row(8, Ui.Tag("Built", "done"), Ui.Text(BuiltInfo(), "small muted"))
                : b.State == "error" ? Ui.Row(8, Ui.Tag("Failed", "orange"), Ui.Text(b.Message, "small")) : Ui.Tag("Not built yet"));
            s.Children.Add(Ui.Text(romfs != null ? "Game files: " + romfs : "Game files not found yet — choose your extracted game (romfs), or a Savage Wind folder.", "faint"));
            var build = Ui.Button(b.State == "ready" ? "Rebuild" : "Build from my game files", () => StartBuild(null), b.State == "ready" && b.Complete ? "" : "primary");
            build.IsEnabled = romfs != null;
            var langs = romfs != null ? GameText.Languages(romfs) : [];
            var lang = new ComboBox { ItemsSource = langs, SelectedItem = langs.Contains(App.Progress.Settings.GameLanguage) ? App.Progress.Settings.GameLanguage : langs.FirstOrDefault(), MinWidth = 110, IsVisible = langs.Count > 0 };
            ToolTip.SetTip(lang, "Language of the in-game text (takes effect on the next build)");
            lang.SelectionChanged += (_, _) => { if (lang.SelectedItem is string l) App.Progress.SaveSettings(x => x.GameLanguage = l); };
            s.Children.Add(Ui.Row(10, build, Ui.Text("Text language", "muted small"), lang, Ui.Button("Choose game files…", async () =>
            {
                var picked = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose your extracted game files (romfs) or a Savage Wind folder" });
                var path = picked.FirstOrDefault()?.TryGetLocalPath();
                if (path != null) StartBuild(path);
            }, "ghost")));
        }
        mapBox.Content = s;
    }

    void OnMapPoll(object? sender, EventArgs e)
    {
        DrawMap();
        if (App.MapBuilder.State != "building") { Icons.Reset(); Assets.Reset(); App.Main.Toast(App.MapBuilder.State == "ready" ? "Built from your game files" : App.MapBuilder.Message); }
    }

    string BuiltInfo()
    {
        try
        {
            var meta = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(App.MapBuilder.Folder, "meta.json"))).RootElement;
            return $"{meta.GetProperty("built").GetDateTime().ToLocalTime():g} · {meta.GetProperty("bytes").GetInt64() / 1048576} MB map · {meta.GetProperty("icons").GetArrayLength()} map icons" + (meta.TryGetProperty("items", out var it) ? $" · {it.GetInt32()} item pictures · {meta.GetProperty("pictures").GetInt32()} photos · {meta.GetProperty("texts").GetInt32()} texts ({meta.GetProperty("language").GetString()})" : "");
        }
        catch (Exception) { return ""; }
    }

    void StartBuild(string? chosen)
    {
        var romfs = MapBuilder.FindRomfs(chosen ?? App.Progress.Settings.GameFilesFolder, App.Progress.Settings.SavageWindFolders);
        if (romfs == null) { App.Main.Toast("No game files found there. Choose your extracted game's romfs folder (Savage Wind keeps it in extracted\\romfs)."); return; }
        if (chosen != null) App.Progress.SaveSettings(s => s.GameFilesFolder = chosen);
        App.MapBuilder.Start(romfs, App.Progress.Settings.GameLanguage);
        DrawMap();
    }

    async void PickFolder()
    {
        var picked = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a Breath of the Wild save folder or a Savage Wind folder" });
        Add(picked.FirstOrDefault()?.TryGetLocalPath());
    }

    async void PickFile()
    {
        var picked = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose game_data.sav or a .zip of a save",
            FileTypeFilter = [new FilePickerFileType("Breath of the Wild saves") { Patterns = ["*.sav", "*.zip", "*.AppImage"] }, FilePickerFileTypes.All],
        });
        Add(picked.FirstOrDefault()?.TryGetLocalPath());
    }

    async void Add(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        // an AppImage (Linux): Savage Wind's data folder, with every save profile, is found from it
        if (!OperatingSystem.IsWindows() && Launcher.IsProgram(path)) path = Launcher.AddProgram(path);
        try
        {
            var folder = await Task.Run(() => MainWindow.AddSave(path));
            var id = App.Progress.Sources.FirstOrDefault(x => x.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase))?.Id ?? "custom:" + folder;
            App.Progress.SaveSettings(s => { s.SelectedSource = SaveLocator.LooksLikeSavageWind(path) ? null : "custom:" + folder; s.SelectedSlot = null; });
            await App.Progress.RefreshAsync();
            App.Main.Toast("Save added");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { App.Main.Toast(e.Message); }
    }

    static void Open(string folder)
    {
        try
        {
            if (OperatingSystem.IsWindows()) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            else System.Diagnostics.Process.Start("xdg-open", folder);
        }
        catch (Exception) { }
    }
}
