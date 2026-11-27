using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace SlateCompanion.UI;

public sealed record SearchHit(string Kind, string Name, string Sub, string View, object? Arg, string Norm);

/// <summary>Ctrl+K: one search over everything. Built on first use; plain prefix and word matching.</summary>
public sealed class SearchOverlay : Border
{
    readonly Action<string, object?> go;
    readonly TextBox box = new() { PlaceholderText = "Search shrines, quests, recipes, materials, armor…", FontSize = 16, BorderThickness = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(0) };
    readonly ListBox results = new() { MaxHeight = 460, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    List<SearchHit>? index;

    public SearchOverlay(Action<string, object?> go)
    {
        this.go = go;
        IsVisible = false;
        Background = new SolidColorBrush(Color.Parse("#C7020A0E"));
        results.ItemTemplate = new FuncDataTemplate<SearchHit>((h, _) =>
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*,Auto") };
            // When the list is replaced while typing, Avalonia clears each old row's content and builds this template
            // once more with no hit at all: an empty row then, instead of a crash (issue #4).
            if (h is null) return g;
            g.Children.Add(Ui.Text((h.Kind ?? "").ToUpperInvariant(), "label"));
            ((TextBlock)g.Children[0]).Foreground = Ui.Brush("Orange");
            var n = Ui.Text(h.Name ?? ""); Grid.SetColumn(n, 1); g.Children.Add(n);
            var s = Ui.Text(h.Sub ?? "", "faint"); Grid.SetColumn(s, 2); g.Children.Add(s);
            return g;
        }, supportsRecycling: false);
        var panel = new Border
        {
            Width = 680, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 90, 0, 0),
            Background = Ui.Brush("Panel"), BorderBrush = Ui.Brush("Glow"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
            Child = Ui.Stack(0, box, results), BoxShadow = BoxShadows.Parse("0 0 40 0 #4039C6FF"),
        };
        Child = panel;
        PointerPressed += (_, e) => { if (e.Source == this) Close(); };
        box.TextChanged += (_, _) => Run();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && results.ItemCount > 0) { results.SelectedIndex = Math.Min(results.ItemCount - 1, results.SelectedIndex + 1); e.Handled = true; }
            else if (e.Key == Key.Up) { results.SelectedIndex = Math.Max(0, results.SelectedIndex - 1); e.Handled = true; }
            else if (e.Key == Key.Enter) { Pick(); e.Handled = true; }
        };
        results.DoubleTapped += (_, _) => Pick();
        results.Tapped += (_, _) => Pick();
    }

    public void Open()
    {
        index ??= Build();
        IsVisible = true;
        box.Text = "";
        results.ItemsSource = null;
        box.Focus();
    }

    public void Close() => IsVisible = false;

    void Pick()
    {
        if (results.SelectedItem is not SearchHit h) return;
        Close();
        go(h.View, h.Arg);
    }

    void Run()
    {
        var q = Ui.Norm(box.Text);
        if (q.Length == 0 || index == null) { results.ItemsSource = null; return; }
        var words = q.Split(' ');
        var hits = new List<(double, SearchHit)>();
        foreach (var it in index)
        {
            double s = it.Norm == q ? 100 : it.Norm.StartsWith(q) ? 80 : it.Norm.Contains(q) ? 60
                : words.All(w => it.Norm.Split(' ').Any(x => x.StartsWith(w))) ? 40 : 0;
            if (s > 0) hits.Add((s - it.Norm.Length / 100.0, it));
        }
        results.ItemsSource = hits.OrderByDescending(h => h.Item1).Take(40).Select(h => h.Item2).ToList();
        if (results.ItemCount > 0) results.SelectedIndex = 0;
    }

    static List<SearchHit> Build()
    {
        var d = App.Data;
        var list = new List<SearchHit>();
        void Add(string kind, string name, string sub, string view, object? arg) => list.Add(new SearchHit(kind, name, sub, view, arg, Ui.Norm(name)));
        foreach (var m in d.Markers)
            if (m.Type is "shrine" or "shrine_dlc") Add("Shrine", m.Name, m.Type == "shrine_dlc" ? "DLC" : "", "shrines", m.Id);
            else Add(GameData.PlaceLabel(m.Type), m.Name, "", "map", m.Id);
        foreach (var (group, label) in new[] { ("mainQuests", "Main quest"), ("sideQuests", "Side quest"), ("shrineQuests", "Shrine quest") })
            foreach (var q in group switch { "mainQuests" => d.Flags.MainQuests, "sideQuests" => d.Flags.SideQuests, _ => d.Flags.ShrineQuests })
                Add(label, q.Name, q.Location ?? q.Giver ?? "", "quests", (group, q.Id));
        foreach (var c in d.Walkthrough.Chapters) Add("Walkthrough", c.Title, "", "quests", ("walkthrough", c.Id));
        foreach (var i in d.Cooking.Ingredients.Where(i => i.VariantOf == null)) Add("Ingredient", i.Name, i.Effect ?? "", "cooking", ("ingredient", i.Id));
        foreach (var r in d.Cooking.Dishes) Add("Recipe", r.Name, r.Kind, "cooking", ("recipe", r.Id));
        foreach (var c in d.Compendium) Add("Compendium", GameData.TitleCase(c.Name), c.Category, "compendium", c.Id);
        foreach (var a in d.Armor.Armor) Add("Armor", a.Name, a.Set ?? "", "armor", a.Id);
        foreach (var t in d.Tips.Sections) Add("Guide", t.Title, "", "guides", t.Id);
        return list;
    }
}
