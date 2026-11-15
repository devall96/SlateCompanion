using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace SlateCompanion.UI.Views;

/// <summary>
/// One long Savage Wind job at a time (an install, an update, a setup), kept here so its progress survives leaving
/// the screen. Errors go to the error window; Cancel stops it where that's safe.
/// </summary>
public static class SwJob
{
    public static bool Running { get; private set; }
    public static string Title { get; private set; } = "";
    public static JobProgress? Last { get; private set; }
    static CancellationTokenSource? cts;
    public static event Action? Changed;

    public static void Cancel() => cts?.Cancel();

    /// <summary>Runs it; true when it finished.</summary>
    public static async Task<bool> Run(string title, Func<IProgress<JobProgress>, CancellationToken, Task> work)
    {
        if (Running) { App.Main.Toast("Something is already running: wait for it to finish."); return false; }
        Running = true; Title = title; Last = new JobProgress(0, "Starting", -1, ""); cts = new CancellationTokenSource();
        Changed?.Invoke();
        var pr = new Progress<JobProgress>(p => { Last = p; Changed?.Invoke(); });
        Log.Info("Job: " + title);
        var token = cts.Token;
        // Off the UI thread: installs and updates walk whole folders, wait on files held by antivirus and copy
        // gigabytes, which froze the window for seconds at a time (progress still comes back through Progress<T>).
        try { await Task.Run(() => work(pr, token)); App.Main.Toast(title + ": done"); return true; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { App.Main.Toast(title + ": cancelled"); Log.Info("Job cancelled: " + title); return false; }
        catch (OperationCanceledException e) { ErrorWindow.Show(title + " failed", "The connection timed out. Nothing was changed past the last finished step; try again.", e); return false; }
        catch (SwInstaller.InstallException e) { ErrorWindow.Show(e.Title, e.Message); return false; }
        // anything else is shown too (a program antivirus took away, a bad path): callers drop the task, so an
        // error left to escape here was never seen
        catch (Exception e) { ErrorWindow.Show(title + " failed", e.Message, e); return false; }
        finally { Running = false; cts.Dispose(); cts = null; Changed?.Invoke(); }
    }
}

/// <summary>
/// Play: a big Play that goes straight into Savage Wind (past Savage Wind Configuration), the copies of Savage Wind on
/// this computer (install, update, set up, go back), Clear Skies, and every Savage Wind setting. Optional: Settings
/// can hide all of it. Starting savagewind.exe yourself, or opening Savage Wind Configuration, is never changed.
/// </summary>
public sealed class LaunchView : ViewBase
{
    readonly ContentControl job = new(), hero = new(), list = new(), clear = new(), options = new();
    readonly ProgressBar overall = new() { Minimum = 0, Maximum = 1, Height = 8 }, stepBar = new() { Minimum = 0, Maximum = 1, Height = 4 };
    readonly TextBlock jobTitle = Ui.Text("", "h3"), jobStep = Ui.Text("", "small"), jobDetail = Ui.Text("", "faint small mono");
    List<SwInstall> installs = [];
    SwInstall? sw;
    ClearSkies? cs;
    Launcher.Release? swRelease, csRelease;
    string? swCheckError, csCheckError;
    bool checkedOnline, lastRunning, showInstall;
    List<SwSetup.GameFiles>? gameFiles;
    static readonly Dictionary<string, string> loggedState = [];
    DispatcherTimer? runningPoll;

    protected override Control Build()
    {
        var cancel = Ui.Button("Cancel", SwJob.Cancel, "small ghost");
        jobStep.TextWrapping = TextWrapping.Wrap;
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(jobTitle); Grid.SetColumn(cancel, 1); head.Children.Add(cancel);
        job.Content = Ui.Panel(Ui.Stack(8, head, overall, jobStep, stepBar, jobDetail), "panel glow");
        job.IsVisible = false;
        var left = Ui.Stack(16, job, hero, list, clear);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("5*,6*") };
        left.Margin = new Thickness(0, 0, 16, 0);
        grid.Children.Add(left);
        Grid.SetColumn(options, 1);
        grid.Children.Add(options);
        var page = Ui.Stack(16, Ui.Header("Play"), grid);
        SwJob.Changed += () => Dispatcher.UIThread.Post(() => { if (shown) DrawJob(); });
        return new ScrollViewer { Content = new Border { Padding = new Thickness(24, 20), Child = page } };
    }

    bool shown;

    protected override void OnShown(object? arg)
    {
        shown = true;
        Refresh();
        DrawJob();
        runningPoll ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        runningPoll.Tick -= OnPoll; runningPoll.Tick += OnPoll; runningPoll.Start();
        if (!checkedOnline) { checkedOnline = true; _ = CheckOnline(); }
    }

    public override void OnHidden() { shown = false; runningPoll?.Stop(); }

    void OnPoll(object? s, EventArgs e)
    {
        bool r = Launcher.GameRunning();
        if (r != lastRunning) { lastRunning = r; DrawHero(); DrawOptions(); }
    }

    void Refresh()
    {
        var st = App.Progress.Settings;
        installs = Launcher.FindInstalls(st);
        sw = installs.FirstOrDefault(i => i.Exe.Equals(st.GameProgram, StringComparison.OrdinalIgnoreCase))
             ?? installs.Where(i => i.GameFilesReady).OrderByDescending(i => i.Version ?? "", Comparer<string>.Create(Launcher.CompareVersions)).FirstOrDefault()
             ?? installs.FirstOrDefault();
        foreach (var i in installs)
        {
            var state = i.SetupState();
            if (loggedState.GetValueOrDefault(i.Exe) != state) { loggedState[i.Exe] = state; Log.Info($"Copy {Log.Scrub(i.Exe)}: {state}{(i.SetupPending ? " -> needs setting up" : "")}"); }
        }
        App.SyncDesktopEntries();  // (Linux: the app menu follows the list of copies)
        cs = ClearSkies.Find(st);
        if (cs != null && st.ClearSkiesProgram != cs.Program) App.Progress.SaveSettings(x => x.ClearSkiesProgram = cs.Program);
        lastRunning = Launcher.GameRunning();
        DrawHero(); DrawList(); DrawClear(); DrawOptions();
    }

    string NameOf(SwInstall i) => App.Progress.Settings.InstallNames.GetValueOrDefault(i.Exe) is { Length: > 0 } n ? n : i.Label;

    // ---------------------------------------------------------------- the running job

    void DrawJob()
    {
        job.IsVisible = SwJob.Running;
        if (SwJob.Last is { } p)
        {
            jobTitle.Text = SwJob.Title;
            overall.IsIndeterminate = p.Overall < 0; if (p.Overall >= 0) overall.Value = p.Overall;
            stepBar.IsIndeterminate = p.StepFraction < 0; if (p.StepFraction >= 0) stepBar.Value = p.StepFraction;
            jobStep.Text = p.Step + (p.Overall >= 0 ? $"  ·  {p.Overall:P0} overall" : "");
            jobDetail.Text = p.Detail;
        }
        if (!SwJob.Running) { Refresh(); }
        else DrawHero();
    }

    // ---------------------------------------------------------------- Play

    void DrawHero()
    {
        var s = Ui.Stack(12);
        var icon = sw != null && File.Exists(Path.Combine(sw.Folder, "icon.png")) ? Load(Path.Combine(sw.Folder, "icon.png"), 64) : null;
        s.Children.Add(Head(Ui.Img(icon, 56) ?? Ui.Glyph("launch", 48), "SAVAGE WIND", sw != null ? NameOf(sw) : "The Legend of Zelda: Breath of the Wild, on PC"));
        if (sw == null)
        {
            s.Children.Add(Ui.Text("No Savage Wind on this computer yet. Install one below (Slate Companion downloads it, checks it and sets it up with your game files), or add a copy you already have.", "muted small"));
            ((TextBlock)s.Children[^1]).TextWrapping = TextWrapping.Wrap;
            hero.Content = Ui.Panel(s, "panel glow");
            return;
        }
        var tags = new WrapPanel();
        void T(string t, string kind) { var b = Ui.Tag(t, kind); b.Margin = new Thickness(0, 0, 6, 4); tags.Children.Add(b); }
        T(sw.Version is { } v ? "Version " + v : "Built from source", "blue");
        if (!sw.GameFilesReady) T("Game files not set up", "orange"); else T("Game files ready", "done");
        if (sw.SetupPending) T("New version: needs setting up", "orange");
        if (cs != null && App.Progress.Settings.UseClearSkies) T("With Clear Skies", "blue");
        if (lastRunning) T("Running now", "done");
        s.Children.Add(tags);

        string label = lastRunning ? "Running" : !sw.GameFilesReady ? "Set up game files" : sw.SetupPending ? "Set up and play" : "Play";
        var play = new Button { Content = Ui.Row(12, Ui.Glyph("launch", 26), new TextBlock { Text = label }), IsEnabled = !lastRunning && !SwJob.Running, HorizontalAlignment = HorizontalAlignment.Left };
        play.Classes.Add("play");
        play.Click += (_, _) => PlayNow();
        s.Children.Add(play);
        string note = lastRunning ? "Savage Wind is running. Settings here apply the next time it starts."
            : !sw.GameFilesReady ? "This copy needs your game files once. Choose them under Your copies of Savage Wind below."
            : sw.SetupPending ? "This version hasn't been set up yet: Slate Companion builds its game code and precompiles its shaders first (a few minutes), then starts the game."
            : "Goes straight into the game. Starting savagewind.exe yourself works as always.";
        var n = Ui.Text(note, "muted small"); n.TextWrapping = TextWrapping.Wrap; s.Children.Add(n);
        s.Children.Add(Ui.Row(8,
            Ui.Button("Savage Wind Configuration", () => Report(Launcher.OpenNormally(sw, config: true), "Savage Wind Configuration didn't open"), "small ghost"),
            Ui.Button("Open folder", () => ErrorWindow.OpenPath(sw.Folder), "small ghost"),
            Ui.Button("Its log", () => ErrorWindow.OpenPath(Path.Combine(sw.DataRoot, "savagewind.log")), "small ghost")));
        hero.Content = Ui.Panel(s, "panel glow");
    }

    async void PlayNow()
    {
        if (sw == null) return;
        var target = sw;
        if (!target.GameFilesReady) { showInstall = false; ShowGameFilesFor(target); return; }
        if (target.SetupPending && !await SwJob.Run($"Setting up Savage Wind {target.Version}", (p, ct) => SwSetup.Run(target, null, p, ct))) return;
        var st = App.Progress.Settings;
        var useCs = st.UseClearSkies && cs != null ? cs : null;
        Log.Info($"Play: {target.Exe}{(useCs != null ? $" with Clear Skies ({st.ClearSkiesPreset ?? "saved preset"})" : "")}");
        var err = Launcher.Play(target, useCs, st.ClearSkiesPreset);
        if (err != null) { Report(err, "Savage Wind didn't start"); return; }
        App.Main.Toast("Starting Savage Wind…");
        DispatcherTimer.RunOnce(() => { lastRunning = Launcher.GameRunning(); DrawHero(); }, TimeSpan.FromSeconds(6));
        if (st.MinimizeOnPlay) App.Main.WindowState = WindowState.Minimized;
    }

    // ---------------------------------------------------------------- the copies: install, update, set up, go back

    SwInstall? pickingFilesFor;

    void DrawList()
    {
        var s = Ui.Stack(10, Ui.Text("YOUR COPIES OF SAVAGE WIND", "h2"));
        if (swRelease != null) s.Children.Add(Ui.Text($"Newest release: {swRelease.Name}", "faint small"));
        else if (swCheckError != null) s.Children.Add(Ui.Row(8, Ui.Text("Couldn't reach GitHub: " + swCheckError, "faint small"), Ui.Button("Try again", () => { _ = CheckOnline(); }, "small ghost")));
        foreach (var i in installs) s.Children.Add(InstallRow(i));
        if (pickingFilesFor != null) s.Children.Add(GameFilesPicker(pickingFilesFor));
        if (showInstall) s.Children.Add(InstallForm());
        else s.Children.Add(Ui.Row(8, Ui.Button("Install Savage Wind…", () => { showInstall = true; pickingFilesFor = null; ScanGameFiles(); DrawList(); }, "primary small"), Ui.Button("Add a copy you have…", ChooseInstall, "small ghost")));
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
        void B(string text, Action a, string cls = "small ghost") { var b = Ui.Button(text, a, cls); b.Margin = new Thickness(0, 4, 6, 0); b.IsEnabled = !SwJob.Running; buttons.Children.Add(b); }
        bool newer = swRelease != null && i.Version != null && Launcher.CompareVersions(swRelease.Version, i.Version) > 0;
        if (!chosen) B("Use this one", () => { App.Progress.SaveSettings(x => x.GameProgram = i.Exe); Refresh(); });
        if (newer) B($"Update to {swRelease!.Version}", () => UpdateInstall(i), "small primary");
        if (!i.GameFilesReady) B("Set up game files…", () => ShowGameFilesFor(i), "small primary");
        else if (i.SetupPending) B("Finish setting up", () => _ = SwJob.Run($"Setting up Savage Wind {i.Version}", (p, ct) => SwSetup.Run(i, null, p, ct)), "small primary");
        if (SwInstaller.CanGoBack(i)) B("Go back to the previous version", () => GoBack(i));
        B("Rename", () => Rename(i));
        B("Open folder", () => ErrorWindow.OpenPath(i.Folder));
        B("Remove from list", () =>
        {
            App.Progress.SaveSettings(x => { if (!x.HiddenInstalls.Contains(i.Exe)) x.HiddenInstalls.Add(i.Exe); if (x.GameProgram == i.Exe) x.GameProgram = null; });
            App.Main.Toast("Removed from the list (nothing was deleted)");
            Refresh();
        });
        var status = i.Version == null ? "Built from source: it updates with git." : newer ? $"{i.Version} · an update is available" : swRelease != null ? $"{i.Version} · up to date" : i.Version;
        return Ui.Panel(Ui.Stack(4, top, Ui.Text(status + (i.GameFilesReady ? "" : " · no game files yet"), "muted small"), buttons), chosen ? "panel glow" : "panel");
    }

    void UpdateInstall(SwInstall i)
    {
        var r = swRelease!;
        _ = SwJob.Run($"Updating to Savage Wind {r.Version}", async (p, ct) =>
        {
            if (i.AppImage)
            {
                var err = await SwUpdate.Install(i, r, (f, t) => ((IProgress<JobProgress>)p).Report(new JobProgress(f * 0.3, t, f, "")), ct);
                if (err != null) throw new SwInstaller.InstallException("Savage Wind wasn't updated", err);
            }
            else await SwInstaller.UpdateWindows(i, r, new Scaled(p, 0, 0.3), ct);
            var now = new SwInstall(i.Exe, i.DataRoot);
            if (now.GameFilesReady) await SwSetup.Run(now, null, new Scaled(p, 0.3, 1), ct);
        });
    }

    // as a job: hundreds of file moves off the UI thread, every error shown, and nothing else started meanwhile
    void GoBack(SwInstall i) => _ = SwJob.Run("Going back to the previous version", (_, _) => { SwInstaller.GoBack(i); return Task.CompletedTask; });

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

    // ---------------------------------------------------------------- game files

    void ScanGameFiles()
    {
        if (gameFiles != null) return;
        var known = installs.ToList();
        _ = Task.Run(() => SwSetup.FindGameFiles(known, TimeSpan.FromSeconds(6))).ContinueWith(t => Dispatcher.UIThread.Post(() => { gameFiles = t.IsCompletedSuccessfully ? t.Result : []; DrawList(); }));
    }

    void ShowGameFilesFor(SwInstall i) { pickingFilesFor = i; showInstall = false; ScanGameFiles(); DrawList(); list.BringIntoView(); }

    /// <summary>The game files found (other copies' first), or a folder chosen by hand.</summary>
    Control GameFilesChoice(Action<string?> picked, string? none = null)
    {
        var s = Ui.Stack(6);
        if (gameFiles == null) { s.Children.Add(Ui.Text("Looking for your game files…", "muted small")); return s; }
        var items = gameFiles.Select(g => g.Label).ToList();
        if (none != null) items.Add(none);
        var combo = new ComboBox { ItemsSource = items, HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = items.Count > 0 ? 0 : -1, PlaceholderText = "No game files found: choose the folder" };
        combo.SelectionChanged += (_, _) => picked(combo.SelectedIndex >= 0 && combo.SelectedIndex < gameFiles.Count ? gameFiles[combo.SelectedIndex].Folder : null);
        picked(gameFiles.Count > 0 ? gameFiles[0].Folder : null);
        var choose = Ui.Button("Choose folder…", async () =>
        {
            var f = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose the folder with your game files (exefs and romfs)" });
            var path = f.FirstOrDefault()?.TryGetLocalPath();
            if (path == null) return;
            if (!File.Exists(Path.Combine(path, "exefs", "main")) && !Directory.EnumerateDirectories(path).Any(d => File.Exists(Path.Combine(d, "exefs", "main"))))
            { ErrorWindow.Show("No game files there", $"{Ui.ShortPath(path)} has no exefs/main. Choose the folder your game was dumped to: it has an exefs and a romfs folder."); return; }
            gameFiles.Insert(0, new SwSetup.GameFiles(path, "Game files in " + Ui.ShortPath(path), false));
            DrawList();
        }, "small ghost");
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        g.Children.Add(combo); choose.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(choose, 1); g.Children.Add(choose);
        s.Children.Add(g);
        s.Children.Add(Ui.Text("Your own game files, dumped from your Switch (Breath of the Wild 1.6.0 with its update; the DLC's files too if you have it). From another copy of Savage Wind they're linked, not copied again, where the disk allows.", "faint small"));
        ((TextBlock)s.Children[^1]).TextWrapping = TextWrapping.Wrap;
        return s;
    }

    Control GameFilesPicker(SwInstall i)
    {
        string? source = null;
        var go = Ui.Button("Set up", () =>
        {
            if (source == null) { ErrorWindow.Show("Choose your game files", "Pick the game files found, or choose their folder."); return; }
            pickingFilesFor = null;
            var src = source;
            _ = SwJob.Run("Setting up Savage Wind's game files", (p, ct) => SwSetup.Run(i, src, p, ct));
        }, "primary small");
        return Ui.Panel(Ui.Stack(8, Ui.Text($"GAME FILES FOR {NameOf(i).ToUpperInvariant()}", "faint small"), GameFilesChoice(f => source = f),
            Ui.Row(8, go, Ui.Button("Cancel", () => { pickingFilesFor = null; DrawList(); }, "small ghost"))), "panel glow");
    }

    Control InstallForm()
    {
        var st = App.Progress.Settings;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string folder = st.InstallFolder ?? (OperatingSystem.IsWindows() ? (Directory.Exists(@"C:\Games") ? @"C:\Games" : Path.Combine(home, "Games")) : Path.Combine(home, "Applications"));
        var where = new TextBox { Text = folder, HorizontalAlignment = HorizontalAlignment.Stretch };
        var browse = Ui.Button("Browse…", async () =>
        {
            var f = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Where to install Savage Wind" });
            if (f.FirstOrDefault()?.TryGetLocalPath() is { } p) where.Text = p;
        }, "small ghost");
        var wg = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        wg.Children.Add(where); browse.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(browse, 1); wg.Children.Add(browse);
        string? source = null;
        var files = GameFilesChoice(f => source = f, "Set up the game files later");
        var go = Ui.Button(swRelease != null ? $"Install Savage Wind {swRelease.Version}" : "Install", () => InstallNew(string.IsNullOrWhiteSpace(where.Text) ? folder : where.Text.Trim(), source), "primary");
        go.IsEnabled = swRelease != null && !SwJob.Running;
        var s = Ui.Stack(10, Ui.Text("INSTALL SAVAGE WIND", "faint small"),
            Ui.Text("Downloads the newest release from GitHub, checks it against Savage Wind's signature, and sets it up with your game files: the game code is built and the shaders precompiled, with progress here. Savage Wind's own screens aren't needed.", "muted small"),
            Ui.Text(OperatingSystem.IsWindows() ? "Where (a Savage Wind folder is made inside it)" : "Where (the AppImage goes in this folder; its data is in ~/.local/share/savagewind)", "faint small"), wg,
            Ui.Text("Game files", "faint small"), files,
            Ui.Row(8, go, Ui.Button("Cancel", () => { showInstall = false; DrawList(); }, "small ghost")));
        ((TextBlock)s.Children[1]).TextWrapping = TextWrapping.Wrap;
        return Ui.Panel(s, "panel glow");
    }

    void InstallNew(string parent, string? source)
    {
        var r = swRelease!;
        if (!Path.IsPathFullyQualified(parent)) { ErrorWindow.Show("Choose a folder", "Where to install needs a full folder path (Browse… picks one)."); return; }
        App.Progress.SaveSettings(x => x.InstallFolder = parent);
        _ = SwJob.Run($"Installing Savage Wind {r.Version}", async (p, ct) =>
        {
            var exe = await SwInstaller.InstallNew(parent, r, new Scaled(p, 0, source != null ? 0.25 : 1), ct);
            var folder = Path.GetDirectoryName(exe)!;
            // (the settings and this screen belong to the UI thread; the job runs on a worker)
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                App.Progress.SaveSettings(x =>
                {
                    if (OperatingSystem.IsWindows()) { if (!x.SavageWindFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) x.SavageWindFolders.Add(folder); }
                    else if (!x.SavageWindPrograms.Contains(exe)) x.SavageWindPrograms.Add(exe);
                    x.HiddenInstalls.Remove(exe);
                    x.GameProgram = exe;
                });
                showInstall = false;
            });
            if (source != null) await SwSetup.Run(new SwInstall(exe, Launcher.DataRootFor(exe)), source, new Scaled(p, 0.25, 1), ct);
        });
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

    // ---------------------------------------------------------------- online checks

    async Task CheckOnline()
    {
        swCheckError = csCheckError = null;
        try { swRelease = await Launcher.LatestRelease(SwUpdate.Repo); }
        catch (Exception e) { swCheckError = e.Message; Log.Warn("Savage Wind update check: " + e.Message); }
        try { csRelease = await Launcher.LatestRelease(ClearSkies.Repo); }
        catch (Exception e) { csCheckError = e.Message; Log.Warn("Clear Skies update check: " + e.Message); }
        DrawList(); DrawClear();
    }

    // ---------------------------------------------------------------- Clear Skies

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
                Ui.Button("Update", () => Report(cs.Open(false), "Clear Skies didn't open"), "small primary")));
            var t = Ui.Text("Update opens Clear Skies, which downloads it, checks it against GitHub's checksum and restarts; your looks and mods are kept.", "faint small");
            t.TextWrapping = TextWrapping.Wrap; s.Children.Add(t);
        }
        else
            s.Children.Add(Ui.Text($"Clear Skies {v ?? "(version unknown)"} · {Ui.ShortPath(cs.Folder)}" + (csRelease != null && v != null ? " · up to date" : csCheckError != null ? " · couldn't check for updates" : ""), "faint small"));
        clear.Content = Ui.Panel(s);
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

    void DrawOptions() => options.Content = SwOptionsPanel.Build(sw, lastRunning);

    // ---------------------------------------------------------------- helpers

    /// <summary>A job's progress mapped into part of the whole (an update's swap, then its setup).</summary>
    sealed class Scaled(IProgress<JobProgress> inner, double from, double to) : IProgress<JobProgress>
    {
        public void Report(JobProgress p) => inner.Report(p with { Overall = p.Overall < 0 ? -1 : from + (to - from) * p.Overall });
    }

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
