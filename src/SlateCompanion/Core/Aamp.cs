using System.Buffers.Binary;
using System.Text;

namespace SlateCompanion;

/// <summary>
/// A reader for AAMP v2 parameter archives (Switch, little endian): .bmodellist, .bgparamlist and friends inside actor
/// packs. Names are stored as CRC32 hashes; <see cref="Crc"/> hashes a name to look it up.
///
/// Layout (public AAMP v2 notes, e.g. the aamp / oead projects; checked against Armor_001_Head.bmodellist):
///   header 0x30: "AAMP", u32 version 2, u32 flags, u32 file size, u32 pio version, u32 root offset (from 0x30),
///     u32 list / object / parameter counts, u32 data / string / unknown section sizes; then the type string ("xml").
///   list (12 bytes): u32 name crc, u16 lists offset (x4, from this list), u16 list count, u16 objects offset (x4),
///     u16 object count.
///   object (8 bytes): u32 name crc, u16 parameters offset (x4, from this object), u16 parameter count.
///   parameter (8 bytes): u32 name crc, u24 data offset (x4, from this parameter), u8 type
///     (0 bool, 1 f32, 2 int, 3 vec2, 4 vec3, 5 vec4, 6 colour, 7/8/15/20 strings, 16 quat, 17 u32, others buffers).
/// </summary>
public static class Aamp
{
    public sealed record Param(uint Name, int Type, object? Value);
    public sealed record Obj(uint Name, List<Param> Params)
    {
        public object? this[string name] => Params.FirstOrDefault(p => p.Name == Crc(name))?.Value;
    }
    public sealed record PList(uint Name, List<PList> Lists, List<Obj> Objects)
    {
        public IEnumerable<Obj> AllObjects() => Objects.Concat(Lists.SelectMany(l => l.AllObjects()));
        public Obj? Object(string name) => Objects.FirstOrDefault(o => o.Name == Crc(name));
    }

    public static PList Parse(byte[] d)
    {
        if (d.Length < 0x34 || d[0] != 'A' || d[1] != 'A' || d[2] != 'M' || d[3] != 'P') throw new InvalidDataException("not an AAMP file");
        if (BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(4)) != 2) throw new InvalidDataException("only AAMP v2 (Switch) is read");
        int root = 0x30 + (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x14));
        return List(d, root, 0);
    }

    static PList List(byte[] d, int at, int depth)
    {
        if (depth > 32) throw new InvalidDataException("AAMP nesting too deep");
        var s = d.AsSpan();
        uint name = BinaryPrimitives.ReadUInt32LittleEndian(s[at..]);
        int lo = BinaryPrimitives.ReadUInt16LittleEndian(s[(at + 4)..]) * 4, ln = BinaryPrimitives.ReadUInt16LittleEndian(s[(at + 6)..]);
        int oo = BinaryPrimitives.ReadUInt16LittleEndian(s[(at + 8)..]) * 4, on = BinaryPrimitives.ReadUInt16LittleEndian(s[(at + 10)..]);
        var lists = new List<PList>(ln);
        for (int i = 0; i < ln; i++) lists.Add(List(d, at + lo + i * 12, depth + 1));
        var objs = new List<Obj>(on);
        for (int i = 0; i < on; i++)
        {
            int o = at + oo + i * 8;
            uint oname = BinaryPrimitives.ReadUInt32LittleEndian(s[o..]);
            int po = BinaryPrimitives.ReadUInt16LittleEndian(s[(o + 4)..]) * 4, pn = BinaryPrimitives.ReadUInt16LittleEndian(s[(o + 6)..]);
            var ps = new List<Param>(pn);
            for (int k = 0; k < pn; k++)
            {
                int p = o + po + k * 8;
                uint pname = BinaryPrimitives.ReadUInt32LittleEndian(s[p..]);
                uint packed = BinaryPrimitives.ReadUInt32LittleEndian(s[(p + 4)..]);
                int data = p + (int)(packed & 0xFFFFFF) * 4, type = (int)(packed >> 24);
                ps.Add(new Param(pname, type, Value(d, data, type)));
            }
            objs.Add(new Obj(oname, ps));
        }
        return new PList(name, lists, objs);
    }

    static object? Value(byte[] d, int at, int type)
    {
        if (at < 0 || at >= d.Length) return null;
        var s = d.AsSpan(at);
        float F(int i) => BinaryPrimitives.ReadSingleLittleEndian(d.AsSpan(at + i * 4));
        return type switch
        {
            0 => BinaryPrimitives.ReadUInt32LittleEndian(s) != 0,
            1 => F(0),
            2 => BinaryPrimitives.ReadInt32LittleEndian(s),
            3 => new[] { F(0), F(1) },
            4 => new[] { F(0), F(1), F(2) },
            5 or 6 or 16 => new[] { F(0), F(1), F(2), F(3) },
            7 or 8 or 15 or 20 => Encoding.UTF8.GetString(s[..Math.Max(0, s.IndexOf((byte)0))]),
            17 => BinaryPrimitives.ReadUInt32LittleEndian(s),
            _ => null,
        };
    }

    static readonly uint[] Table = MakeTable();
    static uint[] MakeTable()
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

    /// <summary>The standard CRC32 of a parameter name, as AAMP stores it.</summary>
    public static uint Crc(string name)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in Encoding.UTF8.GetBytes(name)) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return ~c;
    }
}
