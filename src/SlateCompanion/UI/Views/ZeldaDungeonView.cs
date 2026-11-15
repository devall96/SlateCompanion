using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SlateCompanion.UI.Views;

/// <summary>
/// Zelda Dungeon's own website, shown as it is in the system's web engine (WebView2 on Windows, WebKitGTK on Linux).
/// Nothing from it is copied into the app. It costs nothing until it's opened. Whenever this screen isn't showing
/// (another screen, another window has the focus) the page is hidden, which the engine throttles; 30 seconds later
/// it's closed outright, web processes and all (a hidden page with ads still uses some CPU). Minimized, or with Savage
/// Wind running, it's closed at once. Coming back reloads the page it was on, scrolled to where it was.
/// </summary>
public sealed class ZeldaDungeonView : ViewBase
{
    public const string Host = "zeldadungeon.net";
    public const string HomeUrl = "https://www.zeldadungeon.net/wiki/The_Legend_of_Zelda:_Breath_of_the_Wild";
    // (SLATE_ZD_UNLOAD_SECONDS shortens it for tests)
    static readonly TimeSpan UnloadAfter = int.TryParse(Environment.GetEnvironmentVariable("SLATE_ZD_UNLOAD_SECONDS"), out var t) && t > 0 ? TimeSpan.FromSeconds(t) : TimeSpan.FromSeconds(30);
    public static int Loads, Unloads;  // (for tests: how often the page was created and closed)

    readonly Border host = new();
    readonly TextBlock address = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
    readonly DispatcherTimer unloadTimer = new() { Interval = UnloadAfter };
    Button back = null!, forward = null!;
    NativeWebView? web;
    Uri last = new(HomeUrl);
    bool covered;
    double scrollY;  // where the page was scrolled to when it was last hidden (restored after a reload)
    Uri? scrollFor;
    // A link to another website, or one that wanted a new window: shown in a bar, opened in the browser only when the
    // player asks. (Never by itself: on Linux, WebKitGTK reports every ad frame's loads too, and each would open a tab.)
    readonly Border notice = new() { IsVisible = false, Padding = new Thickness(12, 8), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(8) };
    readonly TextBlock noticeText = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
    Button noticeBack = null!;
    Uri? outside;

    public ZeldaDungeonView() => unloadTimer.Tick += (_, _) => Unload("not looked at for a while");

    protected override Control Build()
    {
        back = Ui.Button("Back", () => web?.GoBack(), "small ghost");
        forward = Ui.Button("Forward", () => web?.GoForward(), "small ghost");
        var reload = Ui.Button("Reload", () => { if (web == null) Load(); else web.Refresh(); }, "small ghost");
        var home = Ui.Button("Breath of the Wild", () => Navigate(new Uri(HomeUrl)), "small ghost");
        var toBrowser = Ui.Button("Open in your browser", () => ErrorWindow.OpenPath(web?.Source?.ToString() ?? last.ToString()), "small");
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 10) };
        bar.Children.Add(Ui.Row(6, back, forward, reload, home));
        address.Margin = new Thickness(12, 0);
        address.Classes.Add("muted"); address.Classes.Add("small");
        Grid.SetColumn(address, 1); bar.Children.Add(address);
        Grid.SetColumn(toBrowser, 2); bar.Children.Add(toBrowser);
        var openOutside = Ui.Button("Open in your browser", () => { if (outside != null) ErrorWindow.OpenPath(outside.ToString()); HideNotice(); }, "small primary");
        noticeBack = Ui.Button("Back to Zelda Dungeon", () => { HideNotice(); Navigate(last); }, "small ghost");
        var close = Ui.Button("Dismiss", HideNotice, "small ghost");
        var ng = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        ng.Children.Add(noticeText);
        var nb = Ui.Row(8, openOutside, noticeBack, close); Grid.SetColumn(nb, 1); ng.Children.Add(nb);
        notice.Child = ng;
        notice.Background = Ui.Brush("Panel");
        notice.BorderBrush = Ui.Brush("Glow"); notice.BorderThickness = new Thickness(1);
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        body.Children.Add(bar);
        Grid.SetRow(notice, 1); body.Children.Add(notice);
        Grid.SetRow(host, 2); body.Children.Add(host);
        var note = Ui.Text("Zelda Dungeon's website, as it is. Not part of Slate Companion.", "faint small");
        return Rows.Page(Ui.Header("Zelda Dungeon", note), body);
    }

    /// <summary>Opens a Zelda Dungeon page in this tab (or, where the system has no web engine, in the browser).</summary>
    public static void Open(string url) => App.Main.Go("zeldadungeon", url);

    protected override void OnShown(object? arg)
    {
        unloadTimer.Stop();
        if (arg is string s && Uri.TryCreate(s, UriKind.Absolute, out var u)) { last = u; if (web != null) web.Navigate(u); }
        if (web == null) Load();
        else if (!covered) web.IsVisible = true;
    }

    public override void OnHidden()
    {
        if (web == null) return;
        RememberScroll(web);
        web.IsVisible = false;
        // minimized, or the game is what has the focus: give everything back now
        if (App.Main.WindowState == WindowState.Minimized) { Unload("minimized"); return; }
        if (Launcher.GameRunning()) { Unload("Savage Wind is running"); return; }
        unloadTimer.Start();
    }

    /// <summary>Something of the app's is drawn on top (the search): a native page would cover it, so it steps aside.</summary>
    public void Cover(bool on)
    {
        covered = on;
        if (web != null) web.IsVisible = !on;
    }

    void Navigate(Uri u)
    {
        last = u;
        if (web == null) Load(); else web.Navigate(u);
    }

    void Load()
    {
        var w = new NativeWebView { Source = last, IsVisible = !covered };
        var info = TryInfo(w);
        if (info is { IsSupported: false } || info is { IsInstalled: false })
        {
            host.Child = Unavailable(info.UnavailableReason);
            return;
        }
        w.EnvironmentRequested += (_, e) =>
        {
            var folder = Path.Combine(App.Store.Folder, "web");
            if (e is WindowsWebView2EnvironmentRequestedEventArgs w2) w2.UserDataFolder = folder;
            else if (e is GtkWebViewEnvironmentRequestedEventArgs g) { g.BaseDataDirectory = folder; g.BaseCacheDirectory = Path.Combine(folder, "cache"); }
        };
        // The page can never open anything outside this tab: no new windows, no browser, no other program. Only the
        // player's own press on "Open in your browser" does that.
        // New windows (target=_blank links, ads' pop-ups): always refused; a Zelda Dungeon page opens here instead,
        // anything else is dropped.
        w.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Request is { } r && IsZeldaDungeon(r)) w.Navigate(r);
        };
        // Links the system would hand to another program (mailto:, app links…) are stopped. Web pages and frames load
        // as in any browser (on Linux this also sees every ad frame, so it must never do more than cancel).
        w.NavigationStarted += (_, e) =>
        {
            if (e.Request is { } r && r.Scheme is not ("http" or "https" or "about" or "data" or "blob")) e.Cancel = true;
        };
        w.NavigationCompleted += async (_, _) =>
        {
            if (w.Source is { } s && IsZeldaDungeon(s)) { last = s; if (outside != null && noticeBack.IsVisible) HideNotice(); }
            else if (w.Source is { Scheme: "http" or "https" } other) ShowNotice(other, $"You're on {other.Host}, outside Zelda Dungeon.", left: true);
            if (scrollFor != null && scrollFor == w.Source && scrollY > 0)
            {
                scrollFor = null;
                try { await w.InvokeScript($"window.scrollTo(0, {scrollY.ToString(System.Globalization.CultureInfo.InvariantCulture)})"); } catch (Exception) { }
            }
            address.Text = w.Source?.ToString() ?? "";
            back.IsEnabled = w.CanGoBack;
            forward.IsEnabled = w.CanGoForward;
        };
        w.ProcessTerminated += (_, e) => { Log.Warn("Zelda Dungeon page stopped: " + e.Reason); Unload("its web process stopped"); };
        web = w;
        host.Child = w;
        address.Text = last.ToString();
        Loads++;
        Log.Info($"Zelda Dungeon: page opened ({(info != null ? $"{info.Type} {info.Version}" : "web engine")})");
    }

    void ShowNotice(Uri u, string text, bool left)
    {
        outside = u;
        noticeText.Text = text;
        noticeBack.IsVisible = left;
        notice.IsVisible = true;
    }

    void HideNotice() { notice.IsVisible = false; outside = null; }

    async void RememberScroll(NativeWebView w)
    {
        try
        {
            var src = w.Source;
            var y = await w.InvokeScript("window.scrollY");
            if (double.TryParse(y?.Trim('"'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) { scrollY = v; scrollFor = src; }
        }
        catch (Exception) { }  // (closed meanwhile, or a page that doesn't answer: it simply opens at the top)
    }

    /// <summary>Closes the page and its web processes; the address is kept, so coming back reloads it.</summary>
    void Unload(string why)
    {
        unloadTimer.Stop();
        if (web == null) return;
        if (web.Source is { } s && IsZeldaDungeon(s)) last = s;
        host.Child = null;  // (taken out of the window, the web engine lets go of the page)
        web = null;
        Unloads++;
        Log.Info($"Zelda Dungeon: page closed ({why})");
    }

    static DetailedWebViewAdapterInfo? TryInfo(NativeWebView w)
    {
        try { return w.AdapterInfo is { } a ? WebViewAdapterInfo.GetAdapterInfo(a.Type) : null; }
        catch (Exception) { return null; }
    }

    Control Unavailable(string? reason)
    {
        var why = OperatingSystem.IsLinux()
            ? "Pages show inside Slate Companion with WebKitGTK (on most systems the package is called webkit2gtk-4.1 or libwebkit2gtk-4.1). Install it and open this tab again, or read Zelda Dungeon in your browser."
            : "This computer has no web engine Slate Companion can use (on Windows it's Microsoft Edge WebView2). Install it and open this tab again, or read Zelda Dungeon in your browser.";
        return Ui.Stack(10, Ui.Text("Zelda Dungeon can't be shown here", "h3"), new TextBlock { Text = why, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 560 },
            reason is { Length: > 0 } ? Ui.Text(reason, "faint small") : new Control(),
            Ui.Button("Open in your browser", () => ErrorWindow.OpenPath(last.ToString()), "primary small"));
    }

    static bool IsZeldaDungeon(Uri u) => u.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith("." + Host, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Which Zelda Dungeon page belongs to each walkthrough chapter, quest and shrine (Data/guides/zeldadungeon.json).</summary>
public static class ZeldaDungeonLinks
{
    public sealed record Link(string Title, string Url);
    static Dictionary<string, Link[]>? links;

    /// <summary>The pages for an app key ("chapter:vah-ruta", "sideQuests:&lt;id&gt;", "shrine:Dungeon000"…); empty when none was checked.</summary>
    public static Link[] For(string key)
    {
        links ??= Read();
        return links.GetValueOrDefault(key) ?? [];
    }

    /// <summary>"Read on Zelda Dungeon" (one button per page: a Divine Beast chapter has the town and the dungeon), or null.</summary>
    public static Control? Buttons(string key)
    {
        var ls = For(key);
        if (ls.Length == 0) return null;
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var l in ls)
        {
            var b = Ui.Button(ls.Length == 1 ? "Read on Zelda Dungeon" : "Zelda Dungeon: " + l.Title, () => ZeldaDungeonView.Open(l.Url), "small ghost");
            b.Margin = new Thickness(0, 0, 8, 6);
            ToolTip.SetTip(b, l.Url);
            row.Children.Add(b);
        }
        return row;
    }

    static Dictionary<string, Link[]> Read()
    {
        try
        {
            var path = Path.Combine(App.Data.Folder, "guides", "zeldadungeon.json");
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            return doc.RootElement.GetProperty("links").EnumerateObject().ToDictionary(p => p.Name,
                p => p.Value.EnumerateArray().Select(l => new Link(l.GetProperty("title").GetString()!, l.GetProperty("url").GetString()!)).ToArray());
        }
        catch (Exception e) { Log.Warn("Zelda Dungeon links not read: " + e.Message); return []; }
    }
}
