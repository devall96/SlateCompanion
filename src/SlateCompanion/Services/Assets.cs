using System.Text.Json;
using Avalonia.Media.Imaging;

namespace SlateCompanion;

/// <summary>
/// What was built from the player's own game files (MapBuilder): inventory icons, compendium photos, memory
/// pictures and the game's own text, looked up by the game's actor names. Everything is optional: with the setting off
/// or nothing built, every lookup returns null and the screens show the app's own text. Pictures are decoded at the
/// size they're shown and cached.
/// </summary>
public static class Assets
{
    static Dictionary<string, string>? text;
    static Dictionary<string, string>? compendiumIndex, armorIndex;
    static readonly Dictionary<(string, int), Bitmap?> bitmaps = [];
    static readonly LinkedList<(string, int)> recent = new();

    public static bool On => !Program.Demo && App.MapBuilder.Complete;
    static string Folder => App.MapBuilder.Folder;

    public static void Reset()
    {
        text = null; compendiumIndex = null; armorIndex = null;
        foreach (var b in bitmaps.Values) b?.Dispose();
        bitmaps.Clear(); recent.Clear();
    }

    static Dictionary<string, string> Texts()
    {
        if (text != null) return text;
        try { text = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Folder, "text.json"))) ?? []; }
        catch (Exception e) when (e is IOException or JsonException) { text = []; }
        return text;
    }

    public static string? Text(string label) => On && Texts().TryGetValue(label, out var t) && t.Length > 0 ? t : null;
    public static string? Name(string actor) => Text(actor + "_Name") ?? Text(actor + "_BaseName");
    public static string? Desc(string actor) => Text(actor + "_Desc");
    public static string? BookDesc(string actor) => Text(actor + "_PictureBook");
    public static (string? name, string? desc, string? finish) Quest(string id) => (Text($"QL_{id}_Name"), Text($"QL_{id}_Desc"), Text($"QL_{id}_Finish"));

    /// <summary>An inventory icon (items/&lt;actor&gt;.png), decoded at <paramref name="size"/> pixels.</summary>
    public static Bitmap? Icon(string? actor, int size = 40) => actor == null || !On ? null : Load(Path.Combine(Folder, "items", actor + ".png"), size);

    /// <summary>A compendium photo (pictures/&lt;actor&gt;.jpg), decoded at <paramref name="width"/> pixels wide.</summary>
    public static Bitmap? Picture(string? actor, int width = 360) => actor == null || !On ? null : Load(Path.Combine(Folder, "pictures", actor + ".jpg"), width);

    public static Bitmap? Album(int index, int width = 360) => !On ? null : Load(Path.Combine(Folder, "album", $"pict_{index:000}.jpg"), width);

    static Bitmap? Load(string path, int width)
    {
        var key = (path, width);
        if (bitmaps.TryGetValue(key, out var b)) { recent.Remove(key); recent.AddFirst(key); return b; }
        try
        {
            if (File.Exists(path)) { using var s = File.OpenRead(path); b = Bitmap.DecodeToWidth(s, width, BitmapInterpolationMode.HighQuality); }
        }
        catch (Exception) { b = null; }
        bitmaps[key] = b;
        recent.AddFirst(key);
        // keep small icons around; let big pictures go after a handful
        while (recent.Count > 600) { var old = recent.Last!.Value; recent.RemoveLast(); if (bitmaps.Remove(old, out var o)) o?.Dispose(); }
        return b;
    }

    /// <summary>The actor of a compendium entry: matched by the game's own name for each photo.</summary>
    public static string? CompendiumActor(CompendiumEntry e)
    {
        if (!On) return null;
        if (compendiumIndex == null)
        {
            compendiumIndex = [];
            var dir = Path.Combine(Folder, "pictures");
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.jpg").OrderBy(f => f))
                {
                    var actor = Path.GetFileNameWithoutExtension(f);
                    if (Name(actor) is string n) compendiumIndex.TryAdd(UI.Ui.Norm(n), actor);
                }
        }
        return compendiumIndex.GetValueOrDefault(UI.Ui.Norm(e.Name));
    }

    /// <summary>The actor of an armor piece (Armor_###_Head/Upper/Lower), matched by its in-game name.</summary>
    public static string? ArmorActor(ArmorPiece a)
    {
        if (!On) return null;
        if (armorIndex == null)
        {
            armorIndex = [];
            foreach (var (k, v) in Texts())
                if (k.StartsWith("Armor_", StringComparison.Ordinal) && k.EndsWith("_Name", StringComparison.Ordinal))
                {
                    var actor = k[..^5];
                    if (File.Exists(Path.Combine(Folder, "items", actor + ".png"))) armorIndex.TryAdd(UI.Ui.Norm(v), actor);
                }
        }
        return armorIndex.GetValueOrDefault(UI.Ui.Norm(a.Name));
    }

    /// <summary>The icon actor of a cooked result: elixirs have one per effect (Item_Cook_C_17_AttackUp …).</summary>
    public static string DishActor(string recipeId, string? effect)
    {
        if (recipeId != "Item_Cook_C_17" || effect == null) return recipeId;
        var suffix = effect switch
        {
            "hasty" => "AllSpeed", "mighty" => "AttackUp", "tough" => "DefenseUp", "enduring" => "ExGutsMaxUp", "fireproof" => "Fireproof",
            "energizing" => "GutsRecover", "hearty" => "LifeMaxUp", "sneaky" => "Quietness", "spicy" => "ResistCold", "chilly" => "ResistHot",
            "electro" => "ResistElectric", _ => null,
        };
        return suffix == null ? recipeId : $"{recipeId}_{suffix}";
    }
}
