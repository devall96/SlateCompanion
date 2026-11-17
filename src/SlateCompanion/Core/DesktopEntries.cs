using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SlateCompanion;

/// <summary>
/// Linux: "Add to applications". Desktop entries in ~/.local/share/applications (the freedesktop.org menu every
/// desktop reads) for Slate Companion, so it shows up in the app menu
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
    public static int Sync()
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
            .AppendLine("Comment=Map, walkthrough, cooking, compendium and your progress for Breath of the Wild")
            .AppendLine($"Exec={Quote(SelfProgram())}").AppendLine("Icon=slatecompanion").AppendLine("Terminal=false")
            .AppendLine("Categories=Game;").AppendLine("StartupWMClass=SlateCompanion").ToString());

        // ours that are left over (older versions also made entries for Savage Wind AppImages)
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

    static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch (Exception) { } }
}
