using System.Buffers.Binary;
using System.Text;

namespace SlateCompanion;

/// <summary>
/// A Breath of the Wild flag save (game_data.sav, caption.sav, option.sav).
/// Layout: 12 byte header (revision, 0xFFFFFFFF, 1), then 8 byte entries (crc32 of the flag name, 4 value bytes)
/// sorted by hash, then the footer 0xFFFFFFFF. Arrays, vectors and strings are runs of entries with the same hash.
/// Wii U saves are big endian, Switch saves (and Savage Wind's) little endian. Strings are plain bytes in both.
/// </summary>
public sealed class SaveFile
{
    const int Header = 12;

    public uint Revision { get; }
    public bool BigEndian { get; }
    // The file's bytes as read. Entries are sorted by hash, so a flag is found by binary search: no table is built.
    readonly byte[] data;
    readonly int count;

    SaveFile(uint revision, bool bigEndian, byte[] data)
    {
        Revision = revision;
        BigEndian = bigEndian;
        this.data = data;
        count = (data.Length - Header - 4) / 8;
    }

    public int EntryCount => count;

    uint HashAt(int i) => BigEndian ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(Header + i * 8))
                                    : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Header + i * 8));

    /// <summary>The first entry index with this hash, and how many entries it has (arrays and strings have several).</summary>
    (int first, int n) Find(uint h)
    {
        int lo = 0, hi = count - 1, hit = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            uint v = HashAt(mid);
            if (v < h) lo = mid + 1;
            else { if (v == h) hit = mid; hi = mid - 1; }
        }
        if (hit < 0) return (-1, 0);
        int end = hit;
        while (end + 1 < count && HashAt(end + 1) == h) end++;
        return (hit, end - hit + 1);
    }

    static readonly uint[] CrcTable = BuildCrcTable();

    static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    /// <summary>zlib CRC32 of the flag name, which is how the game keys every flag.</summary>
    public static uint Hash(string name)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in Encoding.UTF8.GetBytes(name)) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    public static SaveFile Parse(byte[] data)
    {
        if (data.Length < Header + 4 || BinaryPrimitives.ReadUInt32LittleEndian(data[^4..]) != 0xFFFFFFFF)
            throw new InvalidDataException("not a Breath of the Wild flag save (bad size or footer)");
        uint le = BinaryPrimitives.ReadUInt32LittleEndian(data);
        uint be = BinaryPrimitives.ReadUInt32BigEndian(data);
        bool big;
        if (le < 0x10000 && be >= 0x10000) big = false;
        else if (be < 0x10000 && le >= 0x10000) big = true;
        else throw new InvalidDataException($"cannot tell the byte order (header 0x{le:X8})");

        int body = data.Length - Header - 4;
        if (body % 8 != 0) throw new InvalidDataException("size is not a whole number of entries (file still being written?)");

        // One pass to check the order (a half-written or damaged file isn't sorted); no copies are made.
        var span = data.AsSpan();
        uint last = 0;
        for (int off = Header; off < Header + body; off += 8)
        {
            uint h = big ? BinaryPrimitives.ReadUInt32BigEndian(span[off..]) : BinaryPrimitives.ReadUInt32LittleEndian(span[off..]);
            if (h < last) throw new InvalidDataException("entries are not sorted by hash (file damaged or half written)");
            last = h;
        }
        uint rev = big ? be : le;
        return new SaveFile(rev, big, data);
    }

    /// <summary>Reads a save that the game may be writing at the same moment: retries a few times.</summary>
    public static SaveFile Load(string path)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var buf = new byte[fs.Length];
                fs.ReadExactly(buf);
                return Parse(buf);
            }
            catch (Exception e) when (e is IOException or InvalidDataException)
            {
                lastError = e;
                Thread.Sleep(250 + attempt * 150);
            }
        }
        throw lastError!;
    }

    public bool Has(string name) => Find(Hash(name)).n > 0;

    // ---- editing (only the Save files and equipment features, on a copy the caller writes back; never the tracker)

    /// <summary>Sets one word of an existing flag (an array element) in this copy of the file; false if it isn't there.</summary>
    public bool SetWord(string name, int index, uint value)
    {
        var (first, n) = Find(Hash(name));
        if (index < 0 || index >= n) return false;
        var w = data.AsSpan(Header + (first + index) * 8 + 4, 4);
        if (BigEndian) BinaryPrimitives.WriteUInt32BigEndian(w, value); else BinaryPrimitives.WriteUInt32LittleEndian(w, value);
        return true;
    }

    /// <summary>The file's bytes (with any edits), to write back.</summary>
    public byte[] ToBytes() => (byte[])data.Clone();

    public FlagValue? Get(string name)
    {
        var (first, n) = Find(Hash(name));
        if (n == 0) return null;
        var u = new uint[n];
        var raw = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            var w = data.AsSpan(Header + (first + i) * 8 + 4, 4);
            u[i] = BigEndian ? BinaryPrimitives.ReadUInt32BigEndian(w) : BinaryPrimitives.ReadUInt32LittleEndian(w);
            w.CopyTo(raw.AsSpan(i * 4));
        }
        return new FlagValue(u, n > 1 ? DecodeStrings(raw) : [], n > 1 ? Convert.ToBase64String(raw) : "");
    }

    /// <summary>
    /// Text flags are raw bytes. Arrays of strings (PorchItem is String64[]) are runs of fixed-size chunks;
    /// we return the run as one byte string split at NUL padding so callers can pick the layout.
    /// </summary>
    static string[] DecodeStrings(byte[] all)
    {
        // Only treat it as text when every byte is printable ASCII or NUL.
        foreach (var b in all)
            if (b != 0 && (b < 0x20 || b > 0x7E)) return [];
        var parts = new List<string>();
        var sb = new StringBuilder();
        foreach (var b in all)
        {
            if (b == 0) { if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); } }
            else sb.Append((char)b);
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        return parts.ToArray();
    }
}

/// <summary>Words decoded with the file's byte order; Raw is the value bytes in file order (for string arrays).</summary>
public sealed record FlagValue(uint[] Words, string[] Strings, string Raw);

