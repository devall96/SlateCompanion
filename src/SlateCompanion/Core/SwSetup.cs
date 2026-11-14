using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SlateCompanion;

/// <summary>
/// Savage Wind's own setup, run with its window hidden: copying the game files in (a new copy), building the game code
/// from them, precompiling the shaders, and marking a new version as set up. It's the setup Savage Wind's start screen
/// runs (runtime/ui/launcher.mm extract_thread), started through switches every release has: SAVAGEWIND_HIDDEN (no
/// window) and SAVAGEWIND_LAUNCHER_EXTRACT=&lt;folder&gt; (set up from that folder, then exit). Savage Wind doesn't print its
/// progress, so it's measured: the bytes copied, the code generator's own progress logs and the compiled pieces in
/// extracted/.gamebuild, the shaders in cache/xlate, and the lines of savagewind.log.
///
/// One thing the hidden start skips is done here first, the same way: a new version's shader caches are emptied
/// (runtime/post_update.c post_update_reset_shaders).
/// </summary>
public static class SwSetup
{
    sealed record Phase(string Name, double From, double To);

    /// <summary>
    /// Sets the copy up. <paramref name="gameFiles"/>: a folder with the game files (exefs and romfs, and the DLC's if
    /// there) for a new copy; null: the copy's own (a new version's setup, or game code built for another CPU).
    /// </summary>
    public static async Task Run(SwInstall sw, string? gameFiles, IProgress<JobProgress> pr, CancellationToken ct)
    {
        if (Launcher.GameRunning()) throw new SwInstaller.InstallException("Savage Wind is running", "Close it first: its setup can't run while it's open.");
        var extracted = Path.Combine(sw.DataRoot, "extracted");
        var source = gameFiles ?? extracted;
        bool copying = gameFiles != null && !Path.GetFullPath(gameFiles).StartsWith(Path.GetFullPath(extracted), StringComparison.OrdinalIgnoreCase);
        if (!copying && !File.Exists(Path.Combine(extracted, "exefs", "main")))
            throw new SwInstaller.InstallException("No game files yet", "Choose the folder with your game files (exefs and romfs) to set this copy up.");

        if (!copying) ResetShadersForNewVersion(sw);

        long sourceBytes = copying ? await Task.Run(() => FolderBytes(source), ct) : 0;
        var phases = copying
            ? new[] { new Phase("copy", 0, 0.35), new Phase("build", 0.35, 0.8), new Phase("shaders", 0.8, 1) }
            : new[] { new Phase("copy", 0, 0), new Phase("build", 0, 0.7), new Phase("shaders", 0.7, 1) };

        var logPath = Path.Combine(sw.DataRoot, "savagewind.log");
        var psi = new ProcessStartInfo(sw.Exe) { WorkingDirectory = sw.Folder, UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["SAVAGEWIND_HIDDEN"] = "1";
        psi.Environment["SAVAGEWIND_LAUNCHER_EXTRACT"] = Path.GetFullPath(source);
        psi.Environment["SAVAGEWIND_NO_UPDATE_CHECK"] = "1";
        Log.Info($"Savage Wind setup: {sw.Exe} from {(copying ? source : "its own game files")}");
        var started = DateTime.Now;
        using var p = Process.Start(psi) ?? throw new SwInstaller.InstallException("Savage Wind didn't start", sw.Exe);
        using var reg = ct.Register(() => { try { p.Kill(true); } catch (Exception) { } });

        string phase = copying ? "copy" : "build";
        string? failed = null, readyLine = null;
        int toTranslate = 0, seedPipelines = 0;
        bool translated = false;
        var cacheDir = Path.Combine(sw.DataRoot, "cache");
        long logPos = 0;
        var genProgress = new Regex(@": progress (\d+)/(\d+) files (\d+)");
        while (!p.HasExited)
        {
            await Task.Delay(700, CancellationToken.None);
            // what Savage Wind has said since it started
            foreach (var line in NewLogLines(logPath, started, ref logPos))
            {
                if (line.Contains("[gamebuild] generator")) phase = "build";
                else if (line.Contains("[precompile]") && Regex.Match(line, @": (\d+) translations to make") is { Success: true } tm) { phase = "shaders"; toTranslate = int.Parse(tm.Groups[1].Value); }
                else if (line.Contains("[precompile] pipeline seed") && Regex.Match(line, @": (\d+) pipelines") is { Success: true } sm) seedPipelines = int.Parse(sm.Groups[1].Value);
                else if (line.Contains("[precompile]") && line.Contains(" translations in ")) translated = true;
                else if (line.Contains("[precompile]")) phase = "shaders";
                else if (line.Contains("[launcher] FAILED: ")) failed = line[(line.IndexOf("FAILED: ", StringComparison.Ordinal) + 8)..];
                else if (line.Contains("[launcher] setup: ")) readyLine = line;
            }
            var ph = phases.First(x => x.Name == phase);
            double f; string step, detail;
            switch (phase)
            {
                case "copy":
                    long have = FolderBytes(extracted);
                    f = sourceBytes > 0 ? Math.Min(1, (double)have / sourceBytes) : 0;
                    step = "Copying your game files"; detail = $"{have / 1073741824.0:0.0} of {sourceBytes / 1073741824.0:0.0} GB";
                    break;
                case "build":
                    (f, detail) = BuildProgress(Path.Combine(extracted, ".gamebuild"), genProgress);
                    step = "Building the game code from your game files";
                    break;
                default:
                    // the shaders go into one file (cache/vk_precompiled.bin, about 8 KB each), then the pipelines into
                    // cache/vk_pipelines.bin (about 770 bytes each): their sizes say roughly how far it is
                    if (!translated)
                    {
                        long bytes = FileSize(Path.Combine(cacheDir, "vk_precompiled.bin"));
                        f = toTranslate > 0 ? Math.Min(0.95, bytes / (toTranslate * 8260.0)) * 0.6 : -1;
                        step = "Precompiling shaders (no stutter the first time you see a place)";
                        detail = toTranslate > 0 ? $"about {Math.Min(toTranslate, (int)(bytes / 8260)):N0} of {toTranslate:N0} shaders" : "Reading the shader archives";
                    }
                    else
                    {
                        long bytes = FileSize(Path.Combine(cacheDir, "vk_pipelines.bin"));
                        f = 0.6 + 0.38 * (seedPipelines > 0 ? Math.Min(1, bytes / (seedPipelines * 771.0)) : 0.5);
                        step = "Building pipelines for your graphics card";
                        detail = seedPipelines > 0 ? $"about {Math.Min(seedPipelines, (int)(bytes / 771)):N0} of {seedPipelines:N0}" : "";
                    }
                    break;
            }
            var el = DateTime.Now - started;
            pr.Report(new JobProgress(f < 0 ? ph.From : ph.From + (ph.To - ph.From) * f, step, f, $"{detail} · {(int)el.TotalMinutes}:{el.Seconds:00}"));
        }
        foreach (var line in NewLogLines(logPath, started, ref logPos))
        {
            if (line.Contains("[launcher] FAILED: ")) failed = line[(line.IndexOf("FAILED: ", StringComparison.Ordinal) + 8)..];
            else if (line.Contains("[launcher] setup: ")) readyLine = line;
        }
        ct.ThrowIfCancellationRequested();
        Log.Info($"Savage Wind setup: exit {p.ExitCode}; {readyLine ?? "no result line"}{(failed != null ? "; failed: " + failed : "")}");
        bool ready = readyLine?.EndsWith("READY", StringComparison.Ordinal) == true && !readyLine.EndsWith("NOT READY", StringComparison.Ordinal);
        if (!ready)
        {
            var (head, hint) = failed?.Split(" | ", 2) is { Length: 2 } parts ? (parts[0], parts[1]) : (failed ?? "The setup didn't finish", "");
            throw new SwInstaller.InstallException(head, (hint.Length > 0 ? hint + "\n\n" : "") + $"Savage Wind's log has the details: {UI.Ui.ShortPath(logPath)}");
        }
        var after = new SwInstall(sw.Exe, sw.DataRoot);
        if (after.SetupPending) Log.Warn($"Savage Wind setup finished but the copy still counts as not set up: {after.SetupState()}");
        pr.Report(new JobProgress(1, "Ready to play", 1, $"Set up in {(int)(DateTime.Now - started).TotalMinutes} min"));
    }

    /// <summary>runtime/post_update.c post_update_reset_shaders: a new version empties what's derived from the shaders, once.</summary>
    static void ResetShadersForNewVersion(SwInstall sw)
    {
        if (!sw.SetupPending || sw.Version is not { } v) return;
        var cache = Environment.GetEnvironmentVariable("SAVAGEWIND_CACHE_DIR") is { Length: > 0 } c ? c : Path.Combine(sw.DataRoot, "cache");
        var marker = Path.Combine(cache, "shaders-reset-version.txt");
        try { if (File.Exists(marker) && File.ReadLines(marker).FirstOrDefault()?.Trim() == v) return; } catch (IOException) { }
        Directory.CreateDirectory(cache);
        foreach (var d in new[] { "xlate", "spirv" })
        {
            var dir = Path.Combine(cache, d);
            try { if (Directory.Exists(dir) && !new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(dir, true); } catch (Exception e) { Log.Warn($"shader reset: {d}: {e.Message}"); }
            Directory.CreateDirectory(dir);
        }
        foreach (var f in new[] { "vk_pipelines.bin", "vk_precompiled.bin", "precompiled.txt" })
            try { File.Delete(Path.Combine(cache, f)); } catch (Exception) { }
        if (OperatingSystem.IsWindows())
        {
            // AMD's driver keeps its own copy of compiled pipelines; old ones can crash a new version (as Savage Wind does)
            var amd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD");
            var vk = Path.Combine(amd, "VkCache");
            try
            {
                if (Directory.Exists(vk) && !new DirectoryInfo(amd).Attributes.HasFlag(FileAttributes.ReparsePoint) && !new DirectoryInfo(vk).Attributes.HasFlag(FileAttributes.ReparsePoint))
                    foreach (var f in Directory.EnumerateFileSystemEntries(vk))
                        try { if (Directory.Exists(f)) Directory.Delete(f, true); else File.Delete(f); } catch (Exception) { }
            }
            catch (Exception) { }
        }
        File.WriteAllText(marker, v + "\n");
        Log.Info($"Savage Wind setup: new version {v}: shader caches emptied");
    }

    /// <summary>The game code build's progress from its work folder: the generator's logs, then the compiled pieces.</summary>
    static (double f, string detail) BuildProgress(string work, Regex genProgress)
    {
        if (!Directory.Exists(work)) return (0, "Starting");
        try
        {
            long done = 0, all = 0;
            foreach (var log in Directory.EnumerateFiles(work, "gen_*.log"))
            {
                if (Path.GetFileName(log) == "gen_layout.log") continue;
                var tail = Tail(log, 4096);
                var m = genProgress.Matches(tail).LastOrDefault();
                if (m != null) { done += long.Parse(m.Groups[1].Value); all += long.Parse(m.Groups[2].Value); }
            }
            int c = Directory.EnumerateFiles(work, "*.c", SearchOption.AllDirectories).Count();
            int o = Directory.EnumerateFiles(work, "*.o", SearchOption.TopDirectoryOnly).Count();
            double gen = all > 0 ? (double)done / all : 0;
            if (o == 0 || gen < 0.999) return (0.6 * gen, all > 0 ? $"Generating code: {done:N0} of {all:N0} functions" : "Reading the game code");
            double comp = c > 0 ? Math.Min(1, (double)o / c) : 0;
            return (0.6 + 0.38 * comp, comp >= 1 ? "Linking" : $"Compiling: {o} of {c} pieces");
        }
        catch (Exception) { return (0, "Working"); }
    }

    static IEnumerable<string> NewLogLines(string path, DateTime since, ref long pos)
    {
        try
        {
            if (!File.Exists(path) || File.GetLastWriteTime(path) < since.AddSeconds(-2)) return [];
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (f.Length < pos) pos = 0;  // (a new log)
            f.Seek(pos, SeekOrigin.Begin);
            using var r = new StreamReader(f);
            var text = r.ReadToEnd();
            int cut = text.LastIndexOf('\n');
            if (cut < 0) return [];
            pos += System.Text.Encoding.UTF8.GetByteCount(text[..(cut + 1)]);
            return text[..cut].Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        }
        catch (Exception) { return []; }
    }

    static string Tail(string path, int bytes)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (f.Length > bytes) f.Seek(-bytes, SeekOrigin.End);
        using var r = new StreamReader(f);
        return r.ReadToEnd();
    }

    static long FileSize(string f)
    {
        try { return File.Exists(f) ? new FileInfo(f).Length : 0; } catch (Exception) { return 0; }
    }

    static int CountFiles(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.EnumerateFiles(dir).Count() : 0; } catch (Exception) { return 0; }
    }

    public static long FolderBytes(string dir)
    {
        long n = 0;
        try
        {
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                if (!f.FullName.Contains(".gamebuild")) n += f.Length;
        }
        catch (Exception) { }
        return n;
    }

    // ---------------------------------------------------------------- finding the player's game files

    public sealed record GameFiles(string Folder, string Label, bool FromInstall);

    /// <summary>
    /// Folders with the game's files (exefs/main and romfs/, as dumped from a Switch): other Savage Wind copies'
    /// extracted folders first (no new copy needed: Savage Wind links the files where the disk allows), then dumps in
    /// the usual places, a few folders deep, within a time budget.
    /// </summary>
    public static List<GameFiles> FindGameFiles(IEnumerable<SwInstall> installs, TimeSpan budget)
    {
        var found = new List<GameFiles>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in installs)
        {
            var ex = Path.Combine(i.DataRoot, "extracted");
            if (File.Exists(Path.Combine(ex, "exefs", "main")) && Directory.Exists(Path.Combine(ex, "romfs")) && seen.Add(ex))
                found.Add(new GameFiles(ex, $"From {i.Label}", true));
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var starts = new List<string> { Path.Combine(home, "Desktop"), Path.Combine(home, "Downloads"), Path.Combine(home, "Documents"), Path.Combine(home, "Games"), Path.Combine(home, "OneDrive", "Documents"), Path.Combine(home, "OneDrive", "Desktop") };
        if (OperatingSystem.IsWindows())
            foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable && d.IsReady)) starts.Add(d.RootDirectory.FullName);
        var deadline = DateTime.UtcNow + budget;
        foreach (var s in starts.Where(Directory.Exists)) Walk(s, 0);
        return found;

        void Walk(string dir, int depth)
        {
            if (DateTime.UtcNow > deadline || depth > 4 || !seen.Add(dir)) return;
            try
            {
                if (File.Exists(Path.Combine(dir, "exefs", "main")) && Directory.Exists(Path.Combine(dir, "romfs")))
                {
                    found.Add(new GameFiles(dir, "Game files in " + UI.Ui.ShortPath(dir), false));
                    return;
                }
                var name = Path.GetFileName(dir);
                if (name.StartsWith('.') || name is "node_modules" or "Windows" or "Program Files" or "Program Files (x86)" or "$Recycle.Bin" or "ProgramData" or "AppData" or "extracted" or "romfs") return;
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    Walk(sub, depth + 1);
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException) { }
        }
    }
}
