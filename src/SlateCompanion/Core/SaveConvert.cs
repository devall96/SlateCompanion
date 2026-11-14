using System.Buffers.Binary;
using System.Text;

namespace SlateCompanion;

/// <summary>
/// Breath of the Wild saves between Wii U (Cemu) and Switch (Savage Wind, Ryujinx, Yuzu): a port of Savage Wind's own
/// converter (tools/botw_save_convert.py, which its runtime/services/save_convert.c follows byte for byte). The flag
/// table of each revision (name hash, type, length, default) comes from the player's own Pack/Bootup.pack; nothing of
/// the game is embedded. Every save file is rebuilt for the target revision and byte order: flags kept, missing ones
/// given their defaults, ones the target doesn't have dropped.
/// </summary>
public static class SaveConvert
{
    // ---------------------------------------------------------------- BYML (the flag tables use hash, array, string, bool, numbers)

    sealed class Byml
    {
        readonly byte[] d; readonly bool little; readonly List<string> names, strs; readonly int root;
        public Byml(byte[] data)
        {
            d = data;
            little = d[0] == 'Y' && d[1] == 'B';
            if (!little && !(d[0] == 'B' && d[1] == 'Y')) throw new InvalidDataException("not a BYML file");
            int nameTab = (int)U(4), strTab = (int)U(8);
            root = (int)U(12);
            names = nameTab != 0 ? Strings(nameTab) : [];
            strs = strTab != 0 ? Strings(strTab) : [];
        }
        uint U(int o, int n = 4)
        {
            uint v = 0;
            for (int i = 0; i < n; i++) v = little ? v | (uint)d[o + i] << (8 * i) : v << 8 | d[o + i];
            return v;
        }
        int Count(int o) => (int)U(o + 1, 3);
        List<string> Strings(int o)
        {
            int n = Count(o);
            var list = new List<string>(n);
            for (int i = 0; i < n; i++)
            {
                int a = o + (int)U(o + 4 + 4 * i), end = Array.IndexOf(d, (byte)0, a);
                list.Add(Encoding.UTF8.GetString(d, a, end - a));
            }
            return list;
        }
        object? Value(byte t, uint v) => t switch
        {
            0xA0 => strs[(int)v],
            0xD0 => v != 0,
            0xD1 => (long)(int)v,
            0xD2 => (double)BitConverter.Int32BitsToSingle((int)v),
            0xD3 => (long)v,
            0xD4 => little ? BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan((int)v)) : BinaryPrimitives.ReadInt64BigEndian(d.AsSpan((int)v)),
            0xD5 => (double)(little ? BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan((int)v)) : BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan((int)v))),
            0xD6 => BitConverter.Int64BitsToDouble(little ? BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan((int)v)) : BinaryPrimitives.ReadInt64BigEndian(d.AsSpan((int)v))),
            0xFF => null,
            0xC0 or 0xC1 => Node((int)v),
            _ => throw new InvalidDataException($"BYML: unsupported node type 0x{t:X}"),
        };
        object Node(int o)
        {
            byte t = d[o]; int n = Count(o);
            if (t == 0xC0)
            {
                int bas = o + 4 + ((n + 3) & ~3);
                var l = new List<object?>(n);
                for (int i = 0; i < n; i++) l.Add(Value(d[o + 4 + i], U(bas + 4 * i)));
                return l;
            }
            if (t == 0xC1)
            {
                var m = new Dictionary<string, object?>(n);
                for (int i = 0; i < n; i++)
                {
                    int a = o + 4 + 8 * i;
                    m[names[(int)U(a, 3)]] = Value(d[a + 3], U(a + 4));
                }
                return m;
            }
            throw new InvalidDataException($"BYML: container expected, got 0x{t:X}");
        }
        public object Parse() => Node(root);
    }

    // ---------------------------------------------------------------- the flag table of one game revision

    static readonly Dictionary<string, int> Words = new()
    {
        ["bool"] = 1, ["s32"] = 1, ["f32"] = 1, ["vector2f"] = 2, ["vector3f"] = 3, ["vector4f"] = 4,
        ["string"] = 8, ["string64"] = 16, ["string256"] = 64,
    };

    internal sealed record Flag(uint Hash, string Kind, int Words, object? Init)
    {
        public bool IsString => Kind.StartsWith("string", StringComparison.Ordinal);
    }

    public sealed class Format
    {
        public uint Revision { get; }
        internal readonly Dictionary<string, List<uint>> Files = [];
        internal readonly Dictionary<uint, Flag> Flags = [];

        public Format(string bootupPath)
        {
            var pack = GameAssets.Sarc(File.ReadAllBytes(bootupPath));
            var gd = GameAssets.Sarc(GameAssets.Yaz0(pack["GameData/gamedata.ssarc"]));
            foreach (var blob in gd.Values)
                foreach (var (kind, entries) in (Dictionary<string, object?>)new Byml(blob).Parse())
                {
                    var bas = kind.Replace("_array_data", "").Replace("_data", "");
                    if (!Words.TryGetValue(bas, out int w) || entries is not List<object?> list) continue;
                    foreach (Dictionary<string, object?> e in list.Cast<Dictionary<string, object?>>())
                    {
                        var iv = e["InitValue"];
                        int count = kind.EndsWith("_array_data", StringComparison.Ordinal) ? ((List<object?>)((Dictionary<string, object?>)((List<object?>)iv!)[0]!)["Values"]!).Count : 1;
                        uint h = (uint)(Convert.ToInt64(e["HashValue"]) & 0xFFFFFFFF);
                        Flags[h] = new Flag(h, kind, w * count, iv);
                    }
                }
            var fmt = GameAssets.Sarc(GameAssets.Yaz0(pack["GameData/savedataformat.ssarc"]));
            var perFile = new Dictionary<string, List<uint>>();
            foreach (var (name, blob) in fmt)
            {
                if (!name.EndsWith(".bgsvdata", StringComparison.Ordinal)) continue;
                var doc = (Dictionary<string, object?>)new Byml(blob).Parse();
                Revision = (uint)Convert.ToInt64(((Dictionary<string, object?>)((List<object?>)doc["save_info"]!)[0]!)["revision"]);
                var fl = (List<object?>)doc["file_list"]!;
                var desc = (Dictionary<string, object?>)fl[0]!;
                var fname = (string)desc["file_name"]!;
                if (!perFile.TryGetValue(fname, out var hashes)) perFile[fname] = hashes = [];
                foreach (Dictionary<string, object?> x in ((List<object?>)fl[1]!).Cast<Dictionary<string, object?>>())
                    hashes.Add((uint)(Convert.ToInt64(x["HashValue"]) & 0xFFFFFFFF));
            }
            foreach (var (fname, hashes) in perFile)
            {
                int missing = hashes.Count(h => !Flags.ContainsKey(h));
                if (missing > 0) throw new InvalidDataException($"{fname}: {missing} flags have no type in gamedata");
                hashes.Sort();
                Files[fname] = hashes;
            }
        }
    }

    static IEnumerable<object?> Flatten(object? v)
    {
        if (v is List<object?> l) { foreach (var x in l) foreach (var y in Flatten(x)) yield return y; }
        else if (v is Dictionary<string, object?> m) { foreach (var y in Flatten(m["Values"])) yield return y; }
        else yield return v;
    }

    static double Num(object? x) => x switch { bool b => b ? 1 : 0, long l => l, double d => d, int i => i, _ => 0 };

    /// <summary>A flag's default, little-endian (strings as raw bytes), <paramref name="words"/> 32-bit words long.</summary>
    static byte[] EncodeInit(string kind, object? init, int words)
    {
        var bas = kind.Replace("_array_data", "").Replace("_data", "");
        var vals = Flatten(init).ToList();
        var raw = new List<byte>();
        if (bas.StartsWith("string", StringComparison.Ordinal))
        {
            int size = Words[bas] * 4;
            foreach (var s in vals)
            {
                var b = Encoding.UTF8.GetBytes(s is bool bb ? (bb ? "True" : "False") : Convert.ToString(s, System.Globalization.CultureInfo.InvariantCulture) ?? "");
                var cut = b.Take(size - 1).ToList();
                while (cut.Count < size) cut.Add(0);
                raw.AddRange(cut);
            }
        }
        else if (bas == "bool")
            foreach (var x in vals) raw.AddRange(BitConverter.GetBytes(Num(x) == 1 ? 1u : 0u));  // (bool InitValues carry other numbers too: only a 1 means set)
        else if (bas == "f32" || bas.StartsWith("vector", StringComparison.Ordinal))
            foreach (var x in vals) raw.AddRange(BitConverter.GetBytes((float)Num(x)));
        else
            foreach (var x in vals) raw.AddRange(BitConverter.GetBytes((uint)((long)Math.Truncate(Num(x)) & 0xFFFFFFFF)));
        var outb = new byte[words * 4];
        for (int i = 0; i < outb.Length && i < raw.Count; i++) outb[i] = raw[i];
        return outb;
    }

    // ---------------------------------------------------------------- flag save files

    const int Header = 0x0C;

    /// <summary>True: little endian (Switch); false: big endian (Wii U). Judged by the revision in the header.</summary>
    public static bool IsLittle(byte[] data)
    {
        if (data.Length < Header + 4 || BitConverter.ToUInt32(data, data.Length - 4) != 0xFFFFFFFF) throw new InvalidDataException("not a BotW flag save file (bad size or footer)");
        uint le = BinaryPrimitives.ReadUInt32LittleEndian(data), be = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (le < 0x10000 && be >= 0x10000) return true;
        if (be < 0x10000 && le >= 0x10000) return false;
        throw new InvalidDataException($"can't tell the byte order (header 0x{le:X8})");
    }

    static byte[] Rev(byte[] w) { var r = (byte[])w.Clone(); Array.Reverse(r); return r; }

    /// <summary>One flag save file rebuilt for the revision and byte order of <paramref name="fmt"/>.</summary>
    public static byte[] ConvertFlags(byte[] data, Format fmt, string fname, bool toLittle, List<string> report)
    {
        bool srcLittle = IsLittle(data);
        int body = data.Length - Header - 4;
        if (body % 8 != 0) throw new InvalidDataException("size is not a whole number of entries");
        uint srcRev = srcLittle ? BinaryPrimitives.ReadUInt32LittleEndian(data) : BinaryPrimitives.ReadUInt32BigEndian(data);
        var runs = new Dictionary<uint, List<byte[]>>();
        uint? last = null;
        for (int off = Header; off < Header + body; off += 8)
        {
            uint h = srcLittle ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(off)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(off));
            if (last != null && h < last) throw new InvalidDataException("entries are not sorted by hash");
            last = h;
            if (!runs.TryGetValue(h, out var l)) runs[h] = l = [];
            l.Add(data[(off + 4)..(off + 8)]);
        }
        using var o = new MemoryStream();
        void W32(uint v) { var b = new byte[4]; if (toLittle) BinaryPrimitives.WriteUInt32LittleEndian(b, v); else BinaryPrimitives.WriteUInt32BigEndian(b, v); o.Write(b); }
        W32(fmt.Revision); W32(0xFFFFFFFF); W32(1);
        var known = fmt.Files[fname];
        var knownSet = known.ToHashSet();
        int dropped = runs.Keys.Count(h => !knownSet.Contains(h)), kept = 0, defaulted = 0, resized = 0;
        foreach (var h in known)
        {
            var fl = fmt.Flags[h];
            List<byte[]> ws;
            if (!runs.TryGetValue(h, out var words))
            {
                var raw = EncodeInit(fl.Kind, fl.Init, fl.Words);
                ws = Enumerable.Range(0, raw.Length / 4).Select(i => raw[(i * 4)..(i * 4 + 4)]).ToList();
                if (!fl.IsString && !toLittle) ws = ws.Select(Rev).ToList();
                defaulted++;
            }
            else
            {
                if (words.Count != fl.Words)
                {
                    resized++;
                    var init = EncodeInit(fl.Kind, fl.Init, fl.Words);
                    var dflt = Enumerable.Range(0, init.Length / 4).Select(i => init[(i * 4)..(i * 4 + 4)]).ToList();
                    if (!fl.IsString && !srcLittle) dflt = dflt.Select(Rev).ToList();
                    words = words.Concat(dflt.Skip(words.Count)).Take(fl.Words).ToList();
                }
                ws = words;
                if (!fl.IsString && srcLittle != toLittle) ws = ws.Select(Rev).ToList();
                kept++;
            }
            foreach (var w in ws) { W32(h); o.Write(w); }
        }
        W32(0xFFFFFFFF);
        report.Add($"{fname}: revision 0x{srcRev:X} -> 0x{fmt.Revision:X}, {kept} flags kept, {defaulted} defaulted, {dropped} dropped, {resized} resized");
        return o.ToArray();
    }

    /// <summary>A Hero's Path block: 32-bit words swapped, except the 8-byte header (four plain bytes, two 16-bit fields).</summary>
    public static byte[] ConvertTracker(byte[] b)
    {
        if (b.Length % 4 != 0) throw new InvalidDataException("tracker block size is not a multiple of 4");
        var o = new byte[b.Length];
        Array.Copy(b, o, 4);
        o[4] = b[5]; o[5] = b[4]; o[6] = b[7]; o[7] = b[6];
        for (int i = 8; i < b.Length; i += 4) { o[i] = b[i + 3]; o[i + 1] = b[i + 2]; o[i + 2] = b[i + 1]; o[i + 3] = b[i]; }
        return o;
    }

    // ---------------------------------------------------------------- a whole profile folder

    /// <summary>The profile folder: <paramref name="src"/> itself if it has numbered slot folders with a game_data.sav,
    /// else the first folder below it that does (Cemu: …/save/00050000/101c9500/user/80000001).</summary>
    public static string? FindProfile(string src, int maxDepth = 7)
    {
        static bool Looks(string p)
        {
            try { return Directory.Exists(p) && Directory.EnumerateDirectories(p).Any(s => Path.GetFileName(s).All(char.IsAsciiDigit) && File.Exists(Path.Combine(s, "game_data.sav"))); }
            catch (Exception) { return false; }
        }
        if (Looks(src)) return src;
        var q = new Queue<(string, int)>();
        q.Enqueue((src, 0));
        int visits = 0;
        while (q.Count > 0 && visits++ < 20000)
        {
            var (dir, depth) = q.Dequeue();
            if (Looks(dir)) return dir;
            if (depth >= maxDepth) continue;
            try { foreach (var s in Directory.EnumerateDirectories(dir).OrderBy(x => x, StringComparer.Ordinal)) q.Enqueue((s, depth + 1)); }
            catch (Exception) { }
        }
        return null;
    }

    /// <summary>The profile's files converted (relative path → bytes): slots, option.sav, tracker, album, pict_book.</summary>
    public static Dictionary<string, byte[]> ConvertProfile(string root, Format fmt, bool toLittle, List<string> report)
    {
        var results = new Dictionary<string, byte[]>();
        bool? srcLittle = null;
        foreach (var slotDir in Directory.GetDirectories(root).Where(s => Path.GetFileName(s).All(char.IsAsciiDigit)).OrderBy(s => Path.GetFileName(s), StringComparer.Ordinal))
        {
            var slot = Path.GetFileName(slotDir);
            var gd = Path.Combine(slotDir, "game_data.sav");
            if (!File.Exists(gd)) continue;
            var gdb = File.ReadAllBytes(gd);
            srcLittle ??= IsLittle(gdb);
            report.Add($"slot {slot}");
            results[$"{slot}/game_data.sav"] = ConvertFlags(gdb, fmt, "game_data.sav", toLittle, report);
            var cap = Path.Combine(slotDir, "caption.sav");
            if (File.Exists(cap) && fmt.Files.ContainsKey("caption.sav")) results[$"{slot}/caption.sav"] = ConvertFlags(File.ReadAllBytes(cap), fmt, "caption.sav", toLittle, report);
            var jpg = Path.Combine(slotDir, "caption.jpg");
            if (File.Exists(jpg)) results[$"{slot}/caption.jpg"] = File.ReadAllBytes(jpg);
        }
        var opt = Path.Combine(root, "option.sav");
        if (File.Exists(opt) && fmt.Files.ContainsKey("option.sav")) results["option.sav"] = ConvertFlags(File.ReadAllBytes(opt), fmt, "option.sav", toLittle, report);
        var trk = Path.Combine(root, "tracker");
        if (Directory.Exists(trk))
        {
            bool swap = srcLittle != null && srcLittle != toLittle;
            foreach (var f in Directory.GetFiles(trk).OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                var b = File.ReadAllBytes(f);
                results["tracker/" + Path.GetFileName(f)] = swap && b.Length % 4 == 0 ? ConvertTracker(b) : b;
            }
        }
        foreach (var extra in new[] { "album", "pict_book" })
        {
            var folder = Path.Combine(root, extra);
            if (!Directory.Exists(folder)) continue;
            foreach (var f in Directory.GetFiles(folder).Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)).OrderBy(Path.GetFileName, StringComparer.Ordinal))
                results[$"{extra}/{Path.GetFileName(f)}"] = File.ReadAllBytes(f);
        }
        if (results.Count == 0) throw new InvalidDataException("nothing to convert");
        return results;
    }
}
