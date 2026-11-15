using System.IO.Compression;

namespace SlateCompanion;

/// <summary>A save folder to manage: a Savage Wind copy's Default saves or one of its save profiles.</summary>
public sealed record SaveTarget(SwInstall Install, string Profile, string SaveDir, bool InUse)
{
    public string Save0 => Path.Combine(SaveDir, "save_0");
    public string Label => Profile + (InUse ? " (in use)" : "");
}

/// <summary>One slot of a save folder: its number, when the game wrote it, its picture, play time.</summary>
public sealed record SaveSlot(string Name, string Dir, DateTime Saved, string? Picture, double? PlaySeconds);

/// <summary>
/// The save file manager. Saves are only ever changed when the player asks, and never without a whole copy of the save
/// folder first, in Savage Wind's own backup folder (&lt;save&gt;-backups/save-YYYYMMDD-HHMMSS: the Saves tab of Savage
/// Wind Configuration lists and restores the same ones). Nothing runs while the game has the save folder's lock
/// (&lt;save&gt;.lock, which the game holds while it plays), and that lock is taken for the change, as Savage Wind does.
/// Wii U (Cemu) saves are converted on import with <see cref="SaveConvert"/>, from the player's own Bootup.pack.
/// </summary>
public static class SaveManager
{
    public sealed class SaveException(string title, string message) : Exception(message) { public string Title { get; } = title; }

    // ---------------------------------------------------------------- what's there

    public static List<SaveTarget> Targets(SwInstall sw)
    {
        var list = new List<SaveTarget>();
        string? active = null;
        try { var m = Path.Combine(sw.DataRoot, "save_profile.txt"); if (File.Exists(m)) active = File.ReadAllText(m).Trim(); } catch (IOException) { }
        if (string.IsNullOrEmpty(active)) active = "Default";
        list.Add(new SaveTarget(sw, "Default", Path.Combine(sw.DataRoot, "save"), active.Equals("Default", StringComparison.OrdinalIgnoreCase)));
        var profiles = Path.Combine(sw.DataRoot, "save-profiles");
        if (Directory.Exists(profiles))
            foreach (var d in Directory.GetDirectories(profiles).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(d);
                list.Add(new SaveTarget(sw, name, Path.Combine(d, "save"), name.Equals(active, StringComparison.OrdinalIgnoreCase)));
            }
        return list;
    }

    public static List<SaveSlot> Slots(SaveTarget t)
    {
        var list = new List<SaveSlot>();
        if (!Directory.Exists(t.Save0)) return list;
        foreach (var dir in Directory.GetDirectories(t.Save0).Where(d => Path.GetFileName(d).All(char.IsAsciiDigit)).OrderBy(d => int.Parse(Path.GetFileName(d))))
        {
            var gd = Path.Combine(dir, "game_data.sav");
            if (!File.Exists(gd)) continue;
            var cap = Path.Combine(dir, "caption.sav");
            var when = File.GetLastWriteTime(File.Exists(cap) ? cap : gd);
            double? play = null;
            try { if (SaveFile.Load(gd).Get("PlayReport_PlayTime") is { Words.Length: > 0 } v) play = v.Words[0]; } catch (Exception) { }
            var jpg = Path.Combine(dir, "caption.jpg");
            list.Add(new SaveSlot(Path.GetFileName(dir), dir, when, File.Exists(jpg) ? jpg : null, play));
        }
        return list;
    }

    public static List<(string path, DateTime when)> Backups(SaveTarget t)
    {
        var dir = t.SaveDir + "-backups";
        if (!Directory.Exists(dir)) return [];
        return Directory.GetDirectories(dir, "save-*").Select(d => (d, Directory.GetLastWriteTime(d))).OrderByDescending(x => x.Item1, StringComparer.Ordinal).ToList();
    }

    // ---------------------------------------------------------------- the lock and the backup before every change

    sealed class SaveLock : IDisposable
    {
        readonly FileStream? fs;
        public SaveLock(string saveDir)
        {
            if (Launcher.GameRunning()) throw new SaveException("Savage Wind is running", "Close the game first: it writes its saves while it runs. Nothing was changed.");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(saveDir)!);
                var path = saveDir + ".lock";
                if (OperatingSystem.IsWindows())
                {
                    fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                    fs.Lock(0, 1);  // (the same byte Savage Wind locks with LockFileEx)
                }
                else fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);  // (flock, as Savage Wind)
            }
            catch (IOException)
            {
                fs?.Dispose();
                throw new SaveException("These saves are in use", "A running Savage Wind has this save folder. Close it first. Nothing was changed.");
            }
        }
        public void Dispose() { try { if (OperatingSystem.IsWindows()) fs?.Unlock(0, 1); } catch (Exception) { } fs?.Dispose(); }
    }

    /// <summary>A new dated backup folder name: &lt;save&gt;-backups/save-YYYYMMDD-HHMMSS (-2, -3… when taken).</summary>
    static string NewBackupPath(string saveDir)
    {
        var bas = Path.Combine(saveDir + "-backups", "save-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var p = bas;
        for (int i = 2; Directory.Exists(p); i++) p = $"{bas}-{i}";
        return p;
    }

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from))
            if (!new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }

    /// <summary>A whole copy of the save folder; returns where it went (null: there was nothing to back up).</summary>
    static string? BackupNoLock(string saveDir)
    {
        if (!Directory.Exists(saveDir) || !Directory.EnumerateFileSystemEntries(saveDir).Any()) return null;
        var dest = NewBackupPath(saveDir);
        try { CopyTree(saveDir, dest); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { Directory.Delete(dest, true); } catch (Exception) { }
            throw new SaveException("The backup didn't work", $"Nothing was changed. ({e.Message})");
        }
        Log.Info($"Saves: backed up {Log.Scrub(saveDir)} to {Log.Scrub(dest)}");
        return dest;
    }

    public static string? Backup(SaveTarget t)
    {
        using var lk = new SaveLock(t.SaveDir);
        return BackupNoLock(t.SaveDir);
    }

    /// <summary>Puts a backup back: the saves now are backed up first, then replaced by a copy of it (it stays).</summary>
    public static void Restore(SaveTarget t, string backup)
    {
        using var lk = new SaveLock(t.SaveDir);
        BackupNoLock(t.SaveDir);
        ReplaceWith(t.SaveDir, d => CopyTree(backup, d));
        Log.Info($"Saves: restored {Log.Scrub(backup)}");
    }

    /// <summary>The save folder replaced by a new one made by <paramref name="fill"/> beside it; the old one goes only
    /// once the new one is complete (it's in a backup already), and comes back if anything fails.</summary>
    static void ReplaceWith(string saveDir, Action<string> fill)
    {
        var stage = saveDir + "-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var old = saveDir + "-replaced-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        try
        {
            fill(stage);
            if (Directory.Exists(saveDir))
            {
                // what the old folder held besides save_0 (sdcard…) comes along, as in Savage Wind's import
                foreach (var e in Directory.GetFileSystemEntries(saveDir))
                {
                    var name = Path.GetFileName(e);
                    if (name == "save_0" || File.Exists(Path.Combine(stage, name)) || Directory.Exists(Path.Combine(stage, name))) continue;
                    if (Directory.Exists(e)) CopyTree(e, Path.Combine(stage, name)); else File.Copy(e, Path.Combine(stage, name));
                }
                Directory.Move(saveDir, old);
            }
            try { Directory.Move(stage, saveDir); }
            catch (Exception) { if (Directory.Exists(old)) Directory.Move(old, saveDir); throw; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch (Exception) { }
            throw new SaveException("The saves weren't changed", e.Message);
        }
        // The new saves are in place now: the old folder (in a backup already) goes as best it can. A file held open
        // (an album picture in Explorer) mustn't turn this into "the saves weren't changed", which would be untrue.
        TryDeleteDir(old);
    }

    static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn($"Saves: couldn't remove {Log.Scrub(dir)} (it can be deleted by hand): {e.Message}"); }
    }

    /// <summary>A file written and flushed to the disk, so a rename after it never puts an empty file in place after a power cut.</summary>
    static void WriteDurable(string path, byte[] data)
    {
        using var f = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        f.Write(data);
        f.Flush(true);
    }

    /// <summary>A save file read without keeping the game from writing it meanwhile (File.ReadAllBytes would).</summary>
    internal static byte[] ReadShared(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var data = new byte[f.Length];
        f.ReadExactly(data);
        return data;
    }

    // ---------------------------------------------------------------- import

    /// <summary>What a picked save is: where its slots are, the byte order, how many slots.</summary>
    public sealed record Incoming(string Root, bool WiiU, int Slots, string? TempDir);

    /// <summary>Finds the save in a folder, a game_data.sav or a .zip (unpacked to a temporary folder).</summary>
    public static Incoming Inspect(string path)
    {
        string? temp = null;
        var dir = path;
        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            temp = Path.Combine(Path.GetTempPath(), "slate-save-" + Guid.NewGuid().ToString("N")[..8]);
            var full = Path.GetFullPath(temp) + Path.DirectorySeparatorChar;
            using var z = ZipFile.OpenRead(path);
            foreach (var e in z.Entries)
            {
                var dest = Path.GetFullPath(Path.Combine(temp, e.FullName));
                if (!dest.StartsWith(full, StringComparison.Ordinal)) throw new SaveException("That zip isn't safe", "It has files that would go outside their folder.");
                if (e.FullName.EndsWith('/')) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                e.ExtractToFile(dest);
            }
            dir = temp;
        }
        else if (File.Exists(path)) dir = Path.GetDirectoryName(path)!;
        var root = SaveConvert.FindProfile(dir) ?? (File.Exists(Path.Combine(dir, "game_data.sav")) ? Path.GetDirectoryName(dir) : null);
        if (root == null || SaveConvert.FindProfile(root, 0) == null)
            throw new SaveException("No Breath of the Wild save there", "Pick the folder with the numbered slot folders (0, 1, 2… each with a game_data.sav), a game_data.sav, or a .zip of them. For Cemu that's mlc01/usr/save/00050000/<game>/user/80000001.");
        var slots = Directory.GetDirectories(root).Count(d => Path.GetFileName(d).All(char.IsAsciiDigit) && File.Exists(Path.Combine(d, "game_data.sav")));
        var first = Directory.GetDirectories(root).First(d => Path.GetFileName(d).All(char.IsAsciiDigit) && File.Exists(Path.Combine(d, "game_data.sav")));
        bool wiiu = !SaveConvert.IsLittle(File.ReadAllBytes(Path.Combine(first, "game_data.sav")));
        return new Incoming(root, wiiu, slots, temp);
    }

    static string Bootup(SwInstall sw)
    {
        var p = Path.Combine(sw.DataRoot, "extracted", "romfs", "Pack", "Bootup.pack");
        if (File.Exists(p)) return p;
        var app = MapBuilder.FindRomfs(App.Progress.Settings.GameFilesFolder, App.Progress.Settings.SavageWindFolders);
        if (app != null && File.Exists(Path.Combine(app, "Pack", "Bootup.pack"))) return Path.Combine(app, "Pack", "Bootup.pack");
        throw new SaveException("Your game files are needed", "Converting a Wii U save uses the flag tables in your own game files (Pack/Bootup.pack). Set this copy of Savage Wind up with its game files first.");
    }

    /// <summary>
    /// Imports a whole save into the target: everything it has now is backed up, then the save folder is replaced (all
    /// slots, options, Hero's Path, album). A Wii U / Cemu save is converted for this game on the way in.
    /// </summary>
    public static List<string> ImportAll(SaveTarget t, Incoming inc)
    {
        var report = new List<string>();
        using var lk = new SaveLock(t.SaveDir);
        var files = inc.WiiU ? SaveConvert.ConvertProfile(inc.Root, new SaveConvert.Format(Bootup(t.Install)), true, report) : CopyProfile(inc.Root);
        var backup = BackupNoLock(t.SaveDir);
        ReplaceWith(t.SaveDir, stage =>
        {
            foreach (var (rel, data) in files)
            {
                var p = Path.Combine(stage, "save_0", rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllBytes(p, data);
            }
        });
        report.Add(backup != null ? $"Your previous saves are in {Path.GetFileName(backup)}." : "There were no saves before.");
        Log.Info($"Saves: imported {Log.Scrub(inc.Root)} into {Log.Scrub(t.SaveDir)}{(inc.WiiU ? " (converted from Wii U)" : "")}");
        Cleanup(inc);
        return report;
    }

    /// <summary>One slot of the incoming save into one slot here (that slot is replaced; everything is backed up first).</summary>
    public static void ImportSlot(SaveTarget t, Incoming inc, string fromSlot, string toSlot)
    {
        using var lk = new SaveLock(t.SaveDir);
        var report = new List<string>();
        Dictionary<string, byte[]> files;
        if (inc.WiiU)
        {
            var fmt = new SaveConvert.Format(Bootup(t.Install));
            var src = Path.Combine(inc.Root, fromSlot);
            files = new() { ["game_data.sav"] = SaveConvert.ConvertFlags(File.ReadAllBytes(Path.Combine(src, "game_data.sav")), fmt, "game_data.sav", true, report) };
            if (File.Exists(Path.Combine(src, "caption.sav"))) files["caption.sav"] = SaveConvert.ConvertFlags(File.ReadAllBytes(Path.Combine(src, "caption.sav")), fmt, "caption.sav", true, report);
            if (File.Exists(Path.Combine(src, "caption.jpg"))) files["caption.jpg"] = File.ReadAllBytes(Path.Combine(src, "caption.jpg"));
        }
        else files = Directory.GetFiles(Path.Combine(inc.Root, fromSlot)).ToDictionary(Path.GetFileName, File.ReadAllBytes)!;
        BackupNoLock(t.SaveDir);
        WriteSlot(t, toSlot, files);
        Log.Info($"Saves: slot {fromSlot} of {Log.Scrub(inc.Root)} into slot {toSlot} of {Log.Scrub(t.SaveDir)}");
        Cleanup(inc);
    }

    /// <summary>
    /// A slot replaced: the new one is written beside it, the old one moved aside, the new one moved in, and only then
    /// is the old one deleted (backed up already). Deleting first could leave a half-deleted slot if a file is held.
    /// </summary>
    static void WriteSlot(SaveTarget t, string slot, Dictionary<string, byte[]> files)
    {
        var dir = Path.Combine(t.Save0, slot);
        var stage = dir + ".slate-new";
        var old = dir + ".slate-old";
        try
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            Directory.CreateDirectory(stage);
            foreach (var (name, data) in files) WriteDurable(Path.Combine(stage, name), data);
            if (Directory.Exists(old)) Directory.Delete(old, true);
            if (Directory.Exists(dir)) Directory.Move(dir, old);
            try { Directory.Move(stage, dir); }
            catch (Exception) { if (Directory.Exists(old) && !Directory.Exists(dir)) Directory.Move(old, dir); throw; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDeleteDir(stage);
            throw new SaveException($"Slot {slot} wasn't changed", e.Message);
        }
        TryDeleteDir(old);
    }

    static Dictionary<string, byte[]> CopyProfile(string root)
    {
        var files = new Dictionary<string, byte[]>();
        foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            if (rel.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) continue;
            files[rel] = ReadShared(f);
        }
        return files;
    }

    public static void Cleanup(Incoming inc)
    {
        if (inc.TempDir != null) try { Directory.Delete(inc.TempDir, true); } catch (Exception) { }
    }

    // ---------------------------------------------------------------- slots: copy, delete

    public static void CopySlot(SaveTarget from, string slot, SaveTarget to, string toSlot)
    {
        using var lk = new SaveLock(to.SaveDir);
        var files = Directory.GetFiles(Path.Combine(from.Save0, slot)).ToDictionary(Path.GetFileName, File.ReadAllBytes)!;
        BackupNoLock(to.SaveDir);
        WriteSlot(to, toSlot, files);
        Log.Info($"Saves: slot {slot} of {from.Label} copied to slot {toSlot} of {to.Label}");
    }

    public static void DeleteSlot(SaveTarget t, string slot)
    {
        using var lk = new SaveLock(t.SaveDir);
        BackupNoLock(t.SaveDir);
        // moved aside first (one step, so the slot is either there or gone), then deleted as best it can
        var dir = Path.Combine(t.Save0, slot);
        var gone = dir + ".slate-deleted";
        try
        {
            if (Directory.Exists(gone)) Directory.Delete(gone, true);
            Directory.Move(dir, gone);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new SaveException($"Slot {slot} wasn't deleted", e.Message); }
        TryDeleteDir(gone);
        Log.Info($"Saves: slot {slot} of {Log.Scrub(t.SaveDir)} deleted (backed up first)");
    }

    // ---------------------------------------------------------------- editing one slot's game_data.sav (equipment)

    /// <summary>
    /// Changes a slot's game_data.sav: Savage Wind's save lock taken, the whole save folder backed up first, the
    /// edit made on a copy, the copy read back to check it, then swapped in. <paramref name="change"/> edits the
    /// <see cref="SaveFile"/> and returns what it did (for the log and the message).
    /// </summary>
    public static string EditGameData(string saveDir, string slotDir, Func<SaveFile, string> change)
    {
        using var lk = new SaveLock(saveDir);
        var file = Path.Combine(slotDir, "game_data.sav");
        var save = SaveFile.Load(file);
        var before = save.ToBytes();
        var what = change(save);
        var bytes = save.ToBytes();
        if (bytes.AsSpan().SequenceEqual(before)) return what;  // nothing changed: no backup (a whole save folder) and no write
        SaveFile.Parse(bytes);  // (still a well-formed save)
        BackupNoLock(saveDir);
        var tmp = file + ".slate-new";
        WriteDurable(tmp, bytes);
        if (!SaveFile.Parse(File.ReadAllBytes(tmp)).ToBytes().AsSpan().SequenceEqual(bytes)) { File.Delete(tmp); throw new SaveException("The save wasn't changed", "The edited file didn't read back the same. Nothing was changed."); }
        File.Move(tmp, file, true);
        Log.Info($"Saves: {Log.Scrub(file)}: {what}");
        return what;
    }

    // ---------------------------------------------------------------- export

    /// <summary>The saves as a .zip: for Savage Wind / Switch, or converted for Wii U (Cemu: unpack into user/80000001).</summary>
    public static void Export(SaveTarget t, string zipPath, bool forWiiU, string? onlySlot = null)
    {
        var report = new List<string>();
        Dictionary<string, byte[]> files = forWiiU ? SaveConvert.ConvertProfile(t.Save0, new SaveConvert.Format(Bootup(t.Install)), false, report) : CopyProfile(t.Save0);
        if (onlySlot != null) files = files.Where(kv => kv.Key.StartsWith(onlySlot + "/", StringComparison.Ordinal)).ToDictionary();
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var z = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (rel, data) in files)
        {
            using var s = z.CreateEntry(rel, CompressionLevel.Optimal).Open();
            s.Write(data);
        }
        Log.Info($"Saves: exported {Log.Scrub(t.SaveDir)} to {Log.Scrub(zipPath)}{(forWiiU ? " for Wii U" : "")}");
    }
}
