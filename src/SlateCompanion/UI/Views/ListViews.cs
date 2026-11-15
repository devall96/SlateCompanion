using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace SlateCompanion.UI.Views;

// ---------------------------------------------------------------------------------------------- quests

public sealed record QuestRow(string Key, string Title, string Meta, string? Reward, string? Notes, string[]? Steps, bool HasFlag, bool Dlc, string[]? Tips, string[]? Body);

public sealed class QuestsView : ViewBase
{
    string group = "walkthrough", q = "", show = "all";
    MasterDetail<QuestRow> md = null!;
    ToggleSwitch track = null!;
    readonly TextBlock summary = Ui.Text("", "muted small");

    protected override Control Build()
    {
        md = new MasterDetail<QuestRow>(
            r => Rows.Item(Ui.Check(r.Key, Refresh), r.Title, r.Meta, r.Reward != null ? Ui.Tag(r.Reward, "orange") : r.Dlc ? Ui.Tag("DLC", "orange") : null, App.Progress.IsDone(r.Key)),
            Detail, 440, () => Ui.Text("Pick a quest or chapter to see its steps.", "muted"));
        // the walkthrough follows the save: switching it on ticks every step and chapter the save has already done
        track = new ToggleSwitch { IsChecked = App.Progress.Settings.TrackWalkthrough, OnContent = "Tick from my save", OffContent = "Tick from my save", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        ToolTip.SetTip(track, "Walkthrough steps and chapters tick themselves from your save. Ticks you made by hand stay either way.");
        track.IsCheckedChanged += (_, _) =>
        {
            bool on = track.IsChecked == true;
            if (on == App.Progress.Settings.TrackWalkthrough) return;
            App.Progress.SaveSettings(x => x.TrackWalkthrough = on);
            App.Progress.ApplyWalkthroughTracking();
            App.Main.Toast(on ? "Walkthrough synced with your save" : "Walkthrough no longer ticks from your save");
        };
        var filters = Ui.Row(10,
            Rows.Chips([("walkthrough", "Walkthrough"), ("mainQuests", "Main quests"), ("sideQuests", "Side quests"), ("shrineQuests", "Shrine quests")], group, g => { group = g; Refresh(); }),
            Rows.SearchBox("Find a quest…", s => { q = s; Refresh(); }, 220),
            Rows.Chips([("all", "All"), ("todo", "To do"), ("done", "Done")], show, s => { show = s; Refresh(); }), track);
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        body.Children.Add(filters);
        summary.Margin = new Thickness(0, 10);
        Grid.SetRow(summary, 1); body.Children.Add(summary);
        Grid.SetRow(md, 2); body.Children.Add(md);
        return Rows.Page(Ui.Header("Quests & walkthrough"), body);
    }

    static Control Detail(QuestRow r)
    {
        var s = Ui.Stack(10, Ui.Text(r.Title, "h3"));
        if (!string.IsNullOrEmpty(r.Meta)) s.Children.Add(Ui.Text(r.Meta, "muted small"));
        if (ZeldaDungeonLinks.Buttons(r.Key) is { } zd) s.Children.Add(zd);
        if (!r.Key.StartsWith("chapter:"))
        {
            // the game's own quest log: its description, and how it ended once it's done
            var (_, desc, finish) = Assets.Quest(r.Key[(r.Key.IndexOf(':') + 1)..]);
            if (Ui.GameDesc(desc, "QUEST LOG") is { } d) s.Children.Add(d);
            if (App.Progress.IsDone(r.Key) && Ui.GameDesc(finish, "COMPLETED") is { } f) s.Children.Add(f);
        }
        if (r.Notes != null) s.Children.Add(Ui.Text(r.Notes));
        if (r.Steps is { Length: > 0 })
            s.Children.Add(r.Key.StartsWith("chapter:") ? ChapterSteps(r.Key, r.Steps) : Rows.Steps(r.Steps));
        foreach (var p in r.Body ?? []) s.Children.Add(Ui.Text(p));
        if (r.Tips is { Length: > 0 }) { s.Children.Add(Ui.Text("TIPS", "h2")); foreach (var t in r.Tips) s.Children.Add(Ui.Text("• " + t, "small")); }
        if (r.Reward != null) s.Children.Add(Ui.Row(6, Ui.Text("Reward:", "muted small"), Ui.Text(r.Reward, "small")));
        if (!r.HasFlag && !r.Key.StartsWith("chapter:")) s.Children.Add(Ui.Text("This quest's save flag isn't known, so it isn't ticked automatically.", "faint"));
        return s;
    }

    /// <summary>A chapter's steps, each with its own tick: from the save where the walkthrough knows how, else by hand.</summary>
    static Control ChapterSteps(string chapterKey, string[] steps)
    {
        var id = chapterKey["chapter:".Length..];
        var track = App.Data.Walkthrough.Chapters.FirstOrDefault(c => c.Id == id)?.Track?.Steps;
        bool following = App.Progress.Settings.TrackWalkthrough;
        var list = new StackPanel { Spacing = 6 };
        for (int i = 0; i < steps.Length; i++)
        {
            var key = $"{chapterKey}:{i}";
            var text = new TextBlock { Text = steps[i], TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 330 };
            var col = Ui.Stack(2, text);
            if (following && track is { } t && i < t.Length && t[i] is { Length: > 0 } && !App.Progress.IsDone(key))
                col.Children.Add(Ui.Text("Ticks itself from your save", "faint small"));
            var row = Ui.Row(8, Ui.Check(key), col);
            row.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            list.Children.Add(row);
        }
        return list;
    }

    protected override void OnShown(object? arg)
    {
        track.IsChecked = App.Progress.Settings.TrackWalkthrough;
        App.Progress.Changed += Refresh;
        string? pick = null;
        if (arg is ValueTuple<string, string> t) { group = t.Item1; pick = t.Item2; }
        Refresh(pick);
    }
    public override void OnHidden() => App.Progress.Changed -= Refresh;

    void Refresh() => Refresh(null);

    void Refresh(string? pick)
    {
        var d = App.Data; var p = App.Progress;
        List<QuestRow> rows;
        if (group == "walkthrough")
        {
            rows = d.Walkthrough.Chapters.Select(c => new QuestRow($"chapter:{c.Id}", c.Title, c.Recommended ?? "", null, null, c.Steps, false, false, c.Tips, null)).ToList();
            foreach (var part in d.Walkthrough.Dlc?.Values ?? Enumerable.Empty<DlcPart>())
                rows.AddRange(part.Sections.Select(s => new QuestRow($"chapter:{s.Id}", s.Title, part.Title, null, null, null, false, true, null, s.Body)));
        }
        else
        {
            var list = group switch { "mainQuests" => d.Flags.MainQuests, "sideQuests" => d.Flags.SideQuests, _ => d.Flags.ShrineQuests };
            rows = list.Select(x => new QuestRow($"{group}:{x.Id}", x.Name, string.Join(" · ", new[] { x.Giver, x.Location, x.Shrine != null ? "→ " + x.Shrine : null }.Where(v => !string.IsNullOrEmpty(v))),
                x.Reward, x.Notes, x.Steps, x.Flag != null, false, null, null)).ToList();
        }
        int done = rows.Count(r => p.IsDone(r.Key));
        summary.Text = $"{done} of {rows.Count} done · blue = done in your save, green = ticked by hand";
        var shown = rows.Where(r => (q == "" || Ui.Norm(r.Title).Contains(q) || Ui.Norm(r.Meta).Contains(q)) && (show == "all" || (show == "done") == p.IsDone(r.Key))).ToList();
        md.SetItems(shown, pick == null ? null : r => r.Key.EndsWith(":" + pick));
    }
}

// ---------------------------------------------------------------------------------------------- shrines

public sealed record ShrineRow(Marker M, ShrineGuide? G)
{
    public string Key => $"shrine:{M.Id}";
}

public sealed class ShrinesView : ViewBase
{
    string filter = "all", q = "", region = "";
    MasterDetail<ShrineRow> md = null!;
    List<ShrineRow> all = [];
    readonly ContentControl summary = new();

    protected override Control Build()
    {
        var guides = App.Data.ShrineGuides.Shrines.GroupBy(g => Ui.Norm(g.Name)).ToDictionary(g => g.Key, g => g.First());
        all = App.Data.Markers.Where(m => m.Type is "shrine" or "shrine_dlc").Select(m => new ShrineRow(m, guides.GetValueOrDefault(Ui.Norm(m.Name)))).OrderBy(r => r.M.Name).ToList();
        md = new MasterDetail<ShrineRow>(
            r => Rows.Item(Ui.Check(r.Key, Refresh), r.M.Name, string.Join(" · ", new[] { r.G?.Region, r.G?.Trial }.Where(v => v != null)), r.M.Type == "shrine_dlc" ? Ui.Tag("DLC", "orange") : null, App.Progress.IsDone(r.Key)),
            r =>
            {
                var s = Ui.Stack(10, Ui.Text(r.M.Name, "h3"), Ui.Text(string.Join(" · ", new[] { r.G?.Region, r.G?.Trial }.Where(v => v != null)), "muted small"));
                if (r.G?.Quest != null) s.Children.Add(Ui.Row(6, Ui.Tag("Shrine quest", "orange"), Ui.Text(r.G.Quest)));
                s.Children.Add(Ui.Text(r.G?.Solution ?? "No guide written for this shrine yet."));
                if (r.G?.Chests != null) s.Children.Add(Ui.Text($"Treasure chests: {r.G.Chests}", "muted small"));
                var actions = new WrapPanel { Orientation = Orientation.Horizontal };
                var onMap = Ui.Button("Show on map", () => App.Main.Go("map", r.M.Id), "small");
                onMap.Margin = new Thickness(0, 0, 8, 6);
                actions.Children.Add(onMap);
                if (ZeldaDungeonLinks.Buttons(r.Key) is WrapPanel zd) foreach (var b in zd.Children.ToList()) { zd.Children.Remove(b); actions.Children.Add(b); }
                s.Children.Add(actions);
                return s;
            }, 420, () => Ui.Text("Pick a shrine to see how to solve it.", "muted"));
        var regions = new ComboBox { ItemsSource = new[] { "All regions" }.Concat(all.Select(r => r.G?.Region).Where(x => x != null).Distinct().OrderBy(x => x)).ToList(), SelectedIndex = 0, MinWidth = 180 };
        regions.SelectionChanged += (_, _) => { region = regions.SelectedIndex <= 0 ? "" : regions.SelectedItem as string ?? ""; Refresh(); };
        var filters = Ui.Row(10, Rows.SearchBox("Find a shrine…", s => { q = s; Refresh(); }),
            Rows.Chips([("all", "All"), ("todo", "Not done"), ("done", "Done"), ("dlc", "DLC")], filter, f => { filter = f; Refresh(); }), regions);
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        body.Children.Add(filters);
        md.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(md, 1); body.Children.Add(md);
        summary.Width = 260;
        return Rows.Page(Ui.Header("Shrines", summary), body);
    }

    protected override void OnShown(object? arg) { App.Progress.Changed += Refresh; Refresh(arg as string); }
    public override void OnHidden() => App.Progress.Changed -= Refresh;
    void Refresh() => Refresh(null);

    void Refresh(string? pick)
    {
        var p = App.Progress;
        int done = all.Count(r => p.IsDone(r.Key));
        summary.Content = Ui.Stack(4, Ui.Row(0, Ui.Text($"Completed {done}/{all.Count}", "muted small")), Ui.Meter(done, all.Count, true));
        md.SetItems(all.Where(r =>
            (filter == "all" || (filter == "done" ? p.IsDone(r.Key) : filter == "todo" ? !p.IsDone(r.Key) : r.M.Type == "shrine_dlc")) &&
            (region == "" || r.G?.Region == region) && (q == "" || Ui.Norm(r.M.Name).Contains(q) || Ui.Norm(r.G?.Trial).Contains(q))).ToList(),
            pick == null ? null : r => r.M.Id == pick);
    }
}

// ---------------------------------------------------------------------------------------------- koroks

public sealed record KorokRow(Korok K, string Square, double? Distance)
{
    public string Key => $"korok:{K.Id}";
}

public sealed class KoroksView : ViewBase
{
    string show = "todo", kind = "";
    bool nearMe;
    MasterDetail<KorokRow> md = null!;
    readonly ContentControl summary = new();

    static string Square(Korok k)
    {
        char col = "ABCDEFGHIJ"[Math.Clamp((int)Math.Floor((k.X + 5000) / 1000), 0, 9)];
        int row = Math.Clamp((int)Math.Floor((k.Z + 4000) / 1000) + 1, 1, 8);
        return $"{col}-{row}";
    }

    protected override Control Build()
    {
        md = new MasterDetail<KorokRow>(
            r => Rows.Item(Ui.Check(r.Key, Refresh), $"{r.K.Id} · {r.K.Kind ?? "Korok seed"}", $"Map square {r.Square}" + (r.Distance is double d ? $" · {d:0} m from your save" : ""), null, App.Progress.IsDone(r.Key)),
            r => Ui.Stack(10, Ui.Text($"Korok seed {r.K.Id}", "h3"), Ui.Text(r.K.Kind ?? "Korok seed", "muted"),
                Ui.Text($"x {r.K.X:0} · y {r.K.Y:0} · z {r.K.Z:0}", "faint mono"), Ui.Button("Show on map", () => App.Main.Go("map", r.K.Id), "small")),
            380, () => Ui.Text("Pick a seed to see it on the map.", "muted"));
        var kinds = new ComboBox { ItemsSource = new[] { "Every puzzle type" }.Concat(App.Data.Koroks.Select(k => k.Kind).Where(k => k != null).Distinct().OrderBy(k => k)).ToList(), SelectedIndex = 0, MinWidth = 200 };
        kinds.SelectionChanged += (_, _) => { kind = kinds.SelectedIndex <= 0 ? "" : kinds.SelectedItem as string ?? ""; Refresh(); };
        var near = new CheckBox { Content = "Closest to my save first" };
        near.IsCheckedChanged += (_, _) => { nearMe = near.IsChecked == true; Refresh(); };
        var filters = Ui.Row(10, Rows.Chips([("todo", "Not found"), ("all", "All"), ("done", "Found")], show, s => { show = s; Refresh(); }), kinds, near);
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        body.Children.Add(filters);
        md.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(md, 1); body.Children.Add(md);
        summary.Width = 320;
        return Rows.Page(Ui.Header("Korok seeds", summary), body);
    }

    protected override void OnShown(object? arg) { App.Progress.Changed += Refresh; Refresh(); }
    public override void OnHidden() => App.Progress.Changed -= Refresh;

    void Refresh()
    {
        var p = App.Progress;
        int found = App.Data.Koroks.Count(k => p.IsDone($"korok:{k.Id}"));
        const int hestuAll = 441;  // every weapon, bow and shield pouch upgrade together
        summary.Content = Ui.Stack(4, Ui.Text($"Found {found}/{App.Data.Koroks.Length}", "muted small"), Ui.Meter(found, App.Data.Koroks.Length),
            Ui.Text(found >= hestuAll ? "Enough seeds for every one of Hestu's pouch upgrades." : $"{hestuAll - found} more seeds would cover every pouch upgrade from Hestu.", "faint"));
        var pos = p.Position;
        var rows = App.Data.Koroks.Where(k => (kind == "" || k.Kind == kind) && (show == "all" || (show == "done") == p.IsDone($"korok:{k.Id}")))
            .Select(k => new KorokRow(k, Square(k), pos == null ? null : Math.Sqrt((k.X - pos[0]) * (k.X - pos[0]) + (k.Z - pos[2]) * (k.Z - pos[2]))));
        rows = nearMe && pos != null ? rows.OrderBy(r => r.Distance) : rows.OrderBy(r => r.Square).ThenBy(r => r.K.Id);
        md.SetItems(rows.ToList());
    }
}
