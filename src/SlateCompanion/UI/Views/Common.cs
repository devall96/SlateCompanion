using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace SlateCompanion.UI.Views;

/// <summary>A filterable list on the left and the selected item's details on the right. The list is virtualized.</summary>
public sealed class MasterDetail<T> : Grid where T : class
{
    public ListBox List { get; } = new();
    readonly ContentControl detail = new();
    readonly Func<T, Control> makeDetail;
    readonly Func<Control>? emptyDetail;

    public MasterDetail(Func<T, Control> row, Func<T, Control> makeDetail, double detailWidth = 420, Func<Control>? emptyDetail = null)
    {
        this.makeDetail = makeDetail;
        this.emptyDetail = emptyDetail;
        ColumnDefinitions = new ColumnDefinitions($"*,{detailWidth}");
        List.ItemTemplate = new FuncDataTemplate<T>((t, _) => t == null ? new Panel() : row(t), supportsRecycling: false);
        List.SelectionChanged += (_, _) => ShowDetail();
        Children.Add(List);
        var scroll = new ScrollViewer { Content = detail };
        var panel = Ui.Panel(scroll, "panel glow");
        panel.Margin = new Thickness(14, 0, 0, 0);
        Grid.SetColumn(panel, 1);
        Children.Add(panel);
        detail.Content = emptyDetail?.Invoke();
    }

    public void ShowDetail() => detail.Content = List.SelectedItem is T t ? makeDetail(t) : emptyDetail?.Invoke();

    public void SetItems(IReadOnlyList<T> items, Func<T, bool>? select = null)
    {
        var keep = List.SelectedItem as T;
        List.ItemsSource = items;
        T? pick = select != null ? items.FirstOrDefault(select) : keep != null && items.Contains(keep) ? keep : null;
        List.SelectedItem = pick;
        if (pick != null) List.ScrollIntoView(pick);
        ShowDetail();
    }
}

public static class Rows
{
    /// <summary>A list row: check, title over meta, and something on the right.</summary>
    public static Control Item(Control? check, string title, string? meta, Control? right = null, bool done = false)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        if (check != null) { check.Margin = new Thickness(0, 0, 10, 0); check.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(check); }
        var t = Ui.Text(title); t.FontWeight = FontWeight.SemiBold; if (done) t.Foreground = Ui.Brush("Done");
        var s = Ui.Stack(1, t);
        if (!string.IsNullOrEmpty(meta)) s.Children.Add(Ui.Text(meta, "faint"));
        Grid.SetColumn(s, 1); g.Children.Add(s);
        if (right != null) { right.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(right, 2); g.Children.Add(right); }
        return g;
    }

    /// <summary>Filter chips: one is on at a time.</summary>
    public static StackPanel Chips(IEnumerable<(string id, string label)> items, string selected, Action<string> pick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (id, label) in items)
        {
            var b = new Button { Content = label };
            b.Classes.Add("chip");
            b.Classes.Set("on", id == selected);
            b.Click += (_, _) => { foreach (var c in row.Children.OfType<Button>()) c.Classes.Set("on", c == b); pick(id); };
            row.Children.Add(b);
        }
        return row;
    }

    /// <summary>
    /// A search box that reports the normalized text 150 ms after typing stops, and only when it really changed
    /// (setting the text when a screen is redrawn must not trigger another redraw).
    /// </summary>
    public static TextBox SearchBox(string watermark, Action<string> changed, double width = 260, string initial = "")
    {
        var box = new TextBox { PlaceholderText = watermark, Width = width, Text = initial };
        string last = Ui.Norm(initial);
        Avalonia.Threading.DispatcherTimer? t = null;
        box.TextChanged += (_, _) =>
        {
            if (Ui.Norm(box.Text) == last) return;
            t?.Stop();
            t = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            t.Tick += (_, _) => { t!.Stop(); last = Ui.Norm(box.Text); changed(last); };
            t.Start();
        };
        return box;
    }

    public static Control Steps(IEnumerable<string>? steps)
    {
        var s = new StackPanel { Spacing = 4 };
        int i = 1;
        foreach (var step in steps ?? []) s.Children.Add(Ui.Row(8, Ui.Text($"{i++}.", "muted mono"), new TextBlock { Text = step, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 }));
        return s;
    }

    public static Control Page(Control header, Control body)
    {
        var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(24, 20, 24, 20) };
        g.Children.Add(header);
        Grid.SetRow(body, 1);
        g.Children.Add(body);
        return g;
    }
}
