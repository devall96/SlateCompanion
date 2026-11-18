using System.Diagnostics;

namespace SlateCompanion;

/// <summary>
/// Clear Skies (github.com/devall96/ClearSkies): Cemu's Clarity look for Savage Wind, as a Vulkan layer its launcher
/// turns on for the game it starts, plus a mod manager. Slate Companion only starts its launcher with options
/// (--game, --preset, --play, --gamebanana); it never changes Clear Skies' files.
/// </summary>
public sealed record ClearSkies(string Program)
{
    public const string Repo = "devall96/ClearSkies";
    public const string Page = "https://devall96.github.io/ClearSkies/";
    public string Folder => Path.GetDirectoryName(Program)!;
    public bool AppImage => Program.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase);

    public static readonly (string id, string name)[] Presets =
    [
        ("serfrost", "Serfrost's Preset (Recommended)"), ("serfrost_dim", "Serfrost's Preset (Dim Display)"),
        ("serfrost_bright", "Serfrost's Preset (Bright Display)"), ("bruz", "Bruz's Preset"), ("cellhunter", "CellHunter's Preset"),
        ("jamie", "Jamie's Preset"), ("t3nka", "t3nka Neutral+"), ("complaining_gamer", "The Complaining Gamer's Preset"),
        ("viskclarity", "ViskClarity Preset"), ("contrasty", "Contrasty Preset"), ("filmic", "Filmic Preset"),
        ("sotw_warmer", "Survival of the Wild (Warmer)"), ("sotw_colder", "Survival of the Wild (Colder)"),
        ("godot_mexican", "Godot's Mexican Preset"), ("hexaae_clear", "Hexaae's Clear Preset"), ("maranbr", "MaranBr Preset (Little More Intense)"),
        ("yemaowuxin_bright", "YeMaoWuXin's Preset (Bright colors)"), ("yemaowuxin_vivid", "YeMaoWuXin's Preset (Vivid and clear)"),
        ("cobalt", "Cobalt's Preset (Saturated & Bright)"), ("custom", "User-Defined Preset (clearskies.ini)"),
    ];

    /// <summary>Its version: the exe's version resource on Windows, the AppImage's name on Linux.</summary>
    public string? Version
    {
        get
        {
            try
            {
                if (AppImage)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(Program), @"(\d+\.\d+\.\d+)");
                    return m.Success ? m.Groups[1].Value : null;
                }
                var v = FileVersionInfo.GetVersionInfo(Program);
                return v.FileMajorPart + v.FileMinorPart + v.FileBuildPart > 0 ? $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}" : null;
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>The preset saved in its clearskies.ini ([clarity] preset = …).</summary>
    public string? SavedPreset
    {
        get
        {
            try
            {
                var ini = Path.Combine(Folder, "clearskies.ini");
                if (!File.Exists(ini)) return null;
                foreach (var line in File.ReadLines(ini))
                {
                    var t = line.Trim();
                    if (t.StartsWith("preset", StringComparison.Ordinal) && t.Contains('='))
                        return t[(t.IndexOf('=') + 1)..].Split(';')[0].Trim();
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return null;
        }
    }

    public static bool IsProgram(string path)
    {
        var n = Path.GetFileName(path);
        return File.Exists(path) && (n.Equals("ClearSkies.exe", StringComparison.OrdinalIgnoreCase) ||
               n.StartsWith("ClearSkies", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A Clear Skies program in a folder (or the file itself).</summary>
    public static ClearSkies? At(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (IsProgram(path)) return new ClearSkies(path);
        if (!Directory.Exists(path)) return null;
        try
        {
            var f = Directory.EnumerateFiles(path).Where(IsProgram).OrderByDescending(x => x).FirstOrDefault();
            if (f != null) return new ClearSkies(f);
            var sub = Path.Combine(path, "ClearSkies");
            return Directory.Exists(sub) ? At(sub) : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Looks next to the Savage Wind folders and in the usual places, a couple of folders deep.</summary>
    public static ClearSkies? Find(AppSettings s)
    {
        if (At(s.ClearSkiesProgram) is { } saved) return saved;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var starts = new List<string>();
        foreach (var f in s.SavageWindFolders) { starts.Add(f); if (Path.GetDirectoryName(f) is { } up) starts.Add(up); }
        starts.AddRange(new[] { "Desktop", "Downloads", "Documents", "Games", "Applications", Path.Combine("OneDrive", "Desktop"), Path.Combine("OneDrive", "Documents") }.Select(d => Path.Combine(home, d)));
        if (OperatingSystem.IsWindows())
            foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)) starts.Add(d.RootDirectory.FullName);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var st in starts.Where(Directory.Exists))
        {
            if (!seen.Add(st)) continue;
            if (At(st) is { } here) return here;
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(st))
                    if (Path.GetFileName(sub).Contains("Clear", StringComparison.OrdinalIgnoreCase) && At(sub) is { } c) return c;
            }
            catch (Exception) { }
        }
        return null;
    }

    /// <summary>Its own window (the mod manager); with --gamebanana, straight to the GameBanana browser (Windows).</summary>
    public string? Open(bool gamebanana)
    {
        try
        {
            var psi = new ProcessStartInfo(Program) { WorkingDirectory = Folder, UseShellExecute = false };
            if (gamebanana && !AppImage) psi.ArgumentList.Add("--gamebanana");
            using var p = Process.Start(psi);
            return null;
        }
        catch (Exception e) { return e.Message; }
    }
}
