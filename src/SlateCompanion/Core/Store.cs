using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SlateCompanion;

public sealed class AppSettings
{
    public List<string> SavageWindFolders { get; set; } = [];
    public List<string> CemuFolders { get; set; } = [];
    public List<string> CustomFolders { get; set; } = [];
    public string? SelectedSource { get; set; }
    public string? SelectedSlot { get; set; }      // null = newest slot
    public int LivePort { get; set; } = 7777;
    public bool LiveEnabled { get; set; } = true;
    public bool SearchedForSavageWind { get; set; }
    public string? GameFilesFolder { get; set; }   // a romfs (or a folder above it) to build the map and assets from
    public string GameLanguage { get; set; } = "USen"; // the game-text language pack to build from
    public bool SetupSeen { get; set; }             // the welcome (with quick tips) was completed once
    public string LastView { get; set; } = "home";
    public bool HideDone { get; set; }
    public List<string> MapLayersOff { get; set; } = ["memory"];
    public Dictionary<string, int> ArmorPlan { get; set; } = [];
}

/// <summary>
/// The app's own data: settings plus JSON documents (checklist ticks, pins, history). Nothing here touches a save.
/// Windows: %APPDATA%\SlateCompanion. Linux: $XDG_CONFIG_HOME/slatecompanion or ~/.config/slatecompanion.
/// </summary>
public sealed partial class Store
{
    public string Folder { get; }
    readonly Lock gate = new();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public Store()
    {
        var env = Environment.GetEnvironmentVariable("SLATECOMPANION_DATA");
        if (!string.IsNullOrWhiteSpace(env)) Folder = env;
        else if (OperatingSystem.IsWindows())
            Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SlateCompanion");
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            Folder = Path.Combine(!string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg) ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "slatecompanion");
        }
        Directory.CreateDirectory(Folder);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,40}$")]
    private static partial Regex KeyPattern();

    string PathFor(string key) => Path.Combine(Folder, key + ".json");

    public AppSettings LoadSettings()
    {
        lock (gate)
        {
            try
            {
                var p = PathFor("settings");
                if (File.Exists(p)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(p), Json) ?? new();
            }
            catch (Exception e) when (e is IOException or JsonException) { }
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings s) => WriteAtomic(PathFor("settings"), JsonSerializer.Serialize(s, Json));

    public T? Get<T>(string key)
    {
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException("bad key", nameof(key));
        lock (gate)
        {
            var p = PathFor(key);
            if (!File.Exists(p)) return default;
            try { return JsonSerializer.Deserialize<T>(File.ReadAllText(p), Json); }
            catch (Exception e) when (e is IOException or JsonException) { return default; }
        }
    }

    public void Put<T>(string key, T value)
    {
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException("bad key", nameof(key));
        WriteAtomic(PathFor(key), JsonSerializer.Serialize(value, Json));
    }

    void WriteAtomic(string path, string text)
    {
        lock (gate)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, overwrite: true);
        }
    }
}
