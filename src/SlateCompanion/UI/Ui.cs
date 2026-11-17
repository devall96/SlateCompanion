using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;

namespace SlateCompanion.UI;

/// <summary>Small factories for the app's building blocks, so the screens read as layout rather than plumbing.</summary>
public static class Ui
{
    public static IBrush Brush(string key) => (IBrush)Application.Current!.FindResource(key)!;
    public static readonly IBrush TodoBrush = new SolidColorBrush(Color.Parse("#FF9A3C"));
    public static readonly IBrush DoneBrush = new SolidColorBrush(Color.Parse("#4FD2FF"));

    // Line-art rune glyphs on a 32x32 grid (original drawings, not Nintendo artwork).
    public static readonly Dictionary<string, string> Glyphs = new()
    {
        ["home"] = "M4 15 16 5l12 10M8 12v15h16V12 M13 27v-7h6v7",
        ["map"] = "M4 7l8-3 8 3 8-3v21l-8 3-8-3-8 3z M12 4v21 M20 7v21",
        ["quests"] = "M9 4h14a2 2 0 0 1 2 2v20a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z M11 10h10M11 15h10M11 20h6",
        ["shrines"] = "M16 3 27 9v14l-11 6-11-6V9z M12 16a4 4 0 1 0 8 0a4 4 0 1 0-8 0 M16 3v9M16 20v9",
        ["koroks"] = "M16 4c6 4 8 9 8 13a8 8 0 0 1-16 0c0-4 2-9 8-13z M16 10v18M16 17l-4-3M16 21l4-3",
        ["cooking"] = "M5 13h22l-2 10a5 5 0 0 1-5 4h-8a5 5 0 0 1-5-4z M3 13h26M12 9c0-2 2-2 2-4M18 9c0-2 2-2 2-4",
        ["compendium"] = "M6 9h20a2 2 0 0 1 2 2v13a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V11a2 2 0 0 1 2-2z M11 17.5a5 5 0 1 0 10 0a5 5 0 1 0-10 0 M11 9l2-4h6l2 4",
        ["armor"] = "M16 4l10 4v8c0 6-4 10-10 12C10 26 6 22 6 16V8z M16 9v14M11 14h10",
        ["stats"] = "M5 27h22M8 23v-7M14 23V9M20 23v-10M26 23V5",
        ["guides"] = "M4 6c5-1 9 0 12 3 3-3 7-4 12-3v20c-5-1-9 0-12 3-3-3-7-4-12-3z M16 9v20",
        ["settings"] = "M12 16a4 4 0 1 0 8 0a4 4 0 1 0-8 0 M16 3v5M16 24v5M3 16h5M24 16h5M6.8 6.8l3.5 3.5M21.7 21.7l3.5 3.5M6.8 25.2l3.5-3.5M21.7 10.3l3.5-3.5",
        ["inventory"] = "M10 11V8a6 6 0 0 1 12 0v3 M6 11h20l-2 17H8z M12 17h8",
        ["saves"] = "M6 5h16l4 4v18H6z M10 5v7h11V5 M10 27v-9h12v9",

        ["zeldadungeon"] = "M4 16a12 12 0 1 0 24 0a12 12 0 1 0-24 0 M4 16h24 M16 4c-4 3.5-5.5 7.5-5.5 12s1.5 8.5 5.5 12 M16 4c4 3.5 5.5 7.5 5.5 12s-1.5 8.5-5.5 12",
        ["search"] = "M4 10.5a6.5 6.5 0 1 0 13 0a6.5 6.5 0 1 0-13 0 M15.5 15.5 21 21",
    };

    public static Control Glyph(string name, double size = 26, IBrush? stroke = null)
    {
        // Every glyph is drawn on the same 32x32 grid and the whole grid is scaled, so they all line up and share
        // one centre (stretching each path on its own made narrow glyphs bigger and off-centre).
        var p = new Path
        {
            Data = Geometry.Parse(Glyphs[name]),
            StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };
        if (stroke != null) p.Stroke = stroke;
        else p.Bind(Shape.StrokeProperty, p.GetObservable(TemplatedControl.ForegroundProperty));
        var grid = new Canvas { Width = 32, Height = 32, Children = { p } };
        return new Viewbox { Child = grid, Width = size, Height = size, Stretch = Stretch.Uniform, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    }

    /// <summary>The Sheikah eye of the title bar: an original line drawing.</summary>
    public static Control Eye(double width = 40)
    {
        var p = new Path
        {
            Data = Geometry.Parse("M4 20 Q32 -2 60 20 Q32 42 4 20Z M25 20a7 7 0 1 0 14 0a7 7 0 1 0-14 0 M30 20a2 2 0 1 0 4 0a2 2 0 1 0-4 0"),
            Stroke = Brush("Glow"), StrokeThickness = 2.4, Stretch = Stretch.Uniform, Width = width, Height = width * 0.62,
        };
        return p;
    }

    public static TextBlock Text(string text, string? classes = null, double? size = null)
    {
        var t = new TextBlock { Text = text };
        if (classes != null) foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) t.Classes.Add(c);
        if (size != null) t.FontSize = size.Value;
        return t;
    }

    public static Border Panel(Control child, string classes = "panel", Thickness? margin = null)
    {
        var b = new Border { Child = child, Margin = margin ?? new Thickness(0) };
        foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) b.Classes.Add(c);
        return b;
    }

    public static Border Tag(string text, string kind = "")
    {
        var b = new Border { Child = new TextBlock { Text = text }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        b.Classes.Add("tag");
        if (kind != "") b.Classes.Add(kind);
        return b;
    }

    public static Button Button(string text, Action onClick, string classes = "")
    {
        var b = new Button { Content = text };
        foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) b.Classes.Add(c);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static StackPanel Row(double spacing = 10, params Control[] children)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var c in children) { c.VerticalAlignment = VerticalAlignment.Center; s.Children.Add(c); }
        return s;
    }

    public static StackPanel Stack(double spacing = 10, params Control[] children)
    {
        var s = new StackPanel { Spacing = spacing };
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    /// <summary>A glowing progress meter.</summary>
    public static Control Meter(double value, double total, bool orange = false, double height = 8)
    {
        double f = total > 0 ? Math.Clamp(value / total, 0, 1) : 0;
        var fill = new Border
        {
            CornerRadius = new CornerRadius(height / 2),
            Background = orange
                ? new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = { new(Color.Parse("#FF7A1A"), 0), new(Color.Parse("#FF9A3C"), 1) } }
                : new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = { new(Color.Parse("#39C6FF"), 0), new(Color.Parse("#7FE3FF"), 1) } },
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Background = new SolidColorBrush(Color.Parse("#1A39C6FF")), Child = fill, ClipToBounds = true };
        track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * f;
        return track;
    }

    /// <summary>
    /// The hex check: blue when the save says done, green when ticked by hand (click to toggle); save-driven ticks
    /// can't be unticked by hand.
    /// </summary>
    public static Control Check(string key, Action? changed = null)
    {
        var path = new Path { Width = 22, Height = 22, StrokeThickness = 1.6, Stretch = Stretch.None };
        var b = new Button { Content = path, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Width = 26, Height = 26 };
        void Draw()
        {
            bool save = App.Progress.FromSave(key), on = App.Progress.IsDone(key);
            path.Data = Geometry.Parse(on ? "M11 1.8 19 6.4v9.2L11 20.2 3 15.6V6.4z M7 11.3l2.8 2.7L15.2 8" : "M11 1.8 19 6.4v9.2L11 20.2 3 15.6V6.4z");
            path.Stroke = save ? Brush("Glow") : on ? Brush("Done") : Brush("Faint");
            ToolTip.SetTip(b, save ? "Done in your save" : on ? "Ticked by hand (click to untick)" : "Not done (click to tick by hand)");
        }
        Draw();
        b.Click += (_, e) =>
        {
            e.Handled = true;
            if (App.Progress.FromSave(key)) return;
            App.Progress.ToggleManual(key);
            Draw();
            changed?.Invoke();
        };
        return b;
    }

    public static string Ago(DateTime? t)
    {
        if (t == null) return "";
        var s = (DateTime.UtcNow - t.Value.ToUniversalTime()).TotalSeconds;
        if (s < 45) return "just now";
        if (s < 3600) return $"{Math.Round(s / 60)} min ago";
        if (s < 86400) return $"{Math.Round(s / 3600)} h ago";
        return t.Value.ToLocalTime().ToShortDateString();
    }

    public static string Duration(double? sec)
    {
        if (sec == null) return "—";
        var h = (int)(sec / 3600); var m = (int)(sec % 3600 / 60);
        return h > 0 ? $"{h}h {m:00}m" : $"{m}m";
    }

    /// <summary>Page header: an orange eyebrow over the title, with optional controls on the right.</summary>
    public static Control Header(string title, Control? right = null)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 16) };
        var t = Text(title, "h1"); t.VerticalAlignment = VerticalAlignment.Bottom;
        g.Children.Add(t);
        if (right != null) { Grid.SetColumn(right, 1); right.VerticalAlignment = VerticalAlignment.Bottom; g.Children.Add(right); }
        return g;
    }

    public static Control Empty(string text, Control? action = null)
    {
        var s = Stack(12, Eye(64), Text(text, "muted"));
        s.HorizontalAlignment = HorizontalAlignment.Center;
        s.Margin = new Thickness(20, 50);
        foreach (var c in s.Children) c.HorizontalAlignment = HorizontalAlignment.Center;
        if (action != null) { action.HorizontalAlignment = HorizontalAlignment.Center; s.Children.Add(action); }
        return s;
    }

    /// <summary>A picture from the game files, or nothing when there isn't one (the setting is off or it wasn't built).</summary>
    public static Control? Img(Avalonia.Media.Imaging.Bitmap? b, double size, double? height = null)
    {
        if (b == null) return null;
        return new Image { Source = b, Width = size, Height = height ?? size, Stretch = Stretch.Uniform };
    }

    /// <summary>A large picture with a framed, rounded look for detail panels.</summary>
    public static Control? Picture(Avalonia.Media.Imaging.Bitmap? b, double width)
    {
        if (b == null) return null;
        return new Border
        {
            CornerRadius = new CornerRadius(10), ClipToBounds = true, BorderBrush = Brush("LineHi"), BorderThickness = new Thickness(1),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Child = new Image { Source = b, Width = width, Stretch = Stretch.Uniform },
        };
    }

    /// <summary>"From the game" text block: the game's own description, set apart from the app's notes.</summary>
    public static Control? GameDesc(string? text, string label = "IN-GAME")
    {
        if (string.IsNullOrEmpty(text)) return null;
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontStyle = FontStyle.Italic, Foreground = Brush("Text"), LineHeight = 20 };
        return new Border
        {
            BorderBrush = Brush("Glow"), BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 4), Background = new SolidColorBrush(Color.Parse("#0D39C6FF")),
            CornerRadius = new CornerRadius(0, 8, 8, 0),
            Child = Stack(4, Text(label, "label"), t),
        };
    }

    public static StackPanel StackOf(double spacing, params Control?[] children)
    {
        var s = new StackPanel { Spacing = spacing };
        foreach (var c in children) if (c != null) s.Children.Add(c);
        return s;
    }

    public static string Norm(string? s) =>
        new string((s ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()).Trim();

    /// <summary>The home folder shown as ~ (shorter, and keeps the user name off screen).</summary>
    public static string ShortPath(string p)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && p.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + p[home.Length..] : p;
    }
}
