namespace SlateCompanion;

/// <summary>A folder that holds numbered save slots (0, 1, ...), each with a game_data.sav.</summary>
public sealed record SaveSource(string Id, string Kind, string Label, string Folder, string? Note = null);

public sealed record SlotInfo(string Slot, string Path, DateTime Modified, long Size);

/// <summary>Finds Breath of the Wild saves from Savage Wind, Cemu, Ryujinx and Yuzu-family emulators.</summary>
public static class SaveLocator
{
    const string SwitchTitle = "01007EF00011E000";
    static readonly string[] WiiUTitles = ["101C9400", "101C9500", "101C9300"]; // US, EU, JP

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static List<SaveSource> Find(AppSettings settings)
    {
        var found = new List<SaveSource>();
        foreach (var root in SavageWindRoots(settings)) AddSavageWind(root, found);
        AddCemu(settings, found);
        AddRyujinx(found);
        AddYuzuFamily(found);
        foreach (var custom in settings.CustomFolders)
        {
            var slots = FindProfileFolder(custom);
            if (slots != null) found.Add(new SaveSource("custom:" + custom, "custom", "Custom folder", slots, custom));
        }
        // Drop duplicates (same folder found two ways).
        return found.GroupBy(s => Path.GetFullPath(s.Folder).TrimEnd(Path.DirectorySeparatorChar), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First()).ToList();
    }

    // ---------------------------------------------------------------- Savage Wind

    /// <summary>
    /// Savage Wind's data folder: SAVAGEWIND_DATA_ROOT, else next to savagewind.exe on Windows, else
    /// $XDG_DATA_HOME/savagewind or ~/.local/share/savagewind on Linux. See its docs/data-folder.md.
    /// </summary>
    static IEnumerable<string> SavageWindRoots(AppSettings settings)
    {
        var roots = new List<string>();
        var env = Environment.GetEnvironmentVariable("SAVAGEWIND_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(env) && Path.IsPathRooted(env)) roots.Add(env);
        roots.AddRange(settings.SavageWindFolders);
        if (OperatingSystem.IsMacOS())
            roots.Add(Path.Combine(Home, "Library", "Application Support", "local.savagewind"));
        else if (!OperatingSystem.IsWindows())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            roots.Add(!string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg)
                ? Path.Combine(xdg, "savagewind")
                : Path.Combine(Home, ".local", "share", "savagewind"));
        }
        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static bool LooksLikeSavageWind(string folder) =>
        Directory.Exists(Path.Combine(folder, "save", "save_0")) ||
        File.Exists(Path.Combine(folder, "savagewind.exe")) ||
        File.Exists(Path.Combine(folder, "savagewind.ini"));

    static void AddSavageWind(string root, List<SaveSource> found)
    {
        string? active = null;
        var marker = Path.Combine(root, "save_profile.txt");
        if (File.Exists(marker))
        {
            try { active = File.ReadAllText(marker).Trim(); } catch (IOException) { }
            if (string.IsNullOrEmpty(active)) active = null;
        }
        // Test override: SAVAGEWIND_SAVE_DIR replaces the save folder and switches profiles off.
        var overrideDir = Environment.GetEnvironmentVariable("SAVAGEWIND_SAVE_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir) && Directory.Exists(Path.Combine(overrideDir, "save_0")))
            found.Add(new SaveSource("sw:override", "savagewind", "Savage Wind (SAVAGEWIND_SAVE_DIR)", Path.Combine(overrideDir, "save_0")));

        var def = Path.Combine(root, "save", "save_0");
        bool defaultActive = active == null || active.Equals("Default", StringComparison.OrdinalIgnoreCase);
        if (Directory.Exists(def))
            found.Add(new SaveSource("sw:" + root + ":Default", "savagewind",
                "Savage Wind — Default" + (defaultActive ? " (in use)" : ""), def, root));

        var profiles = Path.Combine(root, "save-profiles");
        if (Directory.Exists(profiles))
        {
            foreach (var dir in SafeDirs(profiles))
            {
                var name = Path.GetFileName(dir);
                var slots = Path.Combine(dir, "save", "save_0");
                if (!Directory.Exists(slots)) continue;
                bool inUse = active != null && name.Equals(active, StringComparison.OrdinalIgnoreCase);
                found.Add(new SaveSource("sw:" + root + ":" + name, "savagewind",
                    "Savage Wind — " + name + (inUse ? " (in use)" : ""), slots, root));
            }
        }
        // Put the profile in use first.
        found.Sort((a, b) => (b.Label.EndsWith("(in use)") ? 1 : 0) - (a.Label.EndsWith("(in use)") ? 1 : 0));
    }

    /// <summary>Looks for savagewind.exe in the usual places, a few levels down, within a time budget.</summary>
    public static List<string> SearchSavageWind(TimeSpan budget)
    {
        var results = new List<string>();
        var starts = new List<string>
        {
            Path.Combine(Home, "Desktop"), Path.Combine(Home, "Downloads"), Path.Combine(Home, "Documents"),
            Path.Combine(Home, "OneDrive", "Desktop"), Path.Combine(Home, "OneDrive", "Documents"),
            Path.Combine(Home, "Games"), LocalAppData, Path.Combine(Home, "Applications"),
        };
        if (OperatingSystem.IsWindows())
            foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                starts.Add(Path.Combine(d.RootDirectory.FullName, "Games"));
                starts.Add(d.RootDirectory.FullName);
            }
        var deadline = DateTime.UtcNow + budget;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in starts.Where(Directory.Exists))
            Walk(s, 0);
        return results;

        void Walk(string dir, int depth)
        {
            if (DateTime.UtcNow > deadline || depth > 4 || !seen.Add(dir)) return;
            try
            {
                if (File.Exists(Path.Combine(dir, "savagewind.exe"))) { results.Add(dir); return; }
                var name = Path.GetFileName(dir);
                if (name.StartsWith('.') || name is "node_modules" or "Windows" or "Program Files" or "Program Files (x86)" or "$Recycle.Bin" or "ProgramData")
                    return;
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var info = new DirectoryInfo(sub);
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    Walk(sub, depth + 1);
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException) { }
        }
    }

    // ---------------------------------------------------------------- Emulators

    static void AddCemu(AppSettings settings, List<SaveSource> found)
    {
        var mlcs = new List<string>(settings.CemuFolders.Select(f => Directory.Exists(Path.Combine(f, "mlc01")) ? Path.Combine(f, "mlc01") : f));
        if (OperatingSystem.IsWindows()) mlcs.Add(Path.Combine(AppData, "Cemu", "mlc01"));
        else
        {
            mlcs.Add(Path.Combine(Home, ".local", "share", "Cemu", "mlc01"));
            mlcs.Add(Path.Combine(Home, ".var", "app", "info.cemu.Cemu", "data", "Cemu", "mlc01"));
        }
        foreach (var mlc in mlcs.Where(Directory.Exists))
            foreach (var title in WiiUTitles)
            {
                var userRoot = Path.Combine(mlc, "usr", "save", "00050000", title, "user");
                if (!Directory.Exists(userRoot)) continue;
                foreach (var user in SafeDirs(userRoot))
                    if (HasSlots(user))
                        found.Add(new SaveSource("cemu:" + user, "cemu", "Cemu (Wii U " + Region(title) + ")", user, mlc));
            }
    }

    static string Region(string t) => t switch { "101C9400" => "US", "101C9500" => "EU", _ => "JP" };

    static void AddRyujinx(List<SaveSource> found)
    {
        var roots = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(AppData, "Ryujinx") }
            : new[] { Path.Combine(Home, ".config", "Ryujinx"), Path.Combine(Home, ".var", "app", "org.ryujinx.Ryujinx", "config", "Ryujinx"), Path.Combine(Home, ".var", "app", "io.github.ryubing.Ryujinx", "config", "Ryujinx") };
        foreach (var root in roots)
        {
            var saveRoot = Path.Combine(root, "bis", "user", "save");
            if (!Directory.Exists(saveRoot)) continue;
            // Ryujinx stores each game's save in a numbered folder; the title ID is only in its imkvdb, so we
            // recognise BOTW by its layout: 0/<slot>/game_data.sav plus 0/option.sav.
            foreach (var dir in SafeDirs(saveRoot))
            {
                var user0 = Path.Combine(dir, "0");
                if (HasSlots(user0) && File.Exists(Path.Combine(user0, "option.sav")))
                    found.Add(new SaveSource("ryujinx:" + user0, "ryujinx", "Ryujinx", user0, root));
            }
        }
    }

    static void AddYuzuFamily(List<SaveSource> found)
    {
        string[] names = ["yuzu", "suyu", "sudachi", "citron", "eden", "torzu"];
        foreach (var n in names)
        {
            var bases = OperatingSystem.IsWindows()
                ? new[] { Path.Combine(AppData, n) }
                : new[] { Path.Combine(Home, ".local", "share", n) };
            foreach (var b in bases)
            {
                var saveRoot = Path.Combine(b, "nand", "user", "save", "0000000000000000");
                if (!Directory.Exists(saveRoot)) continue;
                foreach (var user in SafeDirs(saveRoot))
                {
                    var game = Path.Combine(user, SwitchTitle);
                    if (HasSlots(game))
                        found.Add(new SaveSource(n + ":" + game, "yuzu", char.ToUpper(n[0]) + n[1..], game, b));
                }
            }
        }
    }

    // ---------------------------------------------------------------- Slots

    public static bool HasSlots(string folder) => ListSlots(folder).Count > 0;

    public static List<SlotInfo> ListSlots(string folder)
    {
        var list = new List<SlotInfo>();
        if (!Directory.Exists(folder)) return list;
        foreach (var dir in SafeDirs(folder))
        {
            var name = Path.GetFileName(dir);
            if (!int.TryParse(name, out _)) continue;
            var f = Path.Combine(dir, "game_data.sav");
            if (!File.Exists(f)) continue;
            var fi = new FileInfo(f);
            list.Add(new SlotInfo(name, dir, fi.LastWriteTimeUtc, fi.Length));
        }
        return list.OrderByDescending(s => s.Modified).ToList();
    }

    /// <summary>For a folder the player picked: finds the folder with numbered slots at or below it.</summary>
    public static string? FindProfileFolder(string start, int maxDepth = 5)
    {
        if (!Directory.Exists(start)) return null;
        if (HasSlots(start)) return start;
        // A slot folder itself was picked.
        if (File.Exists(Path.Combine(start, "game_data.sav")))
        {
            var parent = Path.GetDirectoryName(start);
            if (parent != null && HasSlots(parent)) return parent;
        }
        var queue = new Queue<(string, int)>();
        queue.Enqueue((start, 0));
        int visited = 0;
        while (queue.Count > 0 && visited < 20000)
        {
            var (dir, depth) = queue.Dequeue();
            visited++;
            if (HasSlots(dir)) return dir;
            if (depth >= maxDepth) continue;
            foreach (var sub in SafeDirs(dir)) queue.Enqueue((sub, depth + 1));
        }
        return null;
    }

    static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).ToList(); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return []; }
    }
}
