using System.Diagnostics;
using System.Text.Json.Nodes;

namespace SlateCompanion;

/// <summary>One copy of Savage Wind: its program, its data folder (savagewind.ini, extracted/, save/) and version.</summary>
public sealed record SwInstall(string Exe, string DataRoot)
{
    public string Folder => Path.GetDirectoryName(Exe)!;
    public bool AppImage => Exe.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase);

    /// <summary>The release's version (sw-manifest.txt, or the AppImage's name); null for a source build.</summary>
    public string? Version
    {
        get
        {
            if (AppImage)
            {
                // An AppImage keeps its file name when Savage Wind updates it, so the name can be an
                // older version than what's inside. What Savage Wind says at each start ("[savagewind] Savage Wind
                // <version> (...)" at the top of savagewind.log) is the truth, when that log is newer than the file.
                if (VersionFromLog() is { } logged) return logged;
                var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(Exe), @"(\d+\.\d+\.\d+[^-_]*(?:-alpha\.\d+|-beta\.\d+)?)");
                return m.Success ? m.Groups[1].Value : null;
            }
            try
            {
                foreach (var line in File.ReadLines(Path.Combine(Folder, "sw-manifest.txt")).Take(5))
                    if (line.StartsWith("version ", StringComparison.Ordinal)) return line[8..].Trim();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }
    }

    /// <summary>The version the last start of this program logged (savagewind.log in the data folder), if it's from this file.</summary>
    string? VersionFromLog()
    {
        try
        {
            foreach (var name in new[] { "savagewind.log", "savagewind.prev.log" })
            {
                var log = Path.Combine(DataRoot, name);
                if (!File.Exists(log) || File.GetLastWriteTimeUtc(log) < File.GetLastWriteTimeUtc(Exe)) continue;
                using var f = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var r = new StreamReader(f);
                for (int i = 0; i < 60 && r.ReadLine() is { } line; i++)
                {
                    int at = line.IndexOf("[savagewind] Savage Wind ", StringComparison.Ordinal);
                    if (at < 0) continue;
                    var v = line[(at + 25)..].Split(' ')[0].Trim();
                    if (v.Length > 0 && char.IsDigit(v[0])) return v;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    public string Label => (Version is { } v ? $"Savage Wind {v}" : "Savage Wind (built from source)") + " · " + UI.Ui.ShortPath(Folder);
}

/// <summary>
/// Starts Savage Wind the normal way (it decides whether to show its Configuration screen), with or without Clear
/// Skies, and finds the copies on this computer. Slate Companion never installs, updates or changes Savage Wind.
/// </summary>
public static class Launcher
{
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static List<SwInstall> FindInstalls(AppSettings s)
    {
        var list = new List<SwInstall>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string exe, string? dataRoot = null)
        {
            if (!File.Exists(exe) || !seen.Add(Path.GetFullPath(exe))) return;
            list.Add(new SwInstall(exe, dataRoot ?? DataRootFor(exe)));
        }
        if (OperatingSystem.IsWindows())
        {
            foreach (var f in s.SavageWindFolders) Add(Path.Combine(f, "savagewind.exe"));
        }
        else
        {
            foreach (var f in s.SavageWindFolders.Where(Directory.Exists))
                foreach (var e in SafeFiles(f)) Add(e);
            foreach (var d in new[] { Path.Combine(Home, "Applications"), Path.Combine(Home, "Downloads"), Path.Combine(Home, "Games"), Path.Combine(Home, "Desktop"), Path.Combine(Home, ".local", "bin") })
                if (Directory.Exists(d)) foreach (var e in SafeFiles(d)) Add(e);
        }
        foreach (var e in s.SavageWindPrograms) Add(e);
        list.RemoveAll(i => s.HiddenInstalls.Contains(i.Exe, StringComparer.OrdinalIgnoreCase));
        return list;

        static IEnumerable<string> SafeFiles(string dir)
        {
            try
            {
                return Directory.EnumerateFiles(dir).Where(f =>
                {
                    var n = Path.GetFileName(f);
                    return n == "savagewind" || n.StartsWith("Savage_Wind", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase);
                }).ToList();
            }
            catch (Exception) { return []; }
        }
    }

    /// <summary>
    /// Linux: the player picked Savage Wind's AppImage (or its savagewind program). Its data folder (saves, game files,
    /// savagewind.ini) is worked out from it the way Savage Wind does, and both are remembered: the program for Play,
    /// the data folder for saves and game files. Returns the data folder.
    /// </summary>
    public static string AddProgram(string program)
    {
        var data = DataRootFor(program);
        App.Progress.SaveSettings(s =>
        {
            if (!s.SavageWindPrograms.Contains(program)) s.SavageWindPrograms.Add(program);
            if (Directory.Exists(data) && !s.SavageWindFolders.Contains(data)) s.SavageWindFolders.Add(data);
            s.HiddenInstalls.Remove(program);
            s.GameProgram ??= program;
        });
        Log.Info($"Savage Wind program added: {Log.Scrub(program)} (data folder {Log.Scrub(data)})");
        return data;
    }

    /// <summary>A picked file that is a Savage Wind program (an AppImage or savagewind).</summary>
    public static bool IsProgram(string path) => File.Exists(path) &&
        (Path.GetFileName(path) is "savagewind" or "savagewind.exe" || path.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase));

    /// <summary>Savage Wind's data folder for a program: SAVAGEWIND_DATA_ROOT, else next to the exe (Windows) or ~/.local/share/savagewind.</summary>
    public static string DataRootFor(string exe)
    {
        var env = Environment.GetEnvironmentVariable("SAVAGEWIND_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(env) && Path.IsPathRooted(env)) return env;
        if (OperatingSystem.IsWindows()) return Path.GetDirectoryName(exe)!;
        // a source build keeps its data next to itself when it's there (portable), as Savage Wind does
        var beside = Path.GetDirectoryName(exe)!;
        if (File.Exists(Path.Combine(beside, "savagewind.ini")) && !exe.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)) return beside;
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return Path.Combine(!string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg) ? xdg : Path.Combine(Home, ".local", "share"), "savagewind");
    }

    /// <summary>Is a Savage Wind running now (any copy)?</summary>
    public static bool GameRunning()
    {
        try
        {
            foreach (var name in new[] { "savagewind", "Savage Wind Configuration", "SW Update" })
            {
                var ps = Process.GetProcessesByName(name);
                bool any = ps.Length > 0;
                foreach (var p in ps) p.Dispose();
                if (any) return true;
            }
        }
        catch (Exception) { }
        return false;
    }

    /// <summary>
    /// Play: Savage Wind started as if opened by hand. With Clear Skies on, Clear Skies starts it (its look and mods).
    /// Returns null, or why it couldn't start.
    /// </summary>
    public static string? Play(SwInstall sw, ClearSkies? cs, string? preset)
    {
        try
        {
            ProcessStartInfo psi;
            if (cs != null)
            {
                psi = new ProcessStartInfo(cs.Program) { WorkingDirectory = cs.Folder, UseShellExecute = false };
                psi.ArgumentList.Add("--game"); psi.ArgumentList.Add(sw.Exe);
                if (!string.IsNullOrEmpty(preset)) { psi.ArgumentList.Add("--preset"); psi.ArgumentList.Add(preset); }
                if (OperatingSystem.IsWindows()) psi.ArgumentList.Add("--no-update-check");  // (its updates are offered here, on the launch screen)
                psi.ArgumentList.Add("--play");  // (Clear Skies' own: start the game now, not its window)
            }
            // nothing passed to Savage Wind: it starts as if opened by hand, and decides itself whether to show
            // Savage Wind Configuration or an update first
            else psi = new ProcessStartInfo(sw.Exe) { WorkingDirectory = sw.Folder, UseShellExecute = false };
            using var p = Process.Start(psi);
            return p == null ? "It didn't start." : null;
        }
        catch (Exception e) { return e.Message; }
    }

    // ---------------------------------------------------------------- GitHub (one small request, only when asked for)

    static readonly HttpClient http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }) { Timeout = TimeSpan.FromSeconds(20) };
    static Launcher() => http.DefaultRequestHeaders.UserAgent.ParseAdd("SlateCompanion");

    public sealed record Release(string Tag, string Version, string Name, string Notes, string Page, DateTime Published, List<(string name, string url, long size)> Assets);

    /// <summary>The newest release of a GitHub repository (pre-releases included: Savage Wind's are all alphas).</summary>
    public static async Task<Release?> LatestRelease(string repo)
    {
        var json = await http.GetStringAsync($"https://api.github.com/repos/{repo}/releases?per_page=5");
        var arr = JsonNode.Parse(json)?.AsArray();
        var r = arr?.FirstOrDefault(x => x?["draft"]?.GetValue<bool>() != true);
        if (r == null) return null;
        var tag = r["tag_name"]!.GetValue<string>();
        var assets = r["assets"]!.AsArray().Select(a => (a!["name"]!.GetValue<string>(), a["browser_download_url"]!.GetValue<string>(), a["size"]!.GetValue<long>())).ToList();
        return new Release(tag, tag.TrimStart('v'), r["name"]?.GetValue<string>() ?? tag, r["body"]?.GetValue<string>() ?? "", r["html_url"]!.GetValue<string>(),
            r["published_at"]?.GetValue<DateTime>() ?? DateTime.MinValue, assets);
    }

    /// <summary>
    /// Compares "0.5.3-alpha.5"-style versions as semantic versions; &gt; 0 when a is newer. A pre-release comes before
    /// its release (0.6.0-alpha.3 &lt; 0.6.0), and pre-release parts compare number by number or by name (alpha &lt; beta).
    /// Comparing only the numbers made 0.6.0-alpha.3 look newer than 0.6.0, so alpha players were never offered 0.6.0.
    /// </summary>
    public static int CompareVersions(string a, string b)
    {
        static (int[] core, string[]? pre) Split(string v)
        {
            v = v.Trim().TrimStart('v', 'V');
            int plus = v.IndexOf('+'); if (plus >= 0) v = v[..plus];  // (build metadata doesn't count)
            int dash = v.IndexOf('-');
            var core = (dash >= 0 ? v[..dash] : v).Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
            return (core, dash >= 0 ? v[(dash + 1)..].Split('.') : null);
        }
        var (x, xp) = Split(a); var (y, yp) = Split(b);
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
            if (p != q) return p.CompareTo(q);
        }
        if (xp == null || yp == null) return xp == null ? (yp == null ? 0 : 1) : -1;  // the release is newer than its pre-releases
        for (int i = 0; i < Math.Min(xp.Length, yp.Length); i++)
        {
            bool xn = int.TryParse(xp[i], out var xi), yn = int.TryParse(yp[i], out var yi);
            int c = xn && yn ? xi.CompareTo(yi) : xn ? -1 : yn ? 1 : string.CompareOrdinal(xp[i], yp[i]);
            if (c != 0) return Math.Sign(c);
        }
        return xp.Length.CompareTo(yp.Length);
    }
}
