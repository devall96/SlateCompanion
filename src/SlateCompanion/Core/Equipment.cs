namespace SlateCompanion;

/// <summary>One item of a loadout: which actor, and its value (durability or count) to tell copies apart.</summary>
public sealed record LoadoutItem(string Actor, int? Value);

/// <summary>
/// A saved set of gear. Each slot holds an item, or null for "nothing" (take it off); a slot that isn't in
/// <see cref="Slots"/> at all is left as it is when applied.
/// </summary>
public sealed record Loadout(string Name, Dictionary<string, LoadoutItem?> Slots);

/// <summary>
/// What Link has equipped is the save's PorchItem_EquipFlag array, parallel to PorchItem. Changing it is an
/// opt-in save edit (SaveManager.EditGameData: game closed, Savage Wind's save lock, backup first) that follows
/// the game's own rules: one item per slot, of the right kind, and arrows only with a bow.
/// </summary>
public static class Equipment
{
    public static readonly string[] Slots = ["Weapon", "Shield", "Bow", "Arrow", "Head", "Upper", "Lower"];
    public static readonly Dictionary<string, string> SlotNames = new()
    {
        ["Weapon"] = "Weapon", ["Shield"] = "Shield", ["Bow"] = "Bow", ["Arrow"] = "Arrows", ["Head"] = "Head", ["Upper"] = "Body", ["Lower"] = "Legs",
    };

    /// <summary>The equipment slot an actor goes in, or null for things that aren't equipped (materials, food…).</summary>
    public static string? SlotOf(string actor)
    {
        if (actor.StartsWith("Weapon_Bow_", StringComparison.Ordinal)) return "Bow";
        if (actor.StartsWith("Weapon_Shield_", StringComparison.Ordinal)) return "Shield";
        if (actor.StartsWith("Weapon_", StringComparison.Ordinal)) return "Weapon";
        if (actor.EndsWith("Arrow", StringComparison.Ordinal) || actor.Contains("Arrow_", StringComparison.Ordinal)) return "Arrow";
        if (actor.StartsWith("Armor_", StringComparison.Ordinal))
            return actor.EndsWith("_Head", StringComparison.Ordinal) || actor.Contains("_Head_", StringComparison.Ordinal) ? "Head"
                 : actor.EndsWith("_Upper", StringComparison.Ordinal) || actor.Contains("_Upper_", StringComparison.Ordinal) ? "Upper"
                 : actor.EndsWith("_Lower", StringComparison.Ordinal) || actor.Contains("_Lower_", StringComparison.Ordinal) ? "Lower" : null;
        return null;
    }

    /// <summary>The pouch as the save has it: (index in PorchItem, actor, value, equipped).</summary>
    public static List<(int index, string actor, int value, bool equipped)> Pouch(SaveFile s)
    {
        var raw = s.Get("PorchItem") is { Raw.Length: > 0 } pi ? Convert.FromBase64String(pi.Raw) : [];
        var value = s.Get("PorchItem_Value1")?.Words ?? [];
        var equip = s.Get("PorchItem_EquipFlag")?.Words ?? [];
        var list = new List<(int, string, int, bool)>();
        for (int i = 0; i * 64 + 64 <= raw.Length; i++)
        {
            int end = Array.IndexOf(raw, (byte)0, i * 64, 64);
            var actor = System.Text.Encoding.ASCII.GetString(raw, i * 64, (end < 0 ? i * 64 + 64 : end) - i * 64);
            if (actor.Length == 0) continue;
            list.Add((i, actor, i < value.Length ? unchecked((int)value[i]) : 0, i < equip.Length && equip[i] != 0));
        }
        return list;
    }

    /// <summary>
    /// Sets what's equipped: for each slot given, the item at that PorchItem index (or nothing, for null) and every
    /// other item of that slot taken off. Slots not given stay as they are. Returns what changed, in words.
    /// <paramref name="expect"/> (index â†’ actor, as the screen showed it) refuses the edit if the pouch changed since:
    /// the game played meanwhile can put another sword at the same index.
    /// </summary>
    public static string Apply(SaveFile s, IReadOnlyDictionary<string, int?> choice, IReadOnlyDictionary<int, string>? expect = null)
    {
        var pouch = Pouch(s);
        // what will be equipped after: arrows need a bow
        var after = Slots.ToDictionary(sl => sl, sl => choice.TryGetValue(sl, out var c) ? c : pouch.FirstOrDefault(p => p.equipped && SlotOf(p.actor) == sl) is { actor: not null } e ? e.index : (int?)null);
        if (after["Arrow"] != null && after["Bow"] == null) throw new SaveManager.SaveException("Arrows need a bow", "Equip a bow too, or leave the arrows off.");
        var changes = new List<string>();
        foreach (var (slot, idx) in choice)
        {
            if (idx is int want && !pouch.Any(p => p.index == want && SlotOf(p.actor) == slot && (expect == null || !expect.TryGetValue(want, out var a) || a == p.actor)))
                throw new SaveManager.SaveException("That item isn't there", $"The item for {SlotNames[slot]} isn't in this save's pouch any more (the game may have saved since). Nothing was changed.");
            foreach (var p in pouch.Where(p => SlotOf(p.actor) == slot))
            {
                bool on = p.index == idx;
                if (on == p.equipped) continue;
                if (!s.SetWord("PorchItem_EquipFlag", p.index, on ? 1u : 0u)) throw new SaveManager.SaveException("The save is missing its equipment list", "PorchItem_EquipFlag isn't in this save. Nothing was changed.");
                changes.Add($"{(on ? "equipped" : "took off")} {p.actor}");
            }
        }
        return changes.Count == 0 ? "nothing to change" : string.Join(", ", changes);
    }

    /// <summary>A loadout matched to what's in the pouch now: the index for each slot, and the items that are missing.</summary>
    public static (Dictionary<string, int?> choice, List<string> missing) Resolve(Loadout l, List<(int index, string actor, int value, bool equipped)> pouch)
    {
        var choice = new Dictionary<string, int?>();
        var missing = new List<string>();
        foreach (var (slot, item) in l.Slots)
        {
            if (item == null) { choice[slot] = null; continue; }
            var candidates = pouch.Where(p => p.actor == item.Actor && SlotOf(p.actor) == slot).ToList();
            if (candidates.Count == 0) { missing.Add($"{SlotNames.GetValueOrDefault(slot, slot)}: {Assets.Name(item.Actor) ?? item.Actor}"); continue; }
            // the same copy if it's still there (same durability), else the one most like it
            var best = candidates.OrderBy(p => item.Value is int v ? Math.Abs(p.value - v) : 0).First();
            choice[slot] = best.index;
        }
        return (choice, missing);
    }

    public static List<Loadout> LoadLoadouts() => App.Store.Get<List<Loadout>>("loadouts") ?? [];
    public static void SaveLoadouts(List<Loadout> list) => App.Store.Put("loadouts", list);

    /// <summary>The Savage Wind save folder (with save_0) the linked slot belongs to, or null when it isn't a Savage Wind save.</summary>
    public static string? SaveDirOf(SaveSource? src)
    {
        if (src == null || src.Kind != "savagewind") return null;
        var folder = src.Folder.TrimEnd('/', '\\');
        return Path.GetFileName(folder) == "save_0" ? Path.GetDirectoryName(folder) : null;
    }
}
