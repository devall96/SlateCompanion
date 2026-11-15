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

    /// <summary>Forget what was loaded (the game files were built again). Bitmaps aren't disposed: see <see cref="UI.Icons.Reset"/>.</summary>
    public static void Reset()
    {
        text = null; compendiumIndex = null; armorIndex = null; armorStars = null;
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

    /// <summary>The game's cooking pot (Item_CookSet), drawn from the game files: built with the rest, or by <see cref="EnsurePotAsync"/>.</summary>
    public static Bitmap? PotPicture() => !On ? null : Load(Path.Combine(Folder, "cook", "pot.png"), 760);

    static Task<bool>? potJob;

    /// <summary>Draws the pot picture if it's missing (an older build of the game assets): once, in the background.</summary>
    public static Task<bool> EnsurePotAsync()
    {
        if (!On) return Task.FromResult(false);
        var path = Path.Combine(Folder, "cook", "pot.png");
        if (File.Exists(path)) return Task.FromResult(true);
        var romfs = MapBuilder.FindRomfs(App.Progress.Settings.GameFilesFolder, App.Progress.Settings.SavageWindFolders);
        if (romfs == null) return Task.FromResult(false);
        return potJob ??= Task.Run(() => MapBuilder.RenderPot(romfs, path));
    }

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
        if (b == null) return null;  // (not cached: a picture that's drawn later, like the cooking pot, shows once it's there)
        bitmaps[key] = b;
        recent.AddFirst(key);
        // keep small icons around; let big pictures go after a handful (not disposed: a list may still show it, and
        // drawing a disposed bitmap throws; the GC frees it once nothing shows it)
        while (recent.Count > 600) { var old = recent.Last!.Value; recent.RemoveLast(); bitmaps.Remove(old); }
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
            // The base piece (lowest number) of each name, preferring one with an inventory picture. The jewelry has
            // none (the game draws those icons from their 3D models), so it still gets its actor for the game's text.
            armorIndex = [];
            var withIcon = new Dictionary<string, string>();
            foreach (var (k, v) in Texts().OrderBy(kv => kv.Key, StringComparer.Ordinal))
                if (k.StartsWith("Armor_", StringComparison.Ordinal) && k.EndsWith("_Name", StringComparison.Ordinal))
                {
                    var actor = k[..^5];
                    armorIndex.TryAdd(UI.Ui.Norm(v), actor);
                    if (File.Exists(Path.Combine(Folder, "items", actor + ".png"))) withIcon.TryAdd(UI.Ui.Norm(v), actor);
                }
            foreach (var (k, v) in withIcon) armorIndex[k] = v;
        }
        return armorIndex.GetValueOrDefault(UI.Ui.Norm(a.Name));
    }

    static Dictionary<string, int>? armorStars;

    /// <summary>
    /// An armor piece's upgrade level: each level is its own actor with the same name in the game (Armor_001_Head,
    /// then the higher numbers), so it's the piece's place among the actors of that name, lowest number first.
    /// </summary>
    public static int ArmorStars(string actor)
    {
        if (!On) return 0;
        if (armorStars == null)
        {
            armorStars = [];
            foreach (var g in Texts().Where(kv => kv.Key.StartsWith("Armor_", StringComparison.Ordinal) && kv.Key.EndsWith("_Name", StringComparison.Ordinal))
                         .GroupBy(kv => kv.Value))
            {
                int i = 0;
                foreach (var a in g.Select(kv => kv.Key[..^5]).OrderBy(a => a, StringComparer.Ordinal)) armorStars[a] = i++;
            }
        }
        return armorStars.GetValueOrDefault(actor);
    }

    static readonly Dictionary<string, string> GemColor = new()
    {
        ["diamond"] = "#E4F6FF", ["ruby"] = "#FF4D5E", ["sapphire"] = "#4C86FF", ["topaz"] = "#FFD23D", ["opal"] = "#F3DDFF", ["amber"] = "#FF9A3C",
    };
    const string GemPath = "M10 6h12l6 7-12 14L4 13z M4 13h24 M12 6l-2 7 6 14 6-14-2-7";

    /// <summary>A piece's inventory picture; for the jewelry, which has none in the game files, a gem in its colour.</summary>
    public static Bitmap? ArmorIcon(ArmorPiece a, int size = 40)
    {
        if (!On) return null;
        if (Icon(ArmorActor(a), size) is { } b) return b;
        var gem = GemColor.FirstOrDefault(g => a.Name.Contains(g.Key, StringComparison.OrdinalIgnoreCase));
        return gem.Key == null ? null : UI.Icons.Drawn(GemPath, Avalonia.Media.Color.Parse(gem.Value), 2.2f);
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
