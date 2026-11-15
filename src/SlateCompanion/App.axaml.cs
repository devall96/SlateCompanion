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
    }

    /// <summary>Once, look for Savage Wind installs in the usual places (a few seconds, in the background).</summary>
    static void FirstRunSearch()
    {
        if (Progress.Settings.SearchedForSavageWind) return;
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
