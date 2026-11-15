using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace SlateCompanion.UI.Views;

/// <summary>One thing in the pouch, as the save has it.</summary>
public sealed record PouchItem(int Index, string Actor, string Category, int Value, bool Equipped, uint Bonus, int BonusValue, (int type, int level)? Food);

/// <summary>
/// Inventory: everything in Link's pouch in the linked save (PorchItem and its value and equip arrays, in the game's own
/// order), what's equipped, rupees and Mon, hearts, stamina and pouch sizes, with the game's own pictures, names and
/// descriptions. Reading is read only; equipping and loadouts (opt-in, "Change equipment") edit the save through
/// SaveManager: only while the game is closed, Savage Wind's save lock taken, everything backed up first.
/// </summary>
public sealed class InventoryView : ViewBase
{
    static readonly (string id, string label)[] Cats =
        [("Weapon", "Weapons"), ("Bow", "Bows"), ("Arrow", "Arrows"), ("Shield", "Shields"), ("Armor", "Armor"), ("Material", "Materials"), ("Food", "Food"), ("Key", "Key items")];
    // weapon bonuses (PorchSword/Bow/Shield_FlagSp bits; the yellow, stronger kind adds 0x80000000)
    static readonly (uint bit, string name)[] Bonuses =
        [(0x1, "Attack up"), (0x2, "Durability up"), (0x4, "Critical hit"), (0x8, "Long throw"), (0x10, "-shot burst"), (0x20, "Zoom"), (0x40, "Quick shot"), (0x80, "Shield surf up"), (0x100, "Shield guard up")];
    // CookEffect0.x: the game's cook effect ids (LifeMaxUp 2, ResistHot 4, ResistCold 5, ResistElectric 6, AttackUp 10,
    // DefenseUp 11, Quietness 12, MovingSpeed 13, GutsRecover 14, ExGutsMaxUp 15, Fireproof 16)
    static readonly Dictionary<int, string> FoodEffects = new()
    {
        [2] = "Hearty", [4] = "Chilly", [5] = "Spicy", [6] = "Electro", [10] = "Mighty", [11] = "Tough", [12] = "Sneaky",
        [13] = "Hasty", [14] = "Energizing", [15] = "Enduring", [16] = "Fireproof",
    };

    readonly StackPanel stats = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
    readonly ContentControl equipped = new(), loadoutsBox = new();
    Outfit? preview;           // a loadout shown on the 3D Link instead of what's equipped
    string? dollKey;           // what the doll on screen shows (Draw rebuilds it only when this changes)
    string? previewName;
    bool busy;
    static bool EquipEdits => App.Progress.Settings.EquipEdits;
    readonly ContentControl body = new();
    readonly TextBlock status = Ui.Text("", "muted small");
    List<PouchItem> items = [];
    Dictionary<string, double> numbers = [];
    string cat = "Weapon";

    protected override Control Build()
    {
        var chips = Rows.Chips(Cats, cat, c => { cat = c; Draw(); });
        var edits = new ToggleSwitch { IsChecked = App.Progress.Settings.EquipEdits, OnContent = "Change equipment", OffContent = "Change equipment" };
        ToolTip.SetTip(edits, "Lets you equip, take off and apply loadouts. That changes your save: only while Savage Wind is closed, and everything is backed up first.");
        edits.IsCheckedChanged += (_, _) => { App.Progress.SaveSettings(x => x.EquipEdits = edits.IsChecked == true); Draw(); };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(Ui.Text("EQUIPPED", "h2")); Grid.SetColumn(edits, 1); head.Children.Add(edits);
        var page = Ui.Stack(14, Ui.Header("Inventory", status), stats,
            Ui.Panel(Ui.Stack(8, head, equipped), "panel glow"), loadoutsBox, chips, body);
        return new ScrollViewer { Content = new Border { Padding = new Thickness(24, 20), Child = page } };
    }

    protected override void OnShown(object? arg)
    {
        App.Progress.Changed += Load;
        Load();
    }

    public override void OnHidden() => App.Progress.Changed -= Load;

    void Load()
    {
        var path = App.Progress.SlotPath;
        if (path == null) { items = []; status.Text = "No save linked: add one in Saves & settings."; Draw(); return; }
        status.Text = $"{App.Progress.Source?.Label.Replace(" (in use)", "")} · slot {App.Progress.Slot} · saved {Ui.Ago(App.Progress.Modified)}";
        _ = Task.Run(() => Read(Path.Combine(path, "game_data.sav"))).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (t.IsCompletedSuccessfully) (items, numbers) = t.Result;
            else { items = []; status.Text = "The save couldn't be read: " + t.Exception?.InnerException?.Message; }
            Draw();
        }));
    }

    /// <summary>The pouch from game_data.sav (read only).</summary>
    static (List<PouchItem>, Dictionary<string, double>) Read(string file)
    {
        var s = SaveFile.Load(file);
        var raw = s.Get("PorchItem") is { Raw.Length: > 0 } pi ? Convert.FromBase64String(pi.Raw) : [];
        var value = s.Get("PorchItem_Value1")?.Words ?? [];
        var equip = s.Get("PorchItem_EquipFlag")?.Words ?? [];
        uint[] W(string n) => s.Get(n)?.Words ?? [];
        var swordSp = W("PorchSword_FlagSp"); var swordV = W("PorchSword_ValueSp");
        var bowSp = W("PorchBow_FlagSp"); var bowV = W("PorchBow_ValueSp");
        var shieldSp = W("PorchShield_FlagSp"); var shieldV = W("PorchShield_ValueSp");
        var cook = W("CookEffect0");
        var list = new List<PouchItem>();
        int nSword = 0, nBow = 0, nShield = 0, nFood = 0;
        for (int i = 0; i * 64 + 64 <= raw.Length; i++)
        {
            int end = Array.IndexOf(raw, (byte)0, i * 64, 64);
            var actor = System.Text.Encoding.ASCII.GetString(raw, i * 64, (end < 0 ? i * 64 + 64 : end) - i * 64);
            if (actor.Length == 0) continue;
            var c = CategoryOf(actor);
            uint bonus = 0; int bv = 0; (int, int)? food = null;
            if (c == "Weapon" && nSword < swordSp.Length) { bonus = swordSp[nSword]; bv = (int)swordV.ElementAtOrDefault(nSword); nSword++; }
            else if (c == "Bow" && nBow < bowSp.Length) { bonus = bowSp[nBow]; bv = (int)bowV.ElementAtOrDefault(nBow); nBow++; }
            else if (c == "Shield" && nShield < shieldSp.Length) { bonus = shieldSp[nShield]; bv = (int)shieldV.ElementAtOrDefault(nShield); nShield++; }
            else if (c == "Food" && nFood * 2 + 1 < cook.Length)
            {
                food = ((int)BitConverter.UInt32BitsToSingle(cook[nFood * 2]), (int)BitConverter.UInt32BitsToSingle(cook[nFood * 2 + 1]));
                nFood++;
            }
            list.Add(new PouchItem(i, actor, c, i < value.Length ? unchecked((int)value[i]) : 0, i < equip.Length && equip[i] != 0, bonus, bv, food));
        }
        var nums = new Dictionary<string, double>();
        void N(string key, string flag, Func<uint, double> f) { if (s.Get(flag) is { Words.Length: > 0 } v) nums[key] = f(v.Words[0]); }
        N("rupees", "CurrentRupee", w => unchecked((int)w));
        N("mon", "CurrentMamo", w => unchecked((int)w));
        N("hearts", "MaxHartValue", w => unchecked((int)w) / 4.0);
        N("stamina", "StaminaMax", w => BitConverter.UInt32BitsToSingle(w) / 1000.0);
        N("weaponSlots", "WeaponPorchStockNum", w => w);
        N("bowSlots", "BowPorchStockNum", w => w);
        N("shieldSlots", "ShieldPorchStockNum", w => w);
        return (list, nums);
    }

    /// <summary>
    /// The paper doll: Link's outline in the middle, his weapon, shield, bow and arrows on the left, head, body and legs
    /// on the right (game pictures), and the armor set's bonus underneath when all three pieces are of one set.
    /// </summary>
    Control Doll()
    {
        if (items.Count == 0) return Ui.Text("No save linked yet.", "muted small");
        var eq = items.Where(i => i.Equipped).ToList();
        PouchItem? Of(string cat, string? suffix = null) => eq.FirstOrDefault(i => i.Category == cat && (suffix == null || i.Actor.EndsWith(suffix, StringComparison.Ordinal)));
        var head = Of("Armor", "_Head"); var body = Of("Armor", "_Upper"); var legs = Of("Armor", "_Lower");

        Control Slot(string label, PouchItem? p, bool right, string slotId)
        {
            var lines = Ui.Stack(1, Ui.Text(label.ToUpperInvariant(), "faint small"));
            if (p == null) lines.Children.Add(Ui.Text("Nothing", "muted small"));
            else
            {
                var n = Ui.Text(NameOf(p), "h3"); n.TextWrapping = TextWrapping.Wrap; lines.Children.Add(n);
                var stars = p.Category == "Armor" ? Assets.ArmorStars(p.Actor) : 0;
                var meta = string.Join(" · ", new[] { stars > 0 ? new string('★', stars) : "", ValueText(p), BonusText(p) }.Where(x => x != ""));
                if (meta != "") lines.Children.Add(Ui.Text(meta, "muted small"));
            }
            var icon = (p != null ? (p.Category == "Armor" ? Ui.Img(Assets.Icon(p.Actor, 64), 56) : Ui.Img(Assets.Icon(p.Actor, 64), 56)) : null)
                       ?? new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(10), BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(1) };
            var g = new Grid { ColumnDefinitions = right ? new ColumnDefinitions("Auto,*") : new ColumnDefinitions("*,Auto"), Width = 270 };
            icon.VerticalAlignment = VerticalAlignment.Center;
            lines.VerticalAlignment = VerticalAlignment.Center;
            if (right) { icon.Margin = new Thickness(0, 0, 12, 0); g.Children.Add(icon); Grid.SetColumn(lines, 1); g.Children.Add(lines); }
            else
            {
                foreach (var c in lines.Children.OfType<TextBlock>()) c.TextAlignment = TextAlignment.Right;
                lines.HorizontalAlignment = HorizontalAlignment.Right;
                g.Children.Add(lines); icon.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(icon, 1); g.Children.Add(icon);
            }
            if (EquipEdits && p != null)
            {
                var off = Ui.Button("Take off", () => Equip(new() { [slotId] = null }), "small ghost");
                off.HorizontalAlignment = right ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                off.IsEnabled = !busy;
                lines.Children.Add(off);
            }
            var card = Ui.Panel(g, "panel");
            card.Margin = new Thickness(0, 0, 0, 10);
            card.Opacity = p != null ? 1 : 0.6;
            // drop an item from the pouch here to equip it
            if (EquipEdits) Drag.Target(card, d => PouchOf(d) is { } it && Equipment.SlotOf(it.Actor) == slotId, d => Equip(new() { [slotId] = PouchOf(d)!.Index }));
            return card;
        }

        // an original line drawing of Link's outline (not game artwork)
        // (a new one each time: a control can only be in one place)
        Control Figure() => new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M50 8a13 13 0 1 0 0.01 0z M38 20l-6 -6 M62 20l6 -6 M35 40h30l8 52H27z M35 44l-16 44 M65 44l16 44 M19 88l-3 8 M81 88l3 8 M40 92l-4 86 M60 92l4 86 M36 178h-8 M64 178h8 M50 92v30"),
            Stroke = Ui.Brush("Glow"), StrokeThickness = 2.2, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform, Width = 150, Height = 290, Opacity = 0.85,
        };
        var mid = new Border { Padding = new Thickness(24, 0), VerticalAlignment = VerticalAlignment.Center };
        // Link himself, in this gear, from the player's game files: live 3D on the graphics card (drag to turn him),
        // or a still picture from the software renderer when drawing in software or if OpenGL can't start. The
        // outline stands in while he's being prepared, or if he can't be drawn at all.
        Control Waiting(string text) => Ui.Stack(6, Figure(), Ui.Text(text, "faint small"));
        mid.Child = Figure();
        if (LinkDoll.Available)
        {
            var outfit = preview ?? new Outfit(head?.Actor, body?.Actor, legs?.Actor, Of("Weapon")?.Actor, Of("Shield")?.Actor, Of("Bow")?.Actor);
            void Still()
            {
                Control Picture(Bitmap b) => new Image { Source = b, Width = 330, Height = 330, Stretch = Stretch.Uniform };
                if (LinkDoll.Cached(outfit) is { } ready) { mid.Child = Picture(ready); return; }
                mid.Child = Waiting("Drawing Link…");
                _ = LinkDoll.DrawAsync(outfit).ContinueWith(t => Dispatcher.UIThread.Post(() =>
                {
                    if (t.Result is { } path && File.Exists(path))
                        try { mid.Child = Picture(new Bitmap(path)); return; } catch (Exception) { }
                    mid.Child = Figure();
                }));
            }
            if (Program.Gpu && !LinkDoll.GpuFailed)
            {
                mid.Child = Waiting("Getting Link ready…");
                _ = LinkDoll.MeshAsync(outfit).ContinueWith(t => Dispatcher.UIThread.Post(() =>
                {
                    if (t.Result is not { } m) { mid.Child = Figure(); return; }
                    var view = new LinkView3D { Width = 330, Height = 400, Mesh = m, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeWestEast) };
                    ToolTip.SetTip(view, "Drag to turn him; double-click to face front");
                    view.Failed += _ => { LinkDoll.GpuFailed = true; Still(); };
                    mid.Child = view;
                }));
            }
            else Still();
        }
        var left = Ui.Stack(0, Slot("Weapon", Of("Weapon"), false, "Weapon"), Slot("Shield", Of("Shield"), false, "Shield"), Slot("Bow", Of("Bow"), false, "Bow"), Slot("Arrows", Of("Arrow"), false, "Arrow"));
        var right = Ui.Stack(0, Slot("Head", head, true, "Head"), Slot("Body", body, true, "Upper"), Slot("Legs", legs, true, "Lower"));
        right.VerticalAlignment = VerticalAlignment.Center;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"), HorizontalAlignment = HorizontalAlignment.Center };
        g.Children.Add(left); Grid.SetColumn(mid, 1); g.Children.Add(mid); Grid.SetColumn(right, 2); g.Children.Add(right);
        var s = Ui.Stack(10, g);
        if (preview != null)
            s.Children.Add(Ui.Row(8, Ui.Tag("Preview", "orange"), Ui.Text($"Showing the loadout \u201c{previewName}\u201d on Link (not equipped).", "small"),
                Ui.Button("Back to what's equipped", () => { preview = null; previewName = null; Draw(); }, "small ghost")));

        // the set: all three pieces of one set; its bonus needs every piece at ★★
        var pieces = new[] { head, body, legs }.Select(p => p == null ? null : App.Data.Armor.Armor.FirstOrDefault(a => Ui.Norm(a.Name) == Ui.Norm(NameOf(p)))).ToList();
        if (pieces.All(p => p?.Set != null) && pieces.Select(p => p!.Set).Distinct().Count() == 1)
        {
            var set = pieces[0]!;
            int minStars = new[] { head, body, legs }.Min(p => Assets.ArmorStars(p!.Actor));
            var bonus = set.SetBonus;
            string text = bonus == null ? $"{set.Set}: all three pieces" :
                minStars >= bonus.RequiresLevel ? $"{set.Set}: {bonus.Name} is on" : $"{set.Set}: {bonus.Name} once every piece is at {new string('★', bonus.RequiresLevel)} (lowest now: {(minStars > 0 ? new string('★', minStars) : "no stars")})";
            s.Children.Add(Ui.Row(8, Ui.Tag("Set", bonus != null && minStars >= bonus.RequiresLevel ? "done" : "orange"), Ui.Text(text, "small")));
        }
        else if (new[] { head, body, legs }.Count(p => p != null) > 0)
            s.Children.Add(Ui.Text("Wear all three pieces of one set (upgraded to ★★) for its set bonus.", "faint small"));
        return s;
    }

    PouchItem? PouchOf(string payload) =>
        payload.StartsWith("pouch:", StringComparison.Ordinal) && int.TryParse(payload[6..], out var i) ? items.FirstOrDefault(x => x.Index == i) : null;

    /// <summary>Equips / takes off (slot → PorchItem index, null = nothing): an edit of the linked save.</summary>
    async void Equip(Dictionary<string, int?> choice, string? what = null)
    {
        var saveDir = Equipment.SaveDirOf(App.Progress.Source);
        var slot = App.Progress.SlotPath;
        if (saveDir == null || slot == null) { ErrorWindow.Show("Only Savage Wind saves", "Equipment can be changed in a Savage Wind save (Saves & settings shows which save is linked)."); return; }
        if (busy) return;  // (a drop while an edit is still running)
        // what the screen showed at each index, so an item that moved in the pouch since isn't equipped by mistake
        var expect = new Dictionary<int, string>();
        foreach (var i in choice.Values.OfType<int>()) if (items.FirstOrDefault(x => x.Index == i) is { } it) expect[i] = it.Actor;
        busy = true; Draw();
        try
        {
            var done = await Task.Run(() => SaveManager.EditGameData(saveDir, slot, s => Equipment.Apply(s, choice, expect)));
            App.Main.Toast(what ?? (done == "nothing to change" ? "Nothing to change" : "Done. Your saves were backed up first."));
            await App.Progress.RefreshAsync();
        }
        catch (SaveManager.SaveException e) { ErrorWindow.Show(e.Title, e.Message); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { ErrorWindow.Show("Equipment not changed", e.Message, e); }
        catch (Exception e) { ErrorWindow.Show("Equipment not changed", e.Message + " Your saves were backed up before anything changed.", e); }
        finally { busy = false; }
        Load();
    }

    // ---------------------------------------------------------------- loadouts

    List<(int index, string actor, int value, bool equipped)> PouchTuples() => items.Select(i => (i.Index, i.Actor, i.Value, i.Equipped)).ToList();

    Outfit OutfitOf(Loadout l, Outfit current)
    {
        string? A(string slot, string? now) => l.Slots.TryGetValue(slot, out var it) ? it?.Actor : now;
        return new Outfit(A("Head", current.Head), A("Upper", current.Upper), A("Lower", current.Lower), A("Weapon", current.Weapon), A("Shield", current.Shield), A("Bow", current.Bow));
    }

    Outfit CurrentOutfit()
    {
        string? E(string slot) => items.FirstOrDefault(i => i.Equipped && Equipment.SlotOf(i.Actor) == slot)?.Actor;
        return new Outfit(E("Head"), E("Upper"), E("Lower"), E("Weapon"), E("Shield"), E("Bow"));
    }

    Control Loadouts()
    {
        var list = Equipment.LoadLoadouts();
        var s = Ui.Stack(8);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(Ui.Text("LOADOUTS", "h2"));
        var add = Ui.Row(8,
            Ui.Button("Save what's equipped", () => Edit(null, true), "small"),
            Ui.Button("New loadout", () => Edit(null, false), "small ghost"));
        Grid.SetColumn(add, 1); top.Children.Add(add);
        s.Children.Add(top);
        if (list.Count == 0) s.Children.Add(Ui.Text("Save sets of gear here (weapon, shield, bow, arrows, head, body, legs) and put one on with one click.", "muted small"));
        var pouch = PouchTuples();
        var wrap = new WrapPanel();
        foreach (var l in list)
        {
            var (choice, missing) = Equipment.Resolve(l, pouch);
            var icons = new WrapPanel();
            foreach (var sl in Equipment.Slots)
            {
                if (!l.Slots.TryGetValue(sl, out var it)) continue;
                var img = it == null ? null : Ui.Img(Assets.Icon(it.Actor, 40), 32);
                Control c = img ?? new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(6), BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(1) };
                if (it != null && !choice.ContainsKey(sl)) c.Opacity = 0.35;
                ToolTip.SetTip(c, $"{Equipment.SlotNames[sl]}: {(it == null ? "nothing" : Assets.Name(it.Actor) ?? it.Actor)}{(it != null && !choice.ContainsKey(sl) ? " (not in your pouch)" : "")}");
                c.Margin = new Thickness(0, 0, 4, 4);
                icons.Children.Add(c);
            }
            var lo = l;
            var buttons = new WrapPanel();
            void B(string text, Action a, string cls = "small ghost", bool enabled = true) { var b = Ui.Button(text, a, cls); b.Margin = new Thickness(0, 4, 6, 0); b.IsEnabled = enabled && !busy; buttons.Children.Add(b); }
            if (EquipEdits) B("Put on", () => Apply(lo), "small primary");
            B("Preview", () => { preview = OutfitOf(lo, CurrentOutfit()); previewName = lo.Name; Draw(); });
            B("Edit", () => Edit(lo, false));
            B("Delete", () => { var all = Equipment.LoadLoadouts(); all.RemoveAll(x => x.Name == lo.Name); Equipment.SaveLoadouts(all); Draw(); });
            var card = Ui.Panel(Ui.Stack(4, Ui.Text(l.Name, "h3"), icons,
                Ui.Text(missing.Count > 0 ? "Missing: " + string.Join(", ", missing) : "Everything's in your pouch", missing.Count > 0 ? "small" : "faint small"), buttons), "panel");
            card.Width = 330; card.Margin = new Thickness(0, 0, 10, 10);
            wrap.Children.Add(card);
        }
        s.Children.Add(wrap);
        if (!EquipEdits && list.Count > 0) s.Children.Add(Ui.Text("Turn on Change equipment (above) to put a loadout on.", "faint small"));
        return Ui.Panel(s);
    }

    void Apply(Loadout l)
    {
        var (choice, missing) = Equipment.Resolve(l, PouchTuples());
        if (choice.Count == 0) { ErrorWindow.Show("Nothing to put on", "None of this loadout's items are in your pouch."); return; }
        Equip(choice, $"\u201c{l.Name}\u201d is on" + (missing.Count > 0 ? $" (skipped: {string.Join(", ", missing)})" : "") + ". Your saves were backed up first.");
        preview = null;
    }

    /// <summary>Make or change a loadout: a name and, per slot, an item from the pouch, nothing, or leave as it is.</summary>
    async void Edit(Loadout? existing, bool fromEquipped)
    {
        var slots = new Dictionary<string, LoadoutItem?>(existing?.Slots ?? []);
        if (fromEquipped)
            foreach (var sl in Equipment.Slots)
                slots[sl] = items.FirstOrDefault(i => i.Equipped && Equipment.SlotOf(i.Actor) == sl) is { } e ? new LoadoutItem(e.Actor, e.Value) : null;
        var name = new TextBox { Text = existing?.Name ?? (fromEquipped ? "My gear" : "New loadout"), Width = 420 };
        var rows = Ui.Stack(6);
        const string Keep = "(leave as it is)", None = "(nothing)";
        foreach (var sl in Equipment.Slots)
        {
            var choices = items.Where(i => Equipment.SlotOf(i.Actor) == sl).ToList();
            var labels = new List<string> { Keep, None };
            labels.AddRange(choices.Select(c => $"{NameOf(c)}{(c.Category is "Weapon" or "Bow" or "Shield" ? $" ({c.Value})" : "")}"));
            // what the loadout has but the pouch doesn't any more stays selectable, marked missing
            LoadoutItem? have = slots.TryGetValue(sl, out var h) ? h : null;
            bool missingItem = have != null && !choices.Any(c => c.Actor == have.Actor);
            if (missingItem) labels.Add($"{Assets.Name(have!.Actor) ?? have.Actor} (not in your pouch)");
            int sel = !slots.ContainsKey(sl) ? 0 : have == null ? 1 : missingItem ? labels.Count - 1
                : 2 + Math.Max(0, choices.FindIndex(c => c.Actor == have.Actor && c.Value == have.Value) is var i && i >= 0 ? i : choices.FindIndex(c => c.Actor == have.Actor));
            var combo = new ComboBox { ItemsSource = labels, SelectedIndex = sel, Width = 300 };
            var slotId = sl;
            combo.SelectionChanged += (_, _) =>
            {
                int k = combo.SelectedIndex;
                if (k == 0) slots.Remove(slotId);
                else if (k == 1) slots[slotId] = null;
                else if (k - 2 < choices.Count) slots[slotId] = new LoadoutItem(choices[k - 2].Actor, choices[k - 2].Value);
            };
            // drop an item from the pouch (the cards below the dialog's owner) onto the row too
            var row = new Border { Child = Ui.Row(10, new TextBlock { Text = Equipment.SlotNames[sl], Width = 70, VerticalAlignment = VerticalAlignment.Center }, combo), Padding = new Thickness(6, 3), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent };
            Drag.Target(row, d => PouchOf(d) is { } it && Equipment.SlotOf(it.Actor) == slotId, d => { var it = PouchOf(d)!; combo.SelectedIndex = 2 + choices.FindIndex(c => c.Index == it.Index); });
            rows.Children.Add(row);
        }
        var w = new Window { Title = existing == null ? "New loadout" : "Edit loadout", SizeToContent = SizeToContent.WidthAndHeight, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.Brush("Bg") };
        var ok = Ui.Button("Save", () => w.Close(true), "primary"); ok.IsDefault = true;
        var cancel = Ui.Button("Cancel", () => w.Close(false), "ghost"); cancel.IsCancel = true;
        w.Content = new Border { Padding = new Thickness(22), Child = Ui.Stack(12, Ui.Text(existing == null ? "New loadout" : "Edit loadout", "h1"), Ui.Row(10, Ui.Text("Name", "muted small"), name), rows,
            Ui.Text("(leave as it is) keeps whatever is equipped in that slot when the loadout is put on.", "faint small"), Ui.Row(8, ok, cancel)) };
        if (!await w.ShowDialog<bool>(App.Main)) return;
        var nm = string.IsNullOrWhiteSpace(name.Text) ? "Loadout" : name.Text.Trim();
        var all = Equipment.LoadLoadouts();
        all.RemoveAll(x => x.Name == (existing?.Name ?? nm) || x.Name == nm);
        all.Add(new Loadout(nm, slots));
        Equipment.SaveLoadouts(all.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList());
        Draw();
    }

    static string CategoryOf(string a)
    {
        if (a.StartsWith("Weapon_Bow_", StringComparison.Ordinal)) return "Bow";
        if (a.StartsWith("Weapon_Shield_", StringComparison.Ordinal)) return "Shield";
        if (a.StartsWith("Weapon_", StringComparison.Ordinal)) return "Weapon";
        if (a.EndsWith("Arrow", StringComparison.Ordinal) || a.Contains("Arrow_", StringComparison.Ordinal)) return "Arrow";
        if (a.StartsWith("Armor_", StringComparison.Ordinal)) return "Armor";
        if (a.StartsWith("Item_Cook", StringComparison.Ordinal) || a.StartsWith("Item_Roast", StringComparison.Ordinal) || a.StartsWith("Item_Boiled", StringComparison.Ordinal) || a.StartsWith("Item_Chilled", StringComparison.Ordinal)) return "Food";
        if (a.StartsWith("Obj_", StringComparison.Ordinal) || a.StartsWith("Get_", StringComparison.Ordinal)) return "Key";
        return "Material";
    }

    static string NameOf(PouchItem p) => Assets.Name(p.Actor) ?? p.Actor;

    static string ValueText(PouchItem p) => p.Category switch
    {
        "Weapon" or "Bow" or "Shield" => $"durability {p.Value}",
        "Material" or "Arrow" => $"×{p.Value}",
        _ => "",
    };

    static string BonusText(PouchItem p)
    {
        if (p.Bonus == 0) return "";
        // (checked against real saves: the value is the bonus's size; for the burst it's the number of arrows, 3 or 5)
        var names = Bonuses.Where(b => (p.Bonus & b.bit) != 0).Select(b => b.bit == 0x10 ? $"{p.BonusValue}-shot burst" : $"{b.name} +{p.BonusValue}").ToList();
        if (names.Count == 0) return "";
        return string.Join(", ", names) + ((p.Bonus & 0x80000000) != 0 ? " (yellow)" : "");
    }

    void Draw()
    {
        stats.Children.Clear();
        void Stat(string label, string value) => stats.Children.Add(Ui.Panel(Ui.Stack(2, Ui.Text(label, "faint small"), Ui.Text(value, "h1")), "panel"));
        if (numbers.TryGetValue("rupees", out var r)) Stat("RUPEES", $"{r:N0}");
        if (numbers.TryGetValue("mon", out var m)) Stat("MON", $"{m:N0}");
        if (numbers.TryGetValue("hearts", out var h)) Stat("HEARTS", $"{h:0.##}");
        if (numbers.TryGetValue("stamina", out var st)) Stat("STAMINA WHEELS", $"{st:0.#}");
        if (numbers.TryGetValue("weaponSlots", out var ws)) Stat("POUCH", $"{ws:0} weapons · {numbers.GetValueOrDefault("bowSlots"):0} bows · {numbers.GetValueOrDefault("shieldSlots"):0} shields");

        // The doll (with the 3D Link) is rebuilt only when what it shows changed: a new LinkView3D compiles its
        // shaders and uploads ~100 MB of textures again, a visible hitch on every chip click or alt-tab otherwise.
        var key = $"{busy}|{EquipEdits}|{preview}|{previewName}|{items.Count}|{string.Join(",", items.Where(i => i.Equipped))}";
        if (key != dollKey || equipped.Content == null) { equipped.Content = Doll(); dollKey = key; }
        loadoutsBox.Content = Loadouts();

        var these = items.Where(i => i.Category == cat).ToList();
        if (these.Count == 0) { body.Content = Ui.Empty(items.Count == 0 ? "No save linked yet." : "Nothing here in this save."); return; }
        var grid = new WrapPanel();
        foreach (var p in these)
        {
            var desc = Assets.Desc(p.Actor);
            // CookEffect0's second number (checked against real saves): hearty, extra quarter hearts; energizing, stamina
            // points (1000 a wheel); the rest, the level
            var food = p.Food is { } f && FoodEffects.TryGetValue(f.type, out var fx)
                ? f.type switch
                {
                    2 => $"{fx}: +{f.level / 4.0:0.##} extra hearts",
                    14 => $"{fx}: restores {f.level / 1000.0:0.#} wheel{(f.level == 1000 ? "" : "s")}",
                    _ => $"{fx}{(f.level > 0 ? $" (level {f.level})" : "")}",
                } : "";
            var lines = Ui.Stack(2, Ui.Text(NameOf(p), "h3"));
            var meta = string.Join(" · ", new[] { ValueText(p), BonusText(p), food, p.Equipped ? "equipped" : "" }.Where(x => x != ""));
            if (meta != "") lines.Children.Add(Ui.Text(meta, p.Equipped ? "small" : "muted small"));
            if (desc != null) { var d = Ui.Text(desc, "faint small"); d.TextWrapping = TextWrapping.Wrap; d.MaxHeight = 52; lines.Children.Add(d); }
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Width = 360 };
            var img = Ui.Img(Assets.Icon(p.Actor, 64), 56) ?? Ui.Glyph("inventory", 40);
            img.Margin = new Thickness(0, 0, 12, 0); img.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(img); Grid.SetColumn(lines, 1); g.Children.Add(lines);
            var card = Ui.Panel(g, p.Equipped ? "panel glow" : "panel");
            card.Margin = new Thickness(0, 0, 10, 10);
            if (EquipEdits && Equipment.SlotOf(p.Actor) is { } slot)
            {
                var item = p;
                var btn = Ui.Button(p.Equipped ? "Take off" : "Equip", () => Equip(new() { [slot] = item.Equipped ? null : item.Index }), "small ghost");
                btn.HorizontalAlignment = HorizontalAlignment.Left; btn.IsEnabled = !busy;
                lines.Children.Add(btn);
                Drag.Source(card, "pouch:" + p.Index);
            }
            ToolTip.SetTip(card, p.Actor);
            grid.Children.Add(card);
        }
        body.Content = Ui.Stack(8, Ui.Text($"{these.Count} {Cats.First(c => c.id == cat).label.ToLowerInvariant()}", "faint small"), grid);
    }
}
