using System.Buffers.Binary;
using System.Text;

namespace SlateCompanion;

/// <summary>
/// The game's own text (MSBT message files, inside Pack/Bootup_&lt;lang&gt;.pack → Message/Msg_&lt;lang&gt;.product.ssarc).
/// Labels map to text; the colour and grammar control tags are dropped and the game's hard line wraps are joined
/// back into paragraphs. Read only.
/// </summary>
public static class GameText
{
    /// <summary>label → text for one MSBT file.</summary>
    public static Dictionary<string, string> Read(byte[] d)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (d.Length < 0x20 || Encoding.ASCII.GetString(d, 0, 8) != "MsgStdBn") return result;
        bool le = d[8] == 0xFF && d[9] == 0xFE;
        uint U32(int at) => le ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at));
        ushort U16(int at) => le ? BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at));
        int sections = U16(0x0E), off = 0x20;
        var labels = new Dictionary<int, string>();
        var texts = new List<string>();
        for (int s = 0; s < sections && off + 0x10 <= d.Length; s++)
        {
            string magic = Encoding.ASCII.GetString(d, off, 4);
            int size = (int)U32(off + 4), body = off + 0x10;
            if (magic == "LBL1")
            {
                int n = (int)U32(body);
                for (int i = 0; i < n; i++)
                {
                    int cnt = (int)U32(body + 4 + i * 8), p = body + (int)U32(body + 8 + i * 8);
                    for (int k = 0; k < cnt; k++)
                    {
                        int len = d[p];
                        labels[(int)U32(p + 1 + len)] = Encoding.ASCII.GetString(d, p + 1, len);
                        p += 1 + len + 4;
                    }
                }
            }
            else if (magic == "TXT2")
            {
                int n = (int)U32(body);
                for (int i = 0; i < n; i++)
                {
                    int start = body + (int)U32(body + 4 + 4 * i);
                    int end = i + 1 < n ? body + (int)U32(body + 8 + 4 * i) : body + size;
                    texts.Add(Clean(d, start, end, le));
                }
            }
            off = (body + size + 15) & ~15;
        }
        foreach (var (i, name) in labels) if (i < texts.Count) result[name] = texts[i];
        return result;
    }

    static string Clean(byte[] d, int start, int end, bool le)
    {
        var sb = new StringBuilder();
        for (int j = start; j + 1 < end;)
        {
            int c = le ? d[j] | d[j + 1] << 8 : d[j] << 8 | d[j + 1];
            j += 2;
            if (c == 0) break;
            if (c == 0x0E)  // control tag: group, type, parameter size, parameters
            {
                int ps = le ? d[j + 4] | d[j + 5] << 8 : d[j + 4] << 8 | d[j + 5];
                j += 6 + ps;
                continue;
            }
            if (c == 0x0F) { j += 4; continue; }  // end of a tag
            sb.Append((char)c);
        }
        // The game wraps lines by hand for its text boxes: keep blank lines as paragraph breaks, join the rest.
        var paras = sb.ToString().Replace("\r", "").Split("\n\n");
        return string.Join("\n\n", paras.Select(p => string.Join(' ', p.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)))).Trim();
    }

    /// <summary>Every label → text in a language pack, from the given message folders (e.g. "ActorType/").</summary>
    public static Dictionary<string, string> FromPack(string romfs, string lang, params string[] folders)
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        var pack = Path.Combine(romfs, "Pack", $"Bootup_{lang}.pack");
        if (!File.Exists(pack)) return all;
        var outer = GameAssets.Sarc(File.ReadAllBytes(pack));
        var msgKey = outer.Keys.FirstOrDefault(k => k.StartsWith("Message/Msg_", StringComparison.Ordinal));
        if (msgKey == null) return all;
        foreach (var (name, data) in GameAssets.Sarc(outer[msgKey]))
            if (name.EndsWith(".msbt", StringComparison.Ordinal) && folders.Any(f => name.StartsWith(f, StringComparison.Ordinal)))
                foreach (var (k, v) in Read(data)) all[k] = v;
        return all;
    }

    /// <summary>The language packs present: USen, EUen, JPja, ...</summary>
    public static List<string> Languages(string romfs) =>
        Directory.Exists(Path.Combine(romfs, "Pack"))
            ? Directory.GetFiles(Path.Combine(romfs, "Pack"), "Bootup_*.pack").Select(f => Path.GetFileNameWithoutExtension(f)["Bootup_".Length..]).Where(l => l.Length == 4).OrderBy(l => l).ToList()
            : [];
}
