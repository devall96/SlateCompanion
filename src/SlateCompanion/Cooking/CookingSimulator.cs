// Slate Companion - BOTW cooking simulator. A line-for-line port of app/wwwroot/data/cooking.js.
// The steps follow the game's CookingMgr (see cooking-rules.md for the rules and their sources).
// Where the JS uses Math.fround this code uses float; everything else is double or int like the JS numbers.
using System;
using System.Collections.Generic;
using System.Linq;

namespace SlateCompanion.Cooking;

/// <summary>
/// Result of one cook, same fields as the JS result. Hearts are quarter hearts.
/// Potency is null for Dubious and Rock-Hard food (the JS leaves it undefined there);
/// StaminaWheels / ExtraStaminaWheels are set only for energizing / enduring dishes.
/// Critical is "no", "possible" or "guaranteed".
/// </summary>
public sealed record CookResult(
    string Name,
    string RecipeId,
    int Hearts,
    bool FullRestore,
    int ExtraHearts,
    string? Effect,
    int Level,
    int DurationSec,
    int SellPrice,
    string Critical,
    int CritChance,
    int? Potency,
    double? StaminaWheels,
    double? ExtraStaminaWheels,
    IReadOnlyList<string> Warnings);

public static class CookingSimulator
{
    public const string DubiousFoodId = "Item_Cook_O_01";
    public const string RockHardFoodId = "Item_Cook_O_02";
    public const string FairyTonicId = "Item_Cook_C_16";
    public const string ElixirId = "Item_Cook_C_17";
    public const string MonsterExtractId = "Item_Material_08";

    private static readonly HashSet<string> Timed = new(StringComparer.Ordinal)
        { "chilly", "spicy", "electro", "fireproof", "hasty", "mighty", "tough", "sneaky" };

    public static bool IsTimedEffect(string? effect) => effect != null && Timed.Contains(effect);

    // One slot (group) of a Recipes entry: try each listed actor/tag in order and take the first
    // distinct ingredient that is still unused. Copies of one ingredient count as one.
    private static bool MatchMulti(MultiRecipeEntry entry, IReadOnlyList<string> uniq, IReadOnlyDictionary<string, Ingredient> ing)
    {
        var used = new bool[uniq.Count];
        foreach (var grp in entry.Actors)
        {
            bool found = false;
            foreach (var a in grp)
            {
                int i = -1;
                for (int k = 0; k < uniq.Count; k++)
                    if (!used[k] && uniq[k] == a) { i = k; break; }
                if (i >= 0) { used[i] = true; found = true; break; }
            }
            if (!found) return false;
        }
        foreach (var grp in entry.Tags)
        {
            bool found = false;
            foreach (var t in grp)
            {
                int i = -1;
                for (int k = 0; k < uniq.Count; k++)
                    if (!used[k] && ing[uniq[k]].Tags.Contains(t)) { i = k; break; }
                if (i >= 0) { used[i] = true; found = true; break; }
            }
            if (!found) return false;
        }
        return true;
    }

    private static bool MatchSingle(SingleRecipeEntry entry, string item, IReadOnlyDictionary<string, Ingredient> ing)
    {
        if (entry.Actors.Count == 0 && entry.Tags.Count == 0) return false;
        if (entry.Actors.Count > 0 && !entry.Actors.Contains(item)) return false;
        if (entry.Tags.Count > 0 && !entry.Tags.Any(t => ing[item].Tags.Contains(t))) return false;
        return true;
    }

    /// <summary>
    /// The recipe-table entry chosen for these distinct ingredients (first-seen order), or null for Dubious Food.
    /// Returns a MultiRecipeEntry or a SingleRecipeEntry.
    /// </summary>
    public static object? FindRecipe(IReadOnlyList<string> uniq, IReadOnlyDictionary<string, Ingredient> ing, RecipeTable table)
    {
        string? single = null;
        if (uniq.Count > 1)
        {
            foreach (var e in table.Recipes)
            {
                if (e.Actors.Count + e.Tags.Count > uniq.Count) continue;
                if (MatchMulti(e, uniq, ing)) return e;
            }
            var nonSpice = uniq.Where(u => !ing[u].Tags.Contains("CookSpice")).ToList();
            single = nonSpice.Count == 1 ? nonSpice[0] : null;
        }
        else
        {
            single = uniq[0];
        }
        if (single != null)
        {
            foreach (var e in table.SingleRecipes) if (MatchSingle(e, single, ing)) return e;
        }
        return null; // nothing matched -> Dubious Food
    }

    /// <summary>Cook up to 5 ingredient ids (duplicates allowed; null/empty ids are dropped, a 6th+ is ignored).</summary>
    /// <exception cref="ArgumentException">No ingredients, or an id not in the data.</exception>
    public static CookResult Cook(IReadOnlyList<string> ingredientIds, CookingData data, bool bloodMoon = false)
    {
        ArgumentNullException.ThrowIfNull(ingredientIds);
        ArgumentNullException.ThrowIfNull(data);
        var ing = data.IngredientById;
        var sys = data.System;
        var warnings = new List<string>();
        var ids = ingredientIds.Where(i => !string.IsNullOrEmpty(i)).ToList();
        if (ids.Count > 5) { warnings.Add("Only the first 5 ingredients are used."); ids = ids.Take(5).ToList(); }
        if (ids.Count == 0) throw new ArgumentException("no ingredients", nameof(ingredientIds));
        foreach (var i in ids) if (!ing.ContainsKey(i)) throw new ArgumentException("unknown ingredient " + i, nameof(ingredientIds));

        var uniq = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var i in ids)
        {
            if (!counts.ContainsKey(i)) { uniq.Add(i); counts[i] = 0; }
            counts[i]++;
        }

        var entry = FindRecipe(uniq, ing, data.Table);
        string recipeId = entry switch
        {
            MultiRecipeEntry m => m.Result,
            SingleRecipeEntry s => s.Result,
            _ => DubiousFoodId,
        };
        int entryHb = entry switch
        {
            MultiRecipeEntry m => m.Hb,
            SingleRecipeEntry s => s.Hb,
            _ => 0,
        };
        bool hasExtract = counts.ContainsKey(MonsterExtractId);

        // --- ingredient boost (cookCalcIngredientsBoost) ---
        int total = ids.Count;
        int life = 0, enemyTime = 0;
        var effOrder = new List<string>();               // insertion order, like a JS Map
        var effCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var effPot = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var u in uniq)
        {
            var e = ing[u]; int c = counts[u];
            if (e.HasTag("CookEnemy"))
            {
                if (e.TimeBoost > 0) enemyTime += e.TimeBoost * c;
            }
            else
            {
                if (e.Hearts > 0) life += e.Hearts * c;
                if (e.Potency > 0 && !string.IsNullOrEmpty(e.Effect))
                {
                    if (!effCount.ContainsKey(e.Effect)) { effOrder.Add(e.Effect); effCount[e.Effect] = 0; effPot[e.Effect] = 0; }
                    effCount[e.Effect] += c;
                    effPot[e.Effect] += e.Potency * c;
                }
            }
        }
        string? effect = null;
        float vitality = 0;
        int time = 0, potency = 0;
        if (effOrder.Count == 1)
        {
            effect = effOrder[0];
            potency = effPot[effect];
            var ed = sys.Effects[effect];
            vitality = (float)((float)potency * (float)ed.Rate);
            if (ed.BaseTime > 0) time = enemyTime + 30 * total + ed.BaseTime * effCount[effect];
        }
        else if (effOrder.Count > 1)
        {
            var keys = effOrder.ToList();
            keys.Sort(string.CompareOrdinal); // JS default sort (UTF-16 code units)
            warnings.Add("Ingredients have different effects (" + string.Join(", ", keys) + "), so the effects cancel out.");
        }
        if (recipeId == FairyTonicId && effect != null)
        {
            effect = null; vitality = 0; time = 0;
            warnings.Add("Fairy Tonic never keeps an effect.");
        }
        if (recipeId == ElixirId && effect == null)
        {
            recipeId = DubiousFoodId;
            warnings.Add("An elixir needs exactly one effect; this one has none, so it fails.");
        }
        if (effect != null) vitality = Math.Min(vitality, (float)sys.Effects[effect].Max);

        if (recipeId == DubiousFoodId) // Dubious Food (cookFail)
        {
            int hpFail = Math.Max(sys.FailLifeRecover, (int)Math.Truncate(life * sys.FailLifeRecoverMultiplier));
            return new CookResult("Dubious Food", recipeId, hpFail, false, 0, null, 0, 0, 2, "no", 0, null, null, null, warnings);
        }
        if (recipeId == RockHardFoodId)
        {
            return new CookResult("Rock-Hard Food", recipeId, sys.StoneFoodLifeRecover, false, 0, null, 0, 0, 2, "no", 0, null, null, null, warnings);
        }

        double hpD = life * sys.LifeRecoverMultiplier;

        // --- critical chance (cookCalcCritBoost): reported, not rolled ---
        int critChance = uniq.Max(u => ing[u].CritBoost) + sys.CritRateByDistinctCount[uniq.Count - 1];
        string critical;
        if (hasExtract)
        {
            critical = "no";
            warnings.Add("Monster Extract replaces the critical roll with a random outcome: hearts may become +3 hearts or a quarter heart, the effect may rise or drop to level 1, and timed effects become 1, 10 or 30 minutes.");
        }
        else if (bloodMoon)
        {
            critical = "guaranteed";
        }
        else
        {
            critical = critChance >= 100 ? "guaranteed" : "possible";
        }

        // --- spice boost (cookCalcSpiceBoost): once per distinct non-monster CookSpice item ---
        foreach (var u in uniq)
        {
            var e = ing[u];
            if (e.HasTag("CookEnemy") || !e.HasTag("CookSpice")) continue;
            if (e.HpBoost > 0) hpD += e.HpBoost;
            if (e.TimeBoost > 0) time += e.TimeBoost;
        }
        // --- recipe boost ---
        hpD += entryHb;
        // --- adjust (cookAdjustItem) ---
        int hp = (int)Math.Truncate(hpD);
        hp = Math.Max(0, Math.Min(hp, sys.LifeRecover.Max));
        int level = 0, extra = 0;
        bool full = false;
        if (effect == null)
        {
            if (hp == 0) hp = 1;
        }
        else
        {
            float v = vitality;
            if (v > 0 && v < 1) v = 1;
            level = Math.Min((int)Math.Truncate(v), sys.Effects[effect].Max);
            if (effect == "hearty")
            {
                if (level % 4 != 0) level = (level + 4) & ~3;
                if (level < 4) level = 4;
                hp = level; full = true; extra = level / 4; // level is a multiple of 4 here
            }
        }
        time = Math.Max(0, Math.Min(time, 1800));

        // --- price (cookCalcItemPrice) ---
        int price;
        if (recipeId == FairyTonicId)
        {
            price = 2;
        }
        else
        {
            int sp = 0, mx = 0, n = 0;
            foreach (var u in uniq)
            {
                var e = ing[u]; int c = counts[u];
                n += c;
                if (e.HasTag("CookLowPrice")) { sp += c; mx += c; } else { sp += e.SellPrice * c; mx += e.BuyPrice * c; }
            }
            float mult = (float)sys.PriceMultiplierByCount[n - 1];
            sp = (int)MathF.Truncate((float)(mult * (float)sp));
            if (sp >= 1 && sp % 10 != 0) sp = sp + 10 - (sp % 10);
            price = Math.Max(Math.Min(mx, sp), 2);
        }

        string name = data.DishNameById.TryGetValue(recipeId, out var dn) ? dn : recipeId;
        if (recipeId == ElixirId) name = sys.ElixirNames[effect!];
        else if (effect != null) name = char.ToUpperInvariant(effect[0]) + effect.Substring(1) + " " + name;

        return new CookResult(name, recipeId, hp, full, extra, effect, level,
            IsTimedEffect(effect) ? time : 0, price, critical, critChance, potency,
            effect == "energizing" ? level / 5.0 : null,
            effect == "enduring" ? level / 5.0 : null,
            warnings);
    }
}
