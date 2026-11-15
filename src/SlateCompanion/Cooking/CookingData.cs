// Slate Companion - BOTW cooking data model and loader.
// Reads app/wwwroot/data/ingredients.json and recipes.json (see cooking-rules.md for units and sources).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlateCompanion.Cooking;

/// <summary>One cookable material from ingredients.json. Hearts are quarter hearts.</summary>
public sealed class Ingredient
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    /// <summary>Raw heal in quarter hearts (cooking doubles it).</summary>
    public int Hearts { get; init; }
    /// <summary>Effect key (chilly, hearty, ...) or null.</summary>
    public string? Effect { get; init; }
    public int Potency { get; init; }
    public int TimeBoost { get; init; }
    public int HpBoost { get; init; }
    public int CritBoost { get; init; }
    public int SellPrice { get; init; }
    public int BuyPrice { get; init; }
    public List<string> Tags { get; init; } = new();
    public string WhereFound { get; init; } = "";
    /// <summary>"roasted", "frozen", ... when this item is a variant of another; otherwise null.</summary>
    public string? Variant { get; init; }
    public List<string>? VariantOf { get; init; }
    /// <summary>Id of the roasted / frozen / boiled form of this item, if any.</summary>
    public string? Roast { get; init; }
    public string? Frozen { get; init; }
    public string? Boiled { get; init; }

    public bool HasTag(string tag) => Tags.Contains(tag);
}

/// <summary>An entry of the game's Recipes list: every actor group and tag group is one slot.</summary>
public sealed class MultiRecipeEntry
{
    public int Index { get; init; }
    public string Result { get; init; } = "";
    /// <summary>Heart bonus in quarter hearts (may be negative).</summary>
    public int Hb { get; init; }
    public List<List<string>> Actors { get; init; } = new();
    public List<List<string>> Tags { get; init; } = new();
}

/// <summary>An entry of the game's SingleRecipes list.</summary>
public sealed class SingleRecipeEntry
{
    public int Index { get; init; }
    public string Result { get; init; } = "";
    public int Hb { get; init; }
    public List<string> Actors { get; init; } = new();
    public List<string> Tags { get; init; } = new();
}

public sealed class RecipeTable
{
    public List<MultiRecipeEntry> Recipes { get; init; } = new();
    public List<SingleRecipeEntry> SingleRecipes { get; init; } = new();
}

public sealed class DishSlot
{
    public List<string>? AnyOf { get; init; }
    public List<string>? AnyOfTags { get; init; }
    public int Min { get; init; }
    /// <summary>Only on elixir variants: the effect the critters must carry.</summary>
    public string? Effect { get; init; }
}

public sealed class DishRequirement
{
    /// <summary>"Recipes" or "SingleRecipes".</summary>
    public string Table { get; init; } = "";
    public int Index { get; init; }
    public int HeartBonus { get; init; }
    public bool OnlyIngredient { get; init; }
    public List<DishSlot> Slots { get; init; } = new();
}

public sealed class Dish
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>food, elixir, tonic, rock-hard or dubious.</summary>
    public string Kind { get; init; } = "";
    public List<DishRequirement> Requires { get; init; } = new();
    public string Notes { get; init; } = "";
    /// <summary>Elixir variants only (e.g. Item_Cook_C_17#chilly): the real result id and effect.</summary>
    public string? BaseId { get; init; }
    public string? Effect { get; init; }
}

public sealed class EffectDef
{
    public string GameName { get; init; } = "";
    public int BaseTime { get; init; }
    /// <summary>Stored as the exact float32 value written as a double; use (float)Rate.</summary>
    public double Rate { get; init; }
    public int Max { get; init; }
    public int? Min { get; init; }
    public int CritAmount { get; init; }
}

public sealed class LifeRecoverDef
{
    public int Max { get; init; }
    public int CritAmount { get; init; }
}

/// <summary>The CookData System block.</summary>
public sealed class CookingSystem
{
    public double LifeRecoverMultiplier { get; init; }
    public int FailLifeRecover { get; init; }
    public double FailLifeRecoverMultiplier { get; init; }
    public int StoneFoodLifeRecover { get; init; }
    public List<double> PriceMultiplierByCount { get; init; } = new();
    public List<int> CritRateByDistinctCount { get; init; } = new();
    public int CritExtraTime { get; init; }
    public Dictionary<string, EffectDef> Effects { get; init; } = new();
    public LifeRecoverDef LifeRecover { get; init; } = new();
    public Dictionary<string, string> ElixirNames { get; init; } = new();
}

public sealed class CookingData
{
    public IReadOnlyList<Ingredient> Ingredients { get; }
    public RecipeTable Table { get; }
    public IReadOnlyList<Dish> Dishes { get; }
    public CookingSystem System { get; }

    /// <summary>Ingredient by id (later duplicates win, like the JS Map).</summary>
    public IReadOnlyDictionary<string, Ingredient> IngredientById { get; }
    /// <summary>Dish name by dish id.</summary>
    public IReadOnlyDictionary<string, string> DishNameById { get; }

    public CookingData(IReadOnlyList<Ingredient> ingredients, RecipeTable table, IReadOnlyList<Dish> dishes, CookingSystem system)
    {
        Ingredients = ingredients;
        Table = table;
        Dishes = dishes;
        System = system;
        var ing = new Dictionary<string, Ingredient>(StringComparer.Ordinal);
        foreach (var e in ingredients) ing[e.Id] = e;
        IngredientById = ing;
        var dish = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var d in dishes) dish[d.Id] = d.Name;
        DishNameById = dish;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class IngredientsFile
    {
        public List<Ingredient> Ingredients { get; init; } = new();
    }

    private sealed class RecipesFile
    {
        public RecipeTable Table { get; init; } = new();
        public List<Dish> Dishes { get; init; } = new();
        public CookingSystem System { get; init; } = new();
    }

    public static CookingData Load(string ingredientsJsonPath, string recipesJsonPath)
    {
        var ingFile = JsonSerializer.Deserialize<IngredientsFile>(File.ReadAllText(ingredientsJsonPath), Options)
            ?? throw new InvalidDataException("ingredients.json is empty");
        var recFile = JsonSerializer.Deserialize<RecipesFile>(File.ReadAllText(recipesJsonPath), Options)
            ?? throw new InvalidDataException("recipes.json is empty");
        return new CookingData(ingFile.Ingredients, recFile.Table, recFile.Dishes, recFile.System);
    }
}
