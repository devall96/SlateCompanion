// Slate Companion - self-test for CookingSimulator (port of app/wwwroot/data/cooking.test.mjs).
// Every case is a real in-game result recorded with a screenshot by savage13/cooking.rs (t/*.json, BSD-2-Clause).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SlateCompanion.Cooking;

public static class CookingSelfTest
{
    private sealed record Case(string[] Ingredients, string[] Names, (string Key, object? Value)[] Expect, string Source);

    private static (string, object?) E(string key, object? value) => (key, value);

    private static readonly Case[] Cases =
    {
        new(new[] { "Animal_Insect_F" }, new[] { "Fairy" },
            new[] { E("name", "Fairy Tonic"), E("sellPrice", 2), E("effect", null), E("hearts", 28) },
            "savage13/cooking.rs t/wkr.json (img/Fairy.jpg)"),
        new(new[] { "Animal_Insect_C", "Item_Enemy_05" }, new[] { "Cold Darner", "Lizalfos Tail" },
            new[] { E("name", "Chilly Elixir"), E("sellPrice", 60), E("effect", "chilly"), E("hearts", 0), E("level", 1), E("durationSec", 340) },
            "savage13/cooking.rs t/elixirs.json (img/Cold_Darner_Lizalfos_Tail.jpg)"),
        new(new[] { "Item_Fruit_C", "Item_Fruit_C" }, new[] { "Voltfruit", "Voltfruit" },
            new[] { E("name", "Simmered Fruit"), E("sellPrice", 20), E("effect", "electro"), E("hearts", 8), E("level", 1), E("durationSec", 300) },
            "savage13/cooking.rs t/other.json (img/Voltfruit2.jpg)"),
        new(new[] { "Item_Fruit_A", "Item_Fruit_D" }, new[] { "Apple", "Hearty Durian" },
            new[] { E("name", "Simmered Fruit"), E("sellPrice", 40), E("effect", "hearty"), E("extraHearts", 4), E("fullRestore", true) },
            "savage13/cooking.rs t/wkr.json (img/Apple_Hearty_Durian.jpg)"),
        new(new[] { "Item_PlantGet_B", "Item_PlantGet_C" }, new[] { "Hearty Radish", "Big Hearty Radish" },
            new[] { E("name", "Fried Wild Greens"), E("sellPrice", 50), E("effect", "hearty"), E("extraHearts", 8), E("fullRestore", true) },
            "savage13/cooking.rs t/wkr.json (img/Big_Hearty_Radish_Hearty_Radish.jpg)"),
        new(new[] { "Item_Material_05", "Item_Material_04", "Item_Material_07", "Item_Material_01", "BeeHome" },
            new[] { "Fresh Milk", "Bird Egg", "Tabantha Wheat", "Cane Sugar", "Courser Bee Honey" },
            new[] { E("name", "Honey Crepe"), E("sellPrice", 70), E("effect", "energizing"), E("hearts", 40), E("staminaWheels", 0.4) },
            "savage13/cooking.rs t/wkr.json (img/Bird_Egg_Cane_Sugar_Courser_Bee_Honey_Fresh_Milk_Tabantha_Wheat.jpg)"),
        new(new[] { "Item_PlantGet_Q", "Item_Material_03", "Item_Material_02" }, new[] { "Endura Carrot", "Hylian Rice", "Goron Spice" },
            new[] { E("name", "Vegetable Curry"), E("sellPrice", 80), E("effect", "enduring"), E("hearts", 24), E("extraStaminaWheels", 0.4) },
            "savage13/cooking.rs t/wkr.json (img/Endura_Carrot_Goron_Spice_Hylian_Rice.jpg)"),
        new(new[] { "Item_InsectGet_Z", "Item_InsectGet_Z" }, new[] { "Bright-Eyed Crab", "Bright-Eyed Crab" },
            new[] { E("name", "Seafood Skewer"), E("sellPrice", 40), E("effect", "energizing"), E("hearts", 16), E("staminaWheels", 1) },
            "savage13/cooking.rs t/wkr.json (img/Bright-Eyed_Crab2.jpg)"),
        new(new[] { "Animal_Insect_C", "Item_Enemy_30", "Item_Chilled_05" }, new[] { "Cold Darner", "Ancient Core", "Frozen Bird Thigh" },
            new[] { E("name", "Chilly Elixir"), E("sellPrice", 230), E("effect", "chilly"), E("hearts", 12), E("level", 1), E("durationSec", 490) },
            "savage13/cooking.rs t/ist.json (img/Ancient_Core_Cold_Darner_Frozen_Bird_Thigh.jpg)"),
        new(new[] { "Item_Mushroom_N", "Item_PlantGet_Q", "Animal_Insect_F", "Item_Enemy_29", "Item_Roast_19" },
            new[] { "Big Hearty Truffle", "Endura Carrot", "Fairy", "Ancient Shaft", "Roasted Big Radish" },
            new[] { E("name", "Fairy Tonic"), E("sellPrice", 2), E("effect", null), E("hearts", 116) },
            "savage13/cooking.rs t/ist.json (img/Ancient_Shaft_Big_Hearty_Truffle_Endura_Carrot_Fairy_Roasted_Big_Radish.jpg)"),
        new(new[] { "Item_FishGet_K" }, new[] { "Hearty Blueshell Snail" },
            new[] { E("name", "Seafood Skewer"), E("sellPrice", 30), E("effect", "hearty"), E("extraHearts", 3), E("fullRestore", true) },
            "savage13/cooking.rs t/wkr.json (img/Hearty_Blueshell_Snail.jpg)"),
        new(new[] { "Item_Fruit_A", "Item_Fruit_D", "Item_Material_07", "Item_Material_01" },
            new[] { "Apple", "Hearty Durian", "Tabantha Wheat", "Cane Sugar" },
            new[] { E("name", "Fruitcake"), E("sellPrice", 60), E("effect", "hearty"), E("extraHearts", 4), E("fullRestore", true) },
            "savage13/cooking.rs t/wkr.json (img/Apple_Cane_Sugar_Hearty_Durian_Tabantha_Wheat.jpg)"),
        new(new[] { "Animal_Insect_F", "Animal_Insect_Q", "Item_Enemy_16", "Obj_FireWoodBundle" },
            new[] { "Fairy", "Summerwing Butterfly", "Yellow Chuchu Jelly", "Wood" },
            new[] { E("name", "Fairy Tonic"), E("sellPrice", 2), E("effect", null), E("hearts", 28) },
            "savage13/cooking.rs t/wkr.json (img/Fairy_Summerwing_Butterfly_Wood_Yellow_Chuchu_Jelly.jpg)"),
        new(new[] { "Obj_FireWoodBundle" }, new[] { "Wood" },
            new[] { E("name", "Rock-Hard Food"), E("sellPrice", 2), E("effect", null), E("hearts", 1) },
            "savage13/cooking.rs t/wkr.json (img/Wood.jpg)"),
        new(new[] { "Animal_Insect_Q" }, new[] { "Summerwing Butterfly" },
            new[] { E("name", "Dubious Food"), E("sellPrice", 2), E("effect", null), E("hearts", 4) },
            "savage13/cooking.rs t/wkr.json (img/Summerwing_Butterfly.jpg)"),
        new(new[] { "Item_PlantGet_B", "Item_PlantGet_C", "Item_PlantGet_Q", "Item_PlantGet_M" },
            new[] { "Hearty Radish", "Big Hearty Radish", "Endura Carrot", "Swift Carrot" },
            new[] { E("name", "Copious Fried Wild Greens"), E("sellPrice", 140), E("effect", null), E("hearts", 72) },
            "savage13/cooking.rs t/wkr.json (img/Big_Hearty_Radish_Endura_Carrot_Hearty_Radish_Swift_Carrot.jpg)"),
        new(new[] { "Item_Fruit_K", "Item_Fruit_L" }, new[] { "Acorn", "Chickaloo Tree Nut" },
            new[] { E("name", "Sautéed Nuts"), E("sellPrice", 10), E("effect", null), E("hearts", 8) },
            "savage13/cooking.rs t/wkr.json (img/Acorn_Chickaloo_Tree_Nut.jpg)"),
        new(new[] { "Item_Fruit_J", "Item_Meat_12" }, new[] { "Fortified Pumpkin", "Raw Whole Bird" },
            new[] { E("name", "Meat-Stuffed Pumpkin"), E("sellPrice", 80), E("effect", "tough"), E("hearts", 28), E("level", 1), E("durationSec", 80) },
            "savage13/cooking.rs t/wkr.json (img/Fortified_Pumpkin_Raw_Whole_Bird.jpg)"),
        new(new[] { "Item_Ore_H", "Item_Material_06", "Item_Material_03", "Item_FishGet_K", "Item_FishGet_G" },
            new[] { "Rock Salt", "Goat Butter", "Hylian Rice", "Hearty Blueshell Snail", "Armored Porgy" },
            new[] { E("name", "Seafood Paella"), E("sellPrice", 100), E("effect", null), E("hearts", 48) },
            "savage13/cooking.rs t/wkr.json (img/Armored_Porgy_Goat_Butter_Hearty_Blueshell_Snail_Hylian_Rice_Rock_Salt.jpg)"),
        new(new[] { "Item_Enemy_00", "Animal_Insect_AB" }, new[] { "Bokoblin Horn", "Smotherwing Butterfly" },
            new[] { E("name", "Fireproof Elixir"), E("sellPrice", 10), E("effect", "fireproof"), E("hearts", 0), E("level", 1), E("durationSec", 220) },
            "savage13/cooking.rs t/other.json (img/Bokoblin_Horn_Smotherwing_Butterfly.jpg)"),
        new(new[] { "Animal_Insect_Q", "Item_Enemy_16" }, new[] { "Summerwing Butterfly", "Yellow Chuchu Jelly" },
            new[] { E("name", "Spicy Elixir"), E("sellPrice", 30), E("effect", "spicy"), E("hearts", 0), E("level", 1), E("durationSec", 260) },
            "savage13/cooking.rs t/wkr.json (img/Summerwing_Butterfly_Yellow_Chuchu_Jelly.jpg)"),
        new(new[] { "Item_Meat_11", "Item_Material_03", "Item_Material_02" }, new[] { "Raw Gourmet Meat", "Hylian Rice", "Goron Spice" },
            new[] { E("name", "Gourmet Meat Curry"), E("sellPrice", 90), E("effect", null), E("hearts", 32) },
            "savage13/cooking.rs t/wkr.json (img/Goron_Spice_Hylian_Rice_Raw_Gourmet_Meat.jpg)"),
        new(new[] { "Item_PlantGet_J" }, new[] { "Silent Princess" },
            new[] { E("name", "Fried Wild Greens"), E("sellPrice", 20), E("effect", "sneaky"), E("hearts", 8), E("level", 1), E("durationSec", 120) },
            "savage13/cooking.rs t/quietness.json (no image)"),
        new(new[] { "Item_FishGet_F", "Item_InsectGet_K" }, new[] { "Mighty Porgy", "Razorclaw Crab" },
            new[] { E("name", "Seafood Skewer"), E("sellPrice", 40), E("effect", "mighty"), E("hearts", 16), E("level", 2), E("durationSec", 100) },
            "savage13/cooking.rs t/wkr.json (img/Mighty_Porgy_Razorclaw_Crab.jpg)"),
    };

    public static int CaseCount => Cases.Length;

    // Dish name without the effect adjective ("Electro Simmered Fruit" -> "Simmered Fruit"); elixirs keep theirs.
    private static string BaseName(CookResult r)
    {
        if (r.RecipeId == CookingSimulator.ElixirId || r.Effect == null) return r.Name;
        return r.Name.Substring(r.Name.IndexOf(' ') + 1);
    }

    private static object? Field(CookResult r, string key) => key switch
    {
        "name" => BaseName(r),
        "sellPrice" => r.SellPrice,
        "effect" => r.Effect,
        "hearts" => r.Hearts,
        "level" => r.Level,
        "durationSec" => r.DurationSec,
        "extraHearts" => r.ExtraHearts,
        "fullRestore" => r.FullRestore,
        "staminaWheels" => r.StaminaWheels,
        "extraStaminaWheels" => r.ExtraStaminaWheels,
        _ => throw new ArgumentException("unknown field " + key),
    };

    private static string Show(object? v) => v switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "null",
    };

    public static (int passed, int failed, List<string> failures) Run(CookingData data)
    {
        int passed = 0, failed = 0;
        var failures = new List<string>();
        foreach (var c in Cases)
        {
            var bad = new List<string>();
            CookResult? r = null;
            try { r = CookingSimulator.Cook(c.Ingredients, data); }
            catch (Exception ex) { bad.Add("threw " + ex.Message); }
            if (r != null)
            {
                foreach (var (key, want) in c.Expect)
                {
                    var got = Field(r, key);
                    bool ok = want switch
                    {
                        // numbers: missing counts as 0, compared within 1e-6 (like the JS test)
                        int or double => Math.Abs(Convert.ToDouble(got ?? 0, CultureInfo.InvariantCulture)
                                                  - Convert.ToDouble(want, CultureInfo.InvariantCulture)) < 1e-6,
                        _ => Equals(got, want),
                    };
                    if (!ok) bad.Add($"{key}: {Show(got)} != {Show(want)}");
                }
            }
            if (bad.Count == 0) passed++;
            else
            {
                failed++;
                failures.Add(string.Join(" + ", c.Names) + " -> " + (r?.Name ?? "(error)") + ": " + string.Join("; ", bad));
            }
        }
        return (passed, failed, failures);
    }
}
