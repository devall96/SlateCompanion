using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace SlateCompanion;

/// <summary>What a long job is doing: overall 0..1 (or -1 unknown), the step's own 0..1, and words for both.</summary>
public sealed record JobProgress(double Overall, string Step, double StepFraction, string Detail);

/// <summary>
/// Installs and updates Savage Wind without its own updater or screens.
///
/// Windows: the same work as Savage Wind's SW Update (runtime/platform/windows_update.c), step for step: the release
/// zip and its signature (ECDSA P-256, Savage Wind's public key), every file checked against the zip's own list
/// (sw-manifest.txt: size and SHA-256), then the swap with SW Update's journal (update/journal.txt: "B path" before an
/// old file moves to update/backup, "P path" before a new one moves into place, "C" when done), savagewind.exe last and
/// the list of files after it, and savagewind.exe --update-health must answer 73 or everything goes back. The same
/// journal means SW Update and Savage Wind can still undo a swap that was cut off, and the same lock keeps both from
/// working on one folder at once.
///
/// Then the setup a new version (or new game files) needs runs through Savage Wind itself, with its window hidden:
/// see <see cref="SwSetup"/>.
/// </summary>
public static class SwInstaller
{
    const int HealthExit = 73;  // (runtime/post_update.h POST_UPDATE_HEALTH_EXIT)
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromHours(2) };
    static SwInstaller() => http.DefaultRequestHeaders.UserAgent.ParseAdd("SlateCompanion");

    public sealed class InstallException(string title, string message) : Exception(message)
    {
        public string Title { get; } = title;
    }

    // ---------------------------------------------------------------- the list of files (sw-manifest.txt)

    public sealed record Manifest(string Version, Dictionary<string, (string sha, long size)> Files);

    public static Manifest ParseManifest(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        if (lines.Length == 0 || lines[0] != "savagewind-update-manifest 1") throw new InvalidDataException("not a file list of a known format");
        string version = "";
        var files = new Dictionary<string, (string, long)>(StringComparer.Ordinal);
        foreach (var l in lines.Skip(1))
        {
            if (l.StartsWith("version ", StringComparison.Ordinal)) version = l[8..];
            else if (l.StartsWith("f ", StringComparison.Ordinal))
            {
                var parts = l[2..].Split(' ', 3);
                if (parts.Length != 3 || !long.TryParse(parts[1], out var size) || !PathSafe(parts[2])) throw new InvalidDataException("a bad line in the file list");
                files[parts[2]] = (parts[0].ToLowerInvariant(), size);
            }
        }
        if (version == "" || files.Count == 0) throw new InvalidDataException("the file list is empty");
        return new Manifest(version, files);
    }

    /// <summary>A relative path that stays inside the folder (update_core.c uc_path_safe).</summary>
    public static bool PathSafe(string p)
    {
        if (string.IsNullOrEmpty(p) || p.Length > 400) return false;
        foreach (var part in p.Split('/'))
        {
            if (part.Length == 0 || part == "." || part == ".." || part.EndsWith('.') || part.EndsWith(' ')) return false;
            if (part.Any(c => c < 0x20 || "\\:*?\"<>|".Contains(c))) return false;
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])) return false;
        }
        return true;
    }

    static Manifest? Current(string folder)
    {
        try { return ParseManifest(File.ReadAllText(Path.Combine(folder, "sw-manifest.txt"))); }
        catch (Exception) { return null; }
    }

    // ---------------------------------------------------------------- the lock SW Update takes on a folder

    /// <summary>SW Update's own mutex for this folder ("Local\SavageWindUpdate-" + the first 32 hex of the SHA-256 of
    /// the lower-case \\?\ path in UTF-16), so the two never work on one folder at once.</summary>
    sealed class FolderLock : IDisposable
    {
        readonly Mutex? m;
        public FolderLock(string folder)
        {
            if (!OperatingSystem.IsWindows()) return;
            var full = Path.GetFullPath(folder).TrimEnd('\\');
            var app = full.StartsWith(@"\\?\") ? full : full.StartsWith(@"\\") ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(app.ToLowerInvariant()))).ToLowerInvariant()[..32];
            m = new Mutex(false, @"Local\SavageWindUpdate-" + hash, out bool created);
            if (!created) { m.Dispose(); m = null; throw new InstallException("Savage Wind is already being updated", "SW Update (or another Slate Companion) is working on this folder. Wait for it to finish."); }
        }
        public void Dispose() => m?.Dispose();
    }

    // ---------------------------------------------------------------- checks before anything changes

    static void NothingRunningFrom(string folder)
    {
        var names = new List<string>();
        foreach (var p in Process.GetProcesses())
            using (p)
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (path != null && path.StartsWith(folder.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) names.Add(Path.GetFileName(path));
                }
                catch (Exception) { }
            }
        if (names.Count > 0)
            throw new InstallException("Savage Wind is still open", $"These programs from that folder are still running: {string.Join(", ", names.Distinct())}. Close them and try again. Nothing was changed.");
    }

    static void EnoughDisk(string folder, long need)
    {
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!);
            if (d.AvailableFreeSpace < need)
                throw new InstallException("Not enough free disk space", $"This needs about {need / 1073741824.0:0.0} GB free on {d.Name} and there are {d.AvailableFreeSpace / 1073741824.0:0.0} GB. Nothing was changed.");
        }
        catch (ArgumentException) { }
    }

    static void Writable(string folder)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "update"));
            var p = Path.Combine(folder, "update", ".write-test");
            File.WriteAllText(p, "");
            File.Delete(p);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InstallException("Can't write in this folder", $"Windows doesn't allow writing in {UI.Ui.ShortPath(folder)} (Program Files, a read-only drive, or Controlled Folder Access). Move Savage Wind somewhere like C:\\Games. ({e.Message})");
        }
    }

    // ---------------------------------------------------------------- downloading

    /// <summary>The release's file, with resume, then checked against its signature. Returns the local path.</summary>
    static async Task<string> DownloadVerified(string url, string sigUrl, long size, string dest, IProgress<JobProgress> pr, double from, double to, string what, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var sigHex = (await http.GetStringAsync(sigUrl, ct)).Trim();
        long have = File.Exists(dest) ? new FileInfo(dest).Length : 0;
        if (have > size) { File.Delete(dest); have = 0; }
        if (have < size)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (have > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            bool resumed = resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (!resumed) have = 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(dest, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
            var buf = new byte[1 << 16];
            int n; long got = have; var last = DateTime.MinValue;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                got += n;
                if ((DateTime.UtcNow - last).TotalMilliseconds > 150)
                {
                    last = DateTime.UtcNow;
                    double f = size > 0 ? (double)got / size : 0;
                    pr.Report(new JobProgress(from + (to - from) * f, "Downloading " + what, f, $"{got / 1048576.0:0} of {size / 1048576.0:0} MB"));
                }
            }
        }
        pr.Report(new JobProgress(to, "Checking the signature", -1, "Is it really Savage Wind's release?"));
        byte[] hash;
        await using (var f = File.OpenRead(dest)) hash = await SHA256.HashDataAsync(f, ct);
        if (!SwUpdate.SignatureOk(hash, sigHex))
        {
            File.Delete(dest);
            throw new InstallException("The download isn't genuine", "It didn't match the signature Savage Wind's releases are made with, so it wasn't used and has been deleted. Nothing was changed. Try again; if it happens again, report it.");
        }
        Log.Info($"Savage Wind installer: {Path.GetFileName(dest)} verified");
        return dest;
    }

    // ---------------------------------------------------------------- unpacking (every file checked against the list)

    static Manifest Unpack(string zipPath, string stagingDir, string expectVersion, IProgress<JobProgress> pr, double from, double to, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        const string top = "Savage Wind/";
        var listEntry = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/') == top + "sw-manifest.txt")
                        ?? throw new InstallException("The release is damaged", "It has no list of files (sw-manifest.txt). Nothing was changed.");
        Manifest m;
        using (var r = new StreamReader(listEntry.Open())) m = ParseManifest(r.ReadToEnd());
        if (Launcher.CompareVersions(m.Version, expectVersion) != 0)
            throw new InstallException("The release isn't the version it should be", $"The file downloaded for {expectVersion} says it is {m.Version}. It wasn't installed.");
        if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
        Directory.CreateDirectory(stagingDir);
        long total = m.Files.Values.Sum(f => f.size), done = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = e.FullName.Replace('\\', '/');
            if (name.EndsWith('/')) continue;
            if (!name.StartsWith(top, StringComparison.Ordinal)) throw new InstallException("The release is damaged", $"It has a file outside its folder: {name}");
            var rel = name[top.Length..];
            bool isList = rel == "sw-manifest.txt";
            if (!isList && !m.Files.ContainsKey(rel)) throw new InstallException("The release is damaged", $"It has a file that isn't in its list: {rel}");
            var dest = Path.Combine(stagingDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using (var src = e.Open())
            using (var dst = File.Create(dest))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buf = new byte[1 << 16]; int n; long len = 0;
                while ((n = src.Read(buf)) > 0) { dst.Write(buf, 0, n); sha.AppendData(buf, 0, n); len += n; }
                if (!isList)
                {
                    var (want, size) = m.Files[rel];
                    if (len != size || Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant() != want)
                        throw new InstallException("The release is damaged", $"{rel} doesn't match its list (size or checksum). Nothing was changed.");
                    seen.Add(rel);
                    done += size;
                }
            }
            double f = total > 0 ? (double)done / total : 1;
            pr.Report(new JobProgress(from + (to - from) * f, "Unpacking and checking every file", f, rel));
        }
        var missing = m.Files.Keys.FirstOrDefault(k => !seen.Contains(k));
        if (missing != null) throw new InstallException("The release is damaged", $"{missing} is in its list but not in the zip. Nothing was changed.");
        return m;
    }

    // ---------------------------------------------------------------- the journal (SW Update's format)

    static int Rank(string rel) => rel.Equals("savagewind.exe", StringComparison.OrdinalIgnoreCase) ? 1
        : rel.Equals("Savage Wind Configuration.exe", StringComparison.OrdinalIgnoreCase) ? 2
        : rel.Equals("SW Update.exe", StringComparison.OrdinalIgnoreCase) ? 3 : 0;

    static string At(string folder, string rel) => Path.Combine(folder, rel.Replace('/', Path.DirectorySeparatorChar));

    static void MoveWithRetry(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        for (int i = 0; ; i++)
        {
            try { File.Move(from, to, true); return; }
            catch (IOException) when (i < 10) { Thread.Sleep(150 * (i + 1)); }  // (an antivirus holds new files for a moment)
            catch (UnauthorizedAccessException) when (i < 10) { Thread.Sleep(150 * (i + 1)); }
        }
    }

    static void Swap(string folder, Manifest? old, Manifest neu, IProgress<JobProgress> pr)
    {
        var upd = Path.Combine(folder, "update");
        var backup = Path.Combine(upd, "backup");
        var staging = Path.Combine(upd, "staging");
        if (Directory.Exists(backup)) Directory.Delete(backup, true);
        var journalPath = Path.Combine(upd, "journal.txt");
        using var journal = new FileStream(journalPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        void J(string kind, string? rel)
        {
            var b = Encoding.UTF8.GetBytes(kind + (rel != null ? " " + rel : "") + "\n");
            journal.Write(b);
            journal.Flush(true);
        }
        string current = "";
        try
        {
            pr.Report(new JobProgress(0.9, "Installing", -1, "Moving the new files into place"));
            // files of the old version the new one doesn't have
            if (old != null)
                foreach (var rel in old.Files.Keys)
                {
                    if (neu.Files.ContainsKey(rel) || !File.Exists(At(folder, rel))) continue;
                    current = rel;
                    J("B", rel);
                    MoveWithRetry(At(folder, rel), At(backup, rel));
                }
            for (int rank = 0; rank <= 3; rank++)
                foreach (var rel in neu.Files.Keys.Where(r => Rank(r) == rank))
                {
                    current = rel;
                    if (File.Exists(At(folder, rel))) { J("B", rel); MoveWithRetry(At(folder, rel), At(backup, rel)); }
                    J("P", rel);
                    MoveWithRetry(At(staging, rel), At(folder, rel));
                }
            // the list of files last: from here on this copy is the new version
            current = "sw-manifest.txt";
            if (File.Exists(At(folder, "sw-manifest.txt"))) { J("B", "sw-manifest.txt"); MoveWithRetry(At(folder, "sw-manifest.txt"), At(backup, "sw-manifest.txt")); }
            J("P", "sw-manifest.txt");
            MoveWithRetry(At(staging, "sw-manifest.txt"), At(folder, "sw-manifest.txt"));
            J("C", null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            journal.Dispose();
            Log.Error($"Savage Wind installer: the swap stopped at {current}; undoing", e);
            if (!UndoJournal(folder)) throw new InstallException("Savage Wind couldn't be put back as it was", $"A file was in use ({current}) and the old files couldn't all be restored. Your saves and game files aren't affected. Download the newest release and unpack it over the folder. ({e.Message})");
            throw new InstallException("A file is in use", $"{current} couldn't be replaced ({e.Message}). Your previous version was put back and still works. Close Savage Wind and anything using its folder (or add it as an antivirus exclusion) and try again.");
        }
    }

    /// <summary>Undoes the journal, last step first (safe to repeat). False when something couldn't be put back.</summary>
    public static bool UndoJournal(string folder)
    {
        var upd = Path.Combine(folder, "update");
        var jp = Path.Combine(upd, "journal.txt");
        if (!File.Exists(jp)) return true;
        var lines = File.ReadAllLines(jp);
        bool ok = true;
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var l = lines[i].TrimEnd('\r');
            if (l.Length < 3 || l[1] != ' ' || !PathSafe(l[2..])) continue;
            var rel = l[2..];
            string target = At(folder, rel), staged = At(Path.Combine(upd, "staging"), rel), backup = At(Path.Combine(upd, "backup"), rel);
            try
            {
                if (l[0] == 'P')
                {
                    if (File.Exists(staged) || !File.Exists(target)) continue;
                    MoveWithRetry(target, Path.Combine(upd, "trash", $"{i}-{Environment.TickCount64}"));
                }
                else if (l[0] == 'B')
                {
                    if (!File.Exists(backup)) continue;
                    MoveWithRetry(backup, target);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ok = false; Log.Error($"undo: {rel}", e); }
        }
        if (ok) File.Move(jp, Path.Combine(upd, "journal.undone.txt"), true);
        return ok;
    }

    /// <summary>A journal without its closing "C": a swap that was cut off (a crash or power cut).</summary>
    public static bool JournalCutOff(string folder)
    {
        var jp = Path.Combine(folder, "update", "journal.txt");
        if (!File.Exists(jp)) return false;
        var t = File.ReadAllText(jp);
        return !(t == "C\n" || t.EndsWith("\nC\n", StringComparison.Ordinal));
    }

    /// <summary>The previous version's files are still kept (until the new one finishes setting up).</summary>
    public static bool CanGoBack(SwInstall sw) => !sw.AppImage && File.Exists(Path.Combine(sw.Folder, "update", "journal.txt")) && !JournalCutOff(sw.Folder);

    static bool HealthCheck(string folder)
    {
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(folder, "savagewind.exe")) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("--update-health");
            using var p = Process.Start(psi)!;
            if (!p.WaitForExit(90_000)) { try { p.Kill(true); } catch (Exception) { } return false; }
            Log.Info($"savagewind.exe --update-health: exit {p.ExitCode}");
            return p.ExitCode == HealthExit;
        }
        catch (Exception e) { Log.Error("health check", e); return false; }
    }

    static void RemoveSub(string folder, string sub)
    {
        try { var d = Path.Combine(folder, "update", sub); if (Directory.Exists(d)) Directory.Delete(d, true); } catch (Exception) { }
    }

    // ---------------------------------------------------------------- the jobs

    /// <summary>Updates a Windows copy to the release (the swap only; <see cref="SwSetup"/> runs after it).</summary>
    public static async Task UpdateWindows(SwInstall sw, Launcher.Release r, IProgress<JobProgress> pr, CancellationToken ct)
    {
        var folder = sw.Folder;
        var asset = SwUpdate.AssetFor(sw, r) ?? throw new InstallException("No signed download", "This release has no signed Windows download yet. Try again later.");
        using var lk = new FolderLock(folder);
        if (!File.Exists(Path.Combine(folder, "savagewind.exe"))) throw new InstallException("Not a Savage Wind folder", "savagewind.exe isn't there.");
        Writable(folder);
        if (JournalCutOff(folder))
        {
            pr.Report(new JobProgress(0, "Finishing an update that was cut off", -1, "Putting the files back as they were"));
            if (!UndoJournal(folder)) throw new InstallException("An earlier update couldn't be undone", "Download the newest release and unpack it over the folder.");
        }
        var old = Current(folder);
        if (old != null && Launcher.CompareVersions(r.Version, old.Version) <= 0) throw new InstallException("Already up to date", $"This copy is {old.Version}.");
        NothingRunningFrom(folder);
        EnoughDisk(folder, asset.size * 7 + (1L << 30));
        RemoveSub(folder, "trash");
        var zip = await DownloadVerified(asset.url, asset.sigUrl, asset.size, Path.Combine(folder, "update", "download", asset.name), pr, 0, 0.7, $"Savage Wind {r.Version}", ct);
        var neu = await Task.Run(() => Unpack(zip, Path.Combine(folder, "update", "staging"), r.Version, pr, 0.7, 0.9, ct), ct);
        NothingRunningFrom(folder);  // (it may have been opened during the download)
        await Task.Run(() => Swap(folder, old, neu, pr));
        pr.Report(new JobProgress(0.97, "Checking the new version starts", -1, "savagewind.exe --update-health"));
        if (!await Task.Run(() => HealthCheck(folder)))
        {
            Log.Error("Savage Wind installer: the new version doesn't start; undoing");
            if (!UndoJournal(folder)) throw new InstallException("Savage Wind couldn't be put back as it was", "The new version didn't start and the old files couldn't all be restored. Download the newest release and unpack it over the folder.");
            throw new InstallException("The new version didn't start", "Your previous version was put back and still works. An antivirus removing a file is the usual cause: add the folder as an exclusion and try again.");
        }
        RemoveSub(folder, "staging");
        RemoveSub(folder, "download");
        Log.Info($"Savage Wind installer: {neu.Version} installed over {old?.Version ?? "?"} in {folder}");
        pr.Report(new JobProgress(1, $"Savage Wind {neu.Version} installed", 1, "Next: setting this version up"));
    }

    /// <summary>Goes back to the version before the last update (while its files are still kept).</summary>
    public static void GoBack(SwInstall sw)
    {
        using var lk = new FolderLock(sw.Folder);
        NothingRunningFrom(sw.Folder);
        if (!CanGoBack(sw)) throw new InstallException("No earlier version", "The previous version's files are only kept until an update finishes setting up.");
        if (!UndoJournal(sw.Folder)) throw new InstallException("Couldn't go back", "Some files couldn't be put back. Download the release you want and unpack it over the folder.");
        RemoveSub(sw.Folder, "staging");
        Log.Info($"Savage Wind installer: back to the previous version in {sw.Folder}");
    }

    /// <summary>A new copy: Windows, the release unpacked into &lt;parent&gt;/Savage Wind; Linux, its AppImage in the folder.</summary>
    public static async Task<string> InstallNew(string parent, Launcher.Release r, IProgress<JobProgress> pr, CancellationToken ct)
    {
        Directory.CreateDirectory(parent);
        if (OperatingSystem.IsWindows())
        {
            var folder = Path.Combine(parent, "Savage Wind");
            if (File.Exists(Path.Combine(folder, "savagewind.exe"))) throw new InstallException("There's already a Savage Wind there", $"{UI.Ui.ShortPath(folder)} already has one. Choose another folder, or update that one.");
            var probe = new SwInstall(Path.Combine(folder, "savagewind.exe"), folder);
            var asset = SwUpdate.AssetFor(probe, r) ?? throw new InstallException("No signed download", "This release has no signed Windows download yet.");
            EnoughDisk(parent, asset.size * 4 + (2L << 30));
            Directory.CreateDirectory(folder);
            Writable(folder);
            var zip = await DownloadVerified(asset.url, asset.sigUrl, asset.size, Path.Combine(folder, "update", "download", asset.name), pr, 0, 0.75, $"Savage Wind {r.Version}", ct);
            var staging = Path.Combine(folder, "update", "staging");
            var m = await Task.Run(() => Unpack(zip, staging, r.Version, pr, 0.75, 0.95, ct), ct);
            await Task.Run(() =>
            {
                foreach (var rel in m.Files.Keys.Append("sw-manifest.txt")) MoveWithRetry(At(staging, rel), At(folder, rel));
            });
            RemoveSub(folder, "staging"); RemoveSub(folder, "download");
            try { Directory.Delete(Path.Combine(folder, "update")); } catch (Exception) { }
            Log.Info($"Savage Wind installer: {m.Version} installed new in {folder}");
            pr.Report(new JobProgress(1, $"Savage Wind {m.Version} installed", 1, folder));
            return Path.Combine(folder, "savagewind.exe");
        }
        else
        {
            var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "x86_64";
            var a = r.Assets.FirstOrDefault(x => x.name.EndsWith($"-{arch}.AppImage", StringComparison.Ordinal));
            var sig = r.Assets.FirstOrDefault(x => x.name == a.name + ".sig");
            if (a.name == null || sig.name == null) throw new InstallException("No signed download", "This release has no signed AppImage for this computer.");
            var dest = Path.Combine(parent, a.name);
            var part = dest + ".slate-download";
            await DownloadVerified(a.url, sig.url, a.size, part, pr, 0, 0.95, $"Savage Wind {r.Version}", ct);
            File.SetUnixFileMode(part, (UnixFileMode)0b111_101_101);
            File.Move(part, dest, true);
            Log.Info($"Savage Wind installer: {r.Version} AppImage at {dest}");
            pr.Report(new JobProgress(1, $"Savage Wind {r.Version} installed", 1, dest));
            return dest;
        }
    }
}
