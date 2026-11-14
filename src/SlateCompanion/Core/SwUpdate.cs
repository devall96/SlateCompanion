using System.Diagnostics;
using System.Security.Cryptography;

namespace SlateCompanion;

/// <summary>
/// Savage Wind's updates, from its GitHub releases. Every download is checked against the release's signature
/// (&lt;file&gt;.sig: ECDSA P-256 over the file's SHA-256, r then s in hex) with the public key Savage Wind's own SW Update
/// uses (runtime/platform/sw_update_key.h), so nothing unsigned is ever installed.
///
/// Linux (AppImage): the new AppImage is downloaded next to the old one, verified, made executable and swapped in;
/// the old one is kept as .previous. Windows: the release is a folder of many files that SW Update replaces with a
/// journal it can undo, so SW Update does the swap.
/// </summary>
public static class SwUpdate
{
    public const string Repo = "Revivedx/SavageWindRecomp";
    public const string Page = "https://github.com/Revivedx/SavageWindRecomp/releases";

    // the public half of the key that signs Savage Wind's releases (X then Y)
    static readonly byte[] PublicKey = Convert.FromHexString(
        "8964c4904547706f39e3ec01768ad657efab6ef858e07659cc6f3305b269b5a5" +
        "8b2193d59c2b8563a2900169a249f406e4143818e4cbf2ec92c071d2bd88ffb8");

    static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(30) };
    static SwUpdate() => http.DefaultRequestHeaders.UserAgent.ParseAdd("SlateCompanion");

    public static bool SignatureOk(byte[] sha256, string sigHex)
    {
        try
        {
            var sig = Convert.FromHexString(sigHex.Trim());
            if (sig.Length != 64 || sha256.Length != 32) return false;
            using var ec = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = PublicKey[..32], Y = PublicKey[32..] } });
            return ec.VerifyHash(sha256, sig, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception) { return false; }
    }

    /// <summary>The release's file for this copy (the Windows zip, or the AppImage for this machine) and its .sig.</summary>
    public static (string name, string url, long size, string sigUrl)? AssetFor(SwInstall sw, Launcher.Release r)
    {
        string arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "x86_64";
        var a = r.Assets.FirstOrDefault(x => sw.AppImage ? x.name.EndsWith($"-{arch}.AppImage", StringComparison.Ordinal) : x.name.EndsWith("-windows.zip", StringComparison.Ordinal));
        if (a.name == null) return null;
        var sig = r.Assets.FirstOrDefault(x => x.name == a.name + ".sig");
        return sig.name == null ? null : (a.name, a.url, a.size, sig.url);
    }

    /// <summary>
    /// Installs the release into this copy. Linux: done here, verified, swapped. Windows: SW Update installs it
    /// (its own journal and rollback). Returns null when done (or handed to SW Update), else why not.
    /// </summary>
    public static async Task<string?> Install(SwInstall sw, Launcher.Release r, Action<double, string> progress, CancellationToken ct)
    {
        if (Launcher.GameRunning()) return "Close Savage Wind first: its files can't be replaced while it runs.";
        var asset = AssetFor(sw, r);
        if (asset == null) return "This release has no signed download for this computer.";
        if (!sw.AppImage) return "A Windows copy is updated by SwInstaller.UpdateWindows.";

        var dir = Path.GetDirectoryName(sw.Exe)!;
        var part = Path.Combine(dir, asset.Value.name + ".slate-download");
        try
        {
            Log.Info($"Savage Wind update: downloading {asset.Value.name}");
            var sigHex = await http.GetStringAsync(asset.Value.sigUrl, ct);
            using (var resp = await http.GetAsync(asset.Value.url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? asset.Value.size;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
                var buf = new byte[1 << 16];
                long got = 0; int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    got += n;
                    progress(total > 0 ? (double)got / total : 0, $"Downloading Savage Wind {r.Version}… {got >> 20} of {total >> 20} MB");
                }
            }
            progress(1, "Checking the signature…");
            byte[] hash;
            await using (var f = File.OpenRead(part)) hash = await SHA256.HashDataAsync(f, ct);
            if (!SignatureOk(hash, sigHex))
            {
                File.Delete(part);
                Log.Error($"Savage Wind update: {asset.Value.name} failed its signature check");
                return "The download didn't match Savage Wind's signature, so it wasn't installed. Nothing was changed.";
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(part, File.GetUnixFileMode(sw.Exe) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            // The same file name keeps desktop entries and shortcuts working; the old version stays as .previous.
            var previous = sw.Exe + ".previous";
            File.Move(sw.Exe, previous, true);
            try { File.Move(part, sw.Exe); }
            catch (Exception) { File.Move(previous, sw.Exe, true); throw; }
            Log.Info($"Savage Wind updated to {r.Version} ({sw.Exe}); the old version is {previous}");
            return null;
        }
        catch (OperationCanceledException) { TryDelete(part); return "Cancelled. Nothing was changed."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            TryDelete(part);
            Log.Error("Savage Wind update failed", e);
            return e.Message;
        }
    }

    static void TryDelete(string f) { try { File.Delete(f); } catch (Exception) { } }
}
