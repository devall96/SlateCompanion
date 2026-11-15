using System.IO.Compression;

namespace SlateCompanion;

/// <summary>
/// A .zip of a save is copied into the app's own folder (imports/&lt;time&gt;/) and read from there. Folders and .sav
/// files are not copied: they are linked where they are (read only), so they stay in sync. Only .sav files are taken
/// from a zip, and nothing is written outside the import folder.
/// </summary>
public static class Importer
{
    const long MaxTotal = 256L * 1024 * 1024;

    public static string FromZip(string appFolder, string zipPath)
    {
        var dest = Path.Combine(appFolder, "imports", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(dest);
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            long total = 0;
            int n = 0;
            var root = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
            foreach (var e in zip.Entries)
            {
                if (string.IsNullOrEmpty(e.Name) || !e.Name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) || e.FullName.Contains("__MACOSX")) continue;
                total += e.Length;
                if (total > MaxTotal) throw new InvalidDataException("That zip is too large to be a save.");
                var target = Path.GetFullPath(Path.Combine(dest, e.FullName.Replace('\\', '/')));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;  // zip slip
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target, overwrite: false);
                n++;
            }
            if (n == 0) throw new InvalidDataException("No .sav files were in that zip.");
            if (File.Exists(Path.Combine(dest, "game_data.sav")))
            {
                var slot = Path.Combine(dest, "save_0", "0");
                Directory.CreateDirectory(slot);
                foreach (var f in Directory.GetFiles(dest, "*.sav")) File.Move(f, Path.Combine(slot, Path.GetFileName(f)));
            }
            var profile = SaveLocator.FindProfileFolder(dest, 8) ?? throw new InvalidDataException("No Breath of the Wild save (game_data.sav) was in that zip.");
            foreach (var s in SaveLocator.ListSlots(profile)) SaveFile.Load(Path.Combine(s.Path, "game_data.sav"));
            return profile;
        }
        catch
        {
            try { Directory.Delete(dest, true); } catch (IOException) { }
            throw;
        }
    }
}
