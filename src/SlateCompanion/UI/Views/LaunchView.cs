using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace SlateCompanion.UI.Views;

/// <summary>
/// Play: starts Savage Wind (with Clear Skies when it's on), the copies of Savage Wind on this computer (which one
/// Play starts), and Clear Skies. Savage Wind is started the normal way, so it decides itself whether to show its
/// Configuration screen or an update first; Slate Companion never installs, updates or changes Savage Wind.
/// Optional: Settings can hide all of it.
/// </summary>
public sealed class LaunchView : ViewBase
{
    readonly ContentControl hero = new(), list = new(), clear = new();
    List<SwInstall> installs = [];
    SwInstall? sw;
    ClearSkies? cs;
    Launcher.Release? csRelease;
    string? csCheckError;
    bool checkedOnline, lastRunning;
    DispatcherTimer? runningPoll;

    protected override Control Build()
    {
        var left = Ui.Stack(16, hero, list);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("6*,5*") };
        left.Margin = new Thickness(0, 0, 16, 0);
        grid.Children.Add(left);
        Grid.SetColumn(clear, 1);
        grid.Children.Add(clear);
        var page = Ui.Stack(16, Ui.Header("Play"), grid);
        return new ScrollViewer { Content = new Border { Padding = new Thickness(24, 20), Child = page } };
    }

    protected override void OnShown(object? arg)
    {
        Refresh();
        runningPoll ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        runningPoll.Tick -= OnPoll; runningPoll.Tick += OnPoll; runningPoll.Start();
        if (!checkedOnline) { checkedOnline = true; _ = CheckClearSkies(); }
    }

    public override void OnHidden() => runningPoll?.Stop();

    void OnPoll(object? s, EventArgs e)
    {
        bool r = Launcher.GameRunning();
        if (r != lastRunning) { lastRunning = r; DrawHero(); }
    }

    void Refresh()
    {
        var st = App.Progress.Settings;
        installs = Launcher.FindInstalls(st);
        sw = installs.FirstOrDefault(i => i.Exe.Equals(st.GameProgram, StringComparison.OrdinalIgnoreCase))
             ?? installs.OrderByDescending(i => i.Version ?? "", Comparer<string>.Create(Launcher.CompareVersions)).FirstOrDefault();
        App.SyncDesktopEntries();  // (Linux: the app menu follows the list of copies)
        cs = ClearSkies.Find(st);
        if (cs != null && st.ClearSkiesProgram != cs.Program) App.Progress.SaveSettings(x => x.ClearSkiesProgram = cs.Program);
        lastRunning = Launcher.GameRunning();
        DrawHero(); DrawList(); DrawClear();
    }

    string NameOf(SwInstall i) => App.Progress.Settings.InstallNames.GetValueOrDefault(i.Exe) is { Length: > 0 } n ? n : i.Label;

    // ---------------------------------------------------------------- Play

    void DrawHero()
    {
        var s = Ui.Stack(12);
        var icon = sw != null && File.Exists(Path.Combine(sw.Folder, "icon.png")) ? Load(Path.Combine(sw.Folder, "icon.png"), 64) : null;
        s.Children.Add(Head(Ui.Img(icon, 56) ?? Ui.Glyph("launch", 48), "SAVAGE WIND", sw != null ? NameOf(sw) : "The Legend of Zelda: Breath of the Wild, on PC"));
        if (sw == null)
        {
            s.Children.Add(Ui.Text("No Savage Wind found on this computer. Add the copy you play with below.", "muted small"));
            ((TextBlock)s.Children[^1]).TextWrapping = TextWrapping.Wrap;
            hero.Content = Ui.Panel(s, "panel glow");
            return;
        }
        var tags = new WrapPanel();
        void T(string t, string kind) { var b = Ui.Tag(t, kind); b.Margin = new Thickness(0, 0, 6, 4); tags.Children.Add(b); }
        T(sw.Version is { } v ? "Version " + v : "Built from source", "blue");
        if (cs != null && App.Progress.Settings.UseClearSkies) T("With Clear Skies", "blue");
        if (lastRunning) T("Running now", "done");
        s.Children.Add(tags);

        var play = new Button { Content = Ui.Row(12, Ui.Glyph("launch", 26), new TextBlock { Text = lastRunning ? "Running" : "Play" }), IsEnabled = !lastRunning, HorizontalAlignment = HorizontalAlignment.Left };
        play.Classes.Add("play");
        play.Click += (_, _) => PlayNow();
        s.Children.Add(play);
        string note = lastRunning ? "Savage Wind is running."
            : cs != null && App.Progress.Settings.UseClearSkies ? "Starts Savage Wind through Clear Skies, with the look picked here. Savage Wind opens as it always does."
            : "Starts Savage Wind as if you opened it yourself: it shows its Configuration screen or an update first when it wants to.";
        var n = Ui.Text(note, "muted small"); n.TextWrapping = TextWrapping.Wrap; s.Children.Add(n);
        s.Children.Add(Ui.Row(8,
            Ui.Button("Open folder", () => ErrorWindow.OpenPath(sw.Folder), "small ghost"),
            Ui.Button("Its log", () => ErrorWindow.OpenPath(Path.Combine(sw.DataRoot, "savagewind.log")), "small ghost")));
        hero.Content = Ui.Panel(s, "panel glow");
    }

    void PlayNow()
    {
        if (sw == null) return;
        var st = App.Progress.Settings;
        var useCs = st.UseClearSkies && cs != null ? cs : null;
        Log.Info($"Play: {sw.Exe}{(useCs != null ? $" with Clear Skies ({st.ClearSkiesPreset ?? "saved preset"})" : "")}");
        var err = Launcher.Play(sw, useCs, st.ClearSkiesPreset);
        if (err != null) { Report(err, "Savage Wind didn't start"); return; }
        App.Main.Toast("Starting Savage Wind…");
        DispatcherTimer.RunOnce(() => { lastRunning = Launcher.GameRunning(); DrawHero(); }, TimeSpan.FromSeconds(6));
        if (st.MinimizeOnPlay) App.Main.WindowState = WindowState.Minimized;
    }

    // ---------------------------------------------------------------- the copies: which one Play starts

    void DrawList()
    {
        var s = Ui.Stack(10, Ui.Text("YOUR COPIES OF SAVAGE WIND", "h2"));
        foreach (var i in installs) s.Children.Add(InstallRow(i));
        s.Children.Add(Ui.Row(8, Ui.Button("Add a copy you have…", ChooseInstall, "small ghost")));
        list.Content = Ui.Panel(s);
    }

    Control InstallRow(SwInstall i)
    {
        bool chosen = i == sw;
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var name = Ui.Text(NameOf(i), "h3"); name.TextWrapping = TextWrapping.Wrap;
        top.Children.Add(Ui.Stack(2, name, Ui.Text(Ui.ShortPath(i.Folder), "faint small mono")));
        if (chosen) { var t = Ui.Tag("Play starts this one", "done"); Grid.SetColumn(t, 1); t.VerticalAlignment = VerticalAlignment.Top; top.Children.Add(t); }
        var buttons = new WrapPanel();
        void B(string text, Action a, string cls = "small ghost") { var b = Ui.Button(text, a, cls); b.Margin = new Thickness(0, 4, 6, 0); buttons.Children.Add(b); }
        if (!chosen) B("Use this one", () => { App.Progress.SaveSettings(x => x.GameProgram = i.Exe); Refresh(); });
        B("Rename", () => Rename(i));
        B("Open folder", () => ErrorWindow.OpenPath(i.Folder));
        B("Remove from list", () =>
        {
            App.Progress.SaveSettings(x => { if (!x.HiddenInstalls.Contains(i.Exe)) x.HiddenInstalls.Add(i.Exe); if (x.GameProgram == i.Exe) x.GameProgram = null; });
            App.Main.Toast("Removed from the list (nothing was deleted)");
            Refresh();
        });
        var status = i.Version == null ? "Built from source" : "Version " + i.Version;
        return Ui.Panel(Ui.Stack(4, top, Ui.Text(status, "muted small"), buttons), chosen ? "panel glow" : "panel");
    }

    async void Rename(SwInstall i)
    {
        var box = new TextBox { Text = NameOf(i), Width = 360 };
        var w = new Window { Title = "Rename", SizeToContent = SizeToContent.WidthAndHeight, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.Brush("Bg") };
        var ok = Ui.Button("Save", () => w.Close(true), "primary"); ok.IsDefault = true;
        var cancel = Ui.Button("Cancel", () => w.Close(false), "ghost"); cancel.IsCancel = true;
        w.Content = new Border { Padding = new Thickness(20), Child = Ui.Stack(12, Ui.Text("A name for this copy", "h3"), box, Ui.Row(8, ok, cancel)) };
        if (await w.ShowDialog<bool>(App.Main) && box.Text is { } name)
        {
            App.Progress.SaveSettings(x => { if (string.IsNullOrWhiteSpace(name)) x.InstallNames.Remove(i.Exe); else x.InstallNames[i.Exe] = name.Trim(); });
            Refresh();
        }
    }

    async void ChooseInstall()
    {
        var top = TopLevel.GetTopLevel(this)!;
        if (OperatingSystem.IsWindows())
        {
            var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose your Savage Wind folder (the one with savagewind.exe)" });
            var path = picked.FirstOrDefault()?.TryGetLocalPath();
            if (path == null) return;
            if (!File.Exists(Path.Combine(path, "savagewind.exe"))) { ErrorWindow.Show("That isn't a Savage Wind folder", "There's no savagewind.exe in " + Ui.ShortPath(path) + "."); return; }
            App.Progress.SaveSettings(x => { if (!x.SavageWindFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) x.SavageWindFolders.Add(path); x.HiddenInstalls.Remove(Path.Combine(path, "savagewind.exe")); x.GameProgram = Path.Combine(path, "savagewind.exe"); });
        }
        else
        {
            var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose Savage Wind's AppImage (or a savagewind program)" });
            var path = picked.FirstOrDefault()?.TryGetLocalPath();
            if (path == null) return;
            App.Progress.SaveSettings(x => { if (!x.SavageWindPrograms.Contains(path)) x.SavageWindPrograms.Add(path); x.HiddenInstalls.Remove(path); x.GameProgram = path; });
        }
        Refresh();
        _ = App.Progress.RefreshAsync();
    }

    // ---------------------------------------------------------------- Clear Skies

    /// <summary>Whether Clear Skies has a newer release (one small request to GitHub, the first time the screen opens).</summary>
    async Task CheckClearSkies()
    {
        csCheckError = null;
        try { csRelease = await Launcher.LatestRelease(ClearSkies.Repo); }
        catch (Exception e) { csCheckError = e.Message; Log.Warn("Clear Skies update check: " + e.Message); }
        DrawClear();
    }

    void DrawClear()
    {
        var st = App.Progress.Settings;
        var s = Ui.Stack(10);
        var logo = Load("avares://SlateCompanion/Assets/clearskies.png", 96);
        s.Children.Add(Head(Ui.Img(logo, 52), "CLEAR SKIES", "Cemu's Clarity look for Savage Wind: 20 presets, plus a mod manager and GameBanana browser."));
        if (cs == null)
        {
            s.Children.Add(Ui.Text("Clear Skies isn't on this computer (or Slate Companion hasn't found it).", "muted small"));
            s.Children.Add(Ui.Row(10, Ui.Button("Get Clear Skies", () => ErrorWindow.OpenPath(ClearSkies.Page), "primary small"), Ui.Button("Choose Clear Skies…", ChooseClearSkies, "small ghost")));
            clear.Content = Ui.Panel(s); return;
        }
        var use = new ToggleSwitch { IsChecked = st.UseClearSkies, OnContent = "Play with Clear Skies", OffContent = "Play with Clear Skies" };
        use.IsCheckedChanged += (_, _) => { App.Progress.SaveSettings(x => x.UseClearSkies = use.IsChecked == true); DrawHero(); };
        s.Children.Add(use);
        var current = st.ClearSkiesPreset ?? cs.SavedPreset ?? "serfrost";
        var preset = new ComboBox { ItemsSource = ClearSkies.Presets.Select(p => p.name).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(ClearSkies.Presets, p => p.id == current)), MinWidth = 300 };
        preset.SelectionChanged += (_, _) => { if (preset.SelectedIndex >= 0) App.Progress.SaveSettings(x => x.ClearSkiesPreset = ClearSkies.Presets[preset.SelectedIndex].id); };
        s.Children.Add(Ui.Row(10, Ui.Text("Look", "muted small"), preset));
        s.Children.Add(Ui.Row(8,
            Ui.Button("Mods & GameBanana", () => Report(cs.Open(gamebanana: true), "Clear Skies didn't open"), "small"),
            Ui.Button("Open Clear Skies", () => Report(cs.Open(gamebanana: false), "Clear Skies didn't open"), "small ghost"),
            Ui.Button("Choose…", ChooseClearSkies, "small ghost")));
        var v = cs.Version;
        bool newer = csRelease != null && v != null && Launcher.CompareVersions(csRelease.Version, v) > 0;
        if (newer)
        {
            s.Children.Add(Ui.Row(8, Ui.Tag("Update", "orange"), Ui.Text($"Clear Skies {csRelease!.Version} is available (you have {v}).", "small"),
                Ui.Button(csUpdating ? "Updating…" : "Update", UpdateClearSkies, "small primary")));
            ((Control)((Panel)s.Children[^1]).Children[^1]).IsEnabled = !csUpdating;
            var t = Ui.Text(cs.AppImage
                ? "Update downloads the new AppImage next to the one you have and checks it against GitHub's checksum. The old one stays where it is."
                : "Update downloads it, checks it against GitHub's checksum and replaces Clear Skies' files. Your clearskies.ini is kept, and the replaced files are copied to a \"previous\" folder first.", "faint small");
            t.TextWrapping = TextWrapping.Wrap; s.Children.Add(t);
        }
        else
            s.Children.Add(Ui.Text($"Clear Skies {v ?? "(version unknown)"} · {Ui.ShortPath(cs.Folder)}" + (csRelease != null && v != null ? " · up to date" : csCheckError != null ? " · couldn't check for updates" : ""), "faint small"));
        clear.Content = Ui.Panel(s);
    }

    bool csUpdating;

    /// <summary>The Update button: Slate Companion downloads the release and puts it in place (Clear Skies 0.2.x has no updater).</summary>
    async void UpdateClearSkies()
    {
        if (csUpdating || cs == null || csRelease == null) return;
        var (from, release) = (cs, csRelease);
        csUpdating = true;
        DrawClear();
        try
        {
            var program = await Task.Run(() => Launcher.UpdateClearSkies(from, release));
            App.Progress.SaveSettings(x => x.ClearSkiesProgram = program);
            cs = ClearSkies.At(program) ?? cs;
        }
        catch (Exception e)
        {
            Log.Warn("Clear Skies update: " + e);
            ErrorWindow.Show("Clear Skies wasn't updated", e is HttpRequestException or TaskCanceledException
                ? "The download failed (" + e.Message + "). Check your connection and try again, or get it from " + ClearSkies.Page : e.Message);
        }
        finally { csUpdating = false; DrawClear(); DrawHero(); }
    }

    async void ChooseClearSkies()
    {
        var top = TopLevel.GetTopLevel(this)!;
        var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = OperatingSystem.IsWindows() ? "Choose ClearSkies.exe" : "Choose Clear Skies' AppImage" });
        var path = picked.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;
        if (!ClearSkies.IsProgram(path)) { ErrorWindow.Show("That isn't Clear Skies", "Choose ClearSkies.exe (Windows) or the ClearSkies AppImage (Linux)."); return; }
        App.Progress.SaveSettings(x => x.ClearSkiesProgram = path);
        cs = new ClearSkies(path);
        DrawClear(); DrawHero();
    }

    // ---------------------------------------------------------------- helpers

    static Control Head(Control? icon, string title, string sub)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        if (icon != null) { icon.Margin = new Thickness(0, 0, 14, 0); icon.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(icon); }
        var t = Ui.Text(sub, "muted small"); t.TextWrapping = TextWrapping.Wrap;
        var st = Ui.Stack(2, Ui.Text(title, "h2"), t); st.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(st, 1); g.Children.Add(st);
        return g;
    }

    static void Report(string? err, string title)
    {
        if (err != null) ErrorWindow.Show(title, err);
    }

    static Bitmap? Load(string path, int width)
    {
        try
        {
            if (path.StartsWith("avares:", StringComparison.Ordinal))
                using (var st = Avalonia.Platform.AssetLoader.Open(new Uri(path))) return Bitmap.DecodeToWidth(st, width);
            using var f = File.OpenRead(path);
            return Bitmap.DecodeToWidth(f, width);
        }
        catch (Exception) { return null; }
    }
}
