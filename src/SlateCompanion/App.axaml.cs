using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SlateCompanion;

public partial class App : Application
{
    public static Store Store { get; private set; } = null!;
    public static GameData Data { get; private set; } = null!;
    public static Progress Progress { get; private set; } = null!;
    public static MapBuilder MapBuilder { get; private set; } = null!;
    public static MainWindow Main { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // A mistake in one screen's code: logged, a crash report written, an error window shown, and the app keeps going.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            var file = Log.Crash(e.Exception, "UI");
            UI.ErrorWindow.Show("Something went wrong", "Slate Companion ran into a problem but kept running. If it keeps happening, please report it with the crash report.", e.Exception, file);
        };
        Store = new Store();
        Data = GameData.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        Progress = new Progress(Store, Data);
        MapBuilder = new MapBuilder(Store.Folder);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Main = new MainWindow();
            desktop.MainWindow = Main;
            desktop.Exit += (_, _) => Progress.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
        _ = Progress.RefreshAsync();
        FirstRunSearch();
        SyncDesktopEntries();
    }

    /// <summary>Linux: keeps the app-menu entries up to date (Slate Companion and each Savage Wind AppImage), in the background.</summary>
    public static void SyncDesktopEntries()
    {
        if (!DesktopEntries.Supported || Program.Args.Contains("--screenshots")) return;
        var s = Progress.Settings;
        if (!s.AddToApplications) return;
        var installs = Launcher.FindInstalls(s);
        var names = new Dictionary<string, string>(s.InstallNames);
        _ = Task.Run(() => { try { DesktopEntries.Sync(installs, names); } catch (Exception e) { Log.Warn("desktop entries: " + e.Message); } });
    }

    /// <summary>Once, look for Savage Wind installs in the usual places (a few seconds, in the background).</summary>
    static void FirstRunSearch()
    {
        // (not for --screenshots: pictures for the README must not list folders of the PC they're taken on)
        if (Progress.Settings.SearchedForSavageWind || Program.Args.Contains("--screenshots")) return;
        _ = Task.Run(() =>
        {
            var found = SaveLocator.SearchSavageWind(TimeSpan.FromSeconds(6));
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Progress.SaveSettings(s =>
                {
                    foreach (var f in found) if (!s.SavageWindFolders.Contains(f, StringComparer.OrdinalIgnoreCase)) s.SavageWindFolders.Add(f);
                    s.SearchedForSavageWind = true;
                });
                if (found.Count > 0) _ = Progress.RefreshAsync();
            });
        });
    }
}
