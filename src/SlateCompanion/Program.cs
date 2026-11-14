using System.Diagnostics;
using Avalonia;

namespace SlateCompanion;

/// <summary>
/// Slate Companion: a native window (Avalonia, drawn in software so the game keeps the graphics card) at below-normal
/// priority. It reads saves (never writes them), builds the map from the player's own game files once, and asks a
/// running Savage Wind where Link is over its local read-only companion socket.
/// </summary>
public static class Program
{
    public static string[] Args { get; private set; } = [];

    /// <summary>
    /// --demo: runs without any game assets (no map image, game icons, pictures or game text) and without asking for
    /// game files: for screenshots that show only the app itself.
    /// </summary>
    public static bool Demo => Args.Contains("--demo");

    [STAThread]
    public static int Main(string[] args)
    {
        // The installer's hooks (install, update, uninstall) run here and exit; a normal start just continues.
        Velopack.VelopackApp.Build().Run();
        Args = args;
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { }

        Log.Start();
        // Anything not caught anywhere else: a crash report, and the player is told where it is.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is not Exception ex) return;
            var file = Log.Crash(ex, "unhandled");
            Log.NativeAlert("Slate Companion crashed", $"Slate Companion ran into a problem it couldn't recover from and has to close.\n\n{ex.Message}\n\nA crash report was saved to:\n{Log.Scrub(file ?? Log.Folder)}\n\nPlease report it at github.com/devall96/SlateCompanion/issues and attach that file.");
        };
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Background task failed", e.Exception); e.SetObserved(); };

        if (args.Length >= 2 && args[0] == "--build-map") return BuildMapCli(args[1]);
        // --render-model <romfs> <file[:model,...]> <out.png> [size] [auto|front|zup|back|side]: one BFRES model drawn
        // offline into a transparent PNG (paper doll prototype)
        // --render-doll <romfs> <out.png> [head=.. upper=.. lower=.. weapon=.. shield=.. bow=.. size=..]: the paper doll;
        // --render-actor <romfs> <actor> <out.png> [size] [view]: one actor's models as a picture (e.g. the cooking pot);
        // --model-info / --actor-info <romfs> <name>: what a model or actor pack holds (debugging)
        if (args.Length >= 3 && args[0] is "--render-model" or "--render-doll" or "--render-actor" or "--model-info" or "--actor-info") return ModelRender.Cli(args);
        // --sw-install <parent folder> [<game files>] / --sw-setup <savagewind program> [<game files>] /
        // --sw-update <savagewind program>: the Play screen's jobs from the command line, progress printed (testing,
        // and for people who'd rather script it)
        if (args.Length >= 2 && args[0] is "--sw-install" or "--sw-setup" or "--sw-update") return SwCli(args);
        // --app-menu on|off (Linux): the app-menu entries, as the switch in Settings does
        if (args.Length >= 2 && args[0] == "--app-menu")
        {
            var st = new Store().LoadSettings();
            if (args[1] == "off") { DesktopEntries.RemoveAll(); Console.WriteLine("removed"); }
            else Console.WriteLine($"{DesktopEntries.Sync(Launcher.FindInstalls(st), st.InstallNames)} entries");
            return 0;
        }
        // --equip <save folder> <slot folder> <Slot>=<PorchItem index|none> ...: the Inventory's Equip as a command (testing)
        if (args.Length >= 4 && args[0] == "--equip")
        {
            try
            {
                var choice = args.Skip(3).Select(a => a.Split('=', 2)).ToDictionary(kv => kv[0], kv => kv[1] == "none" ? (int?)null : int.Parse(kv[1]));
                Console.WriteLine(SaveManager.EditGameData(args[1], args[2], s => Equipment.Apply(s, choice)));
                foreach (var it in Equipment.Pouch(SaveFile.Load(Path.Combine(args[2], "game_data.sav"))).Where(i => i.equipped)) Console.WriteLine($"  equipped: [{it.index}] {it.actor}");
                return 0;
            }
            catch (SaveManager.SaveException e) { Console.WriteLine($"FAILED: {e.Title}: {e.Message}"); return 1; }
        }
        // --sw-list: every copy of Savage Wind found, with its version and whether it needs setting up (and why)
        if (args.Length >= 1 && args[0] == "--sw-list")
        {
            foreach (var i in Launcher.FindInstalls(new Store().LoadSettings()))
            {
                Console.WriteLine(Log.Scrub(i.Exe));
                Console.WriteLine($"  {i.SetupState()}{(i.SetupPending ? " -> needs setting up" : " -> ready")}");
            }
            return 0;
        }
        // --convert-save <save folder> <output folder> <Bootup.pack> switch|wiiu: a whole save converted (testing)
        if (args.Length >= 5 && args[0] == "--convert-save")
        {
            var report = new List<string>();
            var root = SaveConvert.FindProfile(args[1]) ?? throw new DirectoryNotFoundException("no save there");
            var files = SaveConvert.ConvertProfile(root, new SaveConvert.Format(args[3]), args[4] == "switch", report);
            foreach (var (rel, data) in files) { var p = Path.Combine(args[2], rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, data); }
            Console.WriteLine(string.Join(Environment.NewLine, report));
            return 0;
        }
        // --verify-sw <file> <file.sig>: checks a Savage Wind download against its release signature (exit 0: good)
        if (args.Length >= 3 && args[0] == "--verify-sw")
        {
            using var f = File.OpenRead(args[1]);
            bool ok = SwUpdate.SignatureOk(System.Security.Cryptography.SHA256.HashData(f), File.ReadAllText(args[2]));
            Console.WriteLine(ok ? "signature good" : "signature BAD");
            return ok ? 0 : 1;
        }

        try
        {
            int code = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            Log.Info($"Closed ({code})");
            return code;
        }
        catch (Exception e)
        {
            var file = Log.Crash(e, "start-up or main loop");
            Log.NativeAlert("Slate Companion crashed", $"Slate Companion ran into a problem and has to close.\n\n{e.Message}\n\nA crash report was saved to:\n{Log.Scrub(file ?? Log.Folder)}\n\nPlease report it at github.com/devall96/SlateCompanion/issues and attach that file.");
            return 1;
        }
    }

    /// <summary>
    /// Drawing with the graphics card (the default; much faster for the map), or in software (Settings, or
    /// SLATE_RENDER=software). Either way nothing is drawn while the window is minimized or in the background, so it
    /// doesn't compete with the game. If the graphics card can't be used, Avalonia falls back to software by itself.
    /// </summary>
    public static bool Gpu
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("SLATE_RENDER");
            if (env is "software") return false;
            if (env is "gpu") return true;
            try
            {
                var p = Path.Combine(Store.DefaultFolder(), "settings.json");
                if (File.Exists(p) && System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(p))?["gpuDrawing"] is { } v) return v.GetValue<bool>();
            }
            catch (Exception) { }
            return true;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        bool gpu = Gpu;
        Log.Info("Drawing: " + (gpu ? "graphics card (software if it can't be used)" : "software"));
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { RenderingMode = gpu ? [Win32RenderingMode.AngleEgl, Win32RenderingMode.Wgl, Win32RenderingMode.Software] : [Win32RenderingMode.Software] })
            .With(new X11PlatformOptions { RenderingMode = gpu ? [X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software] : [X11RenderingMode.Software] })
            .LogToTrace();
    }

    static int SwCli(string[] args)
    {
        var pr = new ConsoleProgress();
        try
        {
            Task.Run(async () =>
            {
                string? files = args.Length >= 3 ? args[2] : null;
                if (args[0] == "--sw-install")
                {
                    var r = await Launcher.LatestRelease(SwUpdate.Repo) ?? throw new InvalidOperationException("no release");
                    var exe = await SwInstaller.InstallNew(args[1], r, pr, CancellationToken.None);
                    Console.WriteLine("installed: " + exe);
                    if (files != null) await SwSetup.Run(new SwInstall(exe, Launcher.DataRootFor(exe)), files, pr, CancellationToken.None);
                }
                else if (args[0] == "--sw-update")
                {
                    var sw = new SwInstall(Path.GetFullPath(args[1]), Launcher.DataRootFor(Path.GetFullPath(args[1])));
                    var r = await Launcher.LatestRelease(SwUpdate.Repo) ?? throw new InvalidOperationException("no release");
                    await SwInstaller.UpdateWindows(sw, r, pr, CancellationToken.None);
                    await SwSetup.Run(new SwInstall(sw.Exe, sw.DataRoot), null, pr, CancellationToken.None);
                }
                else
                {
                    var exe = Path.GetFullPath(args[1]);
                    await SwSetup.Run(new SwInstall(exe, Launcher.DataRootFor(exe)), files, pr, CancellationToken.None);
                }
            }).GetAwaiter().GetResult();
            Console.WriteLine("done");
            return 0;
        }
        catch (SwInstaller.InstallException e) { Console.WriteLine($"FAILED: {e.Title}: {e.Message}"); return 1; }
        catch (Exception e) { Console.WriteLine("FAILED: " + e); return 1; }
    }

    sealed class ConsoleProgress : IProgress<JobProgress>
    {
        string lastStep = "";
        DateTime at;
        public void Report(JobProgress p)
        {
            var line = $"{(p.Overall < 0 ? "  ?" : $"{p.Overall * 100,3:0}")}% {p.Step} | {p.Detail}";
            if (p.Step == lastStep && (DateTime.UtcNow - at).TotalSeconds < 5) return;
            lastStep = p.Step; at = DateTime.UtcNow;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}");
        }
    }

    static int BuildMapCli(string path)
    {
        var store = new Store();
        var romfs = MapBuilder.FindRomfs(path, []);
        if (romfs == null) { Console.Error.WriteLine("No game files (romfs with UI/MapTex) found there."); return 2; }
        var b = new MapBuilder(store.Folder);
        b.Start(romfs);
        while (b.State == "building") { Thread.Sleep(2000); Console.WriteLine($"{b.Progress:P0} {b.Message}"); }
        Console.WriteLine($"{b.State}: {b.Message}");
        return b.State == "ready" ? 0 : 1;
    }
}
