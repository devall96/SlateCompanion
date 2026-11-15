using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace SlateCompanion.UI;

/// <summary>
/// First start (or whenever nothing is built): Slate Companion needs the player's own game files. It looks for
/// them in Savage Wind's folders (extracted/romfs), lets the player pick a folder, builds the map, icons, pictures and
/// the game's text once, and only then opens the app. The game files are only read.
/// </summary>
public sealed class SetupOverlay : Border
{
    readonly ContentControl body = new();
    readonly Action done;
    DispatcherTimer? poll;
    string? error;

    public SetupOverlay(Action done)
    {
        this.done = done;
        Background = new SolidColorBrush(Color.Parse("#F2071116"));
        var panel = new Border
        {
            Width = 720, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = Ui.Brush("Panel"), BorderBrush = Ui.Brush("Glow"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18),
            Padding = new Thickness(32, 28), BoxShadow = BoxShadows.Parse("0 0 50 0 #3039C6FF"), Child = body,
        };
        Child = panel;
        App.Progress.Changed += () => { if (IsVisible && App.MapBuilder.State != "building") Draw(); };  // Savage Wind installs found later
    }

    public bool Needed => !Program.Demo && !App.MapBuilder.Complete;

    public void Show()
    {
        IsVisible = Needed;
        if (IsVisible) Draw();
    }

    /// <summary>The home folder shown as ~ (shorter, and keeps the user name off screen).</summary>
    static string ShortPath(string p)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && p.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + p[home.Length..] : p;
    }

    static List<string> Candidates()
    {
        var s = App.Progress.Settings;
        var list = new List<string>();
        foreach (var f in new[] { s.GameFilesFolder }.Concat(s.SavageWindFolders))
            if (MapBuilder.FindRomfs(f, []) is string r && !list.Contains(r, StringComparer.OrdinalIgnoreCase)) list.Add(r);
        return list;
    }

    void Draw()
    {
        var b = App.MapBuilder;
        bool firstStart = !App.Progress.Settings.SetupSeen;
        var s = Ui.Stack(14, Ui.Row(14, Ui.Eye(56), Ui.Stack(2, Ui.Text("SHEIKAH SLATE", "eyebrow"),
            Ui.Text(firstStart ? "Welcome to Slate Companion" : "Your game files are needed", "h1"))));
        if (b.State == "building")
        {
            var meter = Ui.Meter(b.Progress, 1, false, 10);
            s.Children.Add(Ui.Text("Building the map, icons, item pictures, compendium photos and in-game text from your game files. This happens once and takes under a minute.", "muted"));
            s.Children.Add(meter);
            s.Children.Add(Ui.Row(10, Ui.Text($"{b.Progress:P0}", "mono"), Ui.Text(b.Message, "faint")));
            s.Children.Add(Ui.Button("Cancel", () => b.Cancel(), "small ghost"));
            body.Content = s;
            return;
        }
        s.Children.Add(Ui.Text("Slate Companion is made to be used with your own, legally obtained copy of Breath of the Wild — the game files in your Savage Wind folder. Everything you see comes from them: the in-game map and its icons, item pictures, compendium photos, your memory photos and the game's own names, descriptions and quest log. No game content ships with the app. Your files are only read, never changed, and nothing is sent anywhere.", "muted"));
        if (firstStart)
        {
            var tips = Ui.Stack(4, Ui.Text("QUICK TIPS", "h2"));
            foreach (var t in new[]
            {
                "Your progress ticks itself off from your save as you play: blue means done in your save, green means ticked by hand.",
                "Turn on Slate Companion in Savage Wind Configuration to see Link move on the map while you play.",
                "Ctrl+K searches everything: shrines, quests, recipes, materials, armor and guides.",
                "Right-click the map to drop your own pin. Drop a save folder or file on the window to read it.",
                "Minimize the window while you play: it goes fully to sleep and keeps your game running smoothly.",
            })
                tips.Children.Add(Ui.Row(8, Ui.Text("◆", "small"), new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, MaxWidth = 610, FontSize = 13 }));
            foreach (var row in tips.Children.Skip(1).OfType<StackPanel>()) ((TextBlock)row.Children[0]).Foreground = Ui.Brush("Orange");
            s.Children.Add(Ui.Panel(tips, "panel"));
        }
        if (error != null || b.State == "error") s.Children.Add(new Border { Classes = { "note" }, Child = Ui.Text(error ?? b.Message, "small") });

        var found = Candidates();
        if (found.Count > 0)
        {
            s.Children.Add(Ui.Text(found.Count == 1 ? "FOUND ON THIS COMPUTER" : $"FOUND ON THIS COMPUTER ({found.Count}) — ANY OF THEM WILL DO", "h2"));
            var list = new StackPanel { Spacing = 8 };
            foreach (var r in found)
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                g.Children.Add(Ui.Stack(2, Ui.Text("Breath of the Wild game files", "h3"), Ui.Text(ShortPath(r), "faint mono")));
                var use = Ui.Button("Use these", () => Start(r), "primary");
                use.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(use, 1); g.Children.Add(use);
                list.Children.Add(Ui.Panel(g, "panel glow"));
            }
            s.Children.Add(new ScrollViewer { Content = list, MaxHeight = 300 });
        }
        else s.Children.Add(Ui.Text(App.Progress.Settings.SearchedForSavageWind ? "No game files found yet." : "Looking for Savage Wind on this computer…", "faint"));

        var pick = Ui.Button("Choose your Savage Wind folder…", Pick, found.Count == 0 ? "primary" : "");
        pick.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(Ui.Stack(4, pick, Ui.Text("The folder with savagewind.exe (or ~/.local/share/savagewind on Linux). An extracted romfs folder works too.", "faint")));
        body.Content = s;
    }

    async void Pick()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose your extracted game files (romfs) or a Savage Wind folder" });
        var path = picked.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;
        var romfs = MapBuilder.FindRomfs(path, []);
        if (romfs == null) { error = "No Breath of the Wild game files there. Pick the romfs folder of your extracted game, or a Savage Wind folder."; Draw(); return; }
        App.Progress.SaveSettings(x => x.GameFilesFolder = path);
        Start(romfs);
    }

    void Start(string romfs)
    {
        error = null;
        App.MapBuilder.Start(romfs, App.Progress.Settings.GameLanguage);
        Draw();
        poll?.Stop();
        poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        poll.Tick += (_, _) =>
        {
            if (App.MapBuilder.State == "building") { Draw(); return; }
            poll!.Stop();
            Icons.Reset(); Assets.Reset();
            if (App.MapBuilder.Complete) { App.Progress.SaveSettings(x => x.SetupSeen = true); IsVisible = false; done(); }
            else Draw();
        };
        poll.Start();
    }
}
