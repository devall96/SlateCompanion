using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlateCompanion;

public sealed record Marker(string Id, string Type, string Name, double X, double Y, double Z, string? Flag, string? Notes);
public sealed record Korok(string Id, double X, double Y, double Z, string Flag, string? Kind);

public sealed record StatDef(string Key, string Label, string? Flag, string? Type, string? Unit);
public sealed record DefeatedDef(string Enemy, string? Actor, string? Flag, string? Type);
public sealed record Quest(string Id, string Name, string? Flag, string? Notes, string[]? Steps, string? Giver, string? Location,
    string? Reward, string? Shrine);
public sealed record Named(string Id, string Name, string? Flag);
public sealed record Flags(StatDef[] Stats, DefeatedDef[] Defeated, Quest[] MainQuests, Quest[] SideQuests, Quest[] ShrineQuests,
    Named[] DivineBeasts, Named[] Memories, DlcFlags? Dlc);
public sealed record DlcFlags(Named[]? Quests);

public sealed record CompendiumEntry(int Id, string Name, string Category,
    [property: JsonPropertyName("common_locations")] string[]? CommonLocations, string[]? Drops,
    [property: JsonPropertyName("cooking_effect")] string? CookingEffect,
    [property: JsonPropertyName("hearts_recovered")] double? HeartsRecovered,
    Dictionary<string, JsonElement>? Properties, string? Image);
public sealed record CompendiumFile(CompendiumEntry[] Entries);

public sealed record Material(string Name, int Count);
public sealed record ArmorUpgrade(int Level, double? Defense, Material[]? Materials, int Rupees);
public sealed record ArmorPiece(string Id, string Name, string? Set, string Slot, double? BaseDefense, bool Upgradeable, string? WhereFound,
    string? Price, SetBonus? SetBonus, string? Effect, ArmorUpgrade[]? Upgrades);
public sealed record SetBonus(string Name, int RequiresLevel);
public sealed record FairyCost(int Order, int Rupees);
public sealed record GreatFairies(string? Note, FairyCost[]? UnlockCostByOrder);
public sealed record ArmorFile(ArmorPiece[] Armor, GreatFairies? GreatFairies);

public sealed record Chapter(string Id, string Title, string? Recommended, string[]? Steps, string[]? Tips, Track? Track);
public sealed record Section(string Id, string Title, string[]? Body, Track? Track);
/// <summary>
/// How a walkthrough chapter follows the save: per step, save keys of which any one done ticks it (null: by hand
/// only); or, for a section without steps, keys that must all be done.
/// </summary>
public sealed record Track(string[]?[]? Steps, string[]? Done);
public sealed record DlcPart(string Title, Section[] Sections);
public sealed record Walkthrough(Chapter[] Chapters, Dictionary<string, DlcPart>? Dlc);
public sealed record ShrineGuide(string Name, string? Region, string? Trial, string? Solution, int? Chests, string? Quest);
public sealed record ShrineGuides(ShrineGuide[] Shrines);
public sealed record Tips(Section[] Sections);

/// <summary>The reference data that ships with the app (Data/), loaded once.</summary>
public sealed class GameData
{
    static readonly JsonSerializerOptions Opt = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public required Marker[] Markers { get; init; }
    public required Korok[] Koroks { get; init; }
    public required Flags Flags { get; init; }
    public required CompendiumEntry[] Compendium { get; init; }
    public required ArmorFile Armor { get; init; }
    public required Walkthrough Walkthrough { get; init; }
    public required ShrineGuides ShrineGuides { get; init; }
    public required Tips Tips { get; init; }
    public required string Folder { get; init; }
    SlateCompanion.Cooking.CookingData? cooking;
    public SlateCompanion.Cooking.CookingData Cooking => cooking ??= SlateCompanion.Cooking.CookingData.Load(Path.Combine(Folder, "ingredients.json"), Path.Combine(Folder, "recipes.json"));

    public static GameData Load(string folder)
    {
        T Read<T>(string rel) => JsonSerializer.Deserialize<T>(File.ReadAllBytes(Path.Combine(folder, rel)), Opt)
                                 ?? throw new InvalidDataException(rel);
        return new GameData
        {
            Folder = folder,
            Markers = Read<Marker[]>("map/markers.json"),
            Koroks = Read<Korok[]>("map/koroks.json"),
            Flags = Read<Flags>("flags.json"),
            Compendium = Read<CompendiumFile>("compendium.json").Entries.OrderBy(e => e.Id).ToArray(),
            Armor = Read<ArmorFile>("armor.json"),
            Walkthrough = Read<Walkthrough>("guides/walkthrough.json"),
            ShrineGuides = Read<ShrineGuides>("guides/shrines.json"),
            Tips = Read<Tips>("guides/tips.json"),
        };
    }

    public static string KindOf(string type) => type == "shrine_dlc" ? "shrine" : type;

    public static string PlaceLabel(string type) => type switch
    {
        "shrine" or "shrine_dlc" => "Shrine", "tower" => "Tower", "stable" => "Stable", "village" => "Village",
        "great_fairy" => "Great Fairy", "memory" => "Memory", "divine_beast" => "Divine Beast", "hestu" => "Hestu",
        "castle" => "Castle", "lab" => "Tech Lab", "landmark" => "Landmark", _ => "Place",
    };

    public static string TitleCase(string s) =>
        string.Join(' ', s.Split(' ').Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w));
}
