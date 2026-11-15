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

        if (args.Length >= 2 && args[0] == "--build-map") return BuildMapCli(args[1]);

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        // Software drawing: no GPU work at all, so nothing competes with the game for the graphics card.
        .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
        .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] })
        .LogToTrace();

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
