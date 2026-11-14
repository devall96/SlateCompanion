using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace SlateCompanion.UI.Views;

/// <summary>One thing in the pouch, as the save has it.</summary>
public sealed record PouchItem(int Index, string Actor, string Category, int Value, bool Equipped, uint Bonus, int BonusValue, (int type, int level)? Food);

/// <summary>
/// Inventory: everything in Link's pouch in the linked save (PorchItem and its value and equip arrays, in the game's own
/// order), what's equipped, rupees and Mon, hearts, stamina and pouch sizes, with the game's own pictures, names and
/// descriptions. Read only, from the same read of the save as everything else.
/// </summary>
public sealed class InventoryView : ViewBase
{
    static readonly (string id, string label)[] Cats =
        [("Weapon", "Weapons"), ("Bow", "Bows"), ("Arrow", "Arrows"), ("Shield", "Shields"), ("Armor", "Armor"), ("Material", "Materials"), ("Food", "Food"), ("Key", "Key items")];
    // weapon bonuses (PorchSword/Bow/Shield_FlagSp bits; the yellow, stronger kind adds 0x80000000)
    static readonly (uint bit, string name)[] Bonuses =
        [(0x1, "Attack up"), (0x2, "Durability up"), (0x4, "Critical hit"), (0x8, "Long throw"), (0x10, "Five-shot burst"), (0x20, "Three-shot burst"), (0x40, "Quick shot"), (0x80, "Shield surf up"), (0x100, "Shield guard up")];
    // CookEffect0.x: the game's cook effect ids (LifeMaxUp 2, ResistHot 4, ResistCold 5, ResistElectric 6, AttackUp 10,
    // DefenseUp 11, Quietness 12, MovingSpeed 13, GutsRecover 14, ExGutsMaxUp 15, Fireproof 16)
    static readonly Dictionary<int, string> FoodEffects = new()
    {
        [2] = "Hearty", [4] = "Chilly", [5] = "Spicy", [6] = "Electro", [10] = "Mighty", [11] = "Tough", [12] = "Sneaky",
        [13] = "Hasty", [14] = "Energizing", [15] = "Enduring", [16] = "Fireproof",
    };

    readonly StackPanel stats = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
    readonly ContentControl equipped = new();
    readonly ContentControl body = new();
    readonly TextBlock status = Ui.Text("", "muted small");
    List<PouchItem> items = [];
    Dictionary<string, double> numbers = [];
    string cat = "Weapon";

    protected override Control Build()
    {
        var chips = Rows.Chips(Cats, cat, c => { cat = c; Draw(); });
        var page = Ui.Stack(14, Ui.Header("Inventory", status), stats,
            Ui.Panel(Ui.Stack(8, Ui.Text("EQUIPPED", "h2"), equipped), "panel glow"), chips, body);
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

        Control Slot(string label, PouchItem? p, bool right)
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
            var card = Ui.Panel(g, p != null ? "panel" : "panel");
            card.Margin = new Thickness(0, 0, 0, 10);
            card.Opacity = p != null ? 1 : 0.6;
            return card;
        }

        // an original line drawing of Link's outline (not game artwork)
        var figure = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M50 8a13 13 0 1 0 0.01 0z M38 20l-6 -6 M62 20l6 -6 M35 40h30l8 52H27z M35 44l-16 44 M65 44l16 44 M19 88l-3 8 M81 88l3 8 M40 92l-4 86 M60 92l4 86 M36 178h-8 M64 178h8 M50 92v30"),
            Stroke = Ui.Brush("Glow"), StrokeThickness = 2.2, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform, Width = 150, Height = 290, Opacity = 0.85,
        };
        var mid = new Border { Child = figure, Padding = new Thickness(24, 0), VerticalAlignment = VerticalAlignment.Center };
        var left = Ui.Stack(0, Slot("Weapon", Of("Weapon"), false), Slot("Shield", Of("Shield"), false), Slot("Bow", Of("Bow"), false), Slot("Arrows", Of("Arrow"), false));
        var right = Ui.Stack(0, Slot("Head", head, true), Slot("Body", body, true), Slot("Legs", legs, true));
        right.VerticalAlignment = VerticalAlignment.Center;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"), HorizontalAlignment = HorizontalAlignment.Center };
        g.Children.Add(left); Grid.SetColumn(mid, 1); g.Children.Add(mid); Grid.SetColumn(right, 2); g.Children.Add(right);
        var s = Ui.Stack(10, g);

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
        var names = Bonuses.Where(b => (p.Bonus & b.bit) != 0).Select(b => b.name).ToList();
        if (names.Count == 0) return "";
        return (p.Bonus & 0x80000000) != 0 ? $"{string.Join(", ", names)} +{p.BonusValue} (yellow)" : $"{string.Join(", ", names)} +{p.BonusValue}";
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

        equipped.Content = Doll();

        var these = items.Where(i => i.Category == cat).ToList();
        if (these.Count == 0) { body.Content = Ui.Empty(items.Count == 0 ? "No save linked yet." : "Nothing here in this save."); return; }
        var grid = new WrapPanel();
        foreach (var p in these)
        {
            var desc = Assets.Desc(p.Actor);
            var food = p.Food is { } f && FoodEffects.TryGetValue(f.type, out var fx) ? $"{fx}{(f.level > 0 ? $" (level {f.level})" : "")}" : "";
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
            ToolTip.SetTip(card, p.Actor);
            grid.Children.Add(card);
        }
        body.Content = Ui.Stack(8, Ui.Text($"{these.Count} {Cats.First(c => c.id == cat).label.ToLowerInvariant()}", "faint small"), grid);
    }
}
