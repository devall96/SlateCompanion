using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SlateCompanion.UI.Views;

// ---------------------------------------------------------------------------------------------- compendium

public sealed class CompendiumView : ViewBase
{
    static readonly (string id, string label)[] Cats = [("", "All"), ("creatures", "Creatures"), ("monsters", "Monsters"), ("materials", "Materials"), ("equipment", "Equipment"), ("treasure", "Treasure")];
    string cat = "", q = "";
    MasterDetail<CompendiumEntry> md = null!;
    readonly ContentControl summary = new();

    protected override Control Build()
    {
        md = new MasterDetail<CompendiumEntry>(
            e =>
            {
                var actor = Assets.CompendiumActor(e);
                var thumb = Ui.Img(Assets.Icon(actor, 36) ?? Assets.Picture(actor, 48), 36);
                var row = Rows.Item(Ui.Check($"compendium:{e.Id}", Refresh), $"#{e.Id:000}  {GameData.TitleCase(e.Name)}", Cats.FirstOrDefault(c => c.id == e.Category).label + (e.CommonLocations is { Length: > 0 } l ? " · " + string.Join(", ", l) : ""));
                return thumb == null ? row : Ui.Row(10, thumb, row);
            },
            e =>
            {
                var actor = Assets.CompendiumActor(e);
                var s = Ui.StackOf(8, Ui.Picture(Assets.Picture(actor, 380), 380), Ui.Text($"#{e.Id:000}  {GameData.TitleCase(e.Name)}", "h3"), Ui.Text(Cats.FirstOrDefault(c => c.id == e.Category).label ?? e.Category, "muted"),
                    Ui.GameDesc(actor == null ? null : Assets.BookDesc(actor) ?? Assets.Desc(actor), "HYRULE COMPENDIUM"));
                if (e.Drops is { Length: > 0 }) s.Children.Add(Ui.Text("Drops: " + string.Join(", ", e.Drops.Select(GameData.TitleCase)), "small"));
                if (e.CookingEffect is { Length: > 0 }) s.Children.Add(Ui.Text("Cooking effect: " + e.CookingEffect, "small"));
                if (e.HeartsRecovered is double h && h > 0) s.Children.Add(Ui.Text($"Hearts recovered: {h}", "small"));
                if (e.Properties is { Count: > 0 }) s.Children.Add(Ui.Text("Stats: " + string.Join(", ", e.Properties.Select(kv => $"{kv.Key} {kv.Value}")), "small"));
                s.Children.Add(Ui.Text(e.CommonLocations is { Length: > 0 } l ? "Found in: " + string.Join(", ", l) : "No usual location listed.", "small muted"));
                return s;
            }, 420, () => Ui.Text("Pick an entry.", "muted"));
        var filters = Ui.Row(10, Rows.SearchBox("Find an entry…", s => { q = s; Refresh(); }), Rows.Chips(Cats, cat, c => { cat = c; Refresh(); }));
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        body.Children.Add(filters);
        md.Margin = new Thickness(0, 12, 0, 0); Grid.SetRow(md, 1); body.Children.Add(md);
        summary.Width = 260;
        return Rows.Page(Ui.Header("Hyrule Compendium", summary), body);
    }

    protected override void OnShown(object? arg) { App.Progress.Changed += Refresh; Refresh(arg as int?); }
    public override void OnHidden() => App.Progress.Changed -= Refresh;
    void Refresh() => Refresh(null);

    void Refresh(int? pick)
    {
        var all = App.Data.Compendium;
        int done = all.Count(e => App.Progress.IsDone($"compendium:{e.Id}"));
        summary.Content = Ui.Stack(4, Ui.Text($"Photos ticked {done}/{all.Length}", "muted small"), Ui.Meter(done, all.Length));
        md.SetItems(all.Where(e => (cat == "" || e.Category == cat) && (q == "" || Ui.Norm(e.Name).Contains(q) || (e.CommonLocations ?? []).Any(l => Ui.Norm(l).Contains(q)))).ToList(),
            pick == null ? null : e => e.Id == pick);
    }
}

// ---------------------------------------------------------------------------------------------- armor

public sealed class ArmorView : ViewBase
{
    string q = "";
    MasterDetail<ArmorPiece> md = null!;
    readonly StackPanel shop = new() { Spacing = 4 };

    protected override Control Build()
    {
        md = new MasterDetail<ArmorPiece>(
            a => Rows.Item(Ui.Img(Assets.ArmorIcon(a, 36), 36), a.Name, string.Join(" · ", new[] { a.Set, a.Slot, a.BaseDefense != null ? $"defense {a.BaseDefense}" : null }.Where(x => x != null)), PlanBox(a)),
            a =>
            {
                var actor = Assets.ArmorActor(a);
                var s = Ui.StackOf(8, Ui.Img(Assets.ArmorIcon(a, 96), 96), Ui.Text(a.Name, "h3"), Ui.Text(string.Join(" · ", new[] { a.Set, a.Slot, a.Effect, a.SetBonus != null ? $"set bonus: {a.SetBonus.Name} (needs ★★ on every piece)" : null }.Where(x => x != null)), "muted small"),
                    Ui.GameDesc(actor == null ? null : Assets.Desc(actor)));
                if (a.WhereFound != null) s.Children.Add(Ui.Text("Where: " + a.WhereFound + (a.Price != null ? " · " + a.Price : ""), "small"));
                if (a.Upgrades is { Length: > 0 })
                    foreach (var u in a.Upgrades)
                        s.Children.Add(Ui.Row(10, Ui.Text(new string('★', u.Level), "small"), Ui.Text(u.Defense?.ToString() ?? "?", "mono small"),
                            new TextBlock { Text = string.Join(", ", (u.Materials ?? []).Select(m => $"{m.Name} ×{m.Count}")), FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 }));
                else s.Children.Add(Ui.Text("This piece can't be upgraded.", "muted small"));
                return s;
            }, 380, () => Ui.Text("Pick a piece to see its upgrades.", "muted"));
        var fairies = App.Data.Armor.GreatFairies;
        var right = Ui.Stack(14,
            Ui.Panel(Ui.Stack(8, Ui.Text("SHOPPING LIST", "h2"), Ui.Text("Pick a target level for any piece; everything still needed adds up here.", "faint"), shop), "panel glow"));
        if (fairies != null)
        {
            var f = Ui.Stack(6, Ui.Text("GREAT FAIRIES", "h2"), Ui.Text(fairies.Note ?? "", "muted small"));
            foreach (var c in fairies.UnlockCostByOrder ?? []) f.Children.Add(Ui.Text($"Fountain {c.Order}: {c.Rupees} rupees", "small"));
            right.Children.Add(Ui.Panel(f));
        }
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,320"), RowDefinitions = new RowDefinitions("Auto,*") };
        g.Children.Add(Rows.SearchBox("Find armor…", s => { q = s; Refresh(); }));
        md.Margin = new Thickness(0, 12, 0, 0); Grid.SetRow(md, 1); g.Children.Add(md);
        var rs = new ScrollViewer { Content = right, Margin = new Thickness(14, 0, 0, 0) }; Grid.SetColumn(rs, 1); Grid.SetRowSpan(rs, 2); g.Children.Add(rs);
        return Rows.Page(Ui.Header("Armor & upgrades"), g);
    }

    Control PlanBox(ArmorPiece a)
    {
        if (!a.Upgradeable) return Ui.Tag("No upgrades");
        var plan = App.Progress.Settings.ArmorPlan;
        var box = new ComboBox { ItemsSource = new[] { "Not planned", "★", "★★", "★★★", "★★★★" }, SelectedIndex = plan.GetValueOrDefault(a.Id), MinWidth = 120 };
        box.SelectionChanged += (_, _) =>
        {
            App.Progress.SaveSettings(s => { if (box.SelectedIndex > 0) s.ArmorPlan[a.Id] = box.SelectedIndex; else s.ArmorPlan.Remove(a.Id); });
            DrawShop();
        };
        return box;
    }

    void DrawShop()
    {
        var need = new Dictionary<string, int>();
        foreach (var a in App.Data.Armor.Armor)
        {
            int target = App.Progress.Settings.ArmorPlan.GetValueOrDefault(a.Id);
            foreach (var u in a.Upgrades ?? []) if (u.Level <= target) foreach (var m in u.Materials ?? []) need[m.Name] = need.GetValueOrDefault(m.Name) + m.Count;
        }
        shop.Children.Clear();
        if (need.Count == 0) { shop.Children.Add(Ui.Text("Nothing planned yet.", "muted")); return; }
        foreach (var (n, c) in need.OrderByDescending(kv => kv.Value))
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            g.Children.Add(Ui.Text(n, "small"));
            var t = Ui.Text($"× {c}", "mono small"); Grid.SetColumn(t, 1); g.Children.Add(t);
            shop.Children.Add(g);
        }
    }

    protected override void OnShown(object? arg) { Refresh(arg as string); DrawShop(); }
    void Refresh() => Refresh(null);
    void Refresh(string? pick) => md.SetItems(App.Data.Armor.Armor.Where(a => q == "" || Ui.Norm(a.Name).Contains(q) || Ui.Norm(a.Set).Contains(q)).ToList(), pick == null ? null : a => a.Id == pick);
}

// ---------------------------------------------------------------------------------------------- stats

public sealed class StatsView : ViewBase
{
    readonly ContentControl body = new();
    protected override Control Build() => new ScrollViewer { Content = new Border { Padding = new Thickness(24, 20), Child = body } };
    protected override void OnShown(object? arg) { App.Progress.Changed += Draw; Draw(); }
    public override void OnHidden() => App.Progress.Changed -= Draw;

    void Draw()
    {
        var p = App.Progress;
        var page = Ui.Stack(16, Ui.Header("Stats"));
        if (!p.Linked) { page.Children.Add(Ui.Empty("Link a save to see your stats.")); body.Content = page; return; }
        var s = p.Stats; var c = p.Count();
        string Fmt(StatDef d)
        {
            var v = s[d.Key];
            return d.Unit switch
            {
                "seconds" => Ui.Duration(v),
                "quarter hearts" => $"{v / 4:0.##} ♥",
                "stamina points" => v == 1000 ? "1 wheel" : $"{v / 1000:0.#} wheels",
                _ => v == Math.Floor(v) ? $"{v:N0}" : $"{v:0.0}",
            };
        }
        // The game's per-enemy counters stop at 10 (MaxValue 10 in its GameData), so 10 means "10 or more".
        const int cap = 10;
        var defeated = App.Data.Flags.Defeated.Where(d => d.Flag != null && s.TryGetValue("defeated:" + d.Flag, out var n) && n > 0)
            .Select(d => (d.Enemy, n: (int)s["defeated:" + d.Flag])).OrderByDescending(d => d.n).ToList();
        bool capped = defeated.Any(d => d.n >= cap);
        var tiles = new WrapPanel();
        Control Tile(string label, string value, string? sub = null)
        {
            var st = Ui.Stack(4, Ui.Text(label.ToUpperInvariant(), "label"), Ui.Text(value, "value"));
            if (sub != null) st.Children.Add(Ui.Text(sub, "faint"));
            var b = Ui.Panel(st, "panel", new Thickness(0, 0, 12, 12)); b.Width = 230;
            return b;
        }
        foreach (var d in App.Data.Flags.Stats.Where(d => s.ContainsKey(d.Key) && d.Unit is not ("raw" or "unknown") && d.Key != "currentHearts"))
            tiles.Children.Add(Tile(d.Label, Fmt(d)));
        tiles.Children.Add(Tile("Enemies defeated", defeated.Count == 0 ? "—" : (capped ? "≥ " : "") + defeated.Sum(d => d.n).ToString("N0"), defeated.Count == 0 ? "Not counted in this save" : $"{defeated.Count} kinds" + (capped ? " · counted up to 10 each" : "")));
        tiles.Children.Add(Tile("Shrines · Koroks", $"{c.Shrines + c.ShrinesDlc} · {c.Koroks}"));
        page.Children.Add(tiles);

        var hist = p.History();
        var two = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        two.Children.Add(Ui.Panel(Ui.Stack(8, Ui.Text("PROGRESS OVER TIME", "h2"), hist.Count < 2 ? Ui.Text("The graph fills in as you play: every time the game saves, Slate Companion notes your totals.", "muted") : new HistoryChart(hist) { Height = 150 }), "panel", new Thickness(0, 0, 12, 0)));
        var sess = Sessions(hist); Grid.SetColumn(sess, 1); two.Children.Add(sess);
        page.Children.Add(two);

        var bars = Ui.Stack(6, Ui.Text("ENEMIES DEFEATED", "h2"));
        int max = Math.Max(1, defeated.Select(d => d.n).DefaultIfEmpty().Max());
        foreach (var (enemy, n) in defeated)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*,56") };
            g.Children.Add(Ui.Text(enemy, "small"));
            var m = Ui.Meter(n, max, true, 10); m.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(m, 1); g.Children.Add(m);
            var t = Ui.Text(n >= cap ? $"{cap}+" : $"{n}", "mono small"); t.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(t, 2); g.Children.Add(t);
            bars.Children.Add(g);
        }
        if (defeated.Count == 0) bars.Children.Add(Ui.Text("No defeated-enemy counters in this save yet.", "muted"));
        else bars.Children.Add(Ui.Text("The game itself stops counting at 10 of each kind, so \"10+\" means ten or more.", "faint"));
        page.Children.Add(Ui.Panel(bars));
        body.Content = page;
    }

    static Control Sessions(List<HistoryPoint> hist)
    {
        var s = Ui.Stack(6, Ui.Text("SESSIONS", "h2"));
        var pts = hist.OrderBy(h => h.T).ToList();
        if (pts.Count == 0) { s.Children.Add(Ui.Text("Each play session shows here with what you found in it.", "muted")); return Ui.Panel(s); }
        var sessions = new List<(HistoryPoint first, HistoryPoint last)>();
        foreach (var h in pts)
        {
            if (sessions.Count == 0 || (h.T - sessions[^1].last.T).TotalMinutes > 45) sessions.Add((h, h));
            else sessions[^1] = (sessions[^1].first, h);
        }
        static string Plus(int n) => n > 0 ? $"+{n}" : "·";
        foreach (var (f, l) in Enumerable.Reverse(sessions).Take(10))
            s.Children.Add(Ui.Text($"{f.T.ToLocalTime():g}   shrines {Plus(l.Shrines + l.ShrinesDlc - f.Shrines - f.ShrinesDlc)}   seeds {Plus(l.Koroks - f.Koroks)}   towers {Plus(l.Towers - f.Towers)}", "small mono"));
        s.Children.Add(Ui.Text("Saves less than 45 minutes apart count as one session.", "faint"));
        return Ui.Panel(s);
    }
}

/// <summary>Shrines + Korok seeds over time, as one glowing line.</summary>
public sealed class HistoryChart(List<HistoryPoint> hist) : Control
{
    public override void Render(DrawingContext ctx)
    {
        var pts = hist.OrderBy(h => h.T).Select(h => (t: h.T.Ticks, v: (double)(h.Shrines + h.ShrinesDlc + h.Koroks))).ToList();
        if (pts.Count < 2) return;
        double w = Bounds.Width, hgt = Bounds.Height, pad = 18;
        long t0 = pts[0].t, t1 = Math.Max(pts[^1].t, t0 + 1);
        double vMin = pts.Min(p => p.v), vMax = Math.Max(pts.Max(p => p.v), vMin + 1);
        Point P((long t, double v) p) => new(pad + (double)(p.t - t0) / (t1 - t0) * (w - 2 * pad), hgt - pad - (p.v - vMin) / (vMax - vMin) * (hgt - 2 * pad));
        var g = new StreamGeometry();
        using (var c = g.Open()) { c.BeginFigure(P(pts[0]), false); foreach (var p in pts.Skip(1)) c.LineTo(P(p)); }
        ctx.DrawGeometry(null, new Pen(Ui.Brush("Glow"), 2), g);
        var ft = new FormattedText($"{vMax:0} shrines + seeds", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, Ui.Brush("Faint"));
        ctx.DrawText(ft, new Point(pad, 0));
    }
}

// ---------------------------------------------------------------------------------------------- guides

public sealed class GuidesView : ViewBase
{
    MasterDetail<Section> md = null!;
    protected override Control Build()
    {
        md = new MasterDetail<Section>(s => Ui.Text(s.Title, "h3"),
            s =>
            {
                var st = Ui.Stack(10, Ui.Text(s.Title, "h1"));
                foreach (var p in s.Body ?? []) st.Children.Add(new TextBlock { Text = p, TextWrapping = TextWrapping.Wrap, LineHeight = 21 });
                return st;
            }, 640);
        return Rows.Page(Ui.Header("Guides & tips"), md);
    }
    protected override void OnShown(object? arg)
    {
        var first = App.Data.Tips.Sections.FirstOrDefault();
        md.SetItems(App.Data.Tips.Sections, arg is string id ? s => s.Id == id : md.List.SelectedItem == null ? s => s == first : null);
    }
}
