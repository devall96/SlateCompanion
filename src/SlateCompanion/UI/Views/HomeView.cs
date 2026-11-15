using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace SlateCompanion.UI.Views;

/// <summary>Adventure log: completion per category, play time, and the closest unfinished things to the save spot.</summary>
public sealed class HomeView : ViewBase
{
    readonly ContentControl body = new();
    protected override Control Build() => new ScrollViewer { Content = new Border { Padding = new Thickness(24, 20), Child = body } };

    protected override void OnShown(object? arg) { App.Progress.Changed += Draw; Draw(); }
    public override void OnHidden() => App.Progress.Changed -= Draw;

    void Draw()
    {
        var p = App.Progress; var d = App.Data;
        var c = p.Count();
        int Total(string t) => d.Markers.Count(m => m.Type == t);
        (int done, int total) Q(string g, Quest[] list) => (list.Count(q => p.IsDone($"{g}:{q.Id}")), list.Length);
        var main = Q("mainQuests", d.Flags.MainQuests); var side = Q("sideQuests", d.Flags.SideQuests); var sq = Q("shrineQuests", d.Flags.ShrineQuests);
        var rows = new (string label, int n, int total, string go)[]
        {
            ("Shrines", c.Shrines, Total("shrine"), "shrines"), ("Champions' Ballad shrines", c.ShrinesDlc, Total("shrine_dlc"), "shrines"),
            ("Korok seeds", c.Koroks, d.Koroks.Length, "koroks"), ("Sheikah towers", c.Towers, Total("tower"), "map"),
            ("Divine Beasts", c.Beasts, Total("divine_beast"), "quests"), ("Memory photo spots", c.Memories, Total("memory"), "map"),
            ("Main quests", main.done, main.total, "quests"), ("Side quests", side.done, side.total, "quests"), ("Shrine quests", sq.done, sq.total, "quests"),
        };
        int sum = rows.Sum(r => r.n), all = rows.Sum(r => r.total);
        int shrinesAll = Total("shrine") + Total("shrine_dlc");

        var page = Ui.Stack(16, Ui.Header("Adventure log"));
        if (!p.Linked)
            page.Children.Add(Ui.Panel(Ui.Stack(8, Ui.Text("LINK YOUR SAVE", "h2"),
                Ui.Text("Slate Companion reads your save (never writes to it) and ticks off shrines, Koroks, quests and more as you play. It finds Savage Wind, Cemu, Ryujinx and Yuzu-family saves by itself; you can also drop a save folder or file on this window.", "muted"),
                Ui.Button("Choose a save…", () => App.Main.Go("settings"), "primary")), "panel glow"));

        var tiles = new UniformGrid { Columns = 4 };
        Control Tile(string label, string value, string sub, Control? extra = null)
        {
            var s = Ui.Stack(6, Ui.Text(label.ToUpperInvariant(), "label"), Ui.Text(value, "value"));
            if (extra != null) s.Children.Add(extra);
            if (sub != "") s.Children.Add(Ui.Text(sub, "faint"));
            return Ui.Panel(s, "panel", new Thickness(0, 0, 12, 0));
        }
        tiles.Children.Add(Tile("Completion", $"{(all > 0 ? 100.0 * sum / all : 0):0.#}%", $"{sum} of {all} tracked"));
        tiles.Children.Add(Tile("Shrines", $"{c.Shrines + c.ShrinesDlc} / {shrinesAll}", "", Ui.Meter(c.Shrines + c.ShrinesDlc, shrinesAll, true)));
        tiles.Children.Add(Tile("Korok seeds", $"{c.Koroks} / {d.Koroks.Length}", "", Ui.Meter(c.Koroks, d.Koroks.Length)));
        tiles.Children.Add(Tile("Play time", p.Stats.TryGetValue("playtime", out var pt) ? Ui.Duration(pt) : "—",
            p.Linked ? $"{p.Source?.Label.Replace(" (in use)", "")} · slot {p.Slot} · {Ui.Ago(p.Modified)}" : "No save linked"));
        page.Children.Add(tiles);

        var bars = Ui.Stack(8, Ui.Text("PROGRESS", "h2"));
        foreach (var r in rows.Where(r => r.total > 0))
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*,64"), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            g.Children.Add(Ui.Text(r.label, "small"));
            var m = Ui.Meter(r.n, r.total); m.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(m, 1); g.Children.Add(m);
            var n = Ui.Text($"{r.n}/{r.total}", "mono small"); n.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(n, 2); g.Children.Add(n);
            var go = r.go;
            g.PointerPressed += (_, _) => App.Main.Go(go);
            g.Background = Avalonia.Media.Brushes.Transparent;
            bars.Children.Add(g);
        }

        var near = Ui.Stack(6, Ui.Text("CLOSEST TO YOUR LAST SAVE", "h2"));
        if (p.Position is { } pos)
        {
            var cand = d.Markers.Where(m => m.Type is "shrine" or "shrine_dlc" && !p.IsDone($"shrine:{m.Id}")).Select(m => (kind: "Shrine", name: m.Name, id: m.Id, x: m.X, z: m.Z))
                .Concat(d.Koroks.Where(k => !p.IsDone($"korok:{k.Id}")).Select(k => (kind: "Korok", name: $"Korok {k.Id} — {k.Kind ?? "seed"}", id: k.Id, x: k.X, z: k.Z)))
                .Select(m => (m, dist: Math.Sqrt((m.x - pos[0]) * (m.x - pos[0]) + (m.z - pos[2]) * (m.z - pos[2])))).OrderBy(m => m.dist).Take(7);
            foreach (var (m, dist) in cand)
            {
                var b = new Button { Content = Rows.Item(Ui.Tag(m.kind, m.kind == "Shrine" ? "orange" : "done"), m.name, null, Ui.Text($"{dist:0} m", "mono faint")), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Classes = { "ghost" } };
                var id = m.id;
                b.Click += (_, _) => App.Main.Go("map", id);
                near.Children.Add(b);
            }
        }
        else near.Children.Add(Ui.Text("Once a save is linked, the closest unfinished shrines and Korok seeds to where you last saved show here.", "muted"));

        var two = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var a = Ui.Panel(bars, "panel", new Thickness(0, 0, 12, 0)); two.Children.Add(a);
        var b2 = Ui.Panel(near, "panel glow"); Grid.SetColumn(b2, 1); two.Children.Add(b2);
        page.Children.Add(two);
        body.Content = page;
    }
}
