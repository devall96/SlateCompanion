using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SlateCompanion.Cooking;

namespace SlateCompanion.UI.Views;

/// <summary>Cooking: the pot (the game's own rules, checked against 617 real results), every recipe and ingredient.</summary>
public sealed class CookingView : ViewBase
{
    static readonly Dictionary<string, string> EffectLabel = new()
    {
        ["chilly"] = "Chilly (heat resist)", ["spicy"] = "Spicy (cold resist)", ["electro"] = "Electro (shock resist)", ["fireproof"] = "Fireproof",
        ["hasty"] = "Hasty (speed)", ["sneaky"] = "Sneaky (stealth)", ["mighty"] = "Mighty (attack)", ["tough"] = "Tough (defense)",
        ["enduring"] = "Enduring (extra stamina)", ["energizing"] = "Energizing (stamina)", ["hearty"] = "Hearty (extra hearts)",
    };
    readonly List<string> pot = [];
    readonly ContentControl body = new();
    string tab = "pot", cat = "", q = "";
    bool bloodMoon;
    CookingData data = null!;
    List<Ingredient> ings = [];

    protected override Control Build()
    {
        data = App.Data.Cooking;
        ings = data.Ingredients.Where(i => i.VariantOf == null || i.VariantOf.Count == 0).OrderBy(i => i.Name).ToList();
        var tabs = Rows.Chips([("pot", "Cooking pot"), ("recipes", "Recipes"), ("ingredients", "Ingredients")], tab, t => { tab = t; q = ""; Draw(); });
        return Rows.Page(Ui.Header("Cooking", tabs), body);
    }

    protected override void OnShown(object? arg)
    {
        if (arg is ValueTuple<string, string> a)
        {
            if (a.Item1 == "ingredient") { tab = "pot"; pot.Clear(); pot.Add(a.Item2); }
            else if (a.Item1 == "recipe") { tab = "recipes"; q = Ui.Norm(data.DishNameById.GetValueOrDefault(a.Item2)); }
        }
        Draw();
    }

    void Draw() => body.Content = tab switch { "recipes" => Recipes(), "ingredients" => Ingredients(), _ => Pot() };

    static string Pretty(string category) => GameData.TitleCase(category.Replace('_', ' '));

    ComboBox CategoryBox()
    {
        var raw = ings.Select(i => i.Category).Distinct().OrderBy(c => c).ToList();
        var box = new ComboBox { ItemsSource = new[] { "All" }.Concat(raw.Select(Pretty)).ToList(), SelectedIndex = cat == "" ? 0 : raw.IndexOf(cat) + 1, MinWidth = 150 };
        box.SelectionChanged += (_, _) => { cat = box.SelectedIndex <= 0 ? "" : raw[box.SelectedIndex - 1]; Draw(); };
        return box;
    }

    Control Pot()
    {
        var slots = Ui.Row(10);
        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var has = i < pot.Count;
            var b = new Button
            {
                Width = 74, Height = 74, CornerRadius = new CornerRadius(14), Padding = new Thickness(4),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                BorderBrush = has ? Ui.Brush("Glow") : Ui.Brush("LineHi"),
                Background = has ? new SolidColorBrush(Color.Parse("#1439C6FF")) : new SolidColorBrush(Color.Parse("#80071116")),
                Content = has && Assets.Icon(pot[i], 56) is { } icon
                    ? new Image { Source = icon, Width = 56, Height = 56 }
                    : new TextBlock { Text = has ? data.IngredientById[pot[i]].Name : "empty", FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = has ? Ui.Brush("Text") : Ui.Brush("Faint") },
            };
            if (has) ToolTip.SetTip(b, data.IngredientById[pot[i]].Name + " (click to remove)");
            if (has) b.Click += (_, _) => { pot.RemoveAt(idx); Draw(); };
            slots.Children.Add(b);
        }
        var bm = new CheckBox { Content = "Blood Moon", IsChecked = bloodMoon };
        bm.IsCheckedChanged += (_, _) => { bloodMoon = bm.IsChecked == true; Draw(); };
        var left = Ui.Stack(12, Ui.Text("POT", "h2"), slots, Ui.Row(10, Ui.Button("Empty the pot", () => { pot.Clear(); Draw(); }, "small"), bm), Result());

        var search = Rows.SearchBox("Find an ingredient…", s => { q = s; Draw(); }, 220, q);
        var grid = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var i in ings.Where(i => (cat == "" || i.Category == cat) && (q == "" || Ui.Norm(i.Name).Contains(q))))
        {
            var label = Ui.Stack(0, Ui.Text(i.Name, "small"), Ui.Text(string.Join(" · ", new[] { i.Effect, i.Hearts > 0 ? $"{i.Hearts / 4.0:0.##}♥" : null }.Where(x => x != null)) is { Length: > 0 } s ? s : Pretty(i.Category), "faint"));
            var pic = Ui.Img(Assets.Icon(i.Id, 32), 32);
            var b = new Button
            {
                Width = 176, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(7, 5), HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = pic != null ? Ui.Row(8, pic, label) : label,
            };
            if (Assets.Desc(i.Id) is string tip) ToolTip.SetTip(b, tip);
            var id = i.Id;
            b.Click += (_, _) => { if (pot.Count < 5) { pot.Add(id); Draw(); } };
            grid.Children.Add(b);
        }
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        right.Children.Add(Ui.Row(10, search, CategoryBox()));
        var gridScroll = new ScrollViewer { Content = grid, Margin = new Thickness(0, 10, 0, 0) };
        Grid.SetRow(gridScroll, 1); right.Children.Add(gridScroll);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        g.Children.Add(Ui.Panel(left, "panel glow", new Thickness(0, 0, 14, 0)));
        var r = Ui.Panel(right); Grid.SetColumn(r, 1); g.Children.Add(r);
        return g;
    }

    Control Result()
    {
        if (pot.Count == 0) return Ui.Text("Add up to five ingredients. The result uses the game's own cooking rules.", "muted");
        CookResult r;
        try { r = CookingSimulator.Cook(pot, data, bloodMoon); }
        catch (ArgumentException e) { return Ui.Text(e.Message, "muted"); }
        var dish = Ui.Text(r.Name); dish.FontSize = 22; dish.FontWeight = FontWeight.SemiBold; dish.Foreground = Ui.Brush("Glow2");
        var actor = Assets.DishActor(r.RecipeId, r.Effect);
        var pic = Ui.Img(Assets.Icon(actor, 72), 72);
        var s = Ui.Stack(8, Ui.Text("RESULT", "eyebrow"), pic != null ? Ui.Row(12, pic, dish) : dish);
        if (Ui.GameDesc(Assets.Desc(actor) ?? Assets.Desc(r.RecipeId)) is { } gd) s.Children.Add(gd);
        if (r.FullRestore) s.Children.Add(Ui.Row(8, Ui.Tag("Full recovery", "done"), Ui.Text($"+{r.ExtraHearts} extra hearts")));
        else s.Children.Add(Ui.Row(8, Hearts(r.Hearts), Ui.Text(r.Hearts == 4 ? "1 heart" : $"{r.Hearts / 4.0:0.##} hearts", "muted")));
        if (r.Effect != null)
        {
            var parts = new List<string>();
            if (r.Level is > 0 and <= 3 && r.Effect is not ("hearty" or "energizing" or "enduring")) parts.Add($"level {r.Level}");
            if (r.DurationSec > 0) parts.Add($"{r.DurationSec / 60}:{r.DurationSec % 60:00}");
            if (r.StaminaWheels is double w) parts.Add($"{w:0.#} wheel{(w == 1 ? "" : "s")}");
            if (r.ExtraStaminaWheels is double ew) parts.Add($"+{ew:0.#} wheels");
            s.Children.Add(Ui.Row(8, Ui.Tag(EffectLabel.GetValueOrDefault(r.Effect, r.Effect), "orange"), Ui.Text(string.Join(" · ", parts), "small")));
        }
        s.Children.Add(Ui.Text($"Sells for {r.SellPrice} rupees · critical cook: {r.Critical} ({r.CritChance}%)", "muted small"));
        foreach (var w in r.Warnings) s.Children.Add(new Border { Classes = { "note" }, Child = Ui.Text(w, "small") });
        return s;
    }

    static Control Hearts(int quarters)
    {
        int full = Math.Min(quarters / 4, 30);
        var t = Ui.Text(new string('♥', full) + (quarters % 4 > 0 ? "♥" : ""));
        t.Foreground = new SolidColorBrush(Color.Parse("#FF6B81")); t.FontSize = 18;
        return t;
    }

    Control Recipes()
    {
        string Slot(DishSlot s) => s.AnyOf != null ? string.Join(" or ", s.AnyOf.Select(i => data.IngredientById.TryGetValue(i, out var x) ? x.Name : i))
            : "any " + string.Join(" / ", (s.AnyOfTags ?? []).Select(t => t.Replace("Cook", "").ToLowerInvariant()));
        var md = new MasterDetail<Dish>(
            d => Rows.Item(Ui.Img(Assets.Icon(Assets.DishActor(d.BaseId ?? d.Id, d.Effect), 36), 36), d.Name, d.Kind),
            d =>
            {
                var actor = Assets.DishActor(d.BaseId ?? d.Id, d.Effect);
                var s = Ui.StackOf(10, Ui.Img(Assets.Icon(actor, 96), 96), Ui.Text(d.Name, "h3"), Ui.Tag(d.Kind), Ui.GameDesc(Assets.Desc(actor) ?? Assets.Desc(d.Id)));
                s.Children.Add(Ui.Text("WAYS TO MAKE IT", "h2"));
                foreach (var req in d.Requires.Take(8)) s.Children.Add(Ui.Text("• " + string.Join(" + ", req.Slots.Select(Slot)), "small"));
                if (!string.IsNullOrEmpty(d.Notes)) s.Children.Add(Ui.Text(d.Notes, "muted small"));
                s.Children.Add(Ui.Text("Effect names (Hasty, Mighty…) come from the ingredients, not the recipe.", "faint"));
                return s;
            }, 460, () => Ui.Text("Pick a dish to see what goes into it.", "muted"));
        md.SetItems(data.Dishes.Where(d => q == "" || Ui.Norm(d.Name).Contains(q)).OrderBy(d => d.Name).ToList(), q == "" ? null : _ => true);
        var search = Rows.SearchBox("Find a recipe…", s => { q = s; Draw(); }, 260, q);
        var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        g.Children.Add(search);
        md.Margin = new Thickness(0, 12, 0, 0); Grid.SetRow(md, 1); g.Children.Add(md);
        return g;
    }

    Control Ingredients()
    {
        var md = new MasterDetail<Ingredient>(
            i => Rows.Item(Ui.Img(Assets.Icon(i.Id, 36), 36), i.Name, string.Join(" · ", new[] { Pretty(i.Category), i.Effect }.Where(x => !string.IsNullOrEmpty(x))), Ui.Text(i.Hearts > 0 ? $"{i.Hearts / 4.0:0.##}♥" : "", "mono faint")),
            i => Ui.StackOf(8, Ui.Img(Assets.Icon(i.Id, 96), 96), Ui.Text(i.Name, "h3"), Ui.Text(Pretty(i.Category), "muted"), Ui.GameDesc(Assets.Desc(i.Id)),
                Ui.Text($"Hearts (raw): {i.Hearts / 4.0:0.##} · sells for {i.SellPrice} rupees" + (i.BuyPrice > 0 ? $" · buy {i.BuyPrice}" : ""), "small"),
                Ui.Text(i.Effect != null ? $"Effect: {EffectLabel.GetValueOrDefault(i.Effect, i.Effect)} (potency {i.Potency})" : "No effect", "small"),
                Ui.Text(string.IsNullOrEmpty(i.WhereFound) ? "" : "Where: " + i.WhereFound, "small muted"),
                Ui.Button("Put in the pot", () => { tab = "pot"; if (pot.Count < 5) pot.Add(i.Id); Draw(); }, "small")),
            420, () => Ui.Text("Pick an ingredient.", "muted"));
        md.SetItems(ings.Where(i => (cat == "" || i.Category == cat) && (q == "" || Ui.Norm(i.Name).Contains(q))).ToList());
        var search = Rows.SearchBox("Find an ingredient…", s => { q = s; Draw(); }, 260, q);
        var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        g.Children.Add(Ui.Row(10, search, CategoryBox()));
        md.Margin = new Thickness(0, 12, 0, 0); Grid.SetRow(md, 1); g.Children.Add(md);
        return g;
    }
}
