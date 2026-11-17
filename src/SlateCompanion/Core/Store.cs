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
    public bool TrackWalkthrough { get; set; } = true;      // walkthrough chapters and steps tick themselves from the save
    public List<string> MapLayersOff { get; set; } = ["memory"];
    public Dictionary<string, int> ArmorPlan { get; set; } = [];
    public bool CheckUpdatesOnStart { get; set; } = true;  // one request to GitHub when the app opens (installed copies)
    public string? SkippedVersion { get; set; }              // "Skip this version" on the update bar
    public List<string> SavageWindPrograms { get; set; } = []; // savagewind / AppImages chosen by hand (Linux mostly)
    // The Play screen's settings: the screen is taken out for now, and these are kept (unused) so a player's choices
    // are still there if it comes back.
    public string? GameProgram { get; set; }                 // the copy Play starts
    public string? ClearSkiesProgram { get; set; }           // ClearSkies.exe or its AppImage
    public bool UseClearSkies { get; set; }
    public string? ClearSkiesPreset { get; set; }            // null: the one saved in clearskies.ini
    public bool UseLauncher { get; set; } = true;            // the Play screen (installs, updates, Savage Wind's settings); off: hidden
    public Dictionary<string, string> InstallNames { get; set; } = [];  // a name per copy (its program's path)
    public List<string> HiddenInstalls { get; set; } = [];   // copies removed from the list (never deleted)
    public string? InstallFolder { get; set; }               // where the last new copy was installed
    public bool MinimizeOnPlay { get; set; }
    public bool AddToApplications { get; set; } = true;     // Linux: a menu entry for Slate Companion
    public bool EquipEdits { get; set; }                     // Inventory: "Change equipment" (equip, take off, loadouts edit the save)
    public bool GpuDrawing { get; set; } = true;            // draw with the graphics card (takes effect at the next start)
    public bool LiveInBackground { get; set; }               // the map keeps following Link while another window has the focus                 // Play minimizes Slate Companion (it then sleeps)
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
        Folder = DefaultFolder();
        Directory.CreateDirectory(Folder);
    }

    /// <summary>Where the app keeps its own files (also its logs, before anything else is up).</summary>
    public static string DefaultFolder()
    {
        var env = Environment.GetEnvironmentVariable("SLATECOMPANION_DATA");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SlateCompanion");
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return Path.Combine(!string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg) ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "slatecompanion");
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
                if (!File.Exists(p)) return new AppSettings();
                try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(p), Json) ?? new(); }
                catch (JsonException e)
                {
                    // Kept aside before the defaults get written over it: the linked folders and names are in it.
                    var aside = p + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    File.Move(p, aside, true);
                    Log.Warn($"settings.json couldn't be read ({e.Message}); kept as {Path.GetFileName(aside)}, starting with defaults");
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn("settings.json couldn't be read: " + e.Message); }
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings s) { lock (gate) WriteAtomic(PathFor("settings"), JsonSerializer.Serialize(s, Json)); }

    /// <summary>A deep copy (through JSON, as it's stored).</summary>
    public AppSettings Clone(AppSettings s) => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, Json), Json) ?? new();

    public T? Get<T>(string key)
    {
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException("bad key", nameof(key));
        lock (gate)
        {
            var p = PathFor(key);
            if (!File.Exists(p)) return default;
            try { return JsonSerializer.Deserialize<T>(File.ReadAllText(p), Json); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { Log.Warn($"{key}.json couldn't be read: {e.Message}"); return default; }
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
            // a temp name of its own (two copies of the app share the folder), flushed before the rename so a
            // power cut can't leave an empty settings file
            var tmp = $"{path}.{Environment.ProcessId}.tmp";
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                f.Write(System.Text.Encoding.UTF8.GetBytes(text));
                f.Flush(true);
            }
            File.Move(tmp, path, overwrite: true);
        }
    }
}
