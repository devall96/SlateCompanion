using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace SlateCompanion.UI;

/// <summary>
/// The error window: what went wrong in plain words, the technical details (folded), and Copy details / Open logs /
/// Report a problem. Used for crashes the app survives and for things that failed (Play, saving settings, updates).
/// </summary>
public sealed class ErrorWindow : Window
{
    public const string IssuesUrl = "https://github.com/devall96/SlateCompanion/issues/new";
    static int open;
    public static ErrorWindow? Last { get; private set; }

    ErrorWindow(string title, string message, string? details, string? crashFile)
    {
        Title = "Slate Companion — " + title;
        Width = 560; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Brush("Bg");
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://SlateCompanion/Assets/icon.ico"))); } catch (Exception) { }

        var head = Ui.Row(12, new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = Ui.Brush("OrangeSoft"), BorderBrush = Ui.Brush("Orange"), BorderThickness = new Thickness(1.5),
            Child = new TextBlock { Text = "!", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Ui.Brush("Orange"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        }, Ui.Text(title, "h1"));
        var msg = Ui.Text(message, "muted");
        msg.TextWrapping = TextWrapping.Wrap;
        var body = Ui.Stack(14, head, msg);

        var all = $"Slate Companion {Log.Version}\n{title}\n{message}\n\n{details}\n\nLast log lines:\n{Log.Recent()}";
        if (!string.IsNullOrWhiteSpace(details))
        {
            var box = new TextBox { Text = Log.Scrub(details), IsReadOnly = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas, DejaVu Sans Mono, monospace"), FontSize = 11, MaxHeight = 220, AcceptsReturn = true };
            ScrollViewer.SetHorizontalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            body.Children.Add(new Expander { Header = "Technical details", Content = box, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        if (crashFile != null) body.Children.Add(Ui.Text("A crash report was saved: " + Ui.ShortPath(crashFile), "faint small"));

        var copy = Ui.Button("Copy details", () => { _ = Clipboard?.SetTextAsync(Log.Scrub(all)); }, "small");
        var logs = Ui.Button("Open logs folder", () => OpenPath(Log.Folder), "small ghost");
        var report = Ui.Button("Report a problem", () => OpenPath(IssuesUrl), "small ghost");
        var ok = Ui.Button("Close", Close, "primary");
        ok.IsDefault = true; ok.IsCancel = true;
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto") };
        void At(Control c, int col) { Grid.SetColumn(c, col); c.Margin = new Thickness(0, 0, 8, 0); buttons.Children.Add(c); }
        At(copy, 0); At(logs, 1); At(report, 2); At(ok, 4);
        body.Children.Add(buttons);
        Content = new Border { Padding = new Thickness(24, 20), Child = body };
        Closed += (_, _) => open--;
    }

    /// <summary>Shows the error window (on the UI thread; at most three at once, the rest only logged).</summary>
    public static void Show(string title, string message, Exception? e = null, string? crashFile = null)
    {
        Log.Error($"{title}: {message}", e);
        Dispatcher.UIThread.Post(() =>
        {
            if (open >= 3) return;
            open++;
            var w = new ErrorWindow(title, message, e?.ToString(), crashFile);
            Last = w;
            if (App.Main is { IsVisible: true } main) w.Show(main); else w.Show();
        });
    }

    public static void OpenPath(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows()) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            else System.Diagnostics.Process.Start("xdg-open", path)?.Dispose();
        }
        catch (Exception e) { Log.Warn($"Couldn't open {path}: {e.Message}"); }
    }
}
