using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SlateCompanion;

/// <summary>
/// Linux: "Add to applications". Desktop entries in ~/.local/share/applications (the freedesktop.org menu every
/// desktop reads) for Slate Companion and for each Savage Wind AppImage it knows, so they show up in the app menu
/// and can be pinned. Only files named slatecompanion*.desktop are ever written or removed, and turning the switch
/// off removes them all again. Nothing system-wide, no root.
/// </summary>
public static class DesktopEntries
{
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string DataHome => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x && Path.IsPathRooted(x) ? x : Path.Combine(Home, ".local", "share");
    static string AppsDir => Path.Combine(DataHome, "applications");
    static string IconsDir => Path.Combine(DataHome, "icons", "hicolor", "256x256", "apps");

    public static bool Supported => OperatingSystem.IsLinux();

    /// <summary>Slate Companion's own program: the AppImage when it runs as one (its runtime sets APPIMAGE), else this binary.</summary>
    static string SelfProgram()
    {
        var ai = Environment.GetEnvironmentVariable("APPIMAGE");
        return !string.IsNullOrEmpty(ai) && File.Exists(ai) ? ai : Environment.ProcessPath ?? "slatecompanion";
    }

    static string Quote(string path) => "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$") + "\"";

    /// <summary>Writes (or rewrites) every entry, and removes ours that no longer have a program. Returns how many there are.</summary>
    public static int Sync(IEnumerable<SwInstall> installs, IReadOnlyDictionary<string, string> names)
    {
        if (!Supported) return 0;
        Directory.CreateDirectory(AppsDir);
        Directory.CreateDirectory(IconsDir);
        var keep = new HashSet<string>(StringComparer.Ordinal);

        // Slate Companion
        try
        {
            var beside = Path.Combine(AppContext.BaseDirectory, "icon.png");  // (copied next to the program: works without the window too)
            using var s = File.Exists(beside) ? File.OpenRead(beside) : Avalonia.Platform.AssetLoader.Open(new Uri("avares://SlateCompanion/Assets/icon.png"));
            using var f = File.Create(Path.Combine(IconsDir, "slatecompanion.png"));
            s.CopyTo(f);
        }
        catch (Exception e) { Log.Warn("desktop entry icon: " + e.Message); }
        keep.Add("slatecompanion.desktop");
        Write("slatecompanion.desktop", new StringBuilder()
            .AppendLine("[Desktop Entry]").AppendLine("Type=Application").AppendLine("Name=Slate Companion")
            .AppendLine("GenericName=Breath of the Wild companion")
            .AppendLine("Comment=Map, walkthrough, cooking, compendium and your progress for Breath of the Wild; a launcher for Savage Wind")
            .AppendLine($"Exec={Quote(SelfProgram())}").AppendLine("Icon=slatecompanion").AppendLine("Terminal=false")
            .AppendLine("Categories=Game;Utility;").AppendLine("StartupWMClass=SlateCompanion").ToString());

        // each Savage Wind AppImage
        foreach (var i in installs.Where(i => i.AppImage && File.Exists(i.Exe)))
        {
            var id = "slatecompanion-savagewind-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(i.Exe)))[..10].ToLowerInvariant();
            keep.Add(id + ".desktop");
            string icon = File.Exists(Path.Combine(IconsDir, id + ".png")) || ExtractIcon(i.Exe, Path.Combine(IconsDir, id + ".png")) ? id : "applications-games";
            var name = names.GetValueOrDefault(i.Exe) is { Length: > 0 } n ? n : "Savage Wind";
            var exe = Quote(i.Exe);
            Write(id + ".desktop", new StringBuilder()
                .AppendLine("[Desktop Entry]").AppendLine("Type=Application").AppendLine($"Name={name.Replace('\n', ' ')}")
                .AppendLine("Comment=The Legend of Zelda: Breath of the Wild, on PC (Savage Wind)")
                .AppendLine($"Exec={exe}").AppendLine($"Icon={icon}").AppendLine("Terminal=false").AppendLine("Categories=Game;")
                .AppendLine("Actions=play;config;").AppendLine()
                .AppendLine("[Desktop Action play]").AppendLine("Name=Play (straight into the game)").AppendLine($"Exec={exe} --play").AppendLine()
                .AppendLine("[Desktop Action config]").AppendLine("Name=Savage Wind Configuration").AppendLine($"Exec={exe} --config").ToString());
        }

        // ours that are left over (a copy removed from the list or deleted)
        foreach (var f in Directory.GetFiles(AppsDir, "slatecompanion*.desktop"))
            if (!keep.Contains(Path.GetFileName(f))) { TryDelete(f); TryDelete(Path.Combine(IconsDir, Path.GetFileNameWithoutExtension(f) + ".png")); }
        Log.Info($"Desktop entries: {keep.Count} in {Log.Scrub(AppsDir)}");
        return keep.Count;
    }

    /// <summary>Removes every entry (and icon) Slate Companion made.</summary>
    public static void RemoveAll()
    {
        if (!Supported) return;
        if (Directory.Exists(AppsDir))
            foreach (var f in Directory.GetFiles(AppsDir, "slatecompanion*.desktop")) TryDelete(f);
        if (Directory.Exists(IconsDir))
            foreach (var f in Directory.GetFiles(IconsDir, "slatecompanion*.png")) TryDelete(f);
        Log.Info("Desktop entries removed");
    }

    static void Write(string name, string text)
    {
        var p = Path.Combine(AppsDir, name);
        try
        {
            if (File.Exists(p) && File.ReadAllText(p) == text) return;
            File.WriteAllText(p + ".tmp", text);
            File.Move(p + ".tmp", p, true);
            File.SetUnixFileMode(p, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn($"desktop entry {name}: {e.Message}"); }
    }

    /// <summary>The AppImage's own icon: its runtime extracts files without starting the program (--appimage-extract).</summary>
    static bool ExtractIcon(string appImage, string dest)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "slate-icon-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tmp);
            foreach (var pattern in new[] { ".DirIcon", "*.png" })
            {
                var psi = new ProcessStartInfo(appImage) { WorkingDirectory = tmp, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                psi.ArgumentList.Add("--appimage-extract"); psi.ArgumentList.Add(pattern);
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                if (!p.WaitForExit(20000)) { try { p.Kill(true); } catch (Exception) { } return false; }
                var root = Path.Combine(tmp, "squashfs-root");
                if (!Directory.Exists(root)) continue;
                var best = Directory.GetFiles(root, "*.png").Append(Path.Combine(root, ".DirIcon")).Where(File.Exists)
                    .Select(f => new FileInfo(f)).Where(f => f.Length > 0).OrderByDescending(f => f.Length).FirstOrDefault();
                if (best == null) continue;
                // .DirIcon can be a link to the png inside: copy what it points to when that came out too
                var src = best.LinkTarget is { } t && File.Exists(Path.Combine(root, t)) ? Path.Combine(root, t) : best.FullName;
                using (var probe = File.OpenRead(src)) { var sig = new byte[4]; if (probe.Read(sig) < 4 || sig[1] != 'P' || sig[2] != 'N' || sig[3] != 'G') continue; }
                File.Copy(src, dest, true);
                return true;
            }
            return false;
        }
        catch (Exception e) { Log.Warn("AppImage icon: " + e.Message); return false; }
        finally { try { Directory.Delete(tmp, true); } catch (Exception) { } }
    }

    static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch (Exception) { } }
}
